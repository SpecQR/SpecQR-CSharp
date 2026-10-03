# 互換性と意図的な差分

基準は [provenance](provenance.md) に固定した JavaScript `3.0.0-rc.2` と Swift の公開契約です。同一の安全な payload・segment・version・ECC・mask では、matrix と codewords の一致を検証します。C# の戻り値・例外・名前は .NET 向けに定義しており、JavaScript オブジェクトとの binary/API compatibility は意味しません。

- 高レベル `Gs1` / `Fnc1Second` と raw text の literal `%` は、`Auto` では byte mode にして元の payload を保持します。明示的 `Alphanumeric` は曖昧なので拒否します。手動セグメントに高レベル FNC1 オプションを足す場合も、`%` を含む alphanumeric を拒否します。これは基準 JS の解釈によるデータ変更を避ける、Swift と共通の意図的な安全修正です。
- 低レベル `QRSegment.Fnc1()` / `Fnc1Second()` では、呼び出し側が QR alphanumeric の FNC1 escaping を管理します。`%` は GS、`%%` は literal `%` を表します。raw element string と escaped alphanumeric string を取り違えないでください。
- C# text は Unicode scalar 単位で扱い、unpaired UTF-16 surrogate を拒否します。黙って replacement character に変えません。UTF-8 byte text と raw `byte[]` は区別します。
- ECI は assignment を記録する control segment です。文字列を任意の charset に transcoding しません。UTF-8 text には assignment 26 を指定してください。他の assignment を使う場合は、その charset の raw bytes を呼び出し側で用意します。
- Canvas、Blob、Object URL、DOM は C# API にありません。代わりに SVG、PNG、data URL、portable pixel buffer を提供します。OS UI integration は利用者の UI framework が担当します。
- 自動 mask の penalty と tie break は基準 JS の方針です。別 encoder の自動 mask 選択との一致は要求せず、独立 encoder 比較は固定条件で行います。
- PNG の compressed bytes、SVG の文字列レイアウトの完全一致は要求しません。pixel geometry、色、metadata、安全性、decode payload を検証します。
- 明示した資源上限、型で表せない入力、例外文言などは C# API 固有です。安全性の上限は [Structured Append](structured-append.md) と rendering 文書に記載します。

GS1 は bounded AI catalog のみです。Digital Link は deterministic helper であり、GS1 全規格・全 AI・全業界制約の認証や全 URL canonicalization を保証しません。Micro QR、rMQR、logo overlay、装飾 module、QR reader は対象外です。

## Digital Link の dot path 値

GS1 の raw value として `.` / `..` 自体は有効ですが、URL path に配置すると URL の dot-segment normalization により qualifier が失われます。そのため `CreateDigitalLink` は path に選んだ AI の値が `.` / `..` の場合に拒否します。`PathAis = Array.Empty<string>()` で query に配置すれば値を保持できます。これは基準 JS にもある data-loss 経路を避ける意図的な差分です。raw value が `%2e` などの文字列の場合は `%` を通常どおり encode して保持します。
