# PDF Correctorium 設計書

現行ソース **1.0.0-dev.274**（コミット前候補）。前回公開版dev.199以降の累積変更を取り込んだ正本です。公開済み成果物はGitHub Releasesで別途確認してください。

- アプリ版はDirectory.Build.propsの2つの入力から導出します。
- 新規保存のプロジェクト形式は1.14、最小読取版dev.262。設定形式21とは別です。
- [dev.199からの変更と現行検証](../../docs/releases/v1.0.0-dev.274.md)
- [機能と文書の追跡・確認表](../../docs/Release-Documentation-Coverage.md)
- [利用者向け操作ガイド原稿](../../wiki/Home.md)
- [設計PDF](PDF-Correctorium-Design-Documentation-dev274-20261030.pdf)

## 読み方

以下の章では冒頭の現行仕様を優先します。過去リビジョンの試験件数・失敗・設計判断は当時の履歴として残し、現行で再実施したという意味に読み替えません。画面構成は新しい模式図SCR-026を参照してください。他の旧模式図は細部が現行と異なる場合があります。

## 章構成

| 章 | 内容 |
|---|---|
| 00 Project | 目的、設計方針、スコープ、実装状況 |
| 01 Requirements | 機能・非機能・実行環境 |
| 02 UI/UX | 操作フロー、共通アイコンと表示ルール |
| 03 Screens | OCR・リンク・墨消し・注釈・読み上げのパネル |
| 04 Architecture | 分離処理、背景読込、描画と配布 |
| 05 Data Model / 08 Project | 保存モデル、互換性、辞書・段落・読み方 |
| 06 PDF / 07 OCR | 安全な編集と出力、再認識と校正 |
| 09 Plugins / 10 Logging | 将来の拡張境界、ローカル診断 |
| 11 Test / 12 Operations | 検証、配布、署名と更新の未完了条件 |
| 13 Roadmap / 90 ADR / 99 Glossary | 残課題、判断記録、用語 |

## 設計資料の生成

`tools/BuildDocumentationPdf.py` はこのREADMEとdocs配下のMarkdown、assets/svgの図からPDFを生成します。表紙・フッター・互換性はソースの値を使います。図の欠落や空画像はエラーにし、生成後はページ画像と本文・リンクを確認します。

[旧READMEの更新履歴](ARCHIVE-README-through-dev272.md)と過去の設計PDFは保持しています。旧版の図・PDFを最新化済みの成果物と誤認させないでください。設計書の更新は、独立レビューや実機での署名・更新テストの代わりにはなりません。
