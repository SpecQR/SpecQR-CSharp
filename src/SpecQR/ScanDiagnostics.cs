using System.Collections.ObjectModel;

namespace SpecQR;

public enum QRWarningSeverity { Info, Warning }

public sealed record QRWarning(string Code, QRWarningSeverity Severity, string Message, IReadOnlyDictionary<string, object> Details);

/// <summary>Heuristic rendering advice, not a guarantee that a particular reader can scan a code.</summary>
public sealed record QRRenderDiagnostics(
    int Dimension, int QuietZoneModules, double? ContrastRatio, byte? ForegroundAlpha, byte? BackgroundAlpha,
    double? ModuleSizeMm, double? SymbolSizeMm, IReadOnlyList<string> Warnings, IReadOnlyList<QRWarning> WarningDetails)
{
    public bool IsQuietZoneSufficient => QuietZoneModules >= 4;
    public bool IsColorInspectable => ContrastRatio.HasValue;
    public bool IsColorStrong => ContrastRatio >= 7;
    public bool IsColorSufficient => ContrastRatio >= 4.5 && ForegroundAlpha == 255 && BackgroundAlpha == 255;
    public double RecommendedMinimumModuleSizeMm => 0.25;
    public bool? IsModuleSizeSufficient => ModuleSizeMm is double mm ? mm >= 0.25 : null;
}

public sealed partial class QRCode
{
    public QRRenderDiagnostics RenderDiagnostics(QRRenderOptions? options = null, bool raster = true) =>
        ScanDiagnostics.Create(Planning, options ?? new(), raster);
}

public static class QRPlanRenderingExtensions
{
    /// <summary>Inspects planned geometry without building codewords or a matrix. Overflow uses EvaluatedVersion; raster warnings are omitted.</summary>
    public static QRRenderDiagnostics RenderDiagnostics(this QRPlan plan, QRRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return ScanDiagnostics.Create(plan, options ?? new(), includeRasterScale: false);
    }
}

internal static class ScanDiagnostics
{
    internal static QRRenderDiagnostics Create(QRPlan plan, QRRenderOptions options, bool includeRasterScale)
    {
        var geometry = RenderGeometry.Create(plan.EvaluatedVersion * 4 + 17, options, raster: false);
        var foreground = RgbaColor.TryParse(options.Foreground);
        var background = RgbaColor.TryParse(options.Background);
        double? ratio = foreground is RgbaColor fg && background is RgbaColor bg
            ? (Math.Max(fg.Luminance, bg.Luminance) + 0.05) / (Math.Min(fg.Luminance, bg.Luminance) + 0.05) : null;
        var warnings = new List<QRWarning>();
        void Add(string code, QRWarningSeverity severity, string message, params (string Key, object Value)[] details) =>
            warnings.Add(new(code, severity, message, new ReadOnlyDictionary<string, object>(details.ToDictionary(d => d.Key, d => d.Value))));

        if (options.Margin < 4)
            Add("QUIET_ZONE_TOO_SMALL", QRWarningSeverity.Warning, "QR Code Model 2 readers expect a quiet zone of at least 4 modules.",
                ("margin", options.Margin), ("recommendedModules", 4));
        if (ratio is null)
            Add("COLOR_CONTRAST_UNKNOWN", QRWarningSeverity.Info, "Color contrast could not be inspected because one or both colors are not supported portable colors.",
                ("foreground", options.Foreground), ("background", options.Background));
        else if (ratio < 4.5)
            Add("COLOR_CONTRAST_LOW", QRWarningSeverity.Warning, "Foreground and background contrast is low for reliable scanning.",
                ("ratio", ratio.Value), ("recommendedMinimumRatio", 4.5));
        else if (ratio < 7)
            Add("COLOR_CONTRAST_MODERATE", QRWarningSeverity.Info, "Stronger contrast is recommended for damaged, small, or printed QR codes.",
                ("ratio", ratio.Value), ("strongRecommendedRatio", 7.0));
        if (foreground is RgbaColor front && background is RgbaColor back)
        {
            if (front.A < 255 || back.A < 255)
                Add("COLOR_ALPHA_USED", QRWarningSeverity.Warning, "Transparent colors can reduce scanner reliability.",
                    ("foregroundAlpha", front.A), ("backgroundAlpha", back.A));
            if (front.Luminance > back.Luminance)
                Add("COLOR_POLARITY_INVERTED", QRWarningSeverity.Warning, "A light foreground on a dark background is not supported by every scanner.");
        }
        // An overflowing plan is not an almost-full successful plan.
        if (plan.RemainingBits >= 0 && (double)plan.RemainingBits / plan.CapacityBits < 0.05)
            Add("CAPACITY_NEAR_LIMIT", QRWarningSeverity.Info, "The selected version is close to full capacity.",
                ("remainingBits", plan.RemainingBits), ("capacityBits", plan.CapacityBits));
        double? moduleSize = options.PrintDpi is double dpi ? geometry.Scale / dpi * 25.4 : null;
        if (moduleSize is double module && module < 0.25)
            Add("PRINT_MODULE_TOO_SMALL", QRWarningSeverity.Warning, "The configured scale and DPI produce modules smaller than the print recommendation.",
                ("dpi", options.PrintDpi!.Value), ("moduleSizeMm", module), ("recommendedMinimumModuleSizeMm", 0.25));
        if (includeRasterScale && geometry.Scale < 3)
            Add("RASTER_SCALE_SMALL", QRWarningSeverity.Info, "Raster output with fewer than 3 pixels per module may scan poorly after resizing.",
                ("scale", geometry.Scale), ("recommendedMinimumScale", 3));
        var blocking = warnings.Where(w => w.Severity == QRWarningSeverity.Warning).Select(w => w.Code).ToArray();
        if (blocking.Length > 0)
            Add("SCAN_RISK", QRWarningSeverity.Warning, "One or more settings may reduce scan reliability.",
                ("blockingWarnings", Array.AsReadOnly(blocking)));
        return new(geometry.Dimension, options.Margin, ratio, ratio.HasValue ? foreground?.A : null, ratio.HasValue ? background?.A : null,
            moduleSize, options.PrintDpi is double printDpi ? geometry.Dimension / printDpi * 25.4 : null,
            Array.AsReadOnly(warnings.Select(w => w.Code).ToArray()), warnings.AsReadOnly());
    }
}
