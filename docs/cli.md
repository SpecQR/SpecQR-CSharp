# CLI

ビルド後は `dotnet src/SpecQR.Cli/bin/Release/net10.0/SpecQR.Cli.dll` で実行できます。開発時は次の形式を使えます。

```sh
dotnet run --project src/SpecQR.Cli -f net10.0 -- encode "HELLO WORLD" --format svg --output qr.svg
dotnet run --project src/SpecQR.Cli -f net10.0 -- estimate "HELLO WORLD" --ecc H
dotnet run --project src/SpecQR.Cli -f net10.0 -- structured-append "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789" --version 1 --ecc H
dotnet run --project src/SpecQR.Cli -f net10.0 -- --help
```

ビルドには .NET 8 SDK と .NET 10 SDK の両方が必要です。.NET 8 で実行する場合は `-f net8.0` に切り替えます。以降では実行部分を `SpecQR.Cli` と略記します。

## 入力

次のいずれか 1 つだけを指定します。

- `TEXT`：1 つの引数。空白を含むテキストはシェルで引用してください。
- `--hex HEX`：空白・接頭辞のない偶数桁の 16 進数。
- `--input-file PATH`：厳密な UTF-8 ファイル。`--binary` を加えると任意のバイト列。

空白・改行・UTF-8 BOM を自動削除しません。空文字列は引用した空引数として渡せます。`--` で始まる文字列は、オプションの後に `--` を置いて渡します。標準入力からの読み取りは提供しません。入力ファイルには 1 MiB の CLI 上限があり、ライブラリ自身の入力上限と QR 容量制約も適用されます。

```sh
SpecQR.Cli encode --hex 00017fff --format png --output binary.png
SpecQR.Cli encode --input-file payload.bin --binary --format matrix
SpecQR.Cli encode --format svg -- --literal-payload
```

## 設定

| オプション | 値・既定 |
| --- | --- |
| `--ecc` | L / M / Q / H。既定 M |
| `--mode` | auto / numeric / alphanumeric / byte / kanji。既定 auto |
| `--version` | 固定版 1–40 |
| `--min-version`, `--max-version` | 自動選択範囲。既定 1 / 40 |
| `--mask` | 0–7。省略は自動 |
| `--eci` | ECI assignment number |
| `--gs1` | 生の GS1 要素文字列を検証して FNC1 第 1 位置を挿入 |
| `--fnc1-second` | FNC1 第 2 位置の application indicator |
| `--boost` | 版を変えずに ECC を引き上げる |
| `--no-optimize` | 混合最適化を無効化 |
| `--max-symbols` | structured-append のみ。2–16、既定 16 |
| `--scale`, `--margin` | 正の pixel/module、非負の余白 module。既定 8 / 4 |
| `--width` | quiet zone 込み module 数の正の整数倍 |
| `--foreground`, `--background` | ポータブルな色。既定 #000000 / #ffffff |
| `--dpi`, `--title` | 印刷診断用 DPI、SVG の title |

数値は invariant culture で解析し、小数点には `.` を使います。オプション名は上表どおりで、同じオプションの重複は拒否します。CLI の手動セグメント入力は未提供です。ライブラリの `GenerateSegments` を使用してください。

`structured-append` は GS1・ECI・FNC1・ECC boost と組み合わせられません。これらのフラグは通常の `encode` / `estimate` 用です。

## 出力契約

`encode` の `--format` は `svg`（既定）、`png`、`matrix`、`json`、`svg-data-url`、`png-data-url` です。`estimate` と `structured-append` は `json` だけです。`--output PATH` はそのファイルへ書き込み、既存ファイルは置き換えます。省略または `--output -` は標準出力です。PNG の標準出力はバイナリで、改行を付けません。他の形式は標準出力の末尾に改行を付けます。

- `matrix`：行優先の `boolean[][]`。quiet zone を含まない。
- `encode --format json`：`version`、`size`、`matrix`、`diagnostics`、`rendering`。
- `estimate`：`planning`、`rendering`。容量不足でも JSON を出力する。
- `structured-append`：`total`、`parity`、`inputLength`、`byteLength`、`diagnostics`、`symbols`。各 symbol は行列・診断を含む。

JSON のプロパティと enum 文字列は camelCase、boolean は真偽値です。`matrix` 出力では描画を行いません。JSON に含む描画診断も画素の割当てを行わないため、画像を作れるサイズかどうかを保証するものではありません。

| 終了コード | 意味 |
| --- | --- |
| 0 | 成功 |
| 1 | ファイル・stream などの I/O エラー |
| 2 | 不正な入力・設定、または生成時の容量不足 |
| 3 | estimate で容量不足。結果 JSON は出力済み |

エラーは標準エラーへ `{"error":{"code":"…","message":"…"}}` として出力します。機密情報をコマンドラインへ渡す場合は、OS のプロセス一覧やシェル履歴に残る点を考慮し、必要に応じてファイル入力を使用してください。
