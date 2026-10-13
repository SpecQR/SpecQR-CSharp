# Structured Append

`QRCode.GenerateStructuredAppend(textOrBytes, options)` は 2〜16 シンボルを生成します。インデックスは公開 API では 1 始まり、QR のヘッダーでは 0 始まりです。全シンボルでバージョンと ECC は共通です。自動バージョン選択は、最少シンボル数より分割可能な最小バージョンを優先します。各分割片は容量内に収まる最大の接頭部を選び、テキストを Unicode スカラーの途中で分割しません。

```csharp
var set = QRCode.GenerateStructuredAppend(new string('A', 60),
    new QRStructuredAppendOptions {
        QrOptions = new QROptions { Version = 1, ErrorCorrectionLevel = ErrorCorrectionLevel.L },
        MaxSymbols = 16
    });
foreach (var symbol in set.Symbols) Console.WriteLine(symbol.ToSvg());
```

ヘッダーを含めて 1 シンボルに収まる入力は拒否します。通常は `Generate` を使用してください。ECI、GS1/FNC1、ECC 引き上げと高水準 SA は組み合わせません。低水準のヘッダーは `QROptions.StructuredAppend` または先頭の `QRSegment.StructuredAppend` で明示できます。

`GenerateSegmentsStructuredAppend` は Numeric / Alphanumeric / Kanji のセグメント境界を保持し、Byte テキストはスカラー、バイナリはバイトで分割します。Byte 以外のセグメント自体が一つのシンボルに収まらない場合は、利用者がそのセグメントを分割します。空セグメント・制御セグメントは高水準の手動 SA に渡せません。

`CalculateStructuredAppendParity` は元のテキストの UTF-8 バイト列、またはバイナリの生バイト列の XOR です。`CalculateStructuredAppendSegmentsParity` の Kanji も UTF-8 で、Shift_JIS の符号化ビット列ではありません。

`MergeStructuredAppendParts` はデコーダーが返したインデックス、総数、パリティとペイロードを検査します。欠落・重複・型の不一致・パリティの不一致を拒否し、正しい順番に結合します。画像の読み取りやスキャナーのメタデータ対応を追加する API ではありません。XOR パリティは完全性検証・認証の代わりにはなりません。

診断は標準で分割単位の数とシンボル範囲のみ保持します。`SplitUnits = QRStructuredAppendSplitUnitsDetail.Full` で単位ごとの情報を要求できます。詳細な診断は手動セグメント入力のみ有効です。既定では入力バイトごとの診断オブジェクトを作りません。

資源保護としてテキスト・正準化した手動メッセージと、復号後に結合するデータ全体は 1,000,000 バイト以下、手動セグメントは 32,768 以下です。実際の QR 容量はこれより小さく、ビット容量を先に検査します。分割探索は範囲を制限し、テキストのビット数を線形に追跡します。
