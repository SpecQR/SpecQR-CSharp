# Security

入力検証、資源上限、SVG escaping、文字列/byte の意味保持をライブラリの責務として扱います。QR payload自体の信頼性やリンク先の安全性は生成器が保証できません。

機密情報を含む報告は公開issueに貼らず、GitHubのprivate vulnerability reportingが利用可能な場合はそちらを使用してください。通常の再現可能な不具合は、最小入力・options・対象framework/OS・期待結果を添えて報告してください。秘密・個人情報・credential・実データは取り除いてください。

対象は README に示す .NET runtime と bounded feature scope です。復号器、全GS1業界ルール、無制限のimage/messageサイズは対象外です。上限緩和を行う変更はchecked arithmetic、allocation前の検証、concurrencyへの影響を再検証します。
