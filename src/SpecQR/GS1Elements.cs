using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SpecQR;

public static partial class GS1
{
    /// <summary>Maximum accepted string length, in UTF-16 code units.</summary>
    public const int MaxInputCharacters = 1_000_000;
    /// <summary>Maximum elements, URI path segments, or query pairs accepted per operation.</summary>
    public const int MaxElements = 16_384;

    /// <summary>GS1 modulo-10 digit, with alternating weights 3 and 1 from the right.</summary>
    public static string CalculateCheckDigit(string digits)
    {
        RequireDigits(digits, "GS1 check digit input");
        int sum = 0, weight = 3;
        for (int i = digits.Length - 1; i >= 0; i--)
        {
            sum = (sum + (digits[i] - '0') * weight) % 10;
            weight = 4 - weight;
        }
        return ((10 - sum) % 10).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Throws for malformed input; returns false for a correctly shaped wrong digit.</summary>
    public static bool ValidateCheckDigit(string digits)
    {
        RequireDigits(digits, "GS1 check digit value");
        if (digits.Length < 2) throw Error("GS1 check digit value must include body digits and one check digit");
        return CalculateCheckDigit(digits[..^1])[0] == digits[^1];
    }
    public static string CalculateGtinCheckDigit(string body)
    {
        RequireDigits(body, "GTIN body");
        if (body.Length is not (7 or 11 or 12 or 13)) throw Error("GTIN body must be 7, 11, 12, or 13 digits");
        return CalculateCheckDigit(body);
    }
    public static string AppendGtinCheckDigit(string body) => body + CalculateGtinCheckDigit(body);
    public static bool ValidateGtinCheckDigit(string gtin)
    {
        RequireDigits(gtin, "GTIN");
        if (gtin.Length is not (8 or 12 or 13 or 14)) throw Error("GTIN must be 8, 12, 13, or 14 digits");
        return ValidateCheckDigit(gtin);
    }
    public static string CalculateSsccCheckDigit(string body)
    {
        RequireDigits(body, "SSCC body");
        if (body.Length != 17) throw Error("SSCC body must be exactly 17 digits");
        return CalculateCheckDigit(body);
    }
    public static string AppendSsccCheckDigit(string body) => body + CalculateSsccCheckDigit(body);
    public static bool ValidateSsccCheckDigit(string sscc)
    {
        RequireDigits(sscc, "SSCC");
        if (sscc.Length != 18) throw Error("SSCC must be exactly 18 digits");
        return ValidateCheckDigit(sscc);
    }

    public static IReadOnlyList<GS1Element> ParseHumanReadable(string input)
    {
        if (input is null) throw Error("GS1 human-readable input must be a string");
        if (input.Length == 0) throw Error("GS1 human-readable input must not be empty");
        CheckTextLimit(input, "GS1 human-readable input");
        var elements = new List<GS1Element>();
        int position = 0;
        while (position < input.Length)
        {
            if (input[position] != '(') throw Error($"GS1 human-readable input must contain an AI in parentheses at offset {position}");
            int close = input.IndexOf(')', position + 1);
            if (close < 0) throw Error($"GS1 AI starting at offset {position} is missing a closing parenthesis");
            int end = input.IndexOf('(', close + 1);
            if (end < 0) end = input.Length;
            CheckElementLimit(elements.Count);
            var element = new GS1Element(input[(position + 1)..close], input[(close + 1)..end]);
            NormalizeElement(element, elements.Count);
            elements.Add(element);
            position = end;
        }
        return elements.AsReadOnly();
    }

    /// <summary>Inserts ASCII GS after every non-final variable-length element.</summary>
    public static string CreateElementString(IEnumerable<GS1Element> elements)
    {
        var list = SnapshotElements(elements);
        if (list.Count == 0) throw Error("GS1 elements must not be empty");
        var builder = new StringBuilder();
        for (int i = 0; i < list.Count; i++)
        {
            var info = NormalizeElement(list[i], i);
            builder.Append(list[i].Ai).Append(list[i].Value);
            if (info.Length.IsVariable && i < list.Count - 1) builder.Append(Fnc1Separator);
        }
        return builder.ToString();
    }

    /// <summary>Parses raw data. Conservatively rejects a final variable value that ends in an apparent fixed element.</summary>
    public static GS1ElementStringParseResult ParseElementString(string input)
    {
        if (input is null) throw Error("GS1 element string input must be a string");
        if (input.Length == 0) throw Error("GS1 element string input must not be empty");
        CheckTextLimit(input, "GS1 element string input");
        if (input.IndexOfAny(['(', ')']) >= 0)
            throw Error("GS1 element string input must be raw data without human-readable parentheses; use parseGs1HumanReadable() and createGs1ElementString() before generate(..., { gs1: true })");
        var elements = new List<GS1Element>();
        int position = 0;
        while (position < input.Length)
        {
            if (input[position] == '\u001D') throw Error($"GS1 element string has an unexpected FNC1 separator at offset {position}");
            var info = ReadAi(input, position) ?? throw Error($"Unsupported GS1 AI at offset {position}");
            int start = position + info.Ai.Length;
            int end = info.Length.Exact is int exact ? start + Math.Min(exact, input.Length - start) : input.IndexOf('\u001D', start);
            if (end < 0) end = input.Length;
            if (info.Length.IsVariable && end == input.Length)
            {
                // Only a fixed element of at most 22 characters can end at the boundary.
                // Skip irrelevant prefix positions without changing the earliest possible match.
                for (int candidate = Math.Max(start + 1, end - 22); candidate < end; candidate++)
                {
                    var next = ReadAi(input, candidate);
                    if (next?.Length.Exact is int length && candidate + next.Ai.Length + length == end)
                        throw Error($"GS1 variable-length element at offset {start} is missing an FNC1 separator before offset {candidate}");
                }
            }
            CheckElementLimit(elements.Count);
            var element = new GS1Element(info.Ai, input[start..end]);
            NormalizeElement(element, elements.Count);
            elements.Add(element);
            position = end;
            if (position < input.Length && input[position] == '\u001D' && info.Length.IsVariable)
            {
                position++;
                if (position == input.Length) throw Error("GS1 element string must not end with an FNC1 separator");
            }
        }
        return new(elements.AsReadOnly(), input.Contains('\u001D'));
    }

    /// <summary>Validates elements; Digital Link context only requires a primary key, not path placement or uniqueness.</summary>
    public static GS1ValidationResult ValidateElements(IEnumerable<GS1Element> elements, GS1ValidationOptions? options = null)
    {
        options ??= new();
        if (ValidationOptionsIssue(options) is { } optionsIssue) return ValidationFailure([optionsIssue]);
        if (elements is null) return ValidationFailure([new("GS1_INVALID_INPUT", "GS1 elements must be an array") { Reason = "invalid-input" }]);
        List<GS1Element> list;
        try { list = SnapshotElements(elements); }
        catch (SpecQRException ex) { return ValidationFailure([ValidationIssue(ex)]); }
        if (list.Count == 0) return ValidationFailure([new("GS1_INVALID_INPUT", "GS1 elements must not be empty") { Reason = "invalid-input" }]);
        var errors = new List<GS1ValidationIssue>();
        for (int i = 0; i < list.Count; i++)
        {
            try { NormalizeElement(list[i], i); }
            catch (SpecQRException ex)
            {
                errors.Add(ValidationIssue(ex, list[i], i));
                if (!options.CollectAllErrors) break;
            }
        }
        if (errors.Count > 0) return ValidationFailure(errors.AsReadOnly());
        if (options.Context == GS1ValidationContext.DigitalLink && !list.Any(e => GetAiInfo(e.Ai)?.DigitalLinkRole == GS1DigitalLinkRole.PrimaryKey))
            return ValidationFailure([new("GS1_INVALID_DIGITAL_LINK_PLACEMENT", "GS1 Digital Link elements must include a primary AI 00, 01, or 414")
            { Reason = "invalid-digital-link-placement", Expected = "primary AI 00, 01, or 414" }]);
        return new(true, list.AsReadOnly(), null, [], []);
    }

    /// <summary>Nonthrowing raw parsing; context and error collection do not change raw parser semantics.</summary>
    public static GS1ValidationResult ValidateElementString(string input, GS1ValidationOptions? options = null)
    {
        options ??= new();
        if (ValidationOptionsIssue(options) is { } optionsIssue) return ValidationFailure([optionsIssue]);
        try
        {
            var parsed = ParseElementString(input);
            return new(true, parsed.Elements, parsed.HasSeparators, [], []);
        }
        catch (SpecQRException ex) { return ValidationFailure([ValidationIssue(ex, input: input)]); }
    }

    private static SpecQRException Error(string message) => new("INVALID_GS1", message);
    private static List<GS1Element> SnapshotElements(IEnumerable<GS1Element> elements)
    {
        if (elements is null) throw Error("GS1 elements must be an array");
        if (elements is ICollection<GS1Element> collection && collection.Count > MaxElements)
            throw Error($"GS1 elements must contain at most {MaxElements} elements");
        var result = new List<GS1Element>();
        foreach (var element in elements)
        {
            CheckElementLimit(result.Count);
            result.Add(element);
        }
        return result;
    }
    private static void CheckTextLimit(string text, string label)
    { if (text.Length > MaxInputCharacters) throw Error($"{label} must contain at most {MaxInputCharacters} UTF-16 code units"); }
    private static void CheckElementLimit(int currentCount)
    { if (currentCount >= MaxElements) throw Error($"GS1 elements must contain at most {MaxElements} elements"); }
    private static GS1AiInfo NormalizeElement(GS1Element element, int index)
    {
        if (element is null) throw Error($"GS1 element {index} must be an object with ai and value");
        string ai = element.Ai, value = element.Value;
        if (ai is not null) CheckTextLimit(ai, $"GS1 element {index} AI");
        if (value is not null) CheckTextLimit(value, $"GS1 element {index} value");
        if (!IsAi(ai!)) throw Error($"GS1 element {index} has invalid AI {JsonQuote(ai)}; expected 2 to 4 digits");
        var info = GetAiInfo(ai!) ?? throw Error($"Unsupported GS1 AI {ai}. Add explicit support before using it.");
        if (value is null) throw Error($"GS1 AI {ai} value must be a string");
        if (value.Length == 0) throw Error($"GS1 AI {ai} value must not be empty");
        if (value.Contains('\u001D')) throw Error($"GS1 AI {ai} value must not contain the FNC1 separator");
        if (value.IndexOfAny(['(', ')']) >= 0) throw Error($"GS1 AI {ai} value must be raw data without human-readable parentheses");
        if (value.Any(c => c < 32 || c > 126)) throw Error($"GS1 AI {ai} value must use printable ASCII characters");
        if (info.ValueKind == GS1ValueKind.Numeric && !IsDigits(value)) throw Error($"GS1 AI {ai} value must contain digits only");
        if (info.Length.Exact is int exact && value.Length != exact) throw Error($"GS1 AI {ai} value must be exactly {exact} characters");
        if (info.Length.IsVariable && value.Length > info.Length.Max) throw Error($"GS1 AI {ai} value must be at most {info.Length.Max} characters");
        if (info.CheckDigitRule == GS1CheckDigitRule.Gtin && !ValidateGtinCheckDigit(value)) throw Error($"GS1 AI {ai} value has an invalid GTIN check digit");
        if (info.CheckDigitRule == GS1CheckDigitRule.Sscc && !ValidateSsccCheckDigit(value)) throw Error($"GS1 AI {ai} value has an invalid SSCC check digit");
        return info;
    }
    private static bool IsAi(string ai) => ai is not null && ai.Length is >= 2 and <= 4 && IsDigits(ai);
    private static bool IsDigits(string text) => !string.IsNullOrEmpty(text) && text.All(c => c is >= '0' and <= '9');
    private static void RequireDigits(string text, string label)
    {
        if (text is not null) CheckTextLimit(text, label);
        if (!IsDigits(text!)) throw Error($"{label} must contain digits only");
    }
    private static GS1AiInfo? ReadAi(string text, int offset)
    {
        for (int length = 4; length >= 2; length--)
            if (length <= text.Length - offset && GetAiInfo(text.Substring(offset, length)) is { } info) return info;
        return null;
    }
    private static string JsonQuote(string? value) => JsonSerializer.Serialize(value, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    private static string? Capture(string pattern, string text)
    {
        var match = Regex.Match(text, pattern, RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value : null;
    }
    private static GS1ValidationResult ValidationFailure(IReadOnlyList<GS1ValidationIssue> errors) => new(false, null, null, errors, []);
    private static GS1ValidationIssue? ValidationOptionsIssue(GS1ValidationOptions options)
    {
        if (!Enum.IsDefined(options.Context)) return new("GS1_INVALID_INPUT", "GS1 validation options.context must be \"element-string\" or \"digital-link\"")
        { Reason = "invalid-options", Expected = "element-string or digital-link" };
        return options.AllowUnsupportedAi ? new("GS1_INVALID_INPUT", "GS1 validation options.allowUnsupportedAi must be false")
        { Reason = "invalid-options", Expected = false } : null;
    }
    private static GS1ValidationIssue ValidationIssue(Exception ex, GS1Element? element = null, int? elementIndex = null, string? input = null)
    {
        string message = ex.Message, code = "GS1_INVALID_INPUT", reason = "invalid-input";
        object? expected = null;
        if (message.Contains("Unsupported GS1 AI", StringComparison.Ordinal))
        { code = "GS1_UNSUPPORTED_AI"; reason = "unsupported-ai"; expected = "supported GS1 AI"; }
        else if (Capture("(exactly [0-9]+ characters|at most [0-9]+ characters)", message) is { } length)
        { code = "GS1_INVALID_LENGTH"; reason = "invalid-length"; expected = length; }
        else if (message.Contains("digits only", StringComparison.Ordinal) || message.Contains("printable ASCII", StringComparison.Ordinal))
        { code = "GS1_INVALID_CHARSET"; reason = "invalid-charset"; expected = message.Contains("digits only", StringComparison.Ordinal) ? "digits only" : "printable ASCII"; }
        else if (message.Contains("missing an FNC1 separator", StringComparison.Ordinal))
        { code = "GS1_MISSING_SEPARATOR"; reason = "missing-separator"; expected = "FNC1 separator before the next GS1 element"; }
        else if (message.Contains("unexpected FNC1 separator", StringComparison.Ordinal) || message.Contains("must not end with an FNC1 separator", StringComparison.Ordinal) || message.Contains("must not contain the FNC1 separator", StringComparison.Ordinal))
        { code = "GS1_UNEXPECTED_SEPARATOR"; reason = "unexpected-separator"; expected = "separator only after a non-final variable-length GS1 element"; }
        else if (message.Contains("invalid GTIN check digit", StringComparison.Ordinal) || message.Contains("invalid SSCC check digit", StringComparison.Ordinal))
        { code = "GS1_INVALID_CHECK_DIGIT"; reason = "invalid-check-digit"; expected = message.Contains("SSCC", StringComparison.Ordinal) ? "valid SSCC check digit" : "valid GTIN check digit"; }
        else if (message.Contains("cannot be placed in the Digital Link path", StringComparison.Ordinal))
        { code = "GS1_INVALID_DIGITAL_LINK_PLACEMENT"; reason = "invalid-digital-link-placement"; }
        else if (Capture("duplicate AI ([0-9]{2,4})", message) is not null)
        { code = "GS1_DUPLICATE_AI"; reason = "duplicate-ai"; expected = "unique GS1 AI within the Digital Link URI"; }
        string? ai = Capture("GS1 AI ([0-9]{2,4})", message) ?? Capture("duplicate AI ([0-9]{2,4})", message);
        int? offset = int.TryParse(Capture("offset ([0-9]+)", message), out int offsetValue) ? offsetValue : null;
        int? index = int.TryParse(Capture("GS1 element ([0-9]+)", message), out int indexValue) ? indexValue : elementIndex;
        if (ai is null && input is not null && offset is int p && p <= input.Length)
        {
            ai = Capture("^([0-9]{2,4})", input[p..]);
            if (ai is null)
                for (int length = 2; length <= 4; length++)
                    if (p >= length && GetAiInfo(input.Substring(p - length, length)) is { } found) { ai = found.Ai; break; }
        }
        return new(code, message) { Reason = reason, Expected = expected, Ai = ai, Offset = offset, ElementIndex = index, Value = element?.Value };
    }
}
