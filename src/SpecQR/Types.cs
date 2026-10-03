namespace SpecQR;

public enum ErrorCorrectionLevel { L, M, Q, H }
public enum QRMode { Auto, Numeric, Alphanumeric, Byte, Kanji, Eci, Fnc1, Fnc1Second, StructuredAppend, Mixed }

/// <summary>A QR Structured Append header; indices are one-based.</summary>
public sealed record QRStructuredAppendHeader(int Index, int Total, byte Parity)
{
    public bool IsValid => Total is >= 2 and <= 16 && Index >= 1 && Index <= Total;
    public int? SequenceIndex => IsValid ? Index - 1 : null;
    public int? SequenceTotal => IsValid ? Total - 1 : null;
    public int? SequenceIndicator => IsValid ? (Index - 1) << 4 | (Total - 1) : null;
}

public sealed record QROptions
{
    public ErrorCorrectionLevel ErrorCorrectionLevel { get; init; } = ErrorCorrectionLevel.M;
    public int? Version { get; init; }
    public int MinVersion { get; init; } = 1;
    public int MaxVersion { get; init; } = 40;
    public int? MaskPattern { get; init; }
    public QRMode Mode { get; init; } = QRMode.Auto;
    public bool OptimizeSegments { get; init; } = true;
    public bool BoostErrorCorrection { get; init; }
    public int? Eci { get; init; }
    public bool Gs1 { get; init; }
    public string? Fnc1Second { get; init; }
    public QRStructuredAppendHeader? StructuredAppend { get; init; }
}

/// <summary>A validation or capacity failure with a stable, language-neutral code.</summary>
public sealed class SpecQRException : ArgumentException
{
    public string Code { get; }
    public int? RequiredBits { get; }
    public int? CapacityBits { get; }
    public int? Version { get; }
    public SpecQRException(string code, string message) : base(message) => Code = code;
    internal SpecQRException(int requiredBits, int capacityBits, int version)
        : this("DATA_TOO_LONG", $"Input requires {requiredBits} bits, but version {version} has {capacityBits} data bits.")
        => (RequiredBits, CapacityBits, Version) = (requiredBits, capacityBits, version);
}

public sealed record QRSegmentDiagnostics(
    QRMode Mode, int CharacterCount, int ByteCount, int BitLength,
    int? AssignmentNumber, string? ApplicationIndicator, int? ApplicationIndicatorCodeword,
    QRStructuredAppendHeader? StructuredAppend)
{
    public int? Index => StructuredAppend?.Index;
    public int? Total => StructuredAppend?.Total;
    public byte? Parity => StructuredAppend?.Parity;
    public int? SequenceIndex => StructuredAppend?.SequenceIndex;
    public int? SequenceTotal => StructuredAppend?.SequenceTotal;
    public int? SequenceIndicator => StructuredAppend?.SequenceIndicator;
}

public sealed record QRGS1ValidationDiagnostics
{
    public bool Enabled { get; }
    public int? ElementCount { get; }
    public IReadOnlyList<string> Ais { get; }
    public bool HasSeparators { get; }
    internal QRGS1ValidationDiagnostics(bool enabled, int? elementCount, IEnumerable<string> ais, bool hasSeparators)
        => (Enabled, ElementCount, Ais, HasSeparators) = (enabled, elementCount, Array.AsReadOnly(ais.ToArray()), hasSeparators);
}

public sealed record QRFNC1SecondDiagnostics(bool Enabled, string? ApplicationIndicator, int? ApplicationIndicatorCodeword);
public sealed record QRMaskPenalty(int MaskPattern, int Penalty);

/// <summary>A planning-only result. Capacity overflow returns Ok=false; malformed inputs throw.</summary>
public sealed class QRPlan
{
    public bool Ok { get; }
    public int? SelectedVersion { get; }
    public int EvaluatedVersion { get; }
    public int MinVersion { get; }
    public int MaxVersion { get; }
    public ErrorCorrectionLevel ErrorCorrectionLevel { get; }
    public ErrorCorrectionLevel RequestedErrorCorrectionLevel { get; }
    public string VersionSelection { get; }
    public string VersionSelectionReason { get; }
    public IReadOnlyList<QRSegment> Segments { get; }
    public IReadOnlyList<QRSegmentDiagnostics> SegmentDiagnostics { get; }
    public int DataBitLength { get; }
    public int CapacityBits { get; }
    public int InputBytes { get; }
    public QRGS1ValidationDiagnostics Gs1Validation { get; }
    public string Phase => "planning";
    public bool RenderPlanned => false;
    public bool MaskEvaluated => false;
    public bool CodewordsBuilt => false;
    public int? Version => SelectedVersion;
    public int? Size => SelectedVersion is int v ? v * 4 + 17 : null;
    public QRMode Mode
    {
        get
        {
            var modes = Segments.Where(s => !s.IsControl).Select(s => s.Mode).Distinct().ToArray();
            return modes.Length > 1 ? QRMode.Mixed : modes.FirstOrDefault(QRMode.Byte);
        }
    }
    public IReadOnlyList<QRSegmentDiagnostics> ControlSegments => Array.AsReadOnly(SegmentDiagnostics.Where(s => !QRValidation.IsData(s.Mode)).ToArray());
    public bool BoostedErrorCorrection => ErrorCorrectionLevel != RequestedErrorCorrectionLevel;
    public int? EciAssignmentNumber => ControlSegments.Select(s => s.AssignmentNumber).FirstOrDefault(v => v.HasValue);
    public string? Fnc1 => Segments.Any(s => s.Mode == QRMode.Fnc1) ? "first-position" : Segments.Any(s => s.Mode == QRMode.Fnc1Second) ? "second-position" : null;
    public bool Gs1 => Segments.Any(s => s.Mode == QRMode.Fnc1);
    public string? Fnc1SecondApplicationIndicator => ControlSegments.Select(s => s.ApplicationIndicator).FirstOrDefault(v => v is not null);
    public QRFNC1SecondDiagnostics Fnc1Second => new(Fnc1 == "second-position", Fnc1SecondApplicationIndicator, ControlSegments.Select(s => s.ApplicationIndicatorCodeword).FirstOrDefault(v => v.HasValue));
    public QRStructuredAppendHeader? StructuredAppend => ControlSegments.Select(s => s.StructuredAppend).FirstOrDefault(v => v is not null);
    public int RemainingBits => CapacityBits - DataBitLength;
    public int OverflowBits => Math.Max(0, -RemainingBits);
    public double CapacityUtilization => (double)DataBitLength / CapacityBits;
    public double UsageRatio => CapacityUtilization;
    public SpecQRException? Error => Ok ? null : new(DataBitLength, CapacityBits, EvaluatedVersion);

    internal QRPlan(bool ok, int? selectedVersion, int evaluatedVersion, QROptions options,
        ErrorCorrectionLevel level, string selection, string reason, QRSegment[] segments,
        int dataBitLength, int capacityBits, int inputBytes, QRGS1ValidationDiagnostics gs1Validation)
    {
        Ok = ok; SelectedVersion = selectedVersion; EvaluatedVersion = evaluatedVersion;
        MinVersion = options.MinVersion; MaxVersion = options.MaxVersion;
        ErrorCorrectionLevel = level; RequestedErrorCorrectionLevel = options.ErrorCorrectionLevel;
        VersionSelection = selection; VersionSelectionReason = reason;
        Segments = Array.AsReadOnly((QRSegment[])segments.Clone());
        SegmentDiagnostics = Array.AsReadOnly(segments.Select(s => SegmentEncoder.Diagnostics(s, evaluatedVersion)).ToArray());
        DataBitLength = dataBitLength; CapacityBits = capacityBits; InputBytes = inputBytes; Gs1Validation = gs1Validation;
    }
}

public sealed class QRDiagnostics
{
    public QRPlan Planning { get; }
    public int MaskPattern { get; }
    public int MaskPenalty { get; }
    public IReadOnlyList<QRMaskPenalty> MaskPenalties { get; }
    public int DataCodewords { get; }
    public int ErrorCorrectionCodewords { get; }
    public int TotalCodewords { get; }
    public int Version => Planning.EvaluatedVersion;
    public int Size => Version * 4 + 17;
    public ErrorCorrectionLevel ErrorCorrectionLevel => Planning.ErrorCorrectionLevel;
    public ErrorCorrectionLevel RequestedErrorCorrectionLevel => Planning.RequestedErrorCorrectionLevel;
    public bool BoostedErrorCorrection => Planning.BoostedErrorCorrection;
    public int DataBitLength => Planning.DataBitLength;
    public int CapacityBits => Planning.CapacityBits;
    public int RemainingBits => Planning.RemainingBits;
    public double CapacityUtilization => Planning.CapacityUtilization;
    public int InputBytes => Planning.InputBytes;
    public QRMode Mode => Planning.Mode;
    public IReadOnlyList<QRSegmentDiagnostics> Segments => Planning.SegmentDiagnostics;
    public IReadOnlyList<QRSegmentDiagnostics> ControlSegments => Planning.ControlSegments;
    public string VersionSelection => Planning.VersionSelection;
    public string VersionSelectionReason => Planning.VersionSelectionReason;
    public string MaskSelectionReason => MaskPenalties.Count == 1
        ? $"Mask pattern {MaskPattern} was requested explicitly."
        : $"Mask pattern {MaskPattern} had the lowest penalty score ({MaskPenalty}) among the evaluated masks.";
    public int? EciAssignmentNumber => Planning.EciAssignmentNumber;
    public string? Fnc1 => Planning.Fnc1;
    public bool Gs1 => Planning.Gs1;
    public QRGS1ValidationDiagnostics Gs1Validation => Planning.Gs1Validation;
    public QRFNC1SecondDiagnostics Fnc1Second => Planning.Fnc1Second;
    public QRStructuredAppendHeader? StructuredAppend => Planning.StructuredAppend;
    internal QRDiagnostics(QRPlan planning, int maskPattern, int maskPenalty, IEnumerable<QRMaskPenalty> penalties, int dataCodewords, int totalCodewords)
    {
        Planning = planning; MaskPattern = maskPattern; MaskPenalty = maskPenalty;
        MaskPenalties = Array.AsReadOnly(penalties.ToArray());
        DataCodewords = dataCodewords; TotalCodewords = totalCodewords;
        ErrorCorrectionCodewords = totalCodewords - dataCodewords;
    }
}

public sealed record QRCapacity(int Version, ErrorCorrectionLevel ErrorCorrectionLevel,
    int DataCodewords, int TotalCodewords, QRMode? Mode, int? CharacterCountBits,
    int ControlBits, int? PayloadBits, int? MaxCharacters, int? MaxBytes)
{
    public int Size => Version * 4 + 17;
    public int CapacityBits => DataCodewords * 8;
    public int? ModeIndicatorBits => Mode is null ? null : 4;
    public int ErrorCorrectionCodewords => TotalCodewords - DataCodewords;
}
