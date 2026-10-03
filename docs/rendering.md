# 描画とスキャン診断

`QRCode` は描画設定と独立したシンボルです。同じ結果を複数の形式で描画でき、描画によって行列は変化しません。

```csharp
var qr = QRCode.Generate("HELLO WORLD");
var options = new QRRenderOptions
{
    Margin = 4, Scale = 8,
    Foreground = "#123456", Background = "white",
    PrintDpi = 300, Title = "製品ラベル"
};
string svg = qr.ToSvg(options);
byte[] png = qr.ToPng(options);
QRRgbaImage image = qr.ToRgba(options);
string svgUrl = qr.ToSvgDataUrl(options);
string pngUrl = qr.ToPngDataUrl(options);

using var stream = File.Create("qr.png");
qr.SavePng(stream, options); // 呼び出し側の stream を閉じない
```

`SaveSvg` も UTF-8 の SVG を書き込み、stream を閉じません。I/O エラー時のファイルの原子的置換は行いません。原子的な保存が必要なアプリは一時ファイルから移動してください。

## ピクセルと色

寸法は `(qr.Size + 2 * Margin) * Scale` です。`Margin` は非負、`Scale` は正の整数です。`Width` を指定した場合はその幅を使用し、モジュールが整数ピクセルになるよう `(qr.Size + 2 * Margin)` の正の整数倍だけを受け付けます。元の `Scale` も正の値である必要があります。

SVG・PNG・RGBA・canvas は `#RGB` / `#RGBA` / `#RRGGBB` / `#RRGGBBAA`、`black` / `white` / `transparent` に対応します。大文字・小文字は区別せず、周囲の空白を除去して検査します。JavaScript 版の SVG に渡せる任意の CSS 色文字列は C# では受け付けません。これは全形式で同じ色を扱い、外部リソースを参照しない自己完結した SVG にするための差分です。

`QRRgbaImage.Pixels` は行優先の非乗算 RGBA8 配列です。長さは `Width * Height * 4` で、アルファ 0 の画素でも RGB は維持されます。返された画素を書き換えても元の QR 行列には影響しません。PNG は 8 bit RGBA、filter 0、非インターレースです。PNG チャンク・CRC は自前実装、zlib 圧縮は BCL `ZLibStream` を使います。圧縮バイト列そのものの実行環境間一致は保証せず、展開後の画素を比較してください。

SVG の `Title` は XML 文字として検証し、`& < > "` をエスケープします。SVG data URL は UTF-8 のパーセント符号化、PNG data URL は Base64 です。JavaScript の `encodeURIComponent` と .NET のエスケープ対象の細部が異なるため、SVG data URL の文字列表現も完全一致の対象外です。

## 上限と失敗

PNG・RGBA・`Draw` は **最大 2048 × 2048 ピクセル**です。RGBA 配列は最大 16 MiB です。PNG は 1 行の作業バッファを再利用し、圧縮出力を保持します。PNG や data URL の返却時には追加のコピー・文字列メモリが必要です。

SVG の座標は正の 32 bit 整数内に制限し、QR の最大 177 × 177 モジュールと色・タイトル各 65,536 文字の上限により出力サイズを制限します。算術オーバーフローを検査してから割当てます。無効な設定・色は `ArgumentException` / `ArgumentOutOfRangeException`、XML に使用できないタイトル文字は `XmlException` です。`PrintDpi` は正の有限値で、計算後の印刷寸法も有限である必要があります。

## 描画前に点検する

```csharp
QRRenderDiagnostics rendered = qr.RenderDiagnostics(options);
QRPlan plan = QRCode.Estimate("HELLO WORLD");
QRRenderDiagnostics planned = plan.RenderDiagnostics(options);
foreach (var warning in rendered.WarningDetails)
    Console.WriteLine($"{warning.Code}: {warning.Message}");
```

容量計画の診断は行列・コードワードを作りません。容量不足の計画は `EvaluatedVersion` の寸法を使います。容量不足には `CAPACITY_NEAR_LIMIT` を付けず、計画段階では `RASTER_SCALE_SMALL` を付けません。

| 警告コード | 判定 |
| --- | --- |
| `QUIET_ZONE_TOO_SMALL` | 余白が 4 モジュール未満 |
| `COLOR_CONTRAST_UNKNOWN` | 診断に渡された色を解析できない |
| `COLOR_CONTRAST_LOW` / `COLOR_CONTRAST_MODERATE` | RGB 相対輝度によるコントラスト比が 4.5 未満 / 7 未満 |
| `COLOR_ALPHA_USED` | 前景または背景に透明度がある |
| `COLOR_POLARITY_INVERTED` | 前景の方が明るい。C# 版で追加した助言 |
| `CAPACITY_NEAR_LIMIT` | 残りデータ容量が 5% 未満で、容量内に収まっている |
| `PRINT_MODULE_TOO_SMALL` | DPI から求めたモジュール幅が 0.25 mm 未満 |
| `RASTER_SCALE_SMALL` | 実効スケールが 3 pixel/module 未満 |
| `SCAN_RISK` | severity が Warning の項目がある |

コントラスト比はアルファ合成後の見た目を予測しません。反転色や透明色も描画できますが、スキャン成功を保証しません。これらは実用上の助言で、GS1 の印刷品質認証や ISO の検証グレードではありません。診断のみの呼び出しでは、実際の画像割当てを行わず、ラスタ寸法上限も適用しません。

## ホストの描画 API と接続する

`IQRCanvas` の `Resize(width, height)` と `FillRectangle(x, y, width, height, color)` をアプリ側で実装し、`qr.Draw(canvas, options)` に渡せます。全て整数ピクセル座標です。背景を 1 回描画してから暗いモジュールを描画します。ライブラリは WPF、WinForms、MAUI、Skia などに依存しません。
