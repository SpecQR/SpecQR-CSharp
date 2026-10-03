using System.Text;

namespace SpecQR;

/// <summary>An immutable QR data or control segment. Byte arrays are copied on entry and access.</summary>
public sealed class QRSegment
{
    private readonly byte[]? _bytes;
    public QRMode Mode { get; }
    public string? Text { get; }
    public byte[]? Bytes => _bytes is null ? null : (byte[])_bytes.Clone();
    internal byte[]? BytesInternal => _bytes;
    public int CharacterCount { get; }
    public int ByteCount { get; }
    public bool IsControl => !QRValidation.IsData(Mode);
    public int? AssignmentNumber { get; }
    public string? ApplicationIndicator { get; }
    public QRStructuredAppendHeader? Header { get; }

    private QRSegment(QRMode mode, string? text = null, byte[]? bytes = null,
        int? assignment = null, string? indicator = null, QRStructuredAppendHeader? header = null)
    {
        Mode = mode; Text = text; AssignmentNumber = assignment; ApplicationIndicator = indicator; Header = header;
        if (text is not null)
        {
            QRValidation.Text(text);
            CharacterCount = text.EnumerateRunes().Count();
            ByteCount = mode == QRMode.Kanji ? CharacterCount * 2 : QRValidation.Utf8.GetByteCount(text);
            if (mode == QRMode.Byte) _bytes = QRValidation.Utf8.GetBytes(text);
        }
        else if (bytes is not null)
        {
            QRValidation.Bytes(bytes);
            _bytes = (byte[])bytes.Clone(); ByteCount = bytes.Length;
        }
        SegmentEncoder.Validate(this);
    }

    public static QRSegment Numeric(string text) => new(QRMode.Numeric, text: QRValidation.RequireText(text));
    public static QRSegment Alphanumeric(string text) => new(QRMode.Alphanumeric, text: QRValidation.RequireText(text));
    public static QRSegment Byte(string text) => new(QRMode.Byte, text: QRValidation.RequireText(text));
    public static QRSegment Byte(byte[] bytes) => new(QRMode.Byte, bytes: bytes ?? throw new SpecQRException("INVALID_INPUT", "Bytes must not be null."));
    public static QRSegment Kanji(string text) => new(QRMode.Kanji, text: QRValidation.RequireText(text));
    public static QRSegment Eci(int assignmentNumber) => new(QRMode.Eci, assignment: assignmentNumber);
    public static QRSegment Fnc1() => new(QRMode.Fnc1);
    public static QRSegment Fnc1Second(string applicationIndicator) => new(QRMode.Fnc1Second, indicator: applicationIndicator ?? throw new SpecQRException("INVALID_MODE", "FNC1 application indicator must not be null."));
    public static QRSegment StructuredAppend(int index, int total, byte parity) => new(QRMode.StructuredAppend, header: new(index, total, parity));
    public int BitLength(int version)
    {
        QRValidation.Version(version);
        return SegmentEncoder.BitLength(this, version);
    }
}

internal sealed class BitBuffer
{
    private readonly List<byte> _bytes = [];
    internal int Count { get; private set; }
    internal byte[] Bytes => _bytes.ToArray();
    internal void Append(int value, int width)
    {
        for (var shift = width - 1; shift >= 0; shift--)
        {
            if (Count % 8 == 0) _bytes.Add(0);
            if (((value >> shift) & 1) != 0) _bytes[^1] |= (byte)(1 << (7 - Count % 8));
            Count++;
        }
    }
}

internal static class QRValidation
{
    internal const int MaxInputLength = 1_000_000;
    internal const int MaxSegments = 65_536;
    internal static readonly UTF8Encoding Utf8 = new(false, true);
    internal static bool IsData(QRMode mode) => mode is QRMode.Numeric or QRMode.Alphanumeric or QRMode.Byte or QRMode.Kanji;
    internal static string RequireText(string text) => text ?? throw new SpecQRException("INVALID_INPUT", "Text must not be null.");
    internal static void Text(string text)
    {
        RequireText(text);
        if (text.Length > MaxInputLength) throw new SpecQRException("INVALID_INPUT", $"Text exceeds the {MaxInputLength} UTF-16 code unit resource limit.");
        for (var i = 0; i < text.Length; i++)
        {
            if (!char.IsSurrogate(text[i])) continue;
            if (!char.IsHighSurrogate(text[i]) || i + 1 == text.Length || !char.IsLowSurrogate(text[i + 1]))
                throw new SpecQRException("INVALID_INPUT", "Text must contain valid Unicode; unpaired UTF-16 surrogates are not accepted.");
            i++;
        }
    }
    internal static void Bytes(byte[] bytes)
    {
        if (bytes is null) throw new SpecQRException("INVALID_INPUT", "Bytes must not be null.");
        if (bytes.Length > MaxInputLength) throw new SpecQRException("INVALID_INPUT", $"Binary input exceeds the {MaxInputLength} byte resource limit.");
    }
    internal static void Version(int version)
    {
        if (version is < 1 or > 40) throw new SpecQRException("INVALID_VERSION", "Version must be in 1...40.");
    }
    internal static void Level(ErrorCorrectionLevel level)
    {
        if (level is < ErrorCorrectionLevel.L or > ErrorCorrectionLevel.H) throw new SpecQRException("INVALID_INPUT", "Unknown error correction level.");
    }
    internal static void Options(QROptions options)
    {
        Level(options.ErrorCorrectionLevel); Version(options.MinVersion); Version(options.MaxVersion);
        if (options.Version is int fixedVersion) Version(fixedVersion);
        if (options.MinVersion > options.MaxVersion) throw new SpecQRException("INVALID_VERSION", "MinVersion must not exceed MaxVersion.");
        if (options.MaskPattern is < 0 or > 7) throw new SpecQRException("INVALID_INPUT", "MaskPattern must be in 0...7.");
        if (options.Mode != QRMode.Auto && !IsData(options.Mode)) throw new SpecQRException("INVALID_MODE", "Input Mode must be Auto, Numeric, Alphanumeric, Byte, or Kanji.");
        if (options.Eci is int eci) _ = QRSegment.Eci(eci);
        if (options.Fnc1Second is string indicator) _ = QRSegment.Fnc1Second(indicator);
        if (options.StructuredAppend is QRStructuredAppendHeader h) _ = QRSegment.StructuredAppend(h.Index, h.Total, h.Parity);
        var kinds = (options.Gs1 ? 1 : 0) + (options.Eci.HasValue ? 1 : 0) + (options.Fnc1Second is not null ? 1 : 0) + (options.StructuredAppend is not null ? 1 : 0);
        if (kinds > 1) throw new SpecQRException(options.Gs1 ? "INVALID_GS1" : "INVALID_MODE", "GS1, ECI, FNC1 second position, and Structured Append options cannot be combined.");
    }
    internal static void Controls(IReadOnlyList<QRSegment> segments)
    {
        QRMode? leading = null;
        var eciPresent = false;
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            if (segment is null) throw new SpecQRException("INVALID_INPUT", "Segments must not contain null.");
            SegmentEncoder.Validate(segment);
            if (segment.Mode == QRMode.Eci) { eciPresent = true; continue; }
            if (!segment.IsControl) continue;
            if (i != 0 || leading.HasValue)
                throw new SpecQRException(segment.Mode == QRMode.Fnc1 ? "INVALID_GS1" : "INVALID_MODE", "FNC1 and Structured Append headers must occur once, at the beginning.");
            leading = segment.Mode;
        }
        if (leading.HasValue && eciPresent)
            throw new SpecQRException(leading == QRMode.Fnc1 ? "INVALID_GS1" : "INVALID_MODE", "ECI cannot be combined with FNC1 or Structured Append.");
    }
}

internal static class SegmentEncoder
{
    internal const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:";
    internal static int AlphaValue(int scalar) => scalar < 128 ? Alphabet.IndexOf((char)scalar) : -1;
    internal static int Indicator(string value)
    {
        if (value.Length == 2 && value[0] is >= '0' and <= '9' && value[1] is >= '0' and <= '9') return (value[0] - '0') * 10 + value[1] - '0';
        if (value.Length == 1 && value[0] is >= 'A' and <= 'Z' or >= 'a' and <= 'z') return value[0] + 100;
        throw new SpecQRException("INVALID_MODE", "FNC1 second position requires two ASCII digits or one Latin letter.");
    }
    internal static void Validate(QRSegment segment)
    {
        switch (segment.Mode)
        {
            case QRMode.Numeric:
                if (segment.Text!.Any(c => c is < '0' or > '9')) throw new SpecQRException("INVALID_MODE", "Numeric mode permits only ASCII digits 0-9.");
                break;
            case QRMode.Alphanumeric:
                if (segment.Text!.Any(c => AlphaValue(c) < 0)) throw new SpecQRException("INVALID_MODE", "Unsupported character in alphanumeric segment.");
                break;
            case QRMode.Kanji:
                if (segment.Text!.EnumerateRunes().Any(r => !KanjiMap.Value(r).HasValue)) throw new SpecQRException("INVALID_MODE", "Kanji mode requires characters in the QR Shift_JIS ranges.");
                break;
            case QRMode.Eci:
                if (segment.AssignmentNumber is < 0 or >= 1_000_000) throw new SpecQRException("INVALID_ECI", "ECI assignment must be in 0...999999.");
                break;
            case QRMode.Fnc1Second: _ = Indicator(segment.ApplicationIndicator!); break;
            case QRMode.StructuredAppend:
                if (!segment.Header!.IsValid) throw new SpecQRException("INVALID_MODE", "Structured Append requires total 2...16 and one-based index 1...total.");
                break;
        }
    }
    internal static int PayloadLength(QRMode mode, int count) => mode switch
    {
        QRMode.Numeric => count / 3 * 10 + ((count % 3) switch { 0 => 0, 1 => 4, _ => 7 }),
        QRMode.Alphanumeric => count / 2 * 11 + count % 2 * 6,
        QRMode.Kanji => count * 13,
        QRMode.Byte => count * 8,
        _ => 0
    };
    internal static int BitLength(QRSegment segment, int version) => segment.Mode switch
    {
        QRMode.Eci => segment.AssignmentNumber < 128 ? 12 : segment.AssignmentNumber < 16384 ? 20 : 28,
        QRMode.Fnc1 => 4,
        QRMode.Fnc1Second => 12,
        QRMode.StructuredAppend => 20,
        _ => 4 + QRTables.CountBits(segment.Mode, version) + PayloadLength(segment.Mode, segment.Mode == QRMode.Byte ? segment.ByteCount : segment.CharacterCount)
    };
    internal static QRSegment[] ApplyingOptions(IEnumerable<QRSegment> segments, QROptions options)
    {
        var result = segments.ToList();
        if (options.Eci is int eci) result.Insert(0, QRSegment.Eci(eci));
        if (options.Gs1) result.Insert(0, QRSegment.Fnc1());
        if (options.Fnc1Second is string indicator) result.Insert(0, QRSegment.Fnc1Second(indicator));
        if (options.StructuredAppend is QRStructuredAppendHeader h) result.Insert(0, QRSegment.StructuredAppend(h.Index, h.Total, h.Parity));
        QRValidation.Controls(result);
        return result.ToArray();
    }
    internal static byte[] Encode(IReadOnlyList<QRSegment> segments, int version, ErrorCorrectionLevel level)
    {
        var buffer = new BitBuffer();
        var capacity = QRTables.DataCodewords(version, level) * 8;
        var required = segments.Sum(s => BitLength(s, version));
        if (required > capacity) throw new SpecQRException(required, capacity, version);
        foreach (var segment in segments)
        {
            Validate(segment);
            switch (segment.Mode)
            {
                case QRMode.Eci:
                    var assignment = segment.AssignmentNumber!.Value;
                    buffer.Append(7, 4);
                    if (assignment < 128) buffer.Append(assignment, 8);
                    else if (assignment < 16384) { buffer.Append(2, 2); buffer.Append(assignment, 14); }
                    else { buffer.Append(6, 3); buffer.Append(assignment, 21); }
                    break;
                case QRMode.Fnc1: buffer.Append(5, 4); break;
                case QRMode.Fnc1Second: buffer.Append(9, 4); buffer.Append(Indicator(segment.ApplicationIndicator!), 8); break;
                case QRMode.StructuredAppend:
                    buffer.Append(3, 4); buffer.Append(segment.Header!.Index - 1, 4); buffer.Append(segment.Header.Total - 1, 4); buffer.Append(segment.Header.Parity, 8);
                    break;
                default:
                    var count = segment.Mode == QRMode.Byte ? segment.ByteCount : segment.CharacterCount;
                    var width = QRTables.CountBits(segment.Mode, version);
                    if (count >= 1 << width) throw new SpecQRException(required, capacity, version);
                    buffer.Append(segment.Mode switch { QRMode.Numeric => 1, QRMode.Alphanumeric => 2, QRMode.Byte => 4, _ => 8 }, 4);
                    buffer.Append(count, width);
                    if (segment.Mode == QRMode.Numeric)
                    {
                        var text = segment.Text!;
                        for (var i = 0; i < text.Length; i += 3)
                        {
                            var length = Math.Min(3, text.Length - i); var value = 0;
                            for (var j = 0; j < length; j++) value = value * 10 + text[i + j] - '0';
                            buffer.Append(value, length switch { 1 => 4, 2 => 7, _ => 10 });
                        }
                    }
                    else if (segment.Mode == QRMode.Alphanumeric)
                    {
                        var text = segment.Text!;
                        for (var i = 0; i < text.Length; i += 2)
                            if (i + 1 < text.Length) buffer.Append(AlphaValue(text[i]) * 45 + AlphaValue(text[i + 1]), 11);
                            else buffer.Append(AlphaValue(text[i]), 6);
                    }
                    else if (segment.Mode == QRMode.Kanji)
                        foreach (var rune in segment.Text!.EnumerateRunes()) buffer.Append(KanjiMap.Value(rune)!.Value, 13);
                    else foreach (var b in segment.BytesInternal!) buffer.Append(b, 8);
                    break;
            }
        }
        buffer.Append(0, Math.Min(4, capacity - buffer.Count));
        buffer.Append(0, (8 - buffer.Count % 8) % 8);
        var pad = 0;
        while (buffer.Count < capacity) buffer.Append(pad++ % 2 == 0 ? 0xEC : 0x11, 8);
        return buffer.Bytes;
    }
    internal static QRSegmentDiagnostics Diagnostics(QRSegment segment, int version) => new(
        segment.Mode, segment.CharacterCount, segment.ByteCount, BitLength(segment, version),
        segment.AssignmentNumber, segment.ApplicationIndicator,
        segment.ApplicationIndicator is string indicator ? Indicator(indicator) : null, segment.Header);
}
