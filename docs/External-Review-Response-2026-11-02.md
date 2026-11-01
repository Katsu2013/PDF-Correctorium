# 2026-11-01 外部レビューへの対応

対象：公開ソースdb4251e／公開アプリdev.274。修正ソース：dev.281（CIの監査データ・音声検証プロファイルの修正を含む）。

## 指摘ごとの対応

| 指摘 | 対応 | 公開・追加確認 |
|---|---|---|
| 2-1 公開Wikiが古い | wiki/を正本として明記。リンク変換と全ページの公開準備ツールを追加 | [公開Wikiへ24ページ同期済み](https://github.com/Katsu2013/PDF-Correctorium/wiki)、コミット2dde472 |
| 2-2 Pagesが404 | READMEの公式サイト入口を削除。正本の操作ガイドへ案内 | Pages設定は変更しない |
| 2-3 NETSDK1185 | 推移的フレームワーク解決の禁止を解除。固定版・オフライン復元元・出力ハッシュ検査は維持 | [GitHub Actions](https://github.com/Katsu2013/PDF-Correctorium/actions/workflows/verify.yml)で最終コミットの結果を確認する |
| 2-4 公開元がdirty | CIの公開用ビルドはクリーンなHEADを発行前後に必須検査 | 過去のZIPやprovenanceは書き換えない |
| 2-4 版のずれ | 文書の272を274へ訂正。保存済み公開EXEの実測は1.0.0.274 | [公開リリース本文も訂正済み](https://github.com/Katsu2013/PDF-Correctorium/releases/tag/v1.0.0-dev.274)。添付5件とタグの不変を照合 |
| 3-1 個人絶対パス | 公開用テキストをプレースホルダー化。CIへ混入検査を追加 | 公開済みのGit履歴や旧PDFは削除しない |
| 3-2 Issue #1未返信 | Issue原文を取得し、既存フォント制約と再現資料の確認を含む返信案を用意 | 承認後に[返信済み](https://github.com/Katsu2013/PDF-Correctorium/issues/1#issuecomment-6560023669)。再現資料待ちであり、U+A729の完全対応とは扱わない |
| 3-3 動作環境 | README・Wiki・Web原稿に2004／19041以降とx64を明記 | 実機検証はWindows 11。OSサポートとAPI条件は別 |
| 古いdev.199直リンク | Wikiの導線をReleases一覧へ統一 | 公開Wiki同期済み |
| 内部監査の見え方 | reports/READMEで日付・版・解決状態の読み方を明記 | 透明性のため履歴は保持 |
| 製品版の診断CLI | 明示的な環境変数がなければ診断を拒否 | 運用ワーカーと通常の文書起動は対象外 |
| releases/latestの404 | 利用者案内はReleases一覧を使用 | 安定版がない状態を隠さない |

## Issue #1について判明した範囲

- 報告対象はU+A729 `ꜩ`。アプリ上の表示・検索と、PDF保存後の文字保持は別の経路である。
- 現行の文字書換えは元PDFフォントを使う。編集画面が表示できても、元フォントの符号・ToUnicode対応で保存できるとは限らない。
- GlyphLessFontという名前だけで全フォントを同一視できない。元PDFの埋込みフォント・CMap・ToUnicodeが必要。
- 利用者の小さな再現PDFと、使用バージョン・出力手順の提供を依頼する。提供資料を公開診断データへ勝手に組み込まない。
- 無条件のフォント差し替えや、文字を消す／近い文字へ置換する処理は追加していない。

### 投稿前の返信案

> Thank you for the detailed report, and sorry for the delayed response. We have started investigating this as a PDF export / font-mapping issue. Displaying and searching the character in the editor does not by itself confirm that the original PDF font can encode it when exporting. We have not yet confirmed the cause for your particular GlyphLessFont, and do not want to claim that U+A729 is fully supported or fixed without a reproduction. Could you share a small, non-sensitive input PDF, the exact application version, and confirm whether you used “Export edited PDF” rather than project save? An exported sample would also help us compare the font encoding and ToUnicode mappings. Please keep the original document unchanged. We will use the sample to check that the character is preserved, or that export fails explicitly instead of silently losing it.

## 検証記録

ローカルでの修正・検証と、公開側への反映を区別する。件数と結果は[dev.277記録](releases/v1.0.0-dev.277.md)へ追記する。

## 2026-11-02 公開反映

- ソース修正と前回までの文書整理をmainへコミット・push済み。
- 公開Wiki24ページのGit上の本文を準備時のSHA-256と照合済み。
- dev.274は本文だけを訂正し、全5資源のID・サイズ・digestとタグの対象が不変であることをAPIで照合済み。
- CIの最終結果は上記Actionsで確認する。ローカルの成功と混同しない。
- 新規アプリリリース、既存ZIPの差し替え、既存タグの付け替えは行っていない。

### CIで追加検出した監査データの環境依存

- dev.277の[CI](https://github.com/Katsu2013/PDF-Correctorium/actions/runs/41324106745)は配布ビルド後の回収監査で停止した。
- dev.278はテスト用データのNTFS所有者を明示する。本番の削除条件は緩和しない。
- ローカルの修正後監査29件と、最終CIの結果は[dev.278記録](releases/v1.0.0-dev.278.md)を参照。

## 最終CIと文書の反映

- [dev.281のCIは成功](https://github.com/Katsu2013/PDF-Correctorium/actions/runs/41333224499)。クリーンなコミット`0b1e84f`、アプリ診断21グループ、完成ZIPと出所を確認した。
- 音声生成のみのCIと、ローカルの実再生確認は区別する。CIは実再生済みと扱わない。
- 途中で出た所有者、日本語音声未導入、MediaPlayerでの実再生失敗は履歴に保持する。最後の成功で過去の失敗を上書きしない。
- 詳細・指紋・検証用ZIPのハッシュは[dev.281記録](releases/v1.0.0-dev.281.md)に集約した。設計PDFもdev.281へ更新し、旧版はarchiveへ保持した。
- 既存dev.274の配布資源は不変。Issue #1は再現資料待ちで、完全解決とは扱わない。
