# Mermaid PDFレンダラー

公式 @mermaid-js/mermaid-cli 12.0.0と推移依存をpackage-lock.jsonへ固定しています。Node.js 22.13以降と既存Chromeを使用します。node_modulesはGitやアプリ配布に含めません。

```powershell
$env:PUPPETEER_SKIP_DOWNLOAD = "true"
npm ci --prefix tools/documentation-renderer --ignore-scripts
python tools/BuildDocumentationPdf.py --source-root outputs/PdfCorrectorium-Documentation --output outputs/PdfCorrectorium-Documentation/design.pdf --chrome "C:/Program Files/Google/Chrome/Application/chrome.exe"
```

MermaidをローカルでPNGへ変換し、コードブロック位置に画像として組み込みます。CLIが無い・構文不正・画像が空の場合はエラーにし、完成してから出力PDFを置き換えます。依存の取得は開発時だけ行い、PDF変換時に自動インストールしません。

通常のコードブロックは背景・輪郭・余白付きで出力し、改ページしても維持します。執筆・検証の規則は[文書ルール](../../docs/Documentation-Authoring.md)を参照してください。
