using System.Globalization;
using System.Text;
using System.Text.Json;

namespace SpecQR;

public static partial class GS1
{
    /// <summary>Creates a deterministic Digital Link. Qualifiers retain input order; query AIs are sorted.</summary>
    public static string CreateDigitalLink(IEnumerable<GS1Element> elements, GS1DigitalLinkOptions options)
    {
        if (options is null || string.IsNullOrEmpty(options.BaseUrl)) throw Error("GS1 Digital Link options.baseUrl is required");
        DigitalLinkUrl url;
        try { url = new(options.BaseUrl); }
        catch (SpecQRException) { throw Error("GS1 Digital Link options.baseUrl must be a valid http or https URL"); }
        if (url.Scheme is not ("http" or "https")) throw Error("GS1 Digital Link options.baseUrl must use http or https");
        if (!string.IsNullOrEmpty(url.Query) || !string.IsNullOrEmpty(url.Fragment)) throw Error("GS1 Digital Link options.baseUrl must not include query or fragment components");
        string primaryAi = options.PrimaryAi;
        CheckPrimaryAi(primaryAi);
        HashSet<string>? pathAis = null;
        if (options.PathAis is not null)
        {
            if (options.PathAis.Count > MaxElements) throw Error($"GS1 Digital Link pathAis must contain at most {MaxElements} elements");
            pathAis = new(StringComparer.Ordinal);
            foreach (string ai in options.PathAis)
            {
                if (!IsAi(ai)) throw Error("GS1 Digital Link pathAis entries must be 2 to 4 digit AI strings");
                if (ai != primaryAi) { AssertPathPlacement(ai, primaryAi); pathAis.Add(ai); }
            }
        }
        var list = SnapshotElements(elements);
        if (list.Count == 0) throw Error("GS1 Digital Link input elements must not be empty");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < list.Count; i++)
        {
            NormalizeElement(list[i], i);
            RejectDuplicate(seen, list[i].Ai);
            seen.Add(list[i].Ai);
        }
        var primary = list.FirstOrDefault(e => e.Ai == primaryAi) ?? throw Error($"GS1 Digital Link input must include primary AI {primaryAi}");
        bool InPath(string ai) => pathAis?.Contains(ai) ?? CanPlaceInPath(ai, primaryAi);
        var pathElements = new[] { primary }.Concat(list.Where(e => e.Ai != primaryAi && InPath(e.Ai)));
        if (pathElements.Any(e => e.Value is "." or ".."))
            throw Error("GS1 Digital Link path values must not be dot segments; use PathAis = [] to place these values in the query");
        var queryElements = list.Where(e => e.Ai != primaryAi && !InPath(e.Ai)).OrderBy(e => e.Ai, StringComparer.Ordinal).ThenBy(e => e.Value, StringComparer.Ordinal);
        string prefix = url.Path.TrimEnd('/');
        string suffix = string.Join("/", pathElements.SelectMany(e => new[] { PercentEncode(e.Ai, false), PercentEncode(e.Value, false) }));
        url.Path = DigitalLinkUrl.NormalizePath(prefix + "/" + suffix);
        string query = string.Join("&", queryElements.Select(e => PercentEncode(e.Ai, true) + "=" + PercentEncode(e.Value, true)));
        url.Query = query.Length == 0 ? null : query;
        return url.ToString();
    }
    public static string CreateDigitalLink(GS1ElementStringParseResult parsed, GS1DigitalLinkOptions options) =>
        CreateDigitalLink(parsed?.Elements!, options);
    public static string CreateDigitalLink(GS1DigitalLinkParseResult parsed, GS1DigitalLinkOptions options) =>
        CreateDigitalLink(parsed?.Elements!, options);

    public static GS1DigitalLinkParseResult ParseDigitalLink(string uri, GS1DigitalLinkParseOptions? options = null)
    {
        options ??= new();
        var url = new DigitalLinkUrl(uri);
        CheckDigitalLinkUrl(url);
        return ParseDigitalLinkUrl(url, options);
    }
    public static GS1DigitalLinkParseResult ParseDigitalLink(Uri uri, GS1DigitalLinkParseOptions? options = null) =>
        ParseDigitalLink(uri?.OriginalString!, options);

    public static GS1DigitalLinkValidationResult ValidateDigitalLink(string uri, GS1DigitalLinkValidationOptions? options = null)
    {
        options ??= new();
        if (options.Normalize) return LinkValidationFailure(new("GS1_INVALID_INPUT", "GS1 Digital Link validation options.normalize is not implemented yet")
        { Reason = "unsupported-option", Expected = false });
        try
        {
            var url = new DigitalLinkUrl(uri);
            if (InvalidPercentEncoding(url.Path) || InvalidPercentEncoding(url.Query ?? "")) throw Error("GS1 Digital Link URI must use valid percent-encoding");
            CheckDigitalLinkUrl(url);
            var parsed = ParseDigitalLinkUrl(url, new() { PrimaryAi = options.PrimaryAi, UnknownQuery = options.UnknownQuery });
            var warnings = new List<GS1ValidationIssue>();
            if (url.Scheme == "http") warnings.Add(new("GS1_DIGITAL_LINK_HTTP", "GS1 Digital Link URI uses http. Use https when transport security is required.") { Reason = "http-uri" });
            if (parsed.UnknownQuery.Count != 0) warnings.Add(new("GS1_DIGITAL_LINK_UNKNOWN_QUERY_PRESERVED", "GS1 Digital Link URI contains non-GS1 query parameters preserved in unknownQuery.")
            { Reason = "unknown-query-preserved", Count = parsed.UnknownQuery.Count });
            return new(true, parsed, [], warnings.AsReadOnly());
        }
        catch (SpecQRException ex) { return LinkValidationFailure(DigitalLinkIssue(ex)); }
    }
    public static GS1DigitalLinkValidationResult ValidateDigitalLink(Uri uri, GS1DigitalLinkValidationOptions? options = null) =>
        ValidateDigitalLink(uri?.OriginalString!, options);

    /// <summary>SpecQR deterministic normalization, not GS1 canonicalization. Unknown query parameters retain relative order.</summary>
    public static string NormalizeDigitalLink(string uri, GS1DigitalLinkNormalizeOptions? options = null)
    {
        options ??= new();
        if (options.Mode != "specqr-deterministic") throw Error("GS1 Digital Link normalization mode must be \"specqr-deterministic\"");
        var url = new DigitalLinkUrl(uri);
        CheckDigitalLinkUrl(url);
        if (InvalidPercentEncoding(url.Path) || InvalidPercentEncoding(url.Query ?? "")) throw Error("GS1 Digital Link URI must use valid percent-encoding");
        var parsed = ParseDigitalLinkUrl(url, new() { PrimaryAi = options.PrimaryAi, UnknownQuery = options.UnknownQuery });
        var segments = PathSegments(url.Path);
        int start = FirstPrimaryIndex(segments, options.PrimaryAi);
        url.Path = "/" + string.Join("/", segments.Take(start));
        url.Query = null; url.Fragment = null;
        var normalized = new DigitalLinkUrl(CreateDigitalLink(parsed.Elements, new(url.ToString()) { PrimaryAi = parsed.Primary.Ai }));
        string unknown = string.Join("&", parsed.UnknownQuery.Select(q => PercentEncode(q.Key, true) + "=" + PercentEncode(q.Value, true)));
        if (unknown.Length != 0) normalized.Query = normalized.Query is null ? unknown : normalized.Query + "&" + unknown;
        return normalized.ToString();
    }
    public static string NormalizeDigitalLink(Uri uri, GS1DigitalLinkNormalizeOptions? options = null) =>
        NormalizeDigitalLink(uri?.OriginalString!, options);

    private static void CheckDigitalLinkUrl(DigitalLinkUrl url)
    {
        if (url.Scheme is not ("http" or "https")) throw Error("GS1 Digital Link URI must use http or https");
        if (!string.IsNullOrEmpty(url.Fragment)) throw Error("GS1 Digital Link URI must not include a fragment");
    }
    private static void CheckPrimaryAi(string ai)
    { if (ai is not ("00" or "01" or "414")) throw Error("GS1 Digital Link primaryAi must be one of 00, 01, or 414"); }
    private static string[] PathSegments(string path)
    {
        string trimmed = path.Trim('/');
        if (trimmed.Length == 0) throw Error("GS1 Digital Link path must include primary AI 00, 01, or 414");
        if (trimmed.Count(c => c == '/') >= MaxElements) throw Error($"GS1 Digital Link path must contain at most {MaxElements} segments");
        string[] segments = trimmed.Split('/');
        if (segments.Any(s => s.Length == 0)) throw Error("GS1 Digital Link path must not contain empty segments");
        return segments;
    }
    private static int FirstPrimaryIndex(string[] segments, string? preferred)
    {
        int index = Array.FindIndex(segments, s => preferred is not null ? s == preferred : GetAiInfo(s)?.DigitalLinkRole == GS1DigitalLinkRole.PrimaryKey);
        if (index < 0) throw Error("GS1 Digital Link path must include primary AI 00, 01, or 414");
        return index;
    }
    private static GS1DigitalLinkParseResult ParseDigitalLinkUrl(DigitalLinkUrl url, GS1DigitalLinkParseOptions options)
    {
        if (options.PrimaryAi is not null) CheckPrimaryAi(options.PrimaryAi);
        if (!Enum.IsDefined(options.UnknownQuery)) throw Error("GS1 Digital Link unknownQuery must be \"preserve\" or \"reject\"");
        string[] segments = PathSegments(url.Path);
        int start = FirstPrimaryIndex(segments, options.PrimaryAi);
        if ((segments.Length - start) % 2 != 0) throw Error("GS1 Digital Link path must contain AI/value pairs");
        var path = new List<GS1Element>(); var query = new List<GS1Element>(); var unknown = new List<GS1UnknownQuery>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int index = start; index < segments.Length; index += 2)
        {
            string ai = segments[index];
            if (!IsAi(ai)) throw Error($"GS1 Digital Link path segment {index + 1} must be a GS1 AI");
            string value = PercentDecode(segments[index + 1], false, true) ?? throw Error($"GS1 Digital Link path value for AI {ai} must be valid percent-encoding");
            var element = new GS1Element(ai, value);
            NormalizeElement(element, path.Count);
            if (path.Count != 0) AssertPathPlacement(ai, path[0].Ai);
            RejectDuplicate(seen, ai); seen.Add(ai); path.Add(element);
        }
        string queryText = url.Query ?? "";
        if (queryText.Count(c => c == '&') >= MaxElements) throw Error($"GS1 Digital Link query must contain at most {MaxElements} pairs");
        foreach (string pair in queryText.Split('&'))
        {
            if (pair.Length == 0) continue;
            int equals = pair.IndexOf('=');
            string key = PercentDecode(equals < 0 ? pair : pair[..equals], true, false)!;
            string value = PercentDecode(equals < 0 ? "" : pair[(equals + 1)..], true, false)!;
            if (IsAi(key))
            {
                var element = new GS1Element(key, value);
                NormalizeElement(element, path.Count + query.Count);
                RejectDuplicate(seen, key); seen.Add(key); query.Add(element);
            }
            else if (options.UnknownQuery == GS1UnknownQueryPolicy.Preserve) unknown.Add(new(key, value));
            else throw Error($"GS1 Digital Link query parameter {JsonQuote(key)} is not a GS1 AI");
        }
        return new(Array.AsReadOnly(path.Concat(query).ToArray()), path[0], path.AsReadOnly(), query.AsReadOnly(), unknown.AsReadOnly());
    }
    private static bool CanPlaceInPath(string ai, string primary) =>
        GetAiInfo(ai) is { DigitalLinkRole: GS1DigitalLinkRole.KeyQualifier } info && (info.DigitalLinkPathForPrimary?.Contains(primary) ?? false);
    private static void AssertPathPlacement(string ai, string primary)
    {
        if (GetAiInfo(ai) is null) throw Error($"Unsupported GS1 AI {ai}. Add explicit support before using it.");
        if (!CanPlaceInPath(ai, primary)) throw Error($"GS1 AI {ai} cannot be placed in the Digital Link path after primary AI {primary}");
    }
    private static void RejectDuplicate(HashSet<string> seen, string ai)
    { if (seen.Contains(ai)) throw Error($"GS1 Digital Link input must not contain duplicate AI {ai}"); }
    private static GS1DigitalLinkValidationResult LinkValidationFailure(GS1ValidationIssue issue) => new(false, null, [issue], []);
    private static GS1ValidationIssue DigitalLinkIssue(Exception ex)
    {
        string message = ex.Message;
        if (message.Contains("absolute http or https URL", StringComparison.Ordinal) || message.Contains("must use http or https", StringComparison.Ordinal))
            return new("GS1_DIGITAL_LINK_INVALID_URI", message) { Reason = "invalid-uri", Expected = "absolute http or https URL" };
        if (message.Contains("must not include a fragment", StringComparison.Ordinal))
            return new("GS1_DIGITAL_LINK_FRAGMENT_NOT_ALLOWED", message) { Reason = "fragment-not-allowed", Expected = "URI without fragment" };
        if (message.Contains("valid percent-encoding", StringComparison.Ordinal))
            return new("GS1_INVALID_PERCENT_ENCODING", message) { Reason = "invalid-percent-encoding", Expected = "percent escapes must use two hexadecimal digits" };
        if (message.Contains("query parameter", StringComparison.Ordinal) && message.Contains("is not a GS1 AI", StringComparison.Ordinal))
        {
            string? raw = Capture("query parameter (\"(?:[^\"\\\\]|\\\\.)*\")", message);
            return new("GS1_DIGITAL_LINK_UNKNOWN_QUERY", message) { Reason = "unknown-query", Expected = "GS1 AI query parameter or unknownQuery: \"preserve\"", Key = raw is null ? null : JsonSerializer.Deserialize<string>(raw) };
        }
        if (message.Contains("primaryAi must be", StringComparison.Ordinal) || message.Contains("unknownQuery must be", StringComparison.Ordinal))
            return new("GS1_INVALID_INPUT", message) { Reason = "invalid-options", Expected = message.Contains("primaryAi", StringComparison.Ordinal) ? "00, 01, or 414" : "preserve or reject" };
        if (message.Contains("path must include primary AI", StringComparison.Ordinal) || message.Contains("path must contain AI/value pairs", StringComparison.Ordinal) || message.Contains("path must not contain empty segments", StringComparison.Ordinal) || Capture("path segment ([0-9]+) must be a GS1 AI", message) is not null)
            return new("GS1_INVALID_INPUT", message) { Reason = "malformed-path", Expected = "Digital Link path containing primary AI and AI/value pairs" };
        return ValidationIssue(ex);
    }
    private static bool InvalidPercentEncoding(string text)
    {
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '%' && (i + 2 >= text.Length || Hex(text[i + 1]) < 0 || Hex(text[i + 2]) < 0)) return true;
        return false;
    }
    private static int Hex(char c) => c is >= '0' and <= '9' ? c - '0' : c is >= 'A' and <= 'F' ? c - 'A' + 10 : c is >= 'a' and <= 'f' ? c - 'a' + 10 : -1;
    private static string PercentEncode(string text, bool form)
    {
        var builder = new StringBuilder();
        string safe = form ? "*-._" : "-_.!~*'()";
        foreach (byte b in Encoding.UTF8.GetBytes(text))
        {
            if (b is >= 48 and <= 57 or >= 65 and <= 90 or >= 97 and <= 122 || safe.Contains((char)b)) builder.Append((char)b);
            else if (form && b == 32) builder.Append('+');
            else builder.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }
        return builder.ToString();
    }
    private static string? PercentDecode(string text, bool form, bool strict)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        int length = 0;
        for (int i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] == '%')
            {
                if (i + 2 < bytes.Length && Hex((char)bytes[i + 1]) is int a && a >= 0 && Hex((char)bytes[i + 2]) is int b && b >= 0)
                { bytes[length++] = (byte)(a * 16 + b); i += 2; continue; }
                if (strict) return null;
            }
            bytes[length++] = form && bytes[i] == '+' ? (byte)' ' : bytes[i];
        }
        try { return (strict ? new UTF8Encoding(false, true) : Encoding.UTF8).GetString(bytes, 0, length); }
        catch (DecoderFallbackException) { return null; }
    }
}
