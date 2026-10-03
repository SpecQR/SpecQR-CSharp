using System.Text.Json;
using SpecQR;

internal static class Protocol
{
    internal static QROptions Options(JsonElement parent)
    {
        if (!parent.TryGetProperty("options", out var e)) return new();
        return new() {
            Version = NullableInt(e,"version"), MinVersion = Int(e,"minVersion",1), MaxVersion = Int(e,"maxVersion",40),
            MaskPattern = NullableInt(e,"maskPattern"), ErrorCorrectionLevel = Enum.Parse<ErrorCorrectionLevel>(String(e,"errorCorrectionLevel","M")),
            Mode = Mode(String(e,"mode","auto")), OptimizeSegments = Bool(e,"optimizeSegments",true), BoostErrorCorrection = Bool(e,"boostErrorCorrection",false),
            Eci = e.TryGetProperty("eci",out var eci) && eci.ValueKind == JsonValueKind.True ? 26 : NullableInt(e,"eci"),
            Gs1 = Bool(e,"gs1",false), Fnc1Second = e.TryGetProperty("fnc1Second",out var second) && second.ValueKind == JsonValueKind.String ? second.GetString() : null,
            StructuredAppend = e.TryGetProperty("structuredAppend",out var header) && header.ValueKind == JsonValueKind.Object
                ? new(Int(header,"index",0),Int(header,"total",0),(byte)Int(header,"parity",0)) : null
        };
    }
    internal static QRMode Mode(string mode) => mode switch {
        "eci" => QRMode.Eci, "fnc1" => QRMode.Fnc1, "fnc1-second" => QRMode.Fnc1Second,
        "structured-append" => QRMode.StructuredAppend, _ => Enum.Parse<QRMode>(mode,true)
    };
    internal static QRSegment[] Segments(JsonElement request) => request.GetProperty("segments").EnumerateArray().Select(Segment).ToArray();
    private static QRSegment Segment(JsonElement s)
    {
        var text=String(s,"text",s.TryGetProperty("data",out var data) && data.ValueKind==JsonValueKind.String ? data.GetString()! : "");
        return String(s,"mode","") switch {
            "numeric" => QRSegment.Numeric(text), "alphanumeric" => QRSegment.Alphanumeric(text), "kanji" => QRSegment.Kanji(text),
            "byte" => s.TryGetProperty("bytes",out var bytes) ? QRSegment.Byte(Bytes(bytes)) : s.TryGetProperty("data",out data) && data.ValueKind==JsonValueKind.Array ? QRSegment.Byte(Bytes(data)) : QRSegment.Byte(text),
            "eci" => QRSegment.Eci(Int(s,"assignmentNumber",0)), "fnc1" => QRSegment.Fnc1(),
            "fnc1-second" => QRSegment.Fnc1Second(String(s,"applicationIndicator","")),
            "structured-append" => QRSegment.StructuredAppend(Int(s,"index",0),Int(s,"total",0),(byte)Int(s,"parity",0)),
            _ => throw new ArgumentException("Unknown test segment mode.")
        };
    }
    internal static byte[] Bytes(JsonElement e) => e.EnumerateArray().Select(x=>x.GetByte()).ToArray();
    internal static int Int(JsonElement e,string name,int defaultValue) => e.TryGetProperty(name,out var x) && x.ValueKind==JsonValueKind.Number ? x.GetInt32() : defaultValue;
    internal static int? NullableInt(JsonElement e,string name) => e.TryGetProperty(name,out var x) && x.ValueKind==JsonValueKind.Number ? x.GetInt32() : null;
    internal static string String(JsonElement e,string name,string defaultValue) => e.TryGetProperty(name,out var x) && x.ValueKind==JsonValueKind.String ? x.GetString()! : defaultValue;
    internal static bool Bool(JsonElement e,string name,bool defaultValue) => e.TryGetProperty(name,out var x) && x.ValueKind is JsonValueKind.True or JsonValueKind.False ? x.GetBoolean() : defaultValue;
    internal static QRCode Generate(JsonElement r) => r.TryGetProperty("segments",out _) ? QRCode.GenerateSegments(Segments(r),Options(r)) : r.TryGetProperty("bytes",out var b) ? QRCode.Generate(Bytes(b),Options(r)) : QRCode.Generate(String(r,"text",""),Options(r));
    internal static string[] Rows(QRCode qr) => qr.Matrix.Select(row=>string.Concat(row.Select(x=>x?'1':'0'))).ToArray();
    internal static object Execute(JsonElement r)
    {
        if(String(r,"command","")=="identity")
            return new { framework=AppContext.TargetFrameworkName,runtime=Environment.Version.ToString(),testAssembly=typeof(Protocol).Assembly.GetName().Name,library=typeof(QRCode).Assembly.GetName().Name };
        if(String(r,"command","")=="structured-append") {
            var options=new QRStructuredAppendOptions { QrOptions=Options(r),MaxSymbols=r.TryGetProperty("options",out var o)?Int(o,"maxSymbols",16):16,Diagnostics=true };
            var result=r.TryGetProperty("segments",out _) ? QRCode.GenerateSegmentsStructuredAppend(Segments(r),options) : r.TryGetProperty("bytes",out var b) ? QRCode.GenerateStructuredAppend(Bytes(b),options) : QRCode.GenerateStructuredAppend(String(r,"text",""),options);
            return new { total=result.Total,parity=result.Parity,symbols=result.Symbols.Select(q=>new {
                matrix=Rows(q),version=q.Version,maskPattern=q.MaskPattern,dataCodewords=q.DataCodewords,
                errorCorrectionLevel=q.Planning.ErrorCorrectionLevel.ToString(),
                png=r.TryGetProperty("png",out var saPng)&&saPng.GetBoolean()?Convert.ToBase64String(q.ToPng()):null
            }) };
        }
        var qr=Generate(r);
        return new { matrix=Rows(qr),dataCodewords=qr.DataCodewords,codewords=qr.Codewords,version=qr.Version,maskPattern=qr.MaskPattern,
            errorCorrectionLevel=qr.Planning.ErrorCorrectionLevel.ToString(),png=r.TryGetProperty("png",out var png)&&png.GetBoolean()?Convert.ToBase64String(qr.ToPng()):null };
    }
}
