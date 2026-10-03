using System.Text.Json;
using SpecQR;

internal static class StructuredAppendTests
{
    public static int Run()
    {
        using var fixtures=JsonDocument.Parse(File.ReadAllBytes(CoreTests.Fixture("structured-append-differential.json")));var count=0;
        foreach(var c in fixtures.RootElement.GetProperty("cases").EnumerateArray()) {
            var id=c.GetProperty("id").GetString()!;var expected=c.GetProperty("expected");var options=new QRStructuredAppendOptions{QrOptions=Protocol.Options(c),MaxSymbols=Protocol.Int(c.GetProperty("options"),"maxSymbols",16),Diagnostics=true,SplitUnits=c.GetProperty("type").GetString()=="manual"?QRStructuredAppendSplitUnitsDetail.Full:QRStructuredAppendSplitUnitsDetail.Summary};
            var result=c.GetProperty("type").GetString() switch {
                "manual"=>QRCode.GenerateSegmentsStructuredAppend(Protocol.Segments(c),options),"binary"=>QRCode.GenerateStructuredAppend(Protocol.Bytes(c.GetProperty("bytes")),options),_=>QRCode.GenerateStructuredAppend(c.GetProperty("text").GetString()!,options)
            };
            Check.Equal(expected.GetProperty("total").GetInt32(),result.Total,id+" total");Check.Equal(expected.GetProperty("parity").GetByte(),result.Parity,id+" parity");
            Check.Equal(expected.GetProperty("inputLength").GetInt32(),result.InputLength,id+" input length");Check.Equal(expected.GetProperty("byteLength").GetInt32(),result.ByteLength,id+" byte length");
            var matrices=expected.GetProperty("matrices").EnumerateArray().ToArray();
            for(var i=0;i<result.Total;i++) {
                Check.Sequence(matrices[i].EnumerateArray().Select(x=>x.GetString()),Protocol.Rows(result.Symbols[i]),id+" matrix "+i);
                var ds=expected.GetProperty("diagnostics").GetProperty("symbols")[i];var actual=result.Diagnostics.Symbols[i];
                Check.Equal(ds.GetProperty("dataBitLength").GetInt32(),actual.DataBitLength,id+" symbol bits");Check.Equal(ds.GetProperty("byteStart").GetInt32(),actual.ByteStart,id+" byte start");Check.Equal(ds.GetProperty("byteLength").GetInt32(),actual.ByteLength,id+" symbol bytes");
                Check.Equal(i+1,actual.Index,id+" index");Check.Equal((i<<4)|(result.Total-1),actual.SequenceIndicator,id+" sequence");
            }
            if(c.GetProperty("type").GetString()=="manual") {Check.True(result.Diagnostics.SplitUnits is not null,"full split units retained");Check.Equal(expected.GetProperty("diagnostics").GetProperty("splitUnitCount").GetInt32(),result.Diagnostics.SplitUnitCount,id+" split count");}
            count++;
        }
        var parity=QRCode.CalculateStructuredAppendParity("ABCD");
        var merged=QRCode.MergeStructuredAppendParts([new(2,2,parity,"CD"),new(1,2,parity,"AB")]);Check.Equal("ABCD",merged.Text,"merge ordering");Check.True(merged.Diagnostics.ParityCheck.Matches,"merge parity");
        var binaryParity=QRCode.CalculateStructuredAppendParity(new byte[]{0,255,128,29});var bin=QRCode.MergeStructuredAppendParts([new(2,2,binaryParity,new byte[]{128,29}),new(1,2,binaryParity,new byte[]{0,255})]);
        Check.Sequence(new byte[]{0,255,128,29},bin.Bytes,"merge binary");Check.True(bin.Text is null,"binary no implicit UTF8");var mutable=bin.Bytes;mutable[0]=42;Check.Equal((byte)0,bin.Bytes[0],"merge immutable");
        Check.Throws(()=>QRCode.MergeStructuredAppendParts([new(1,2,parity,"AB")]),"merge missing");Check.Throws(()=>QRCode.MergeStructuredAppendParts([new(1,2,parity,"AB"),new(1,2,parity,"CD")]),"merge duplicate");
        Check.Throws(()=>QRCode.MergeStructuredAppendParts([new(1,2,0,"AB"),new(2,2,0,"CD")]),"merge bad parity");
        Check.Throws(()=>QRCode.MergeStructuredAppendParts([new(1,2,parity,"AB"),new(2,2,parity,new byte[]{67,68})]),"merge mixed types");
        Check.Throws(()=>QRCode.GenerateStructuredAppend("a",new(){MaxSymbols=1}),"invalid maximum count");
        Check.Throws(()=>QRCode.GenerateStructuredAppend("a",new(){QrOptions=new(){Gs1=true}}),"SA plus GS1");
        Check.Throws(()=>QRCode.GenerateSegmentsStructuredAppend([QRSegment.Eci(26),QRSegment.Byte("hello")]),"SA manual control rejection");
        var summary=QRCode.GenerateSegmentsStructuredAppend([QRSegment.Byte(new string('x',100))],new(){QrOptions=new(){Version=1}});Check.True(summary.Diagnostics.SplitUnits is null,"summary split units omitted");
        return count+12;
    }
}
