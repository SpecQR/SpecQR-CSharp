namespace SpecQR;

/// <summary>GS1 helpers for the deliberately bounded SpecQR catalog, not a full GS1 validator.</summary>
public static partial class GS1
{
    public const string Fnc1Separator = "\u001D";
    private static readonly IReadOnlyList<GS1AiInfo> SupportedAis = BuildCatalog();
    private static readonly IReadOnlyDictionary<string, GS1AiInfo> AiDictionary =
        SupportedAis.ToDictionary(i => i.Ai, StringComparer.Ordinal);

    /// <summary>Returns the 50 supported concrete AIs in stable upstream order.</summary>
    public static IReadOnlyList<GS1AiInfo> GetSupportedAis() => SupportedAis;
    public static GS1AiInfo? GetAiInfo(string ai) => ai is not null && AiDictionary.TryGetValue(ai, out var info) ? info : null;

    private static IReadOnlyList<GS1AiInfo> BuildCatalog()
    {
        static GS1AiInfo Entry(string ai, string label, int length, bool variable = false,
            GS1ValueKind kind = GS1ValueKind.Numeric, GS1CheckDigitRule check = GS1CheckDigitRule.None,
            GS1DigitalLinkRole role = GS1DigitalLinkRole.DataAttribute) =>
            new(ai, label, new(variable ? null : length, variable ? 1 : length, length), kind, check, role,
                role == GS1DigitalLinkRole.KeyQualifier ? Array.AsReadOnly(new[] { "01" }) : null);
        var entries = new List<GS1AiInfo>
        {
            Entry("00", "Serial shipping container code", 18, check: GS1CheckDigitRule.Sscc, role: GS1DigitalLinkRole.PrimaryKey),
            Entry("01", "Global trade item number", 14, check: GS1CheckDigitRule.Gtin, role: GS1DigitalLinkRole.PrimaryKey),
            Entry("02", "Contained trade item GTIN", 14, check: GS1CheckDigitRule.Gtin),
            Entry("10", "Batch or lot number", 20, true, GS1ValueKind.Text, role: GS1DigitalLinkRole.KeyQualifier),
            Entry("11", "Production date", 6), Entry("12", "Due date", 6), Entry("13", "Packaging date", 6),
            Entry("15", "Best before date", 6), Entry("16", "Sell by date", 6), Entry("17", "Expiration date", 6),
            Entry("20", "Internal product variant", 2),
            Entry("21", "Serial number", 20, true, GS1ValueKind.Text, role: GS1DigitalLinkRole.KeyQualifier),
            Entry("22", "Consumer product variant", 20, true, GS1ValueKind.Text, role: GS1DigitalLinkRole.KeyQualifier),
            Entry("30", "Variable count", 8, true), Entry("37", "Count of contained trade items", 8, true),
            Entry("240", "Additional product identification", 30, true, GS1ValueKind.Text),
            Entry("241", "Customer part number", 30, true, GS1ValueKind.Text),
            Entry("400", "Customer purchase order number", 30, true, GS1ValueKind.Text),
            Entry("410", "Ship to global location number", 13), Entry("411", "Bill to global location number", 13),
            Entry("412", "Purchased from global location number", 13), Entry("413", "Ship for global location number", 13),
            Entry("414", "Identification of a physical location", 13, role: GS1DigitalLinkRole.PrimaryKey),
            Entry("415", "Global location number of the invoicing party", 13),
            Entry("420", "Ship to postal code", 20, true, GS1ValueKind.Text),
            Entry("422", "Country of origin", 3), Entry("424", "Country of processing", 3),
            Entry("425", "Country of disassembly", 3), Entry("426", "Country covering full process chain", 3)
        };
        for (int ai = 3100; ai <= 3105; ai++) entries.Add(Entry(ai.ToString(System.Globalization.CultureInfo.InvariantCulture), "Net weight in kilograms", 6));
        for (int ai = 3200; ai <= 3205; ai++) entries.Add(Entry(ai.ToString(System.Globalization.CultureInfo.InvariantCulture), "Net weight in pounds", 6));
        for (int ai = 91; ai <= 99; ai++) entries.Add(Entry(ai.ToString(System.Globalization.CultureInfo.InvariantCulture), "Company internal information", 90, true, GS1ValueKind.Text));
        return entries.AsReadOnly();
    }
}
