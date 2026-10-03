using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace SpecQR;

/// <summary>Portable rendering settings. Margin is measured in modules, Scale in pixels per module.</summary>
public sealed record QRRenderOptions
{
    public int Margin { get; init; } = 4;
    public int Scale { get; init; } = 8;
    /// <summary>Optional exact output width. Must be a positive integer multiple of Size + 2 * Margin; overrides Scale.</summary>
    public int? Width { get; init; }
    public string Foreground { get; init; } = "#000000";
    public string Background { get; init; } = "#ffffff";
    public double? PrintDpi { get; init; }
    public string? Title { get; init; }
}

/// <summary>Row-major, straight (unpremultiplied) RGBA8 pixels. Mutating this buffer does not modify the QR code.</summary>
public sealed record QRRgbaImage(int Width, int Height, byte[] Pixels);

/// <summary>Minimal adapter implemented by an application to draw into its own graphics framework.</summary>
public interface IQRCanvas
{
    void Resize(int width, int height);
    void FillRectangle(int x, int y, int width, int height, string color);
}

public sealed partial class QRCode
{
    /// <summary>Creates a self-contained SVG. Colors use the same portable formats as raster rendering.</summary>
    public string ToSvg(QRRenderOptions? options = null)
    {
        options ??= new();
        var geometry = RenderGeometry.Create(Size, options, raster: false);
        _ = RgbaColor.ParseRequired(options.Foreground, nameof(options.Foreground));
        _ = RgbaColor.ParseRequired(options.Background, nameof(options.Background));
        var text = new StringBuilder(512 + Size * Size * 32);
        text.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{geometry.Dimension}\" height=\"{geometry.Dimension}\" viewBox=\"0 0 {geometry.Dimension} {geometry.Dimension}\" role=\"img\">");
        if (options.Title is not null)
            text.Append("<title>").Append(EscapeXml(options.Title)).Append("</title>");
        text.Append("<rect width=\"100%\" height=\"100%\" fill=\"").Append(EscapeXml(options.Background)).Append("\"/>");
        text.Append("<path fill=\"").Append(EscapeXml(options.Foreground)).Append("\" d=\"");
        var matrix = MatrixInternal;
        for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
                if (matrix[y][x])
                    text.Append(CultureInfo.InvariantCulture, $"M{(x + options.Margin) * geometry.Scale},{(y + options.Margin) * geometry.Scale}h{geometry.Scale}v{geometry.Scale}h-{geometry.Scale}z");
        return text.Append("\"/></svg>").ToString();
    }

    public string ToSvgDataUrl(QRRenderOptions? options = null) =>
        "data:image/svg+xml;charset=utf-8," + Uri.EscapeDataString(ToSvg(options));

    public QRRgbaImage ToRgba(QRRenderOptions? options = null)
    {
        options ??= new();
        var geometry = RenderGeometry.Create(Size, options, raster: true);
        var foreground = RgbaColor.ParseRequired(options.Foreground, nameof(options.Foreground));
        var background = RgbaColor.ParseRequired(options.Background, nameof(options.Background));
        var pixels = new byte[checked(geometry.Dimension * geometry.Dimension * 4)];
        for (var y = 0; y < geometry.Dimension; y++)
            FillPixelRow(pixels.AsSpan(y * geometry.Dimension * 4, geometry.Dimension * 4), y, geometry, options.Margin, foreground, background);
        return new(geometry.Dimension, geometry.Dimension, pixels);
    }

    /// <summary>Creates a portable RGBA PNG using BCL zlib and independently implemented PNG framing and CRC.</summary>
    public byte[] ToPng(QRRenderOptions? options = null)
    {
        using var output = new MemoryStream();
        SavePng(output, options);
        return output.ToArray();
    }

    /// <summary>Writes PNG bytes without closing the caller's stream. Invalid options are checked before writing.</summary>
    public void SavePng(Stream destination, QRRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite) throw new ArgumentException("The destination must be writable.", nameof(destination));
        options ??= new();
        var geometry = RenderGeometry.Create(Size, options, raster: true);
        var foreground = RgbaColor.ParseRequired(options.Foreground, nameof(options.Foreground));
        var background = RgbaColor.ParseRequired(options.Background, nameof(options.Background));
        // One scanline is reused. The 2048-square limit bounds both compressed and uncompressed storage.
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            var row = new byte[checked(geometry.Dimension * 4 + 1)];
            for (var y = 0; y < geometry.Dimension; y++)
            {
                FillPixelRow(row.AsSpan(1), y, geometry, options.Margin, foreground, background);
                zlib.Write(row);
            }
        }
        destination.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        Span<byte> header = stackalloc byte[13];
        header.Clear();
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)geometry.Dimension);
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], (uint)geometry.Dimension);
        header[8] = 8;
        header[9] = 6;
        PngCodec.WriteChunk(destination, "IHDR"u8, header);
        PngCodec.WriteChunk(destination, "IDAT"u8, compressed.GetBuffer().AsSpan(0, checked((int)compressed.Length)));
        PngCodec.WriteChunk(destination, "IEND"u8, ReadOnlySpan<byte>.Empty);
    }

    public string ToPngDataUrl(QRRenderOptions? options = null) =>
        "data:image/png;base64," + Convert.ToBase64String(ToPng(options));

    public void SaveSvg(Stream destination, QRRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite) throw new ArgumentException("The destination must be writable.", nameof(destination));
        destination.Write(Encoding.UTF8.GetBytes(ToSvg(options)));
    }

    public void Draw(IQRCanvas canvas, QRRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        options ??= new();
        var geometry = RenderGeometry.Create(Size, options, raster: true);
        _ = RgbaColor.ParseRequired(options.Foreground, nameof(options.Foreground));
        _ = RgbaColor.ParseRequired(options.Background, nameof(options.Background));
        canvas.Resize(geometry.Dimension, geometry.Dimension);
        canvas.FillRectangle(0, 0, geometry.Dimension, geometry.Dimension, options.Background);
        var matrix = MatrixInternal;
        for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
                if (matrix[y][x])
                    canvas.FillRectangle((x + options.Margin) * geometry.Scale, (y + options.Margin) * geometry.Scale, geometry.Scale, geometry.Scale, options.Foreground);
    }

    private void FillPixelRow(Span<byte> row, int y, RenderGeometry geometry, int margin, RgbaColor foreground, RgbaColor background)
    {
        var my = y / geometry.Scale - margin;
        var matrix = MatrixInternal;
        for (var x = 0; x < geometry.Dimension; x++)
        {
            var mx = x / geometry.Scale - margin;
            var dark = mx >= 0 && my >= 0 && mx < Size && my < Size && matrix[my][mx];
            var color = dark ? foreground : background;
            var pixel = row.Slice(x * 4, 4);
            pixel[0] = color.R; pixel[1] = color.G; pixel[2] = color.B; pixel[3] = color.A;
        }
    }

    private static string EscapeXml(string text) => text.Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal);
}

internal readonly record struct RenderGeometry(int Dimension, int Scale)
{
    internal static RenderGeometry Create(int size, QRRenderOptions options, bool raster)
    {
        if (options.Margin < 0) throw new ArgumentOutOfRangeException(nameof(options.Margin), "Margin must be nonnegative.");
        if (options.Scale < 1) throw new ArgumentOutOfRangeException(nameof(options.Scale), "Scale must be positive.");
        if (options.Foreground is null || options.Background is null) throw new ArgumentException("Colors must not be null.", nameof(options));
        if (options.Foreground.Length > 65536 || options.Background.Length > 65536 || options.Title?.Length > 65536)
            throw new ArgumentOutOfRangeException(nameof(options), "Color and title strings are limited to 65,536 characters.");
        if (options.Title is not null) XmlConvert.VerifyXmlChars(options.Title);
        if (options.PrintDpi is double dpi && (!double.IsFinite(dpi) || dpi <= 0))
            throw new ArgumentOutOfRangeException(nameof(options.PrintDpi), "PrintDpi must be finite and positive.");
        var span = (long)size + (long)options.Margin * 2;
        var scale = options.Scale;
        if (options.Width is int width)
        {
            if (width <= 0 || width % span != 0) throw new ArgumentOutOfRangeException(nameof(options.Width), "Width must be a positive integer multiple of the module span including the quiet zone.");
            scale = checked((int)(width / span));
        }
        if (span > int.MaxValue / scale)
            throw new ArgumentOutOfRangeException(nameof(options), "Render geometry exceeds the signed 32-bit coordinate range.");
        var dimension = span * scale;
        if (raster && dimension > 2048)
            throw new ArgumentOutOfRangeException(nameof(options), raster ? "Raster output is limited to 2048 pixels per side (4,194,304 pixels)." : "Render geometry exceeds the signed 32-bit coordinate range.");
        if (options.PrintDpi is double printDpi && !double.IsFinite(dimension / printDpi * 25.4))
            throw new ArgumentOutOfRangeException(nameof(options.PrintDpi), "Physical print dimensions exceed the numeric range.");
        return new((int)dimension, scale);
    }
}

internal readonly record struct RgbaColor(byte R, byte G, byte B, byte A)
{
    internal static RgbaColor ParseRequired(string value, string parameter) => TryParse(value) ??
        throw new ArgumentException("Use #RGB, #RGBA, #RRGGBB, #RRGGBBAA, black, white, or transparent.", parameter);

    internal static RgbaColor? TryParse(string value)
    {
        var text = value.Trim().ToLowerInvariant();
        if (text == "black") return new(0, 0, 0, 255);
        if (text == "white") return new(255, 255, 255, 255);
        if (text == "transparent") return new(0, 0, 0, 0);
        if (!text.StartsWith('#') || text.Length is not (4 or 5 or 7 or 9)) return null;
        var hex = text.AsSpan(1);
        foreach (var c in hex) if (!char.IsAsciiHexDigit(c)) return null;
        if (hex.Length <= 4)
        {
            static byte Nibble(char c) => (byte)((c <= '9' ? c - '0' : c - 'a' + 10) * 17);
            return new(Nibble(hex[0]), Nibble(hex[1]), Nibble(hex[2]), hex.Length == 4 ? Nibble(hex[3]) : (byte)255);
        }
        return new(byte.Parse(hex[..2], NumberStyles.HexNumber), byte.Parse(hex[2..4], NumberStyles.HexNumber),
            byte.Parse(hex[4..6], NumberStyles.HexNumber), hex.Length == 8 ? byte.Parse(hex[6..], NumberStyles.HexNumber) : (byte)255);
    }

    internal double Luminance
    {
        get
        {
            static double Linear(byte c) { var n = c / 255.0; return n <= 0.03928 ? n / 12.92 : Math.Pow((n + 0.055) / 1.055, 2.4); }
            return 0.2126 * Linear(R) + 0.7152 * Linear(G) + 0.0722 * Linear(B);
        }
    }
}

internal static class PngCodec
{
    internal static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(number, (uint)data.Length);
        output.Write(number); output.Write(type); output.Write(data);
        uint crc = 0xffffffff;
        foreach (var b in type) crc = UpdateCrc(crc, b);
        foreach (var b in data) crc = UpdateCrc(crc, b);
        BinaryPrimitives.WriteUInt32BigEndian(number, crc ^ 0xffffffff);
        output.Write(number);
    }

    private static uint UpdateCrc(uint crc, byte value)
    {
        crc ^= value;
        for (var bit = 0; bit < 8; bit++) crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xedb88320;
        return crc;
    }
}
