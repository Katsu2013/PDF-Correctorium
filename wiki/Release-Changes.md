# dev.199からの更新

<!-- FINAL-LOCAL-VERIFICATION -->
最終のローカル確認：dev.274のRelease・契約・21種類のアプリ診断・ZIP照合は成功。コミット・署名・外部公開はまだ行っていません。詳しい結果は上記／下記の累積リリース文書を参照してください。


現行ソースは1.0.0-dev.274。前回公開dev.199からの累積変更は、[リリース文書](../docs/releases/v1.0.0-dev.274.md)を参照してください。GitHubで実際に取得できる配布物は[Releases](https://github.com/Katsu2013/PDF-Correctorium/releases)で確認してください。

- [RapidOCRによる行の再認識](RapidOCR.md)
- [OCR品質分析と資料の辞書](OCR-Quality-And-Lexicon.md)
- [読み上げ・段落・読み方](Read-Aloud.md)
- [作業文字・コメント・完成画像](Data-Exports.md)
- [リンクと一括処理](Comments-Tags.md)
- [テーマ・モード・ツールバー・配置](Settings-Workspaces.md)
- [プロジェクト互換性](Project-Files.md)

新しい自己完結型ZIPは.NET 10.0.14を同梱します。dev.199のZIPは.NET 8 Desktop Runtimeが必要です。新規保存するプロジェクト形式1.14をdev.199で開くことはできないため、移行前のファイルを残してください。未OCR範囲・ページ・PDF全体の新規OCRは未実装です。
