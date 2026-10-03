# 資源制限と診断

ライブラリは QR の実容量を超える入力を符号化前に検出します。`Estimate` / `AnalyzeSegments` は capacity overflow を `Ok=false` として返し、生成は `DATA_TOO_LONG` で拒否します。不正 input/options は planning でも拒否します。

Core の text 上限は 1,000,000 UTF-16 code units、binary は 1,000,000 bytes です。unpaired surrogate は拒否します。text の UTF-8 bytes は元の文字により増えますが、上限内の線形検査に留め、numeric lower bound でも入らない入力では mixed optimizer の状態を展開しません。

Manual core は 65,536 segments / 合計 1,000,000 diagnostic bytes 以下です。Kanji segment の diagnostic byte count は QR Shift_JIS の 2 bytes per character、SA parity の canonical bytes は UTF-8 と、用途を区別します。SA の上限と compact/full detail は [SA 文書](structured-append.md) に記載します。

Raster は一辺 2,048 pixels / 合計 4,194,304 pixels 以下、RGBA buffer は最大 16 MiB です。PNG は scanline buffer を再使用し、PNG chunkのCRCを独自に計算します。SVGはpixel bufferを作りませんが、signed32bit coordinateを超えるgeometryは拒否します。描画の色・titleは各65,536文字以下です。allocationやdestinationstreamへの書き込み前にoptionsを検証します。詳細は [rendering](rendering.md) を参照してください。

返却matrix・codewords・binary segmentsはdefensivecopyです。共有のmutableencoderstateはありません。concurrent generationは個別の作業状態を使用します。ユーザー実装のstream/canvas/enumerableを複数threadで同時に変更する責務は呼び出し側にあります。

`CAPACITY_NEAR_LIMIT` は成功したcapacityだけに返し、負のremainingBitsを持つoverflowplanには返しません。contrast・quietzone・印刷module寸法・小さいraster scaleの警告はheuristicです。読取機の全組合せで成功するという保証ではありません。
