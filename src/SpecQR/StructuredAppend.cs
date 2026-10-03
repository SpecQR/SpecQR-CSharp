using System.Text;

namespace SpecQR;

public sealed partial class QRCode
{
    public static QRStructuredAppendResult GenerateStructuredAppend(string text, QRStructuredAppendOptions? options = null)
        => StructuredAppendEncoder.Generate(text, options ?? new());
    public static QRStructuredAppendResult GenerateStructuredAppend(byte[] bytes, QRStructuredAppendOptions? options = null)
        => StructuredAppendEncoder.Generate(bytes, options ?? new());
    public static QRStructuredAppendResult GenerateSegmentsStructuredAppend(IEnumerable<QRSegment> segments, QRStructuredAppendOptions? options = null)
        => StructuredAppendEncoder.Generate(segments, options ?? new());

    /// <summary>XOR of the original UTF-8 bytes, before QR mode encoding.</summary>
    public static byte CalculateStructuredAppendParity(string text)
        => CalculateStructuredAppendParity(StructuredAppendEncoder.TextBytes(text));
    public static byte CalculateStructuredAppendParity(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        byte parity = 0;
        foreach (byte value in bytes) parity ^= value;
        return parity;
    }
    /// <summary>Text segments, including Kanji, contribute UTF-8. Binary segments contribute their raw bytes.</summary>
    public static byte CalculateStructuredAppendSegmentsParity(IEnumerable<QRSegment> segments)
    {
        QRSegment[] data = StructuredAppendEncoder.ValidateSegments(segments);
        byte parity = 0;
        foreach (QRSegment segment in data) parity ^= CalculateStructuredAppendParity(StructuredAppendEncoder.CanonicalBytes(segment));
        return parity;
    }

    /// <summary>Validate and join a complete decoded set. This helper does not read images.</summary>
    public static QRStructuredAppendMergeResult MergeStructuredAppendParts(IEnumerable<QRStructuredAppendPart> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        var list = new List<QRStructuredAppendPart>();
        foreach (var part in parts)
        {
            if (part is null || list.Count == 16) throw new SpecQRException("INVALID_INPUT", "Structured Append requires 2–16 non-null parts.");
            list.Add(part);
        }
        if (list.Count == 0) throw new SpecQRException("INVALID_INPUT", "Structured Append parts must not be empty.");
        QRStructuredAppendPart first = list[0];
        var seen = new HashSet<int>();
        int byteLength = 0;
        foreach (var part in list)
        {
            if (part.Total is < 2 or > 16 || part.Index < 1 || part.Index > part.Total)
                throw new SpecQRException("INVALID_INPUT", "Structured Append indices are one-based; total must be 2–16.");
            if (part.Total != first.Total || part.Parity != first.Parity || part.DataType != first.DataType)
                throw new SpecQRException("INVALID_INPUT", "Structured Append total, parity and data type must agree.");
            if (!seen.Add(part.Index)) throw new SpecQRException("INVALID_INPUT", "Duplicate Structured Append part index.");
            byteLength = checked(byteLength + part.BytesInternal.Length);
            if (byteLength > StructuredAppendEncoder.MaximumMessageBytes)
                throw new SpecQRException("INVALID_INPUT", "Decoded set exceeds the Structured Append resource budget.");
        }
        if (list.Count != first.Total) throw new SpecQRException("INVALID_INPUT", "Structured Append set has missing parts.");
        list.Sort((a, b) => a.Index.CompareTo(b.Index));
        byte[] bytes = new byte[byteLength];
        int offset = 0;
        foreach (var part in list) { part.BytesInternal.CopyTo(bytes, offset); offset += part.BytesInternal.Length; }
        byte actual = CalculateStructuredAppendParity(bytes);
        if (actual != first.Parity) throw new SpecQRException("INVALID_INPUT", "Structured Append payload parity does not match metadata.");
        string? text = first.Text is null ? null : string.Concat(list.Select(p => p.Text));
        var metadata = list.Select(p => new QRStructuredAppendPartMetadata(p.Index, p.Total, p.Parity, p.DataType, p.BytesInternal.Length)).ToArray();
        return new(text, bytes, first.Total, first.Parity, metadata,
            new(list.Count, first.Total, first.Parity, first.DataType, byteLength,
                Array.Empty<int>(), Array.Empty<int>(), new(first.Parity, actual)));
    }
}

internal static class StructuredAppendEncoder
{
    // More than the maximum possible 16 Model 2 symbols, even for UTF-8 text
    // whose most compact representation is numeric/alphanumeric/Kanji.
    internal const int MaximumMessageBytes = 1_000_000;
    private const int MaximumManualSegments = 32_768;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    internal static byte[] TextBytes(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > MaximumMessageBytes) throw new SpecQRException("INVALID_INPUT", "Text exceeds the resource budget.");
        try
        {
            if (StrictUtf8.GetByteCount(text) > MaximumMessageBytes) throw new SpecQRException("INVALID_INPUT", "Text exceeds the resource budget.");
            return StrictUtf8.GetBytes(text);
        }
        catch (EncoderFallbackException) { throw new SpecQRException("INVALID_INPUT", "Text contains an unpaired UTF-16 surrogate."); }
    }
    internal static byte[] CanonicalBytes(QRSegment segment) => segment.Text is { } text ? TextBytes(text) : segment.Bytes ?? [];
    internal static QRSegment[] ValidateSegments(IEnumerable<QRSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        var result = new List<QRSegment>();
        int length = 0;
        foreach (var segment in segments)
        {
            if (segment is null || result.Count >= MaximumManualSegments) throw new SpecQRException("INVALID_INPUT", "Too many or null manual segments.");
            if (segment.Mode == QRMode.Fnc1) throw new SpecQRException("INVALID_GS1", "Structured Append cannot be combined with FNC1.");
            if (segment.IsControl) throw new SpecQRException("INVALID_MODE", "High-level Structured Append accepts data segments only.");
            SegmentEncoder.Validate(segment);
            var bytes = CanonicalBytes(segment);
            if (bytes.Length == 0) throw new SpecQRException("INVALID_INPUT", "Structured Append segments must be nonempty.");
            length = checked(length + bytes.Length);
            if (length > MaximumMessageBytes) throw new SpecQRException("DATA_TOO_LONG", "Manual data exceeds the Structured Append resource budget.");
            result.Add(segment);
        }
        if (result.Count == 0) throw new SpecQRException("INVALID_INPUT", "Structured Append requires nonempty segments.");
        return result.ToArray();
    }
    private static void ValidateOptions(QRStructuredAppendOptions options, bool manual)
    {
        if (options.QrOptions is null) throw new SpecQRException("INVALID_INPUT", "QrOptions cannot be null.");
        var qr = options.QrOptions;
        QRValidation.Options(qr);
        if (options.MaxSymbols is < 2 or > 16) throw new SpecQRException("INVALID_MODE", "MaxSymbols must be 2–16.");
        if (!Enum.IsDefined(options.SplitUnits)) throw new SpecQRException("INVALID_INPUT", "Unknown split-unit detail.");
        if (!manual && options.SplitUnits != QRStructuredAppendSplitUnitsDetail.Summary)
            throw new SpecQRException("INVALID_INPUT", "Full split-unit diagnostics require manual segments.");
        if (manual && (qr.Mode != QRMode.Auto || !qr.OptimizeSegments))
            throw new SpecQRException("INVALID_MODE", "Manual Structured Append preserves its source segment modes.");
        if (qr.Gs1) throw new SpecQRException("INVALID_GS1", "High-level Structured Append cannot be combined with GS1.");
        if (qr.Eci is not null || qr.Fnc1Second is not null || qr.StructuredAppend is not null || qr.BoostErrorCorrection)
            throw new SpecQRException("INVALID_MODE", "High-level Structured Append owns its headers and does not support ECI, FNC1 or ECC boosting.");
    }
    private static int SingleBits(QRMode mode, int count, int version)
        => checked(4 + QRTables.CountBits(mode, version) + SegmentEncoder.PayloadLength(mode, count));
    private static SpecQRException TooLong(QRStructuredAppendOptions options)
        => new("DATA_TOO_LONG", $"Input cannot be split into 2–{options.MaxSymbols} symbols in the selected version range.");

    internal static QRStructuredAppendResult Generate(string text, QRStructuredAppendOptions options)
    {
        ValidateOptions(options, false);
        byte[] bytes = TextBytes(text);
        if (bytes.Length == 0) throw new SpecQRException("INVALID_INPUT", "Structured Append input must be nonempty.");
        if (options.QrOptions.Mode != QRMode.Auto) SegmentEncoder.Validate(SegmentOptimizer.Segment(text, options.QrOptions.Mode));
        var source = new Source(text);
        Preflight(source.UnitCount, bytes.Length, options.QrOptions.Mode, options);
        return Build(source, QRCode.CalculateStructuredAppendParity(bytes), options);
    }
    internal static QRStructuredAppendResult Generate(byte[] bytes, QRStructuredAppendOptions options)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ValidateOptions(options, false);
        if (options.QrOptions.Mode is not (QRMode.Auto or QRMode.Byte)) throw new SpecQRException("INVALID_MODE", "Binary input requires byte or auto mode.");
        if (bytes.Length == 0) throw new SpecQRException("INVALID_INPUT", "Structured Append input must be nonempty.");
        Preflight(bytes.Length, bytes.Length, QRMode.Byte, options);
        return Build(new Source((byte[])bytes.Clone()), QRCode.CalculateStructuredAppendParity(bytes), options);
    }
    internal static QRStructuredAppendResult Generate(IEnumerable<QRSegment> segments, QRStructuredAppendOptions options)
    {
        ValidateOptions(options, true);
        QRSegment[] data = ValidateSegments(segments);
        int version = options.QrOptions.Version ?? options.QrOptions.MaxVersion;
        long required = data.Sum(s => (long)SegmentEncoder.BitLength(s, version));
        long available = options.MaxSymbols * Math.Max(0, QRTables.DataCodewords(version, options.QrOptions.ErrorCorrectionLevel) * 8 - 20);
        if (required > available) throw TooLong(options);
        byte parity = 0;
        foreach (var segment in data) parity ^= QRCode.CalculateStructuredAppendParity(CanonicalBytes(segment));
        return Build(new Source(data), parity, options);
    }
    private static void Preflight(int count, int byteCount, QRMode mode, QRStructuredAppendOptions options)
    {
        int version = options.QrOptions.Version ?? options.QrOptions.MaxVersion;
        int capacity = QRTables.DataCodewords(version, options.QrOptions.ErrorCorrectionLevel) * 8;
        long payload = mode == QRMode.Auto ? (long)count / 3 * 10 + new[] { 0, 4, 7 }[count % 3]
            : mode == QRMode.Byte ? (long)byteCount * 8 : mode == QRMode.Kanji ? (long)count * 13
            : mode == QRMode.Alphanumeric ? (long)count / 2 * 11 + count % 2 * 6 : (long)count / 3 * 10 + new[] { 0, 4, 7 }[count % 3];
        int countBits = mode == QRMode.Auto ? DataModes.Min(m => QRTables.CountBits(m, version)) : QRTables.CountBits(mode, version);
        if (payload > (long)Math.Max(0, capacity - 24 - countBits) * options.MaxSymbols) throw TooLong(options);
    }
    private static readonly QRMode[] DataModes = [QRMode.Numeric, QRMode.Alphanumeric, QRMode.Kanji, QRMode.Byte];

    private sealed class TextIndex
    {
        internal readonly string Text;
        internal readonly Rune[] Runes;
        internal readonly int[] CharOffsets;
        internal readonly int[] ByteOffsets;
        internal readonly QRMode? UniformMode;
        internal TextIndex(string text)
        {
            Text = text; Runes = text.EnumerateRunes().ToArray();
            CharOffsets = new int[Runes.Length + 1]; ByteOffsets = new int[Runes.Length + 1];
            bool numeric = true, alphaNonNumeric = true, kanji = true, onlyByte = true;
            for (int i = 0; i < Runes.Length; i++)
            {
                Rune r = Runes[i];
                CharOffsets[i + 1] = CharOffsets[i] + r.Utf16SequenceLength;
                ByteOffsets[i + 1] = ByteOffsets[i] + r.Utf8SequenceLength;
                bool n = r.Value is >= 48 and <= 57;
                bool a = IsAlpha(r), k = KanjiMap.Value(r) is not null;
                numeric &= n; alphaNonNumeric &= a && !n; kanji &= k; onlyByte &= !a && !k;
            }
            UniformMode = numeric ? QRMode.Numeric : alphaNonNumeric ? QRMode.Alphanumeric : kanji ? QRMode.Kanji : onlyByte ? QRMode.Byte : null;
        }
        internal string Slice(int start, int length) => Text.Substring(CharOffsets[start], CharOffsets[start + length] - CharOffsets[start]);
        internal int ByteCount(int start, int length) => ByteOffsets[start + length] - ByteOffsets[start];
    }
    private static bool IsAlpha(Rune rune) => rune.Value <= 127 && "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:".Contains((char)rune.Value);

    private sealed class TextBitTracker(int version)
    {
        private const int Infinity = int.MaxValue / 4;
        private int[] costs = Enumerable.Repeat(Infinity, 7).ToArray();
        internal int Best { get; private set; }
        internal int Append(Rune rune)
        {
            int[] next = Enumerable.Repeat(Infinity, 7).ToArray();
            if (rune.Value is >= 48 and <= 57)
            {
                next[0] = costs[2] + 3;
                next[1] = Math.Min(costs[0] + 4, Best + 8 + QRTables.CountBits(QRMode.Numeric, version));
                next[2] = costs[1] + 3;
            }
            if (IsAlpha(rune))
            {
                next[3] = costs[4] + 5;
                next[4] = Math.Min(costs[3] + 6, Best + 10 + QRTables.CountBits(QRMode.Alphanumeric, version));
            }
            if (KanjiMap.Value(rune) is not null)
                next[5] = Math.Min(costs[5], Best + 4 + QRTables.CountBits(QRMode.Kanji, version)) + 13;
            next[6] = Math.Min(costs[6], Best + 4 + QRTables.CountBits(QRMode.Byte, version)) + rune.Utf8SequenceLength * 8;
            costs = next; Best = next.Min(); return Best;
        }
    }
    private sealed class Descriptor
    {
        internal QRSegment Segment { get; }
        internal int SourceIndex { get; }
        internal int ByteStart { get; }
        internal int ByteLength { get; }
        internal int UnitStart { get; }
        internal int UnitCount { get; }
        internal TextIndex? Text { get; }
        private readonly byte[]? binary;
        internal Descriptor(QRSegment segment, int index, int byteStart, int unitStart)
        {
            Segment = segment; SourceIndex = index; ByteStart = byteStart; UnitStart = unitStart;
            ByteLength = CanonicalBytes(segment).Length;
            if (segment.Mode == QRMode.Byte && segment.Text is { } text)
            {
                Text = new(text); UnitCount = Text.Runes.Length;
            }
            else if (segment.Mode == QRMode.Byte) { binary = segment.Bytes!; UnitCount = binary.Length; }
            else UnitCount = 1;
        }
        internal (int Start, int Length)? Overlap(int start, int length)
        {
            int lo = Math.Max(start, UnitStart), hi = Math.Min(start + length, UnitStart + UnitCount);
            return lo < hi ? (lo - UnitStart, hi - lo) : null;
        }
        internal int ByteOffset(int unit) => ByteStart + (Text is not null ? Text.ByteOffsets[unit] : Segment.Mode == QRMode.Byte ? unit : 0);
        internal int ByteCount(int start, int length) => Text is not null ? Text.ByteCount(start, length) : Segment.Mode == QRMode.Byte ? length : ByteLength;
        internal int Bits(int start, int length, int version) => Segment.Mode == QRMode.Byte ? SingleBits(QRMode.Byte, ByteCount(start, length), version) : SegmentEncoder.BitLength(Segment, version);
        internal QRSegment Materialize(int start, int length) => Text is not null ? QRSegment.Byte(Text.Slice(start, length))
            : binary is not null ? QRSegment.Byte(binary.AsSpan(start, length).ToArray()) : Segment;
    }
    private sealed class Source
    {
        internal TextIndex? Text { get; }
        internal byte[]? Binary { get; }
        internal Descriptor[]? Descriptors { get; }
        internal int UnitCount { get; }
        internal int ByteLength { get; }
        internal int InputLength => Descriptors?.Length ?? UnitCount;
        internal bool IsManual => Descriptors is not null;
        internal Source(string text) { Text = new(text); UnitCount = Text.Runes.Length; ByteLength = Text.ByteOffsets[^1]; }
        internal Source(byte[] bytes) { Binary = bytes; UnitCount = ByteLength = bytes.Length; }
        internal Source(QRSegment[] segments)
        {
            var descriptors = new List<Descriptor>();
            int unit = 0, bytes = 0;
            for (int i = 0; i < segments.Length; i++)
            {
                var d = new Descriptor(segments[i], i, bytes, unit); descriptors.Add(d);
                unit += d.UnitCount; bytes += d.ByteLength;
            }
            Descriptors = descriptors.ToArray(); UnitCount = unit; ByteLength = bytes;
        }
        internal int DataBits(int start, int length, QROptions options, int version)
        {
            if (Binary is not null) return SingleBits(QRMode.Byte, length, version);
            if (Text is not null)
            {
                QRMode? uniform = options.Mode == QRMode.Auto ? Text.UniformMode : options.Mode;
                if (uniform is { } mode) return SingleBits(mode, mode == QRMode.Byte ? Text.ByteCount(start, length) : length, version);
                if (options.OptimizeSegments)
                {
                    var tracker = new TextBitTracker(version);
                    for (int i = start; i < start + length; i++) tracker.Append(Text.Runes[i]);
                    return tracker.Best;
                }
                return SegmentOptimizer.Single(Text.Slice(start, length), QRMode.Auto, true).Sum(s => SegmentEncoder.BitLength(s, version));
            }
            int bits = 0;
            foreach (var d in Descriptors!) if (d.Overlap(start, length) is { } overlap) bits += d.Bits(overlap.Start, overlap.Length, version);
            return bits;
        }
        internal int? AutoPrefix(int start, int maxLength, QROptions options, int version, int capacity)
        {
            if (Text is null || options.Mode != QRMode.Auto || Text.UniformMode is not null) return null;
            var tracker = new TextBitTracker(version);
            int best = 0, bytes = 0;
            bool numeric = true, alpha = true, kanji = true;
            for (int offset = 0; offset < maxLength; offset++)
            {
                Rune r = Text.Runes[start + offset]; int bits;
                if (options.OptimizeSegments) bits = tracker.Append(r);
                else
                {
                    numeric &= r.Value is >= 48 and <= 57; alpha &= IsAlpha(r); kanji &= KanjiMap.Value(r) is not null;
                    bytes += r.Utf8SequenceLength;
                    var mode = numeric ? QRMode.Numeric : alpha ? QRMode.Alphanumeric : kanji ? QRMode.Kanji : QRMode.Byte;
                    bits = SingleBits(mode, mode == QRMode.Byte ? bytes : offset + 1, version);
                }
                if (bits > capacity) break;
                best = offset + 1;
            }
            return best;
        }
        internal Chunk Materialize(int start, int length)
        {
            if (Text is not null) return new(Text.Slice(start, length), null, null, Text.ByteOffsets[start], Text.ByteCount(start, length), null, null);
            if (Binary is not null) return new(null, Binary.AsSpan(start, length).ToArray(), null, start, length, null, null);
            var segments = new List<QRSegment>(); int? first = null, last = null, byteStart = null; int byteLength = 0;
            foreach (var d in Descriptors!)
            {
                if (d.Overlap(start, length) is not { } overlap) continue;
                segments.Add(d.Materialize(overlap.Start, overlap.Length)); first ??= d.SourceIndex; last = d.SourceIndex + 1;
                byteStart ??= d.ByteOffset(overlap.Start); byteLength += d.ByteCount(overlap.Start, overlap.Length);
            }
            return new(null, null, segments.ToArray(), byteStart ?? 0, byteLength, first, last);
        }
        internal IReadOnlyList<QRStructuredAppendSplitUnit>? SplitUnits()
        {
            if (Descriptors is null) return null;
            var units = new List<QRStructuredAppendSplitUnit>(UnitCount);
            foreach (var d in Descriptors)
                for (int unit = 0; unit < d.UnitCount; unit++)
                    units.Add(new(d.SourceIndex, d.Segment.Mode, unit, d.Segment.Mode == QRMode.Byte ? 1 : d.Segment.CharacterCount,
                        d.ByteOffset(unit), d.ByteCount(unit, 1)));
            return units.AsReadOnly();
        }
    }
    private sealed record Chunk(string? Text, byte[]? Bytes, QRSegment[]? Segments, int ByteStart, int ByteLength, int? SegmentStart, int? SegmentEnd);
    private enum AttemptKind { Single, TooLong, Split }
    private sealed record Attempt(AttemptKind Kind, List<(int Start, int Length)> Ranges);
    private static Attempt TrySplit(Source source, QRStructuredAppendOptions options, int version)
    {
        int capacity = QRTables.DataCodewords(version, options.QrOptions.ErrorCorrectionLevel) * 8 - 20;
        bool Fits(int start, int length)
        {
            if (source.Text is not null && SegmentEncoder.PayloadLength(QRMode.Numeric, length) + 4 + DataModes.Min(m => QRTables.CountBits(m, version)) > capacity) return false;
            return source.DataBits(start, length, options.QrOptions, version) <= capacity;
        }
        if (Fits(0, source.UnitCount)) return new(AttemptKind.Single, []);
        if (source.UnitCount < 2) return new(AttemptKind.TooLong, []);
        var ranges = new List<(int, int)>(); int position = 0;
        while (position < source.UnitCount)
        {
            if (ranges.Count == options.MaxSymbols) return new(AttemptKind.TooLong, []);
            int maxLength = source.UnitCount - position - (ranges.Count == 0 ? 1 : 0);
            int? prefix = source.AutoPrefix(position, maxLength, options.QrOptions, version, capacity);
            int best = prefix ?? 0;
            if (prefix is null)
            {
                int low = 1, high = maxLength;
                while (low <= high)
                {
                    int middle = low + (high - low) / 2;
                    if (Fits(position, middle)) { best = middle; low = middle + 1; } else high = middle - 1;
                }
            }
            if (best == 0) return new(AttemptKind.TooLong, []);
            ranges.Add((position, best)); position += best;
        }
        return new(ranges.Count >= 2 ? AttemptKind.Split : AttemptKind.Single, ranges);
    }
    private static QRStructuredAppendResult Build(Source source, byte parity, QRStructuredAppendOptions options)
    {
        var qr = options.QrOptions;
        int lower = qr.Version ?? qr.MinVersion, upper = qr.Version ?? qr.MaxVersion;
        bool sawTooLong = false; Attempt? selected = null; int version;
        for (version = lower; version <= upper; version++)
        {
            var attempt = TrySplit(source, options, version);
            if (attempt.Kind == AttemptKind.Split) { selected = attempt; break; }
            sawTooLong |= attempt.Kind == AttemptKind.TooLong;
        }
        if (selected is null)
        {
            if (sawTooLong) throw TooLong(options);
            throw new SpecQRException("INVALID_INPUT", "Input fits one header-bearing symbol; use Generate or the low-level StructuredAppend option.");
        }
        var symbols = new List<QRCode>(); var metadata = new List<QRStructuredAppendSymbolDiagnostics>();
        int total = selected.Ranges.Count;
        for (int i = 0; i < total; i++)
        {
            var (start, length) = selected.Ranges[i]; var chunk = source.Materialize(start, length);
            var symbolOptions = qr with { Version = version, MinVersion = version, MaxVersion = version, StructuredAppend = new(i + 1, total, parity) };
            QRCode code = chunk.Text is not null ? QRCode.Generate(chunk.Text, symbolOptions)
                : chunk.Bytes is not null ? QRCode.Generate(chunk.Bytes, symbolOptions) : QRCode.GenerateSegments(chunk.Segments!, symbolOptions);
            symbols.Add(code);
            metadata.Add(new(i + 1, total, parity, source.IsManual ? null : start, source.IsManual ? null : length,
                chunk.SegmentStart, chunk.SegmentEnd, source.IsManual ? start : null, source.IsManual ? length : null,
                chunk.ByteStart, chunk.ByteLength, version, code.ErrorCorrectionLevel, code.Diagnostics.DataBitLength,
                code.Diagnostics.CapacityBits, code.Diagnostics.RemainingBits, code.MaskPattern));
        }
        var warnings = new List<QRStructuredAppendWarning>();
        if (total == options.MaxSymbols) warnings.Add(new("STRUCTURED_APPEND_MAX_SYMBOLS_NEAR_LIMIT", "info", "The set uses the configured maximum number of symbols.", total, options.MaxSymbols));
        if (options.Diagnostics) warnings.Add(new("STRUCTURED_APPEND_DECODER_SUPPORT_VARIES", "info", "Decoder APIs vary in their exposure of Structured Append metadata.", total, null));
        string reason = qr.Version is not null ? $"Version {version} was requested explicitly."
            : $"Version {version} is the smallest version in {lower}..{upper} that splits the payload into {total} symbols at {qr.ErrorCorrectionLevel}.";
        return new(symbols.AsReadOnly(), total, parity, source.InputLength, source.ByteLength,
            new(version, qr.ErrorCorrectionLevel, qr.Version is null ? "auto-minimum" : "fixed", reason, total, parity,
                source.ByteLength, source.InputLength, source.IsManual ? source.InputLength : null, options.MaxSymbols,
                source.IsManual ? "segment-boundary-byte-chunk" : "greedy-largest-fitting", source.IsManual ? source.UnitCount : null,
                source.IsManual ? options.SplitUnits : null,
                options.SplitUnits == QRStructuredAppendSplitUnitsDetail.Full ? source.SplitUnits() : null,
                metadata.AsReadOnly(), warnings.AsReadOnly()));
    }
}
