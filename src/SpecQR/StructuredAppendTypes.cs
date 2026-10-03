namespace SpecQR;

/// <summary>Full split-unit detail is explicitly opt-in; Summary retains only counts and ranges.</summary>
public enum QRStructuredAppendSplitUnitsDetail { Summary, Full }

public sealed record QRStructuredAppendOptions
{
    public QROptions QrOptions { get; init; } = new();
    public int MaxSymbols { get; init; } = 16;
    public bool Diagnostics { get; init; }
    public QRStructuredAppendSplitUnitsDetail SplitUnits { get; init; } = QRStructuredAppendSplitUnitsDetail.Summary;
}

public sealed class QRStructuredAppendPart
{
    private readonly byte[] bytes;
    public int Index { get; }
    public int Total { get; }
    public byte Parity { get; }
    public string? Text { get; }
    public byte[] Bytes => (byte[])bytes.Clone();
    internal byte[] BytesInternal => bytes;
    public string DataType => Text is null ? "binary" : "string";
    public QRStructuredAppendPart(int index, int total, byte parity, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Index = index; Total = total; Parity = parity; Text = text;
        bytes = StructuredAppendEncoder.TextBytes(text);
    }
    public QRStructuredAppendPart(int index, int total, byte parity, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length > StructuredAppendEncoder.MaximumMessageBytes)
            throw new SpecQRException("INVALID_INPUT", "Decoded part exceeds the Structured Append resource budget.");
        Index = index; Total = total; Parity = parity; this.bytes = (byte[])bytes.Clone();
    }
}

public sealed record QRStructuredAppendPartMetadata(int Index, int Total, byte Parity, string DataType, int ByteLength);
public sealed record QRStructuredAppendParityCheck(byte Expected, byte Actual)
{
    public bool Matches => Expected == Actual;
}
public sealed record QRStructuredAppendMergeDiagnostics(
    int PartCount, int Total, byte Parity, string DataType, int ByteLength,
    IReadOnlyList<int> Missing, IReadOnlyList<int> Duplicate, QRStructuredAppendParityCheck ParityCheck);

public sealed class QRStructuredAppendMergeResult
{
    private readonly byte[] bytes;
    public string? Text { get; }
    public byte[] Bytes => (byte[])bytes.Clone();
    public int Total { get; }
    public byte Parity { get; }
    public IReadOnlyList<QRStructuredAppendPartMetadata> Parts { get; }
    public QRStructuredAppendMergeDiagnostics Diagnostics { get; }
    internal QRStructuredAppendMergeResult(string? text, byte[] bytes, int total, byte parity,
        QRStructuredAppendPartMetadata[] parts, QRStructuredAppendMergeDiagnostics diagnostics)
    {
        Text = text; this.bytes = bytes; Total = total; Parity = parity;
        Parts = Array.AsReadOnly(parts); Diagnostics = diagnostics;
    }
}

public sealed record QRStructuredAppendSplitUnit(
    int SourceSegmentIndex, QRMode Mode, int UnitStart, int UnitLength, int ByteStart, int ByteLength);
public sealed record QRStructuredAppendWarning(string Code, string Severity, string Message, int Total, int? MaxSymbols);
public sealed record QRStructuredAppendSymbolDiagnostics(
    int Index, int Total, byte Parity, int? InputStart, int? InputLength,
    int? SourceSegmentStart, int? SourceSegmentEnd, int? SplitUnitStart, int? SplitUnitLength,
    int ByteStart, int ByteLength, int Version, ErrorCorrectionLevel ErrorCorrectionLevel,
    int DataBitLength, int CapacityBits, int RemainingBits, int MaskPattern)
{
    public int SequenceIndex => Index - 1;
    public int SequenceTotal => Total - 1;
    public int SequenceIndicator => SequenceIndex << 4 | SequenceTotal;
}
public sealed record QRStructuredAppendDiagnostics(
    int Version, ErrorCorrectionLevel ErrorCorrectionLevel, string VersionSelection, string VersionSelectionReason,
    int Total, byte Parity, int ByteLength, int InputLength, int? SegmentCount, int MaxSymbols,
    string SplitStrategy, int? SplitUnitCount, QRStructuredAppendSplitUnitsDetail? SplitUnitsDetail,
    IReadOnlyList<QRStructuredAppendSplitUnit>? SplitUnits, IReadOnlyList<QRStructuredAppendSymbolDiagnostics> Symbols,
    IReadOnlyList<QRStructuredAppendWarning> Warnings);
public sealed record QRStructuredAppendResult(
    IReadOnlyList<QRCode> Symbols, int Total, byte Parity, int InputLength, int ByteLength,
    QRStructuredAppendDiagnostics Diagnostics);
