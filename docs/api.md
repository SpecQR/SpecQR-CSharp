# API と実例

公開型は `SpecQR` 名前空間にあります。設定は `record` の init-only プロパティ、生成結果は読み取り専用です。入力配列・行列・コードワードは防御的にコピーされ、結果を並行して読み取れます。各スレッドで同じ外部 stream や canvas を操作する場合の同期は呼び出し側で行います。

## 通常入力・バイナリ・手動セグメント

```csharp
var qr = QRCode.Generate("01234567890123", new QROptions
{
    ErrorCorrectionLevel = ErrorCorrectionLevel.M,
    MinVersion = 1, MaxVersion = 40,
    OptimizeSegments = true, BoostErrorCorrection = false
});
var raw = QRCode.Generate(new byte[] { 0, 0xff, 0x80 });
var mixed = QRCode.GenerateSegments(new[]
{
    QRSegment.Numeric("12345678901234567890"),
    QRSegment.Byte("hello"),
    QRSegment.Kanji("漢字")
});
```

`Version` は固定版、`MaskPattern` は固定マスクです。省略時は許された版の最小容量と全 8 マスクのペナルティを評価します。`BoostErrorCorrection` は選択済みの版を増やさず、収まる上位 ECC に引き上げます。`Mode` を Numeric / Alphanumeric / Byte / Kanji に固定した場合、不適合文字はエラーです。`Auto` は混合最適化を使用し、`OptimizeSegments = false` は単一モード選択に切り替えます。

Byte 文字列は UTF-8 です。`QRSegment.Byte(byte[])` は渡した値をそのまま符号化します。ECI を指定しても自動的に別の文字コードへ変換しません。QR Kanji は QR が許す Shift JIS 範囲の文字だけに対応し、OS のコードページに依存しません。文字列に未対応文字がある場合、Auto は UTF-8 Byte を選べます。不正な UTF-16 サロゲートは拒否します。

制御セグメントは `QRSegment.Eci(number)`、`Fnc1()`、`Fnc1Second(indicator)`、`StructuredAppend(index,total,parity)` です。範囲・順序・重複を検査します。高水準の GS1・ECI・FNC1 第 2 位置・Structured Append オプションは相互排他です。低水準 API でも ECI と FNC1 / Structured Append の組合せはこの契約では拒否します。

## 計画・容量・診断

```csharp
var plan = QRCode.Estimate("HELLO", new QROptions { Version = 1 });
var manualPlan = QRCode.AnalyzeSegments(new[] { QRSegment.Numeric("12345") });
var capacity = QRCode.GetCapacity(1, ErrorCorrectionLevel.M, QRMode.Byte);
if (!plan.Ok)
    Console.WriteLine($"Version {plan.EvaluatedVersion}: {plan.OverflowBits} bits over capacity");
```

`Estimate` / `AnalyzeSegments` は入力不正には例外、容量不足には `Ok = false` の `QRPlan` を返します。Reed–Solomon・マスク・行列を生成しません。`Generate` は容量不足にも `SpecQRException` を投げます。`Code` は安定したエラー識別子です。`RequiredBits` / `CapacityBits` / `Version` は容量エラー時の情報です。

`qr.Planning` はセグメント選択・版選択・データ容量、`qr.Diagnostics` はさらにコードワード数・選択マスク・評価ペナルティを持ちます。`DataCodewords` はパディング済みデータ、`Codewords` は ECC を加えたインターリーブ後のバイト列です。`Matrix` は quiet zone を含まない `bool[row][column]` です。

## GS1

```csharp
var elements = new[]
{
    new GS1Element("01", "09506000134352"),
    new GS1Element("10", "LOT42"),
    new GS1Element("17", "271231")
};
string raw = GS1.CreateElementString(elements);
GS1ValidationResult validation = GS1.ValidateElementString(raw);
var qr = QRCode.Generate(raw, new QROptions { Gs1 = true });

string uri = GS1.CreateDigitalLink(elements, new GS1DigitalLinkOptions("https://id.example.com"));
var parsed = GS1.ParseDigitalLink(uri);
string normalized = GS1.NormalizeDigitalLink(uri);
```

`ParseHumanReadable` は括弧付き AI 表記、`ParseElementString` は実際の要素文字列を扱います。AI の先頭ゼロを維持するため、AI と値は文字列です。可変長の要素が後続要素を持つ場合の GS 区切りは `CreateElementString` が挿入します。対象辞書は `GetSupportedAis` / `GetAiInfo` で確認してください。未対応 AI は受け付けません。

高水準 `Gs1` / `Fnc1Second` の `%` は入力ペイロードとして保持します。Auto は Byte を使用し、明示的 Alphanumeric は拒否します。低水準 FNC1 + Alphanumeric は QR の標準エスケープ規則（`%` が区切り、`%%` が文字 `%`）で渡します。この意味の差を理解した上で利用してください。

Digital Link の正規化モードは `specqr-deterministic` です。GS1 の包括的な canonicalization や、任意の URI の同値判定を提供するものではありません。未知のクエリーの保持・拒否を設定できます。GS1 文字列や URI はネットワークで送信・解決しません。

## Structured Append

```csharp
var set = QRCode.GenerateStructuredAppend(new string('A', 200), new QRStructuredAppendOptions
{
    QrOptions = new QROptions { Version = 1, ErrorCorrectionLevel = ErrorCorrectionLevel.M },
    MaxSymbols = 16
});
for (var i = 0; i < set.Symbols.Count; i++)
    File.WriteAllBytes($"part-{i + 1}.png", set.Symbols[i].ToPng());
```

バイナリ入力と `GenerateSegmentsStructuredAppend` もあります。2–16 シンボルを扱い、インデックスは API では **1 始まり**です。文字列の分割は Unicode スカラー境界を守ります。高水準の分割では GS1・ECI・FNC1・既存 SA ヘッダー・ECC boost を組み合わせず、手動分割はデータセグメントだけを受け付けます。手動分割に限り、細かな分割単位の診断を `SplitUnits = QRStructuredAppendSplitUnitsDetail.Full` で要求できます。

`CalculateStructuredAppendParity` / `CalculateStructuredAppendSegmentsParity` でパリティを計算し、スキャナーが返した各部を `QRStructuredAppendPart` として `MergeStructuredAppendParts` に渡せます。並べ替え・不足・重複・総数・パリティを検査します。本ライブラリ自身は画像からデコードしません。`Text` と `Bytes` のどちらを使うかはデコーダーが保持する実際のデータに合わせてください。

## リソースと互換性

入力は 1,000,000 UTF-16 コード単位または 1,000,000 バイトまでで、QR の実容量はそれより小さいため通常は先に容量制約に達します。制約の詳細と意図的な SpecQR JavaScript との差分は検証資料を参照してください。画像の上限は [描画](rendering.md) に記載しています。

日本語を含む診断・文書用の文字列とは別に、エラーコード・警告コードをプログラム上の判定に使ってください。JavaScript のオブジェクトをそのまま移した形ではなく、.NET の型付き API と例外・ストリームに合わせた設計です。
