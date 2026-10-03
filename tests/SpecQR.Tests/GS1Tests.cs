using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SpecQR;

internal static class GS1Tests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static int Run()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "gs1-upstream.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Equal("15ad15e5c770ea0e39072f8f88b2733018f02ffd", document.RootElement.GetProperty("upstreamCommit").GetString(), "GS1 fixture identity");
        int count = 0, intentionalDifferences = 0;
        foreach (var fixture in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            object? result = Evaluate(fixture);
            JsonNode? actual = JsonSerializer.SerializeToNode(result, JsonOptions);
            JsonNode? expected = JsonNode.Parse(fixture.GetProperty("expected").GetRawText());
            // JS/Swift silently remove literal dot-segment qualifier payloads. This port
            // rejects that operation; caller can explicitly place the value in the query.
            if (fixture.GetProperty("op").GetString() == "linkCreate" &&
                fixture.TryGetProperty("elements", out var fixtureElements) && fixtureElements.EnumerateArray().Any(e => e.GetProperty("value").GetString() is "." or "..") &&
                !fixture.GetProperty("options").TryGetProperty("pathAis", out _))
            {
                expected = JsonSerializer.SerializeToNode(new { throws = new { code = "INVALID_GS1", message = "GS1 Digital Link path values must not be dot segments; use PathAis = [] to place these values in the query" } });
                intentionalDifferences++;
            }
            if (!JsonNode.DeepEquals(actual, expected)) throw new InvalidOperationException($"GS1 fixture {count} ({fixture.GetProperty("op").GetString()}) differs. Input: {fixture}. Actual: {actual}");
            count++;
        }
        Equal(1411, count, "GS1 fixture count");
        Equal(2, intentionalDifferences, "explicit dot-segment safety differences");
        count += ResourceAndSafetyTests();
        return count;
    }

    private static object? Evaluate(JsonElement f)
    {
        string op = f.GetProperty("op").GetString()!;
        string input = f.TryGetProperty("input", out var i) ? i.GetString()! : "";
        JsonElement opts = f.TryGetProperty("options", out var o) ? o : default;
        string? Option(string key) => opts.ValueKind == JsonValueKind.Object && opts.TryGetProperty(key, out var value) ? value.GetString() : null;
        bool Flag(string key, bool fallback = false) => opts.ValueKind == JsonValueKind.Object && opts.TryGetProperty(key, out var value) ? value.GetBoolean() : fallback;
        var elements = f.TryGetProperty("elements", out var e) ? e.EnumerateArray().Select(x => new GS1Element(x.GetProperty("ai").GetString()!, x.GetProperty("value").GetString()!)).ToArray() : [];
        var unknown = Option("unknownQuery") == "reject" ? GS1UnknownQueryPolicy.Reject : GS1UnknownQueryPolicy.Preserve;
        var validation = new GS1ValidationOptions
        {
            Context = Option("context") == "digital-link" ? GS1ValidationContext.DigitalLink : GS1ValidationContext.ElementString,
            AllowUnsupportedAi = Flag("allowUnsupportedAi"), CollectAllErrors = Flag("collectAllErrors", true)
        };
        try
        {
            return op switch
            {
                "dictionary" => GS1.GetSupportedAis().Select(Info).ToArray(),
                "info" => Info(GS1.GetAiInfo(input)),
                "checkDigit" => GS1.CalculateCheckDigit(input),
                "validateCheckDigit" => GS1.ValidateCheckDigit(input),
                "gtinDigit" => GS1.CalculateGtinCheckDigit(input),
                "gtinAppend" => GS1.AppendGtinCheckDigit(input),
                "gtinValidate" => GS1.ValidateGtinCheckDigit(input),
                "ssccDigit" => GS1.CalculateSsccCheckDigit(input),
                "ssccAppend" => GS1.AppendSsccCheckDigit(input),
                "ssccValidate" => GS1.ValidateSsccCheckDigit(input),
                "human" => GS1.ParseHumanReadable(input),
                "raw" => GS1.ParseElementString(input),
                "create" => GS1.CreateElementString(elements),
                "validateElements" => Validation(GS1.ValidateElements(elements, validation)),
                "validateRaw" => Validation(GS1.ValidateElementString(input, validation)),
                "linkCreate" => GS1.CreateDigitalLink(elements, new GS1DigitalLinkOptions(Option("baseUrl") ?? "")
                {
                    PrimaryAi = Option("primaryAi") ?? "01",
                    PathAis = opts.TryGetProperty("pathAis", out var paths) ? paths.EnumerateArray().Select(x => x.GetString()!).ToArray() : null
                }),
                "linkParse" => GS1.ParseDigitalLink(input, new() { PrimaryAi = Option("primaryAi"), UnknownQuery = unknown }),
                "linkValidate" => LinkValidation(GS1.ValidateDigitalLink(input, new() { PrimaryAi = Option("primaryAi"), UnknownQuery = unknown, Normalize = Flag("normalize") })),
                "linkNormalize" => GS1.NormalizeDigitalLink(input, new() { PrimaryAi = Option("primaryAi"), UnknownQuery = unknown, Mode = Option("mode") ?? "specqr-deterministic" }),
                _ => throw new InvalidOperationException("Unknown GS1 fixture operation " + op)
            };
        }
        catch (SpecQRException ex) { return new { throws = new { code = ex.Code, message = ex.Message } }; }
    }

    private static object? Info(GS1AiInfo? info) => info is null ? null : new
    {
        info.Ai, info.Label,
        length = info.Length.IsVariable ? (object)new { type = "variable", min = info.Length.Min, max = info.Length.Max } : new { type = "fixed", exact = info.Length.Exact },
        valueKind = info.ValueKind.ToString().ToLowerInvariant(),
        checkDigitRule = info.CheckDigitRule.ToString().ToLowerInvariant(),
        digitalLinkRole = info.DigitalLinkRole switch { GS1DigitalLinkRole.PrimaryKey => "primary-key", GS1DigitalLinkRole.KeyQualifier => "key-qualifier", GS1DigitalLinkRole.DataAttribute => "data-attribute", _ => "not-supported" },
        info.DigitalLinkPathForPrimary, info.Separator
    };
    private static object Validation(GS1ValidationResult value) => value.Ok
        ? new { ok = true, value.Elements, value.HasSeparators, value.Warnings }
        : new { ok = false, value.Errors, value.Warnings };
    private static object LinkValidation(GS1DigitalLinkValidationResult value) => value.Ok
        ? new { ok = true, value.Result, value.Warnings }
        : new { ok = false, value.Errors, value.Warnings };

    private static int ResourceAndSafetyTests()
    {
        int count = 0;
        void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); count++; }
        void Throws(Action action, string message)
        {
            try { action(); } catch (SpecQRException ex) { Assert(ex.Code == "INVALID_GS1", message + " error code"); return; }
            throw new InvalidOperationException(message + " did not reject invalid data");
        }
        var elements = new[] { new GS1Element("01", "04912345678904"), new GS1Element("10", "100%") };
        string raw = GS1.CreateElementString(elements);
        Assert(raw == "010491234567890410100%", "literal percent preserved in raw GS1");
        Assert(GS1.ParseElementString(raw).Elements.SequenceEqual(elements), "literal percent raw round trip");
        foreach (string dot in new[] { ".", ".." })
        {
            var dots = new[] { elements[0], new GS1Element("10", dot) };
            Throws(() => GS1.CreateDigitalLink(dots, new("https://example.com")), "dot path safety");
            var link = GS1.CreateDigitalLink(dots, new("https://example.com") { PathAis = [] });
            Assert(GS1.ParseDigitalLink(link).Elements.SequenceEqual(dots), "dot query payload preserved");
        }
        var escaped = new[] { elements[0], new GS1Element("10", "%2e") };
        Assert(GS1.ParseDigitalLink(GS1.CreateDigitalLink(escaped, new("https://example.com"))).Elements.SequenceEqual(escaped), "literal percent escape round trip");
        foreach (string bad in new[] { "https://example.com/01/04912345678904/10/%C0%AF", "https://example.com/01/04912345678904/10/%ED%A0%80", "https://example.com/01/04912345678904/10/%F4%90%80%80" })
            Assert(!GS1.ValidateDigitalLink(bad).Ok, "invalid UTF-8 rejected");
        Assert(!GS1.ValidateElements(null!).Ok, "null elements validator is nonthrowing");
        Assert(!GS1.ValidateElements(new GS1Element[] { null! }).Ok, "null element validator is nonthrowing");
        Assert(!GS1.ValidateElements([new("10", null!)]).Ok, "null value validator is nonthrowing");
        Assert(!GS1.ValidateElements([new(null!, "A")]).Ok, "null AI validator is nonthrowing");
        Assert(!GS1.ValidateDigitalLink((string)null!).Ok, "null URL validator is nonthrowing");
        Assert(!GS1.ValidateElementString(null!).Ok, "null raw validator is nonthrowing");
        string large = new('1', GS1.MaxInputCharacters + 1);
        Throws(() => GS1.ParseElementString(large), "raw resource bound");
        Throws(() => GS1.ParseHumanReadable(large), "human resource bound");
        Throws(() => GS1.CalculateCheckDigit(large), "check digit resource bound");
        Assert(!GS1.ValidateElementString(large).Ok, "raw validator resource bound");
        Assert(!GS1.ValidateElements([new(large, "A")]).Ok, "AI resource bound");
        Assert(!GS1.ValidateElements([new("10", large)]).Ok, "value resource bound");
        Assert(!GS1.ValidateDigitalLink("https://example.com/" + large).Ok, "URI validator resource bound");
        var tooMany = Enumerable.Repeat(new GS1Element("20", "00"), GS1.MaxElements + 1);
        Throws(() => GS1.CreateElementString(tooMany), "enumerable resource bound");
        Assert(!GS1.ValidateElements(tooMany).Ok, "enumerable validator resource bound");
        IEnumerable<GS1Element> Infinite() { while (true) yield return new("20", "00"); }
        Assert(!GS1.ValidateElements(Infinite()).Ok, "infinite enumerable bounded");
        string manyRaw = string.Concat(Enumerable.Repeat("2000", GS1.MaxElements + 1));
        Assert(!GS1.ValidateElementString(manyRaw).Ok, "parsed element count bound");
        string linkQuery = "https://example.com/01/04912345678904?" + string.Join("&", Enumerable.Repeat("x=1", GS1.MaxElements + 1));
        Assert(!GS1.ValidateDigitalLink(linkQuery).Ok, "query count bound");
        string linkPath = "https://example.com/" + string.Join("/", Enumerable.Repeat("stem", GS1.MaxElements + 1));
        Assert(!GS1.ValidateDigitalLink(linkPath).Ok, "path count bound");
        Assert(GS1.GetSupportedAis().Count == 50 && GS1.GetAiInfo("3106") is null && GS1.GetAiInfo("90") is null, "catalog boundary");
        Assert(GS1.ValidateElements([new("17", "999999"), new("414", "1234567890123")]).Ok, "bounded date and GLN semantics");
        Assert(!GS1.ValidateElements(elements, new() { Context = (GS1ValidationContext)99 }).Ok, "invalid validation enum");
        Assert(!GS1.ValidateDigitalLink("https://example.com/01/04912345678904", new() { UnknownQuery = (GS1UnknownQueryPolicy)99 }).Ok, "invalid query enum");
        Parallel.For(0, 200, n =>
        {
            string body = n.ToString("D13", System.Globalization.CultureInfo.InvariantCulture);
            string gtin = GS1.AppendGtinCheckDigit(body);
            if (!GS1.ValidateGtinCheckDigit(gtin)) throw new InvalidOperationException("concurrent GS1 check digit mismatch");
            string link = GS1.CreateDigitalLink([new("01", gtin), new("10", "100%")], new("https://example.com"));
            if (GS1.NormalizeDigitalLink(link) != link) throw new InvalidOperationException("concurrent Digital Link mismatch");
        });
        count += 200;
        return count;
    }
    private static void Equal<T>(T expected, T actual, string context)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"{context}: expected {expected}, actual {actual}"); }
}
