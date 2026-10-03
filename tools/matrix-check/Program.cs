using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text.Json;
using SpecQR;

static string Hash(IEnumerable<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes.ToArray())).ToLowerInvariant();
static string MatrixHash(bool[][] rows) => Hash(rows.SelectMany(row => row.Select(value => value ? (byte)1 : (byte)0)));
int cases = 0;
for (int version = 1; version <= 40; version++)
{
    Console.WriteLine($"table {version} {QRTables.Size(version)} {QRTables.RawCodewords(version)} {string.Join(',', QRTables.AlignmentCenters(version))}");
    foreach (ErrorCorrectionLevel level in Enum.GetValues<ErrorCorrectionLevel>())
    {
        int capacity = QRTables.DataCodewords(version, level);
        for (int seed = 0; seed < 3; seed++)
        {
            var data = Enumerable.Range(0, capacity).Select(index => seed switch {
                0 => (byte)0, 1 => (byte)255,
                _ => (byte)(((index * 149 + version * 43 + (int)level * 89 + seed * 67) ^ (index >> (seed + 1))) & 255)
            }).ToArray();
            byte[] saved = (byte[])data.Clone();
            byte[] codewords = ReedSolomon.Interleave(data, version, level);
            if (!data.SequenceEqual(saved)) throw new Exception("Interleave changed input");
            string codewordHash = Hash(codewords);
            for (int mask = -1; mask < 8; mask++)
            {
                var result = MatrixBuilder.Build(codewords, version, level, mask < 0 ? null : mask);
                Console.WriteLine($"case {version} {level} {seed} {mask} {capacity} {codewordHash} {MatrixHash(result.Matrix)} {result.MaskPattern} {result.Penalty} {string.Join(',', result.Penalties.Select(p => p.Penalty))}");
                cases++;
            }
        }
    }
}

var products = new byte[256 * 256];
for (int left = 0; left < 256; left++)
    for (int right = 0; right < 256; right++)
        products[left * 256 + right] = ReedSolomon.Multiply((byte)left, (byte)right);
Console.WriteLine("gf " + Hash(products));
for (int degree = 1; degree <= 255; degree++)
{
    byte[] generator = ReedSolomon.Generator(degree);
    byte[] input = Enumerable.Range(0, 300).Select(n => (byte)(n * 61 + degree)).ToArray();
    Console.WriteLine($"rs {degree} {Hash(generator)} {Hash(ReedSolomon.Remainder(input, generator))}");
}

int negative = 0;
void Reject(Action action)
{
    try { action(); }
    catch (ArgumentException) { negative++; return; }
    throw new Exception("Expected input rejection");
}
foreach (int version in new[] {int.MinValue, -1, 0, 41, int.MaxValue})
{
    Reject(() => QRTables.RawCodewords(version));
    Reject(() => QRTables.Size(version));
    Reject(() => QRTables.CountBits(QRMode.Byte, version));
    Reject(() => QRTables.AlignmentCenters(version));
    Reject(() => ReedSolomon.Interleave([], version, ErrorCorrectionLevel.M));
    Reject(() => MatrixBuilder.Build([], version, ErrorCorrectionLevel.M, null));
}
foreach (int value in new[] {-1, 4, int.MinValue, int.MaxValue})
{
    Reject(() => QRTables.DataCodewords(1, (ErrorCorrectionLevel)value));
    Reject(() => QRTables.FormatBits((ErrorCorrectionLevel)value));
    Reject(() => MatrixBuilder.Build(new byte[26], 1, (ErrorCorrectionLevel)value, null));
}
Reject(() => QRTables.CountBits(QRMode.Auto, 1));
Reject(() => QRTables.CountBits(QRMode.Mixed, 1));
Reject(() => QRTables.CountBits((QRMode)int.MaxValue, 1));
Reject(() => ReedSolomon.Interleave(null!, 1, ErrorCorrectionLevel.L));
Reject(() => ReedSolomon.Interleave(new byte[20], 1, ErrorCorrectionLevel.L));
Reject(() => ReedSolomon.Interleave(new byte[18], 1, ErrorCorrectionLevel.L));
Reject(() => MatrixBuilder.Build(null!, 1, ErrorCorrectionLevel.L, null));
Reject(() => MatrixBuilder.Build(new byte[25], 1, ErrorCorrectionLevel.L, null));
Reject(() => MatrixBuilder.Build(new byte[27], 1, ErrorCorrectionLevel.L, null));
Reject(() => MatrixBuilder.Build(new byte[26], 1, ErrorCorrectionLevel.L, -1));
Reject(() => MatrixBuilder.Build(new byte[26], 1, ErrorCorrectionLevel.L, 8));
Reject(() => ReedSolomon.Generator(0));
Reject(() => ReedSolomon.Generator(256));
Reject(() => ReedSolomon.Remainder([], []));
Reject(() => ReedSolomon.Remainder([], [1]));
Reject(() => ReedSolomon.Remainder([], [2, 1]));
Reject(() => ReedSolomon.Remainder([], new byte[257]));
Reject(() => MaskPenaltyEvaluator.Score([], 0));
Reject(() => MaskPenaltyEvaluator.Score(new bool[4], int.MaxValue));
Reject(() => MaskPenaltyEvaluator.Score(new bool[3], 2));

byte[] source = Enumerable.Range(0, QRTables.DataCodewords(32, ErrorCorrectionLevel.Q)).Select(n => (byte)n).ToArray();
byte[] reference = ReedSolomon.Interleave(source, 32, ErrorCorrectionLevel.Q);
string expected = MatrixHash(MatrixBuilder.Build(reference, 32, ErrorCorrectionLevel.Q, null).Matrix);
Parallel.For(0, 128, _ => {
    byte[] encoded = ReedSolomon.Interleave(source, 32, ErrorCorrectionLevel.Q);
    if (!encoded.SequenceEqual(reference)) throw new Exception("Concurrent interleave mismatch");
    if (MatrixHash(MatrixBuilder.Build(encoded, 32, ErrorCorrectionLevel.Q, null).Matrix) != expected)
        throw new Exception("Concurrent matrix mismatch");
});
Console.Error.WriteLine($"PASS {cases} matrices, 65536 GF products, 255 generator/remainder cases, {negative} negative cases, 128 parallel builds");
Console.Error.WriteLine("RESULT " + JsonSerializer.Serialize(new {
    targetFramework = AppContext.TargetFrameworkName,
    runtime = RuntimeInformation.FrameworkDescription,
    architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    matrices = cases,
    galoisProducts = 65536,
    generatorRemainderCases = 255,
    negativeCases = negative,
    concurrentBuilds = 128
}));

// Only type contracts are stubbed in this isolated internal-unit harness.
// The actual runtime implementation is compile-linked by MatrixCheck.csproj.
namespace SpecQR
{
    public enum ErrorCorrectionLevel { L, M, Q, H }
    public enum QRMode { Auto, Numeric, Alphanumeric, Byte, Kanji, Eci, Fnc1, Fnc1Second, StructuredAppend, Mixed }
    public sealed record QRMaskPenalty(int MaskPattern, int Penalty);
}
