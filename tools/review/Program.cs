using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using SpecQR;

// Reviewer-owned checks, deliberately separate from the author's conformance harness.
var assembly = typeof(QRCode).Assembly;
var expectedIndex = Array.IndexOf(args, "--expected-framework");
if (expectedIndex >= 0)
{
    if (expectedIndex + 1 >= args.Length) throw new ArgumentException("Missing expected framework.");
    var expectedMajor = args[expectedIndex + 1] switch { "net8.0" => 8, "net10.0" => 10, _ => throw new ArgumentException("Unsupported expected framework.") };
    if (AppContext.TargetFrameworkName != $".NETCoreApp,Version=v{expectedMajor}.0" || Environment.Version.Major != expectedMajor)
        throw new InvalidOperationException($"Framework identity mismatch: expected {args[expectedIndex + 1]}, target {AppContext.TargetFrameworkName}, runtime {Environment.Version}.");
}
Console.WriteLine(JsonSerializer.Serialize(new { framework = AppContext.TargetFrameworkName, runtime = RuntimeInformation.FrameworkDescription,
    librarySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))).ToLowerInvariant() }));
var checks = 0;
void Assert(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException(message); }
void Reject(Action action, string message) { checks++; try { action(); } catch (ArgumentException) { return; } throw new InvalidOperationException(message); }

// Large byte segments must split before count fields are emitted.
var bytes = Enumerable.Range(0, 300).Select(i => (byte)i).ToArray();
var manualOptions = new QRStructuredAppendOptions { QrOptions = new() { Version = 2, ErrorCorrectionLevel = ErrorCorrectionLevel.L } };
var summary = QRCode.GenerateSegmentsStructuredAppend([QRSegment.Byte(bytes)], manualOptions);
Assert(summary.Total == 10, "300 bytes at V2-L should split into ten 30-byte symbols");
Assert(summary.Diagnostics.SplitUnits is null && summary.Diagnostics.SplitUnitCount == 300, "Summary must remain compact");
var full = QRCode.GenerateSegmentsStructuredAppend([QRSegment.Byte(bytes)], manualOptions with { SplitUnits = QRStructuredAppendSplitUnitsDetail.Full });
Assert(full.Diagnostics.SplitUnits?.Count == 300, "Full detail should contain every byte unit");
for (int i = 0; i < summary.Total; i++) Assert(summary.Symbols[i].Codewords.SequenceEqual(full.Symbols[i].Codewords), "Detail policy must not alter symbols");

// V1-L has 152 data bits: after SA+mode+count headers these homogeneous
// modes fit exactly 35 digits, 21 letters, 15 bytes, or 9 Kanji scalars.
foreach (var (character, perSymbol) in new[] { ("1", 35), ("A", 21), ("a", 15), ("漢", 9) })
foreach (int length in new[] { perSymbol + 1, perSymbol + 2, perSymbol * 2, perSymbol * 2 + 1, perSymbol * 3 + 1 })
{
    string text = string.Concat(Enumerable.Repeat(character, length));
    var result = QRCode.GenerateStructuredAppend(text, new() { QrOptions = new() { Version = 1, ErrorCorrectionLevel = ErrorCorrectionLevel.L } });
    Assert(result.Total == (length + perSymbol - 1) / perSymbol, "Homogeneous V1-L symbol count");
    Assert(result.Diagnostics.Symbols.Take(result.Total - 1).All(s => s.InputLength == perSymbol), "Homogeneous V1-L chunk boundary");
}

// The SA incremental tracker must agree with public, fully materialized plans.
var random = new Random(733197);
var alphabet = new[] { "0", "1", "A", "%", "a", "é", "漢", "字", "😀" };
var splitCases = 0;
foreach (int version in new[] { 1, 2, 4, 9, 10, 26, 27, 40 })
foreach (bool optimize in new[] { true, false })
for (int iteration = 0; iteration < 5; iteration++)
{
    var text = string.Concat(Enumerable.Range(0, version <= 4 ? 60 + random.Next(20) : version <= 10 ? 500 : 2600).Select(_ => alphabet[random.Next(alphabet.Length)]));
    var runes = text.EnumerateRunes().Select(r => r.ToString()).ToArray();
    var qrOptions = new QROptions { Version = version, ErrorCorrectionLevel = ErrorCorrectionLevel.L, OptimizeSegments = optimize };
    var withHeader = qrOptions with { StructuredAppend = new(1, 2, 0) };
    var whole = QRCode.Estimate(text, withHeader);
    if (whole.Ok) { Reject(() => QRCode.GenerateStructuredAppend(text, new() { QrOptions = qrOptions }), "A one-symbol SA request must fail"); continue; }
    var result = QRCode.GenerateStructuredAppend(text, new() { QrOptions = qrOptions });
    splitCases++;
    Assert(result.Total is >= 2 and <= 16, "SA symbol bounds");
    Assert(result.ByteLength == Encoding.UTF8.GetByteCount(text), "SA canonical bytes");
    var consumed = 0;
    foreach (var diagnostic in result.Diagnostics.Symbols)
    {
        var length = diagnostic.InputLength!.Value;
        Assert(diagnostic.InputStart == consumed, "Contiguous scalar ranges");
        var chunk = string.Concat(runes.Skip(consumed).Take(length));
        var plan = QRCode.Estimate(chunk, qrOptions with { StructuredAppend = new(diagnostic.Index, result.Total, result.Parity) });
        Assert(plan.Ok && plan.DataBitLength == diagnostic.DataBitLength, "Tracker and materialized plan must agree");
        if (consumed + length < runes.Length)
            Assert(!QRCode.Estimate(chunk + runes[consumed + length], withHeader).Ok, "Every non-final chunk must be a largest-fitting prefix");
        consumed += length;
    }
    Assert(consumed == runes.Length, "SA preserves all scalars");
}

// Output and input mutation must not alter retained code/segment/set state.
var original = new byte[] { 1, 2, 3, 4 };
var segment = QRSegment.Byte(original); original[0] = 9;
Assert(segment.Bytes![0] == 1, "Segment owns its input");
var segmentView = segment.Bytes!; segmentView[0] = 8;
Assert(segment.Bytes![0] == 1, "Segment owns its output");
var code = QRCode.Generate("Review 123 漢😀");
var matrix = code.Matrix; var first = matrix[0][0]; matrix[0][0] = !first;
Assert(code.Matrix[0][0] == first, "Matrix rows are isolated");
var words = code.Codewords; var firstWord = words[0]; words[0] ^= 255;
Assert(code.Codewords[0] == firstWord, "Codewords are isolated");
var dataWords = code.DataCodewords; var firstDataWord = dataWords[0]; dataWords[0] ^= 255;
Assert(code.DataCodewords[0] == firstDataWord, "Data codewords are isolated");
Parallel.For(0, 32, _ => { if (!QRCode.Generate("Review 123 漢😀").Codewords.SequenceEqual(code.Codewords)) throw new InvalidOperationException("Concurrent generation changed codewords"); }); checks += 32;

// High-level percent safety and explicit low-level escaping have different contracts.
foreach (bool optimized in new[] { false, true })
foreach (bool gs1 in new[] { false, true })
foreach (string text in new[] { "10ABC%DEF", "10ABC%%DEF", "10LOT%\u001d21SER%IAL" })
{
    var options = new QROptions { Gs1 = gs1, Fnc1Second = gs1 ? null : "A", OptimizeSegments = optimized };
    Assert(QRCode.Generate(text, options).Codewords.SequenceEqual(QRCode.Generate(text, options with { Mode = QRMode.Byte }).Codewords), "High-level literal percent must use byte-equivalent payload");
    Reject(() => QRCode.Generate(text, options with { Mode = QRMode.Alphanumeric }), "Unsafe explicit high-level alphanumeric must reject");
    if (!text.Contains('\u001d')) Reject(() => QRCode.GenerateSegments([QRSegment.Alphanumeric(text)], options), "Manual alphanumeric plus high-level FNC1 must reject");
}
Assert(QRCode.GenerateSegments([QRSegment.Fnc1(), QRSegment.Alphanumeric("10ABC%%DEF")]).Planning.Fnc1 == "first-position", "Low-level FNC1 retains explicit escaping");

// A URL path must not silently remove a valid GS1 qualifier payload.
foreach (string dot in new[] { ".", ".." })
{
    GS1Element[] elements = [new("01", "09501101530003"), new("10", dot)];
    Reject(() => GS1.CreateDigitalLink(elements, new GS1DigitalLinkOptions("https://example.com")), "Dot-only path qualifier must reject instead of losing data");
    var url = GS1.CreateDigitalLink(elements, new GS1DigitalLinkOptions("https://example.com") { PathAis = Array.Empty<string>() });
    Assert(GS1.ParseDigitalLink(url).Elements.Single(e => e.Ai == "10").Value == dot, "Dot-only query qualifier must preserve data");
}

// Validate all render options before touching the caller's stream.
foreach (var invalid in new[] {
    new QRRenderOptions { Scale = int.MaxValue }, new QRRenderOptions { Margin = int.MaxValue },
    new QRRenderOptions { Width = 1 }, new QRRenderOptions { Scale = 1000 },
    new QRRenderOptions { Foreground = "url(https://example.com)" }, new QRRenderOptions { Title = "bad\u0000title" },
    new QRRenderOptions { PrintDpi = double.NaN }, new QRRenderOptions { PrintDpi = double.Epsilon }
})
{
    using var output = new MemoryStream();
    try { code.SavePng(output, invalid); throw new InvalidOperationException("Invalid render options accepted"); }
    catch (Exception e) when (e is ArgumentException or System.Xml.XmlException) { }
    Assert(output.Length == 0, "Invalid options must not partially write PNG");
}
var svg = XDocument.Parse(code.ToSvg(new() { Title = "<review> & \"text\"" }));
Assert(svg.Root!.Element(XName.Get("title", "http://www.w3.org/2000/svg"))!.Value == "<review> & \"text\"", "SVG title escaping");

// Malformed user-created metadata must be inspectable without arithmetic traps.
foreach (int invalid in new[] { int.MinValue, -1, 0, 17, int.MaxValue })
{
    var header = new QRStructuredAppendHeader(invalid, 16, 0);
    Assert(!header.IsValid && header.SequenceIndex is null && header.SequenceTotal is null && header.SequenceIndicator is null, "Invalid header getters are safe");
}
Console.WriteLine(JsonSerializer.Serialize(new { status = "passed", checks, structuredAppendRandomCases = splitCases, skipped = 0 }));
