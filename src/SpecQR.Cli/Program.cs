using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpecQR;

return SpecQRCli.Run(args);

internal static class SpecQRCli
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private const int MaximumInputFileBytes = 1_048_576;

    internal static int Run(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
            {
                Console.WriteLine(Help);
                return 0;
            }
            var command = args[0];
            if (command is not ("encode" or "estimate" or "structured-append"))
                throw new ArgumentException("Command must be encode, estimate, or structured-append. Use --help for usage.");
            var parsed = Parse(args[1..]);
            var qrOptions = new QROptions
            {
                ErrorCorrectionLevel = ParseEnum<ErrorCorrectionLevel>(parsed.Get("ecc") ?? "M"),
                Mode = ParseMode(parsed.Get("mode") ?? "auto"),
                Version = parsed.Integer("version"), MinVersion = parsed.Integer("min-version") ?? 1,
                MaxVersion = parsed.Integer("max-version") ?? 40, MaskPattern = parsed.Integer("mask"),
                Eci = parsed.Integer("eci"), Gs1 = parsed.Has("gs1"), Fnc1Second = parsed.Get("fnc1-second"),
                OptimizeSegments = !parsed.Has("no-optimize"), BoostErrorCorrection = parsed.Has("boost")
            };
            var render = new QRRenderOptions
            {
                Scale = parsed.Integer("scale") ?? 8, Margin = parsed.Integer("margin") ?? 4,
                Width = parsed.Integer("width"), Foreground = parsed.Get("foreground") ?? "#000000",
                Background = parsed.Get("background") ?? "#ffffff", Title = parsed.Get("title"),
                PrintDpi = parsed.Number("dpi")
            };
            if (command != "structured-append" && parsed.Has("max-symbols"))
                throw new ArgumentException("--max-symbols is only valid for structured-append.");
            var format = parsed.Get("format") ?? (command == "encode" ? "svg" : "json");
            if (command != "encode" && format != "json")
                throw new ArgumentException("estimate and structured-append support only --format json.");
            if (format is not ("svg" or "png" or "matrix" or "json" or "svg-data-url" or "png-data-url"))
                throw new ArgumentException("Unsupported format. Use svg, png, matrix, json, svg-data-url, or png-data-url.");
            var input = ReadInput(parsed);
            byte[] output;
            var exitCode = 0;
            if (command == "estimate")
            {
                var plan = input.Bytes is not null ? QRCode.Estimate(input.Bytes, qrOptions) : QRCode.Estimate(input.Text!, qrOptions);
                output = Serialize(new { planning = PlanView(plan), rendering = plan.RenderDiagnostics(render) });
                exitCode = plan.Ok ? 0 : 3;
            }
            else if (command == "structured-append")
            {
                var options = new QRStructuredAppendOptions { QrOptions = qrOptions, MaxSymbols = parsed.Integer("max-symbols") ?? 16, Diagnostics = true };
                var result = input.Bytes is not null ? QRCode.GenerateStructuredAppend(input.Bytes, options) : QRCode.GenerateStructuredAppend(input.Text!, options);
                output = Serialize(new
                {
                    result.Total, result.Parity, result.InputLength, result.ByteLength, result.Diagnostics,
                    Symbols = result.Symbols.Select(qr => new { qr.Version, qr.Size, qr.Matrix, qr.Diagnostics, rendering = qr.RenderDiagnostics(render) })
                });
            }
            else
            {
                var qr = input.Bytes is not null ? QRCode.Generate(input.Bytes, qrOptions) : QRCode.Generate(input.Text!, qrOptions);
                output = format switch
                {
                    "svg" => Utf8.GetBytes(qr.ToSvg(render)),
                    "png" => qr.ToPng(render),
                    "svg-data-url" => Utf8.GetBytes(qr.ToSvgDataUrl(render)),
                    "png-data-url" => Utf8.GetBytes(qr.ToPngDataUrl(render)),
                    "matrix" => Serialize(qr.Matrix),
                    _ => Serialize(new { qr.Version, qr.Size, qr.Matrix, qr.Diagnostics, rendering = qr.RenderDiagnostics(render) })
                };
            }
            var path = parsed.Get("output");
            if (path is not null && path != "-") File.WriteAllBytes(path, output);
            else
            {
                using var stdout = Console.OpenStandardOutput();
                stdout.Write(output);
                if (format != "png") stdout.WriteByte((byte)'\n');
            }
            return exitCode;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException or System.Xml.XmlException)
        {
            WriteError(ex is SpecQRException spec ? spec.Code : "INVALID_ARGUMENT", ex.Message);
            return 2;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            WriteError("IO_ERROR", ex.Message);
            return 1;
        }
    }

    private static byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Json);
    private static void WriteError(string code, string message) => Console.Error.WriteLine(JsonSerializer.Serialize(new { error = new { code, message } }, Json));

    private static object PlanView(QRPlan plan) => new
    {
        plan.Ok, plan.Phase, plan.SelectedVersion, plan.EvaluatedVersion, plan.Version, plan.Size,
        plan.MinVersion, plan.MaxVersion, plan.ErrorCorrectionLevel, plan.RequestedErrorCorrectionLevel,
        plan.BoostedErrorCorrection, plan.VersionSelection, plan.VersionSelectionReason,
        plan.Mode, Segments = plan.SegmentDiagnostics, plan.ControlSegments, plan.DataBitLength,
        plan.CapacityBits, plan.RemainingBits, plan.OverflowBits, plan.CapacityUtilization, plan.InputBytes,
        plan.Gs1, plan.Gs1Validation, plan.Fnc1, plan.Fnc1Second, plan.EciAssignmentNumber, plan.StructuredAppend,
        plan.RenderPlanned, plan.MaskEvaluated, plan.CodewordsBuilt,
        Error = plan.Error is SpecQRException error ? new { error.Code, error.Message } : null
    };

    private static (string? Text, byte[]? Bytes) ReadInput(Arguments parsed)
    {
        var inputCount = (parsed.Text is not null ? 1 : 0) + (parsed.Has("hex") ? 1 : 0) + (parsed.Has("input-file") ? 1 : 0);
        if (inputCount != 1) throw new ArgumentException("Provide exactly one TEXT argument, --hex HEX, or --input-file PATH.");
        if (parsed.Has("binary") && !parsed.Has("input-file")) throw new ArgumentException("--binary requires --input-file.");
        if (parsed.Get("hex") is string hex)
        {
            if (hex.Length > MaximumInputFileBytes * 2) throw new ArgumentException("Hex input exceeds the 1 MiB CLI input budget.");
            return (null, Convert.FromHexString(hex));
        }
        if (parsed.Get("input-file") is string path)
        {
            using var stream = File.OpenRead(path);
            if (stream.Length > MaximumInputFileBytes) throw new ArgumentException("Input file exceeds the 1 MiB CLI input budget.");
            using var memory = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = stream.Read(buffer)) > 0)
            {
                if (memory.Length + count > MaximumInputFileBytes) throw new ArgumentException("Input file exceeds the 1 MiB CLI input budget.");
                memory.Write(buffer, 0, count);
            }
            var bytes = memory.ToArray();
            return parsed.Has("binary") ? (null, bytes) : (Utf8.GetString(bytes), null);
        }
        return (parsed.Text, null);
    }

    private static T ParseEnum<T>(string value) where T : struct, Enum =>
        Enum.GetNames<T>().Any(name => string.Equals(name, value, StringComparison.OrdinalIgnoreCase)) && Enum.TryParse<T>(value, true, out var parsed)
        ? parsed : throw new ArgumentException($"Invalid {typeof(T).Name}: {value}.");

    private static QRMode ParseMode(string value)
    {
        var mode = ParseEnum<QRMode>(value);
        return mode is QRMode.Auto or QRMode.Numeric or QRMode.Alphanumeric or QRMode.Byte or QRMode.Kanji
            ? mode : throw new ArgumentException("CLI mode must be auto, numeric, alphanumeric, byte, or kanji.");
    }

    private static Arguments Parse(string[] args)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        string? text = null;
        var positionalOnly = false;
        var flags = new HashSet<string>(StringComparer.Ordinal) { "boost", "no-optimize", "gs1", "binary" };
        var valued = new HashSet<string>(StringComparer.Ordinal)
        {
            "ecc", "mode", "version", "min-version", "max-version", "mask", "eci", "fnc1-second",
            "format", "output", "input-file", "hex", "scale", "margin", "width", "foreground", "background", "dpi", "title", "max-symbols"
        };
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!positionalOnly && arg == "--") { positionalOnly = true; continue; }
            if (!positionalOnly && arg.StartsWith("--", StringComparison.Ordinal))
            {
                var key = arg[2..];
                if (!flags.Contains(key) && !valued.Contains(key)) throw new ArgumentException($"Unknown option: {arg}.");
                if (values.ContainsKey(key)) throw new ArgumentException($"Duplicate option: {arg}.");
                string? value = null;
                if (valued.Contains(key))
                {
                    if (++i >= args.Length) throw new ArgumentException($"Missing value for {arg}.");
                    value = args[i];
                }
                values.Add(key, value);
            }
            else
            {
                if (text is not null) throw new ArgumentException("Only one TEXT argument is allowed. Quote text containing spaces.");
                text = arg;
            }
        }
        return new(text, values);
    }

    private sealed record Arguments(string? Text, Dictionary<string, string?> Values)
    {
        internal bool Has(string key) => Values.ContainsKey(key);
        internal string? Get(string key) => Values.GetValueOrDefault(key);
        internal int? Integer(string key) => Get(key) is string s ? int.Parse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) : null;
        internal double? Number(string key) => Get(key) is string s ? double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture) : null;
    }

    private const string Help = """
        SpecQR — dependency-free QR Model 2 for .NET

        Usage:
          specqr encode TEXT [options]
          specqr estimate TEXT [options]
          specqr structured-append TEXT [options]

        Input: replace TEXT with --hex HEX, or --input-file PATH [--binary].
        Text files use strict UTF-8; bytes, whitespace, and newlines are not trimmed.
        Use -- before TEXT starting with --. Empty TEXT is allowed as a quoted argument.

        Encoding:
          --ecc L|M|Q|H                    Default M
          --mode auto|numeric|alphanumeric|byte|kanji
          --version N                     Fixed version 1..40
          --min-version N --max-version N  Automatic range
          --mask N                        Fixed mask 0..7
          --eci N --gs1 --fnc1-second CODE
          --boost --no-optimize
          --max-symbols N                  Structured Append only, 2..16

        Output:
          --format svg|png|matrix|json|svg-data-url|png-data-url
          --output PATH                   Otherwise stdout; '-' also means stdout
          --scale N --margin N --width N   Integer pixel geometry; default 8 / 4
          --foreground COLOR --background COLOR --title TEXT --dpi NUMBER

        encode defaults to SVG. estimate and structured-append emit JSON only.
        matrix emits a JSON array of boolean rows. json includes diagnostics.
        Exit codes: 0 success, 1 I/O error, 2 invalid input/options or encode overflow,
                    3 estimate reports capacity overflow (JSON is still written).
        """;
}
