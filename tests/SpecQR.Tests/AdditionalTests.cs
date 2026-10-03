using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using SpecQR;

internal static class AdditionalTests
{
    public static int Core()
    {
        var invalid=new QROptions[] {
            new(){Version=0},new(){Version=41},new(){MinVersion=10,MaxVersion=9},new(){MaskPattern=-1},new(){MaskPattern=8},
            new(){ErrorCorrectionLevel=(ErrorCorrectionLevel)99},new(){Mode=(QRMode)99},new(){Mode=QRMode.Eci},
            new(){Eci=-1},new(){Eci=1_000_000},new(){Gs1=true,Eci=26},new(){Gs1=true,Fnc1Second="A"},new(){Fnc1Second="A",Eci=26},
            new(){Fnc1Second=""},new(){Fnc1Second="1"},new(){Fnc1Second="AAA"},new(){Fnc1Second="あ"},
            new(){StructuredAppend=new(0,2,0)},new(){StructuredAppend=new(1,1,0)},new(){StructuredAppend=new(1,17,0)}
        };
        foreach(var o in invalid) { Check.Throws(()=>QRCode.Generate("x",o),"invalid options generate");Check.Throws(()=>QRCode.Estimate("x",o),"invalid options estimate"); }
        Check.Throws(()=>QRCode.Generate((string)null!),"null text");Check.Throws(()=>QRCode.Generate((byte[])null!),"null binary");
        Check.Throws(()=>QRCode.Generate("\ud800"),"unpaired high surrogate");Check.Throws(()=>QRCode.Generate("\udc00"),"unpaired low surrogate");
        Check.Throws(()=>QRCode.Generate("abc",new(){Mode=QRMode.Numeric}),"numeric alphabet");Check.Throws(()=>QRCode.Generate("abc",new(){Mode=QRMode.Alphanumeric}),"alpha alphabet");
        Check.Throws(()=>QRCode.Generate("😀",new(){Mode=QRMode.Kanji}),"Kanji range");
        Check.Throws(()=>QRCode.GenerateSegments([QRSegment.Byte("a"),QRSegment.Fnc1()]),"misplaced FNC1");
        Check.Throws(()=>QRCode.GenerateSegments([QRSegment.Fnc1(),QRSegment.Eci(26),QRSegment.Byte("a")]),"FNC1 plus ECI");
        Check.Throws(()=>QRCode.GenerateSegments([QRSegment.Fnc1(),QRSegment.Fnc1(),QRSegment.Byte("a")]),"duplicate control");
        Check.Throws(()=>QRCode.GenerateSegments([null!]),"null segment");
        foreach(var control in new QROptions[]{new(){Gs1=true},new(){Fnc1Second="A"}}) {
            Check.Throws(()=>QRCode.Generate("10ABC%DEF",control with{Mode=QRMode.Alphanumeric}),"unsafe explicit percent");
            Check.Throws(()=>QRCode.GenerateSegments([QRSegment.Alphanumeric("10ABC%DEF")],control),"unsafe segment percent via high-level option");
            foreach(var optimize in new[]{true,false}) {
                var actual=QRCode.Generate("10ABC%DEF",control with{OptimizeSegments=optimize});
                var expected=QRCode.Generate("10ABC%DEF",control with{Mode=QRMode.Byte});
                Check.Sequence(Protocol.Rows(expected),Protocol.Rows(actual),"literal percent retained");
            }
        }
        Check.Throws(()=>QRCode.Generate(new string('x',1_000_001)),"text resource cap");
        Check.Throws(()=>QRCode.Generate(new byte[1_000_001]),"binary resource cap");
        Check.Throws(()=>QRCode.GenerateSegments(Enumerable.Repeat(QRSegment.Byte(""),65_537)),"segment count resource cap");
        var large=QRCode.Estimate(new byte[100_000]);Check.True(!large.Ok&&large.OverflowBits>0,"overflow planning without encoding");
        Check.Throws(()=>QRCode.Generate(new byte[100_000]),"large overflow generation");
        var input=new byte[]{0,1,2,255};var segment=QRSegment.Byte(input);input[0]=99;
        Check.Equal((byte)0,segment.Bytes![0],"segment copies input");var accessed=segment.Bytes!;accessed[1]=99;Check.Equal((byte)1,segment.Bytes![1],"segment copies output");
        var qr=QRCode.GenerateSegments([segment]);var rows=Protocol.Rows(qr);var words=qr.Codewords;words[0]^=255;var data=qr.DataCodewords;data[0]^=255;var matrix=qr.Matrix;matrix[0][0]=!matrix[0][0];
        Check.Sequence(rows,Protocol.Rows(qr),"matrix deep defensive copy");Check.True(qr.Codewords[0]!=words[0]&&qr.DataCodewords[0]!=data[0],"codeword defensive copies");
        var concurrent=Enumerable.Range(0,64).Select(i=>new {Text=$"Thread {i} 漢字 {i*i}",Options=new QROptions{ErrorCorrectionLevel=(ErrorCorrectionLevel)(i%4),MaskPattern=i%8}}).ToArray();
        var expectedConcurrent=concurrent.Select(x=>Protocol.Rows(QRCode.Generate(x.Text,x.Options))).ToArray();
        Parallel.For(0,concurrent.Length,i=>{var c=concurrent[i];Check.Sequence(expectedConcurrent[i],Protocol.Rows(QRCode.Generate(c.Text,c.Options)),"concurrent generation");});
        var plan=QRCode.AnalyzeSegments([QRSegment.Eci(26),QRSegment.Byte("雪")]);Check.True(plan.Ok&&plan.EciAssignmentNumber==26&&!plan.MaskEvaluated,"manual planning control");
        // V9 uses an eight-bit byte count field; V10 widens to sixteen bits.
        Check.True(!QRCode.AnalyzeSegments([QRSegment.Byte(new byte[256])],new(){Version=9,ErrorCorrectionLevel=ErrorCorrectionLevel.L}).Ok,"byte count width at V9");
        Check.True(QRCode.AnalyzeSegments([QRSegment.Byte(new byte[256])],new(){Version=10,ErrorCorrectionLevel=ErrorCorrectionLevel.L}).Ok,"byte count width at V10");
        return invalid.Length*2+29+64;
    }
    public static int Rendering()
    {
        var qr=QRCode.Generate("PNG portable 漢字");var count=0;
        foreach(var options in new QRRenderOptions[]{new(),new(){Scale=1,Margin=0},new(){Scale=3,Margin=4,Foreground="#1358",Background="transparent"},new(){Scale=2,Foreground="#12345678",Background="#abcdef90"}}) {
            var rgba=qr.ToRgba(options);var png=qr.ToPng(options);Check.Sequence(new byte[]{137,80,78,71,13,10,26,10},png.Take(8),"PNG signature");
            using var idat=new MemoryStream();var offset=8;var seen=new List<string>();
            while(offset<png.Length) {
                var length=checked((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset,4)));var type=Encoding.ASCII.GetString(png,offset+4,4);
                Check.True(offset+12+length<=png.Length,"PNG chunk length");
                var crc=uint.MaxValue;foreach(var value in png.AsSpan(offset+4,length+4)) {crc^=value;for(var bit=0;bit<8;bit++)crc=(crc&1)!=0?(crc>>1)^0xedb88320:crc>>1;}
                Check.Equal(~crc,BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset+8+length,4)),"PNG CRC "+type);seen.Add(type);
                if(type=="IHDR") {Check.Equal(rgba.Width,(int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset+8,4)),"PNG width");Check.Equal((byte)8,png[offset+16],"PNG depth");Check.Equal((byte)6,png[offset+17],"PNG RGBA");}
                if(type=="IDAT")idat.Write(png,offset+8,length);offset+=length+12;
            }
            Check.Equal(png.Length,offset,"PNG no trailing bytes");Check.Sequence(new[]{"IHDR","IDAT","IEND"},seen,"PNG chunks");
            idat.Position=0;using var zlib=new ZLibStream(idat,CompressionMode.Decompress);using var decoded=new MemoryStream();zlib.CopyTo(decoded);var scanlines=decoded.ToArray();
            Check.Equal((rgba.Width*4+1)*rgba.Height,scanlines.Length,"PNG raw size");
            for(var y=0;y<rgba.Height;y++){var start=y*(rgba.Width*4+1);Check.Equal((byte)0,scanlines[start],"PNG filter");Check.Sequence(rgba.Pixels.Skip(y*rgba.Width*4).Take(rgba.Width*4),scanlines.Skip(start+1).Take(rgba.Width*4),"PNG pixels");}
            Check.Equal("data:image/png;base64,"+Convert.ToBase64String(png),qr.ToPngDataUrl(options),"PNG data URL");count++;
        }
        var xml=XDocument.Parse(qr.ToSvg(new(){Title="<a>&\"日本語"}));Check.Equal("<a>&\"日本語",xml.Root!.Elements().First().Value,"SVG title escaped");
        Check.Equal(qr.ToSvg(),Uri.UnescapeDataString(qr.ToSvgDataUrl().Split(',',2)[1]),"SVG data URL");
        using var stream=new MemoryStream();qr.SavePng(stream);Check.True(stream.CanWrite&&stream.Length>0,"stream ownership");
        foreach(var options in new QRRenderOptions[]{new(){Margin=-1},new(){Scale=0},new(){Scale=int.MaxValue},new(){Margin=int.MaxValue},new(){Width=1},new(){Foreground="<script>"},new(){Background="invalid"},new(){PrintDpi=double.NaN},new(){PrintDpi=0}})
            Check.Throws(()=>qr.ToPng(options),"invalid render option");
        Check.Throws(()=>qr.ToRgba(new(){Scale=100}),"raster allocation cap");
        var canvas=new RecordingCanvas();qr.Draw(canvas,new(){Scale=2});Check.Equal((qr.Size+8)*2,canvas.Width,"canvas size");Check.Equal(qr.Matrix.Sum(r=>r.Count(x=>x))+1,canvas.Rectangles,"canvas dark modules");
        return count+14;
    }
    private sealed class RecordingCanvas:IQRCanvas {public int Width{get;private set;}public int Rectangles{get;private set;}public void Resize(int width,int height){Width=width;Check.Equal(width,height,"canvas square");}public void FillRectangle(int x,int y,int width,int height,string color){Check.True(width>0&&height>0,"rectangle dimensions");Rectangles++;}}
}
