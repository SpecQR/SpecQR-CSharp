# SpecQR-CSharp

SpecQR-CSharp is a from-scratch QR Code Model 2 encoder for .NET 8 and .NET 10. It uses only the .NET base class library: no third-party runtime packages, native graphics dependencies, or QR engines. SVG, PNG, and RGBA rendering are portable across Windows, Linux, and macOS.

SpecQR の仕様・動作契約を C# で実装したライブラリです。エンコーダー、Reed–Solomon、配置・マスク評価、GS1 処理、PNG のチャンク構築まで自前実装し、文字列・ストリーム・zlib などの汎用処理には .NET 標準ライブラリを使用します。

## はじめる

リポジトリのビルドには **.NET 8 SDK と .NET 10 SDK の両方**をインストールしてください。`global.json` は .NET 10 SDK を選択し、.NET 8 SDK はオフライン復元に必要な targeting pack と .NET 8 実行環境を提供します。NuGet ソースを空にしているため、不足する pack を外部から自動取得しません。ターゲットは `net8.0` / `net10.0`、言語バージョンは C# 12 です。

```sh
git clone https://github.com/SpecQR/SpecQR-CSharp.git
cd SpecQR-CSharp
dotnet build src/SpecQR/SpecQR.csproj -c Release
dotnet run --project examples/Basic -f net10.0
```

.NET 8 での実行は、両 SDK を用意した同じ環境で対象を切り替えます。

```sh
dotnet run --project examples/Basic -f net8.0
```

既存アプリからは `src/SpecQR/SpecQR.csproj` に `ProjectReference` を追加してください。NuGet へのパッケージ公開は行っていません。

```csharp
using SpecQR;

var qr = QRCode.Generate("こんにちは SpecQR", new QROptions
{
    ErrorCorrectionLevel = ErrorCorrectionLevel.Q,
    Eci = 26 // Byte セグメントの UTF-8 を明示
});

File.WriteAllText("qr.svg", qr.ToSvg());
File.WriteAllBytes("qr.png", qr.ToPng());
bool[][] modules = qr.Matrix; // [row][column]、true が黒。防御的コピー。
Console.WriteLine($"Version {qr.Version}, mask {qr.MaskPattern}");
```

## 対応範囲

| 分野 | 内容 |
| --- | --- |
| QR Model 2 | バージョン 1–40、ECC L/M/Q/H、マスク 0–7 と自動選択 |
| 入力 | Numeric / Alphanumeric / UTF-8 Byte / QR Kanji、バイナリ、混合最適化、手動セグメント |
| 制御 | ECI、FNC1 第 1・第 2 位置、Structured Append |
| 計画・診断 | `Estimate` / `AnalyzeSegments` / `GetCapacity`、選択理由、容量、コードワード、各マスクのペナルティ |
| GS1 | 対応 AI の限定辞書、チェックデジット、要素文字列、検証、Digital Link の作成・解析・正規化 |
| 分割 | 最大 16 シンボルの Structured Append、自動分割・手動セグメント分割・パリティ検証付き再結合 |
| 描画 | SVG / PNG / data URL / RGBA、ストリーム出力、ホスト側 canvas インターフェース |
| スキャン助言 | quiet zone、コントラスト、透明度、印刷寸法、容量・ピクセル密度の警告 |

GS1 の全 AI・全業務規則に対応するという主張ではありません。対応 AI は `GS1.GetSupportedAis()` で確認できます。Micro QR、rMQR、画像からのデコードは対象外です。

## CLI

```sh
dotnet run --project src/SpecQR.Cli -f net10.0 -- encode "HELLO WORLD" --format png --output qr.png
dotnet run --project src/SpecQR.Cli -f net10.0 -- encode --hex 000102ff --format json
dotnet run --project src/SpecQR.Cli -f net10.0 -- estimate "1234567890" --ecc H
```

CLI は外部コマンド・外部パッケージを呼び出しません。基本的なエンコード・容量計画・Structured Append を提供します。GS1 の辞書操作・Digital Link・手動セグメントなどはライブラリ API を使用します。詳細は [CLI](docs/cli.md) を参照してください。

## 意味を変えずに扱うために

- 通常の文字列の Byte セグメントは UTF-8 です。ECI は省略時に勝手に挿入せず、必要なら `Eci = 26` を指定します。ECI 指定は文字コード変換の要求ではありません。
- 高水準の `Gs1` / `Fnc1Second` で文字列にリテラル `%` がある場合、自動モードは Byte を選びます。明示的 Alphanumeric は曖昧な変換を避けるため拒否します。
- 明示的な `QRSegment.Fnc1()` と Alphanumeric の組合せは低水準 API です。QR の FNC1 規則では `%` が区切り、`%%` がリテラル `%` です。呼び出し側でエスケープしてください。
- Structured Append のパリティは誤った組合せの検出を補助しますが、認証・暗号学的な完全性検証ではありません。また、スキャナー側の対応が必要です。

## ドキュメントと検証

- [API と実例](docs/api.md)
- [描画・リソース上限・スキャン助言](docs/rendering.md)
- [CLI 入出力と終了コード](docs/cli.md)
- [GS1 対応 AI と Digital Link](docs/gs1.md)
- [Structured Append](docs/structured-append.md)
- [機能一覧と受入条件](docs/feature-parity.md)
- [互換性と意図的な差分](docs/compatibility.md)
- [検証結果](docs/verification.md)・[独立レビュー](docs/integrated-review.md)
- [再現できるテスト手順](tests/README.md)・[出典と依存関係](docs/provenance.md)

実行環境は Windows / Linux / macOS 上の通常の .NET 8 / 10 を対象としています。.NET Framework、Unity、ブラウザー WASM、Native AOT については検証済みという主張をしていません。現行 .NET の標準 API を使うことで、System.Drawing やプラットフォーム別画像ライブラリへの依存を避けています。

リポジトリのテスト実行プログラムも NuGet テストフレームワークに依存しません。独立した参照エンコーダー・デコーダーを利用する追加検証は開発時だけのもので、ライブラリや配布物へ取り込みません。実際の検証範囲・比較元コミット・実行結果は、リポジトリ内の検証資料と GitHub Actions の結果を参照してください。

## ライセンス・由来

[MIT](LICENSE)。SpecQR のユーザー所有 JavaScript 実装 [SpecQR/SpecQR](https://github.com/SpecQR/SpecQR)、Swift 実装 [SpecQR/SpecQR-Swift](https://github.com/SpecQR/SpecQR-Swift)、[Conformance Lab](https://github.com/SpecQR/SpecQR-Conformance-Lab) の契約を基準にした C# 実装です。第三者の QR ランタイムソースをコピーした実装ではありません。参照ツールと比較ベクトルの由来はそれぞれの検証資料で区別します。
