# dev.274 コミット前の確認

比較元は前回公開のdev.199。今回の作業はコミット直前までであり、コミット、push、タグ作成、GitHub Release、Wikiへの公開を行わない。

## レビューする文書

- [dev.199からの累積更新内容・互換性・検証](releases/v1.0.0-dev.274.md)
- [機能と文書の対応表](Release-Documentation-Coverage.md)
- [設計書の正本](../outputs/PdfCorrectorium-Documentation/README.md)
- [文書処理・利用上限・墨消しの範囲](Local-Processing-And-Redaction-Scope.md)
- [配布運用](Distribution-Operations.md)、[署名準備](../CODE_SIGNING.md)、[版の管理](../VERSIONING.md)

## 記録とステージ

既存HEADは `9015082f3d78092a1e73d7f5c9a45c38ccecdc23`。dev.199以降にはこのHEADまでの既存コミットも含まれるため、今回のステージ差分だけを累積リリース説明と混同しない。

Gitの改行規則、入力指紋、上流の原本保持を照合する。ビルド入力455件のうち440件はGitステージのバイト、15件はSHA-256で固定して再取得できるNuGetアーカイブである。ロック内69ファイルと、ステージに含む独自コード・ライブラリ等の一致を確認する。フォントのライセンス1件は以前のGitの改行キャッシュも明示的に更新し、原本のバイトを保持した。

利用者のPDF・プロジェクト・秘密鍵、ローカルのscratch_inputs.txt、診断・キャッシュ・使用済みビルドはコミットや配布に含めない。ライブラリの原本・辞書の空文字、Markdownの明示改行を、一般の空白チェックに合わせて削除しない。独自コードの空白検査はその範囲を分けて実行する。

全7プロジェクトRelease、契約76件、21種類のアプリ診断、全件配布・版・ZIP照合は成功した。結果と正確な出力先・ZIPハッシュは累積リリース文書に記録した。収納パネルの診断が一度停止した記録も残し、単独226件と最終の一括再確認の成功と区別する。

診断対象：`persistence, page-history, theme-speech, document-ui, review-mode, similar-word, rapid-ocr, ocr-notices, pane-visibility, recent-files, settings, updates, keyboard, ocr-rendering, link-editing, ocr-property, document-data, avalondock, file-launch, character-estimation, document-organization`。

| アプリ診断 | 成功したチェック数／結果 |
|---|---|
| persistence | 61 |
| page-history | 44 |
| theme-speech | 274 |
| document-ui | 211 |
| review-mode | 69 |
| similar-word | 103 |
| rapid-ocr | 186 |
| ocr-notices | 142 |
| pane-visibility | 226 |
| recent-files | 76 |
| settings | 123 |
| updates | 19 |
| keyboard | 3601 |
| ocr-rendering | 41 |
| link-editing | 278 |
| ocr-property | 92 |
| document-data | 44 |
| avalondock | 18 |
| file-launch | 80 |
| character-estimation | 38 |
| document-organization | 成功 |

検証済みソース指紋：`A7EFE09E906C430FF8F03D94E5C629D431ACA65BC1705921B7B3AA56A7328EF8`。コミット・push・タグ・公開は行っていない。

## コミット文の案

```text
Prepare dev.274 release with cumulative changes since dev.199

- Complete OCR correction, dictionaries, speech and editing workflows
- Preserve local document processing and original searchable text
- Verify .NET 10 self-contained payloads, dependencies and exact archives
- Synchronize manuals, design source/PDF and pinned Windows verification
```

## コミット後の公開とは別

現在のビルドはコミット前のため `gitDirty=true` を記録する。正式な配布照合には、コミット後のクリーンなソースから新しい時刻付きフォルダーへビルドして、そのGit記録と署名・審査を検証する必要がある。ビルド入力が変わらない検証再ビルドは同じdev.274を使用できる。入力を変更する場合は先にリビジョンを上げる。

署名、SignPathの申請・連携、独立レビュー、クリーンOSでの導入、署名済みMSIXの更新、Store掲載とGitHub Actionsの実行結果は、未実施のまま成功扱いしない。ローカルの一括検証が通ったことは、これらの承認や実公開を意味しない。

## 正式公開条件の実測

dev.274に対するTestReleaseReadinessは`readyForRelease=false`。未コミットの記録2項目、自前のEXE／DLL計5件のタイムスタンプ付き署名、依存性の勧告確認と独立セキュリティレビューの証跡が未完了として検出された。版・ソース指紋・全配布内容の不一致は検出されていない。これはコミット直前という今回の到達点と区別して記録する。
