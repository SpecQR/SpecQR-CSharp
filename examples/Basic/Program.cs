using SpecQR;

var text = args.Length > 0 ? args[0] : "SpecQR: こんにちは、世界！";
var outputDirectory = args.Length > 1 ? args[1] : "specqr-output";
Directory.CreateDirectory(outputDirectory);

var code = QRCode.Generate(text, new QROptions { ErrorCorrectionLevel = ErrorCorrectionLevel.Q, Eci = 26 });
var rendering = new QRRenderOptions { Scale = 8, Margin = 4, Title = "SpecQR example" };
File.WriteAllText(Path.Combine(outputDirectory, "hello.svg"), code.ToSvg(rendering));
File.WriteAllBytes(Path.Combine(outputDirectory, "hello.png"), code.ToPng(rendering));
Console.WriteLine($"Version {code.Version}, {code.Size} × {code.Size}, mask {code.MaskPattern}, {code.Planning.RemainingBits} bits remaining");

var binary = QRCode.Generate(new byte[] { 0, 1, 2, 0xff }, new QROptions { ErrorCorrectionLevel = ErrorCorrectionLevel.H });
File.WriteAllBytes(Path.Combine(outputDirectory, "binary.png"), binary.ToPng());

var manual = QRCode.GenerateSegments(new[] { QRSegment.Numeric("12345678901234567890"), QRSegment.Byte("abc") });
Console.WriteLine($"Manual segments: {manual.Planning.Mode}, {manual.Planning.DataBitLength} bits");

var elements = new[] { new GS1Element("01", "09506000134352"), new GS1Element("10", "LOT42") };
var gs1 = QRCode.Generate(GS1.CreateElementString(elements), new QROptions { Gs1 = true });
File.WriteAllText(Path.Combine(outputDirectory, "gs1.svg"), gs1.ToSvg());
Console.WriteLine(GS1.CreateDigitalLink(elements, new GS1DigitalLinkOptions("https://id.example.com")));

var plan = QRCode.Estimate(new string('A', 100), new QROptions { Version = 1, ErrorCorrectionLevel = ErrorCorrectionLevel.H });
Console.WriteLine($"Fixed-version estimate: fits={plan.Ok}, overflow={plan.OverflowBits} bits");
Console.WriteLine($"Files saved in {outputDirectory}");
