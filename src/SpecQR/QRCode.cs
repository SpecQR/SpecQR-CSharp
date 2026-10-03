namespace SpecQR;

/// <summary>Dependency-free QR Model 2 generation and immutable symbol results.</summary>
public sealed partial class QRCode
{
    private readonly bool[][] _matrix;
    private readonly byte[] _dataCodewords;
    private readonly byte[] _codewords;
    /// <summary>A defensive copy, indexed as [row][column]; true means a dark module.</summary>
    public bool[][] Matrix => _matrix.Select(row => (bool[])row.Clone()).ToArray();
    internal bool[][] MatrixInternal => _matrix;
    public int Size => _matrix.Length;
    public int Version { get; }
    public ErrorCorrectionLevel ErrorCorrectionLevel { get; }
    public int MaskPattern { get; }
    public IReadOnlyList<QRSegment> Segments => Planning.Segments;
    public byte[] DataCodewords => (byte[])_dataCodewords.Clone();
    public byte[] Codewords => (byte[])_codewords.Clone();
    internal byte[] DataCodewordsInternal => _dataCodewords;
    internal byte[] CodewordsInternal => _codewords;
    public QRDiagnostics Diagnostics { get; }
    public QRPlan Planning => Diagnostics.Planning;

    private QRCode(MatrixBuildResult built, byte[] data, byte[] codewords, QRPlan plan)
    {
        _matrix = built.Matrix; _dataCodewords = data; _codewords = codewords;
        Version = plan.EvaluatedVersion; ErrorCorrectionLevel = plan.ErrorCorrectionLevel; MaskPattern = built.MaskPattern;
        Diagnostics = new(plan, built.MaskPattern, built.Penalty, built.Penalties, data.Length, codewords.Length);
    }

    public static QRCode Generate(string text, QROptions? options = null)
    {
        options ??= new();
        return Build(Estimate(text, options), options);
    }
    public static QRCode Generate(byte[] bytes, QROptions? options = null)
    {
        options ??= new();
        return Build(Estimate(bytes, options), options);
    }
    /// <summary>Low-level FNC1 segments follow QR alphanumeric escaping: % is a separator; %% is literal %.</summary>
    public static QRCode GenerateSegments(IEnumerable<QRSegment> segments, QROptions? options = null)
    {
        options ??= new();
        return Build(AnalyzeSegments(segments, options), options);
    }

    /// <summary>Select versions and correction levels without generating parity, masks, or modules.</summary>
    public static QRPlan Estimate(string text, QROptions? options = null)
    {
        options ??= new();
        QRValidation.Options(options); QRValidation.Text(text);
        QRGS1ValidationDiagnostics? gs1Validation = null;
        if (options.Gs1)
        {
            var parsed = GS1.ParseElementString(text);
            gs1Validation = new(true, parsed.Elements.Count, parsed.Elements.Select(e => e.Ai), parsed.HasSeparators);
        }
        var encodingMode = options.Mode;
        // Preserve raw high-level payload semantics. Low-level FNC1 segments
        // deliberately retain the standard QR percent escaping contract.
        if ((options.Gs1 || options.Fnc1Second is not null) && text.Contains('%'))
        {
            if (encodingMode == QRMode.Alphanumeric)
                throw new SpecQRException(options.Gs1 ? "INVALID_GS1" : "INVALID_MODE", "Raw FNC1 strings containing literal % require Byte or Auto mode.");
            if (encodingMode == QRMode.Auto) encodingMode = QRMode.Byte;
        }
        var limitVersion = options.Version ?? options.MaxVersion;
        var count = text.EnumerateRunes().Count();
        QRMode[] modes = options.Eci.HasValue
            ? [QRMode.Numeric, QRMode.Alphanumeric, QRMode.Byte]
            : [QRMode.Numeric, QRMode.Alphanumeric, QRMode.Byte, QRMode.Kanji];
        var minimumCountBits = modes.Min(mode => QRTables.CountBits(mode, limitVersion));
        var controlBits = SegmentEncoder.ApplyingOptions([], options).Sum(s => SegmentEncoder.BitLength(s, limitVersion));
        // Numeric is a lower bound for every supported text mode. Do not allocate
        // optimizer state for input that cannot fit even with this lower bound.
        var preflightOverflow = 4 + minimumCountBits + SegmentEncoder.PayloadLength(QRMode.Numeric, count) + controlBits > QRTables.DataCodewords(limitVersion, options.ErrorCorrectionLevel) * 8;
        var cache = new Dictionary<int, QRSegment[]>();
        return Plan(options, QRValidation.Utf8.GetByteCount(text), gs1Validation, version =>
        {
            var band = version < 10 ? 0 : version < 27 ? 1 : 2;
            if (cache.TryGetValue(band, out var found)) return found;
            var basis = encodingMode != QRMode.Auto || !options.OptimizeSegments || preflightOverflow
                ? SegmentOptimizer.Single(text, encodingMode, options.Eci is null)
                : SegmentOptimizer.Optimized(text, version, options.Eci is null);
            var prepared = SegmentEncoder.ApplyingOptions(basis, options);
            cache.Add(band, prepared);
            return prepared;
        });
    }

    public static QRPlan Estimate(byte[] bytes, QROptions? options = null)
    {
        options ??= new();
        QRValidation.Options(options); QRValidation.Bytes(bytes);
        if (options.Gs1) throw new SpecQRException("INVALID_GS1", "GS1 input must be a raw GS1 element string, not binary input.");
        if (options.Mode is not (QRMode.Auto or QRMode.Byte)) throw new SpecQRException("INVALID_MODE", "Binary input requires Byte or Auto mode.");
        var prepared = SegmentEncoder.ApplyingOptions([QRSegment.Byte(bytes)], options);
        return Plan(options, bytes.Length, null, _ => prepared);
    }

    public static QRPlan AnalyzeSegments(IEnumerable<QRSegment> segments, QROptions? options = null)
    {
        options ??= new();
        QRValidation.Options(options);
        if (segments is null) throw new SpecQRException("INVALID_INPUT", "Segments must not be null.");
        var list = new List<QRSegment>();
        var inputBytes = 0;
        foreach (var segment in segments)
        {
            if (segment is null) throw new SpecQRException("INVALID_INPUT", "Segments must not contain null.");
            if (list.Count == QRValidation.MaxSegments) throw new SpecQRException("INVALID_INPUT", $"Manual segment count exceeds the {QRValidation.MaxSegments} segment resource limit.");
            inputBytes += segment.ByteCount;
            if (inputBytes > QRValidation.MaxInputLength) throw new SpecQRException("INVALID_INPUT", "Manual segment payload exceeds the resource limit.");
            list.Add(segment);
        }
        QRValidation.Controls(list);
        if ((options.Gs1 || options.Fnc1Second is not null) && list.Any(s => s.Mode == QRMode.Alphanumeric && s.Text!.Contains('%')))
            throw new SpecQRException(options.Gs1 ? "INVALID_GS1" : "INVALID_MODE", "FNC1 options with literal % in an alphanumeric segment are ambiguous; use Byte or explicit low-level FNC1 control segments with QR escaping.");
        var prepared = SegmentEncoder.ApplyingOptions(list, options);
        return Plan(options, inputBytes, null, _ => prepared);
    }

    public static QRCapacity GetCapacity(int version, ErrorCorrectionLevel errorCorrectionLevel = ErrorCorrectionLevel.M,
        QRMode? mode = null, int controlBits = 0)
    {
        QRValidation.Version(version); QRValidation.Level(errorCorrectionLevel);
        if (controlBits < 0) throw new SpecQRException("INVALID_INPUT", "ControlBits must be nonnegative.");
        if (mode is QRMode suppliedMode && !QRValidation.IsData(suppliedMode)) throw new SpecQRException("INVALID_MODE", "Capacity Mode must be Numeric, Alphanumeric, Byte, or Kanji.");
        var data = QRTables.DataCodewords(version, errorCorrectionLevel);
        int? countBits = mode.HasValue ? QRTables.CountBits(mode.Value, version) : null;
        int? payload = countBits.HasValue ? Math.Max(0, data * 8 - Math.Min(controlBits, data * 8) - 4 - countBits.Value) : null;
        int? maximum = mode switch
        {
            QRMode.Numeric => payload / 10 * 3 + (payload % 10 >= 7 ? 2 : payload % 10 >= 4 ? 1 : 0),
            QRMode.Alphanumeric => payload / 11 * 2 + (payload % 11 >= 6 ? 1 : 0),
            QRMode.Byte => payload / 8,
            QRMode.Kanji => payload / 13,
            _ => null
        };
        return new(version, errorCorrectionLevel, data, QRTables.RawCodewords(version), mode,
            countBits, controlBits, payload, mode == QRMode.Byte ? null : maximum, mode == QRMode.Byte ? maximum : null);
    }

    private static QRPlan Plan(QROptions options, int inputBytes, QRGS1ValidationDiagnostics? gs1Validation, Func<int, QRSegment[]> create)
    {
        var lower = options.Version ?? options.MinVersion;
        var upper = options.Version ?? options.MaxVersion;
        for (var version = lower; version <= upper; version++)
        {
            var segments = create(version);
            var bits = segments.Sum(s => SegmentEncoder.BitLength(s, version));
            var fits = bits <= QRTables.DataCodewords(version, options.ErrorCorrectionLevel) * 8;
            if (!fits && version != upper) continue;
            var level = options.ErrorCorrectionLevel;
            if (fits && options.BoostErrorCorrection)
                for (var candidate = level + 1; candidate <= ErrorCorrectionLevel.H; candidate++)
                    if (bits <= QRTables.DataCodewords(version, candidate) * 8) level = candidate;
            var selection = options.Version.HasValue ? "fixed" : fits ? "auto-minimum" : "auto-range";
            var reason = options.Version.HasValue ? $"Version {version} was requested explicitly."
                : fits ? $"Version {version} is the smallest version in {lower}..{upper} that fits the encoded data at error correction {options.ErrorCorrectionLevel}."
                : $"No version in {lower}..{upper} fits the encoded data at error correction {options.ErrorCorrectionLevel}; capacity is reported for version {version}.";
            var hasFnc1 = segments.Any(s => s.Mode == QRMode.Fnc1);
            return new(fits, fits || options.Version.HasValue ? version : null, version, options, level,
                selection, reason, segments, bits, QRTables.DataCodewords(version, level) * 8, inputBytes,
                gs1Validation ?? new(hasFnc1, hasFnc1 ? null : 0, [], false));
        }
        throw new SpecQRException("INVALID_VERSION", "Empty version range.");
    }

    private static QRCode Build(QRPlan plan, QROptions options)
    {
        if (!plan.Ok) throw plan.Error!;
        var version = plan.SelectedVersion!.Value;
        var data = SegmentEncoder.Encode(plan.Segments, version, plan.ErrorCorrectionLevel);
        var codewords = ReedSolomon.Interleave(data, version, plan.ErrorCorrectionLevel);
        var built = MatrixBuilder.Build(codewords, version, plan.ErrorCorrectionLevel, options.MaskPattern);
        return new(built, data, codewords, plan);
    }
}
