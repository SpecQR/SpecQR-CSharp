namespace SpecQR;

/// <summary>A GS1 application identifier and its raw value. Leading zeros are significant.</summary>
public sealed record GS1Element(string Ai, string Value);

public enum GS1ValueKind { Numeric, Text, Date, Decimal }
public enum GS1CheckDigitRule { None, Gtin, Sscc }
public enum GS1DigitalLinkRole { PrimaryKey, KeyQualifier, DataAttribute, NotSupported }
public enum GS1ValidationContext { ElementString, DigitalLink }
public enum GS1UnknownQueryPolicy { Preserve, Reject }

/// <summary>Fixed or bounded variable length metadata in characters (all supported values are ASCII).</summary>
public sealed record GS1AiLength(int? Exact, int Min, int Max)
{
    public bool IsVariable => Exact is null;
    public string Type => IsVariable ? "variable" : "fixed";
}

public sealed record GS1AiInfo(string Ai, string Label, GS1AiLength Length,
    GS1ValueKind ValueKind, GS1CheckDigitRule CheckDigitRule, GS1DigitalLinkRole DigitalLinkRole,
    IReadOnlyList<string>? DigitalLinkPathForPrimary)
{
    public string Separator => Length.IsVariable ? "required-when-followed" : "none";
}

public sealed record GS1ElementStringParseResult(IReadOnlyList<GS1Element> Elements, bool HasSeparators);

public sealed record GS1ValidationOptions
{
    public GS1ValidationContext Context { get; init; } = GS1ValidationContext.ElementString;
    /// <summary>Unsupported AIs are never accepted; setting this flag reports invalid options.</summary>
    public bool AllowUnsupportedAi { get; init; }
    public bool CollectAllErrors { get; init; } = true;
}

public sealed record GS1ValidationIssue(string Code, string Message)
{
    public string? Ai { get; init; }
    public string? Value { get; init; }
    public string? Key { get; init; }
    /// <summary>UTF-16 offset, matching the upstream JavaScript string index.</summary>
    public int? Offset { get; init; }
    public int? ElementIndex { get; init; }
    public string? Reason { get; init; }
    public object? Expected { get; init; }
    public int? Count { get; init; }
}

public sealed record GS1ValidationResult(bool Ok, IReadOnlyList<GS1Element>? Elements,
    bool? HasSeparators, IReadOnlyList<GS1ValidationIssue> Errors, IReadOnlyList<GS1ValidationIssue> Warnings);

public sealed record GS1DigitalLinkOptions
{
    public GS1DigitalLinkOptions(string baseUrl) { BaseUrl = baseUrl; }
    public GS1DigitalLinkOptions(Uri baseUrl) { BaseUrl = baseUrl?.OriginalString!; }
    public string BaseUrl { get; init; }
    public string PrimaryAi { get; init; } = "01";
    /// <summary>Null uses eligible qualifiers. An empty list puts all non-primary AIs in the query.</summary>
    public IReadOnlyList<string>? PathAis { get; init; }
}

public sealed record GS1DigitalLinkParseOptions
{
    public string? PrimaryAi { get; init; }
    public GS1UnknownQueryPolicy UnknownQuery { get; init; } = GS1UnknownQueryPolicy.Preserve;
}

public sealed record GS1DigitalLinkValidationOptions
{
    public string? PrimaryAi { get; init; }
    public GS1UnknownQueryPolicy UnknownQuery { get; init; } = GS1UnknownQueryPolicy.Preserve;
    /// <summary>Unsupported; use NormalizeDigitalLink for deterministic normalization.</summary>
    public bool Normalize { get; init; }
}

public sealed record GS1DigitalLinkNormalizeOptions
{
    public string? PrimaryAi { get; init; }
    public GS1UnknownQueryPolicy UnknownQuery { get; init; } = GS1UnknownQueryPolicy.Preserve;
    /// <summary>Only specqr-deterministic is supported. This does not claim GS1 canonicalization.</summary>
    public string Mode { get; init; } = "specqr-deterministic";
}

public sealed record GS1UnknownQuery(string Key, string Value);
public sealed record GS1DigitalLinkParseResult(IReadOnlyList<GS1Element> Elements, GS1Element Primary,
    IReadOnlyList<GS1Element> PathElements, IReadOnlyList<GS1Element> QueryElements,
    IReadOnlyList<GS1UnknownQuery> UnknownQuery);
public sealed record GS1DigitalLinkValidationResult(bool Ok, GS1DigitalLinkParseResult? Result,
    IReadOnlyList<GS1ValidationIssue> Errors, IReadOnlyList<GS1ValidationIssue> Warnings);
