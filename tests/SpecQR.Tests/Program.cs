using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using SpecQR;

if (args.Contains("--json-lines", StringComparer.Ordinal))
{
    string? line;
    while ((line = Console.ReadLine()) is not null)
    {
        try { using var request = JsonDocument.Parse(line); Console.WriteLine(JsonSerializer.Serialize(Protocol.Execute(request.RootElement))); }
        catch (Exception error) { Console.WriteLine(JsonSerializer.Serialize(new { error = error.Message, code = (error as SpecQRException)?.Code ?? error.GetType().Name })); }
    }
    return 0;
}

var target = AppContext.TargetFrameworkName ?? "unknown";
var expectedIndex = Array.IndexOf(args, "--expected-framework");
if (expectedIndex >= 0)
{
    if (expectedIndex + 1 >= args.Length) throw new ArgumentException("Expected framework value is missing.");
    var expected = args[expectedIndex + 1];
    var expectedMajor = expected switch { "net8.0" => 8, "net10.0" => 10, _ => throw new ArgumentException("Unsupported expected framework.") };
    if (target != $".NETCoreApp,Version=v{expectedMajor}.0" || Environment.Version.Major != expectedMajor)
        throw new InvalidOperationException($"Framework identity mismatch: expected {expected}, target {target}, runtime {Environment.Version}.");
}
var library = typeof(QRCode).Assembly;
var identity = new
{
    framework = target, runtime = RuntimeInformation.FrameworkDescription,
    operatingSystem = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    testAssembly = Assembly.GetExecutingAssembly().GetName().Name,
    library = library.GetName().Name, version = library.GetName().Version?.ToString(),
    librarySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(library.Location))).ToLowerInvariant()
};
Console.WriteLine(JsonSerializer.Serialize(new { identity }));
var timer = Stopwatch.StartNew();
var groups = new List<object>();
var failed = 0;
foreach (var (name, run) in new (string, Func<int>)[] {
    ("fixture-integrity", CoreTests.Integrity), ("exact-core-and-codewords", CoreTests.Matrices),
    ("capacity-and-planning-boundaries", CoreTests.Planning), ("negative-resource-mutation-concurrency", AdditionalTests.Core),
    ("portable-rendering", AdditionalTests.Rendering), ("structured-append", StructuredAppendTests.Run), ("gs1", GS1Tests.Run)
})
{
    var groupTimer = Stopwatch.StartNew();
    try { var count = run(); var result = new { name, status = "passed", cases = count, elapsedMilliseconds = groupTimer.ElapsedMilliseconds }; groups.Add(result); Console.WriteLine(JsonSerializer.Serialize(result)); }
    catch (Exception error) { failed++; var result = new { name, status = "failed", error = error.ToString() }; groups.Add(result); Console.Error.WriteLine(JsonSerializer.Serialize(result)); }
}
Console.WriteLine(JsonSerializer.Serialize(new { status = failed == 0 ? "passed" : "failed", groups, elapsedMilliseconds = timer.ElapsedMilliseconds,
    externalDecoder = "not-run-by-this-harness; run tools/verify-decode.py separately", skipped = 0 }));
return failed == 0 ? 0 : 1;

internal static class Check
{
    public static void Equal<T>(T expected, T actual, string context) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"{context}: expected {expected}, actual {actual}"); }
    public static void True(bool value, string context) { if (!value) throw new InvalidOperationException(context); }
    public static void Sequence<T>(IEnumerable<T> expected, IEnumerable<T> actual, string context) { if (!expected.SequenceEqual(actual)) throw new InvalidOperationException(context + ": sequence differs"); }
    public static void Throws(Action action, string context) { try { action(); } catch (ArgumentException) { return; } throw new InvalidOperationException(context + ": expected argument/capacity exception"); }
}
