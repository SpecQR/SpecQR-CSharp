using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using SpecQR;

internal static class CoreTests
{
    internal static string Fixture(string name)=>Path.Combine(AppContext.BaseDirectory,"Fixtures",name);
    private static JsonDocument ReadCore() { using var file=File.OpenRead(Fixture("core.json.gz"));using var gzip=new GZipStream(file,CompressionMode.Decompress);return JsonDocument.Parse(gzip); }
    public static int Integrity()
    {
        using var manifest=JsonDocument.Parse(File.ReadAllBytes(Fixture("manifest.json")));
        Check.Equal("15ad15e5c770ea0e39072f8f88b2733018f02ffd",manifest.RootElement.GetProperty("source").GetProperty("commit").GetString(),"source commit");
        var count=0;foreach(var f in manifest.RootElement.GetProperty("files").EnumerateArray()) { var name=f.GetProperty("file").GetString()!;Check.Equal(f.GetProperty("sha256").GetString(),Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Fixture(name)))).ToLowerInvariant(),name);count++; }
        return count;
    }
    public static int Matrices()
    {
        using var fixtures=ReadCore();var count=0;
        foreach(var c in fixtures.RootElement.GetProperty("cases").EnumerateArray()) {
            var id=c.GetProperty("id").GetString()!;var expected=c.GetProperty("expected");var qr=Protocol.Generate(c);var d=expected.GetProperty("diagnostics");
            Check.Sequence(expected.GetProperty("matrix").EnumerateArray().Select(x=>x.GetString()),Protocol.Rows(qr),id+" matrix");
            Check.Sequence(Convert.FromBase64String(expected.GetProperty("dataCodewords").GetString()!),qr.DataCodewords,id+" data codewords");
            Check.Sequence(Convert.FromBase64String(expected.GetProperty("codewords").GetString()!),qr.Codewords,id+" interleaved codewords");
            Check.Equal(d.GetProperty("version").GetInt32(),qr.Version,id+" version");
            Check.Equal(d.GetProperty("maskPattern").GetInt32(),qr.MaskPattern,id+" mask");
            Check.Equal(d.GetProperty("maskPenalty").GetInt32(),qr.Diagnostics.MaskPenalty,id+" penalty");
            Check.Sequence(d.GetProperty("maskPenalties").EnumerateArray().Select(x=>(x.GetProperty("maskPattern").GetInt32(),x.GetProperty("penalty").GetInt32())),qr.Diagnostics.MaskPenalties.Select(x=>(x.MaskPattern,x.Penalty)),id+" penalties");
            ComparePlan(qr.Planning,d,id);
            Check.Sequence(d.GetProperty("segments").EnumerateArray().Select(x=>(Protocol.Mode(x.GetProperty("mode").GetString()!),x.GetProperty("characterCount").GetInt32(),x.GetProperty("byteCount").GetInt32(),x.GetProperty("bitLength").GetInt32())),qr.Planning.SegmentDiagnostics.Select(x=>(x.Mode,x.CharacterCount,x.ByteCount,x.BitLength)),id+" segments");
            count++;
        }
        return count;
    }
    private static void ComparePlan(QRPlan actual,JsonElement expected,string id)
    {
        Check.Equal(expected.GetProperty("errorCorrectionLevel").GetString(),actual.ErrorCorrectionLevel.ToString(),id+" ECC");
        Check.Equal(expected.GetProperty("dataBitLength").GetInt32(),actual.DataBitLength,id+" data bits");
        Check.Equal(expected.GetProperty("capacityBits").GetInt32(),actual.CapacityBits,id+" capacity bits");
        Check.Equal(expected.GetProperty("remainingBits").GetInt32(),actual.RemainingBits,id+" remaining bits");
        Check.Equal(expected.GetProperty("inputBytes").GetInt32(),actual.InputBytes,id+" input bytes");
        Check.Equal(Protocol.Mode(expected.GetProperty("mode").GetString()!),actual.Mode,id+" mode");
    }
    public static int Planning()
    {
        using var fixtures=ReadCore();var count=0;
        foreach(var c in fixtures.RootElement.GetProperty("capacities").EnumerateArray()) {
            var o=Protocol.Options(c);var e=c.GetProperty("expected");var capacity=QRCode.GetCapacity(o.Version!.Value,o.ErrorCorrectionLevel,o.Mode);
            Check.Equal(e.GetProperty("capacityBits").GetInt32(),capacity.CapacityBits,"capacity bits");
            Check.Equal(Protocol.NullableInt(e,"maxCharacters"),capacity.MaxCharacters,"capacity characters");
            Check.Equal(Protocol.NullableInt(e,"maxBytes"),capacity.MaxBytes,"capacity bytes");
            Check.Equal(e.GetProperty("characterCountBits").GetInt32(),capacity.CharacterCountBits,"count bits");count++;
        }
        foreach(var c in fixtures.RootElement.GetProperty("estimates").EnumerateArray()) {
            var options=Protocol.Options(c);var e=c.GetProperty("expected");var text=string.Concat(Enumerable.Repeat(c.GetProperty("character").GetString(),c.GetProperty("count").GetInt32()));
            var plan=QRCode.Estimate(text,options);var id=$"estimate v{options.Version} {options.ErrorCorrectionLevel} {options.Mode} length {text.Length}";
            Check.Equal(e.GetProperty("ok").GetBoolean(),plan.Ok,id+" ok");Check.Equal(Protocol.NullableInt(e,"selectedVersion"),plan.SelectedVersion,id+" selected");
            ComparePlan(plan,e,id);Check.True(!plan.MaskEvaluated&&!plan.CodewordsBuilt&&!plan.RenderPlanned,id+" planning purity");count++;
        }
        return count;
    }
}
