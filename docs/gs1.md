# GS1

Dependency-free GS1 helpers for the bounded 50-AI SpecQR catalog. This is not a complete GS1 specification validator. Values preserve leading zeros; URL normalization is deterministic SpecQR behavior, not GS1 canonicalization.

C# 版では `SpecQR.GS1` と不変の `GS1Element` を利用します。値には印字可能 ASCII を使い、括弧と ASCII GS（U+001D）を含めません。人間向け `(AI)value` 表記の括弧は `ParseHumanReadable` が解釈します。

```csharp
using SpecQR;

var elements = GS1.ParseHumanReadable("(01)04912345678904(10)LOT-A(17)251231");
string raw = GS1.CreateElementString(elements);
// 010491234567890410LOT-A\u001D17251231
var parsed = GS1.ParseElementString(raw);
var validation = GS1.ValidateElements(elements);
var qr = QRCode.Generate(raw, new() { Gs1 = true });

string link = GS1.CreateDigitalLink(elements,
    new GS1DigitalLinkOptions("https://example.com/products"));
// https://example.com/products/01/04912345678904/10/LOT-A?17=251231
```

## 対応 AI

`GetSupportedAis()` は次の 50 個の具体的な AI を安定した順序で返します。`GetAiInfo(ai)` は長さ、数値・テキスト、チェックデジット、Digital Link の配置情報を返し、未対応 AI では `null` を返します。

| AI | 値の型・長さ | 意味 |
| --- | --- | --- |
| 00 | 数字 18 桁、SSCC チェックデジット | 物流単位識別コード |
| 01, 02 | 数字 14 桁、GTIN チェックデジット | GTIN、内容品 GTIN |
| 10 | テキスト 1–20 文字 | ロット |
| 11, 12, 13, 15, 16, 17 | 数字 6 桁 | 各種日付 |
| 20 | 数字 2 桁 | 商品バリエーション |
| 21, 22 | テキスト 1–20 文字 | シリアル、消費者向け商品バリエーション |
| 30, 37 | 数字 1–8 桁 | 数量 |
| 240, 241, 400 | テキスト 1–30 文字 | 商品・顧客部品・注文番号 |
| 410, 411, 412, 413, 414, 415 | 数字 13 桁 | 各種 GLN |
| 420 | テキスト 1–20 文字 | 郵便番号 |
| 422, 424, 425, 426 | 数字 3 桁 | 国コード |
| 3100–3105, 3200–3205 | 数字 6 桁 | kg・lb の重量 |
| 91–99 | テキスト 1–90 文字 | 企業内部情報 |

日付の暦としての妥当性、国コードの実在、GLN チェックデジットは検証しません。AI 3106、3206、90 などは未対応です。GS1 全仕様・適用業種の組み合わせ制約を満たす保証はありません。

## 要素文字列と検証

`CreateElementString` は最後以外の可変長要素の後ろに `GS1.Fnc1Separator`（U+001D）を挿入します。固定長要素の直後や末尾の区切りは `ParseElementString` が拒否します。最後の可変長値が固定長 AI とその値らしい末尾を持つ場合、区切り欠落の疑いとして保守的に拒否します。この既存 SpecQR のヒューリスティックは、末尾の値自体の妥当性を検証せず、正しいロット文字列を曖昧として拒否する場合もあります。

`ValidateElements` / `ValidateElementString` は `Ok`、`Errors`、`Warnings` と成功時の `Elements` を返します。`CollectAllErrors = false` は個別要素検証を最初のエラーで停止します。`Context = GS1ValidationContext.DigitalLink` は主要 AI の存在を要求しますが、重複や URL 内の配置まで検証しません。配置を検証するには `ValidateDigitalLink` を利用してください。生の文字列の解析は最初のエラーで止まり、`Context` と `CollectAllErrors` によって挙動を変えません。

未対応 AI を許可する拡張はありません。`AllowUnsupportedAi = true` は不正なオプションとして失敗します。スローする API の検証エラーは `SpecQRException.Code == "INVALID_GS1"` です。各 validator は不正な入力を構造化結果で返します。呼び出し元が作った `IEnumerable` 自身の例外、メモリ不足などの実行環境例外は捕捉しません。

`CalculateCheckDigit` / `ValidateCheckDigit` は汎用 modulo-10、`CalculateGtinCheckDigit` / `AppendGtinCheckDigit` / `ValidateGtinCheckDigit` は GTIN、`Sscc` を含む同名メソッドは SSCC 用です。GTIN 本体は 7・11・12・13 桁、SSCC 本体は 17 桁です。`Validate*CheckDigit` は形式不正で例外を投げ、正しい長さ・文字種でチェックデジットだけが異なる場合に `false` を返します。

## Digital Link

`GS1DigitalLinkOptions` には明示的な `BaseUrl` を指定します。主要 AI は 01 が既定で、00・414 も指定できます。主要 AI 01 の後ろには 10・21・22 をパス修飾子として配置できます。他の AI はクエリに配置します。主要 AI 00・414 に対応するパス修飾子はありません。

パス修飾子は入力順、GS1 クエリは AI の文字列順で出力します。`PathAis = []` は主要 AI 以外をすべてクエリへ置きます。GS1 AI の重複はパスとクエリを通じて拒否します。未知の数値 AI を黙って通常クエリとして扱うことはありません。

```csharp
var parsedLink = GS1.ParseDigitalLink(link);
var checkedLink = GS1.ValidateDigitalLink(link,
    new() { UnknownQuery = GS1UnknownQueryPolicy.Reject });
string normalized = GS1.NormalizeDigitalLink(link);
```

非 GS1 クエリの既定ポリシーは `Preserve` です。同じキーを複数回含められ、入力順を保持します。`Reject` はそのようなクエリを拒否します。`ValidateDigitalLink` は HTTP と未知クエリ保持について警告します。正規化は適格なクエリ AI をパスへ移し、GS1 クエリを並べ、未知クエリを元の相対順で末尾へ付けます。`Mode` は `"specqr-deterministic"` のみです。

HTTP(S) のみ対応し、非空フラグメントを拒否します。パス値の percent escape は厳密な UTF-8 として解釈します。既存 API と同じく `ParseDigitalLink` のクエリはフォームデコードで不正な escape を寛容に保持・置換します。厳密な `%HH` 構文検証には `ValidateDigitalLink` または `NormalizeDigitalLink` を利用してください。

BCL の IDNA と IPv6 機能を利用し、SpecQR が必要とする URL パス・クエリ・IPv4 の正規化を実装しています。ブラウザ URL API 全体を再実装・保証するものではありません。ネットワーク通信は行いません。

## データ保持のための意図的な差分

AI 値が `.` または `..` で、それを Digital Link のパスに配置すると、既存 JavaScript / Swift 版は URL の dot-segment 処理により値を失います。C# 版はその操作を拒否します。`PathAis = []` でクエリに置くと値を維持できます。文字列 `%2e` は `%252e` にエンコードされ、通常のデータとして維持します。

高水準 QR API の GS1/FNC1 データに含まれるリテラル `%` は payload として保持します。自動モードは安全な byte エンコードを選び、安全でない alphanumeric の明示指定は拒否します。低水準セグメント API の FNC1 alphanumeric は QR 規格の `%`・`%%` の意味をそのまま扱うため、呼び出し元がエスケープを管理します。

## リソース上限

`GS1.MaxInputCharacters = 1_000_000`（UTF-16 コード単位）、`GS1.MaxElements = 16_384` です。生文字列・人間向け文字列・Digital Link URI・汎用チェックデジット入力に文字数上限を適用します。要素の列挙は 16,384 個で制限されるため、無限 `IEnumerable` を最後まで列挙しません。Digital Link のパス区切りによるセグメント数とクエリ pair 数にも同じ上限があります。AI 値は上表のさらに小さい上限で検証します。

通常の GS1 データの意味を変えず、極端な入力による無制限な確保・走査を避けるための C# 版の明示的な制限です。これは QR の格納容量とは別です。
