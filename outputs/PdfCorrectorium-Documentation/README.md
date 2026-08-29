# PDF Correctorium 開発ドキュメント

本書群は、PDF Correctorium Version 1.0 の実装・レビュー・OSS公開に用いる設計基準である。

2026-08-29に現行ソース（配布物dev.122）との整合性監査を行った。要求文はVersion 1.0の目標を表し、実装済みであることを意味しない。各文書の「現行実装」または「実装状況」を実装判定の根拠とし、全体の既知問題はリポジトリ直下の`IMPLEMENTATION_STATUS.md`で管理する。

Markdownファイルを正本とする。`PDF-Correctorium-Design-Documentation.pdf`は2026-08-09時点の発行スナップショットであり、PDF生成手順が再整備されるまではMarkdownより古い場合がある。

> OCR済みPDFの透明テキスト、配置、読み順、文字方向、回転、ルビおよび文書構造を、安全かつ効率よく編集できるプロジェクト管理型PDF編集ソフト。

## 文書の読み順

1. [プロジェクト概要](docs/00_Project/00-01_ProjectOverview.md)
2. [設計思想](docs/00_Project/00-02_DesignPhilosophy.md)
3. [スコープとロードマップ](docs/00_Project/00-03_ScopeAndRoadmap.md)
4. [機能要求](docs/01_Requirements/01-01_FunctionalRequirements.md)
5. [非機能・プラットフォーム要求](docs/01_Requirements/01-02_NonFunctionalAndPlatform.md)
6. [UX原則と操作フロー](docs/02_UIUX/02-01_UXAndWorkflows.md)
7. [画面設計](docs/03_Screens/03-01_ScreenSpecification.md)
8. [アーキテクチャ](docs/04_Architecture/04-01_Architecture.md)
9. [データモデル](docs/05_DataModel/05-01_DataModel.md)
10. [PDF編集仕様](docs/06_PDF/06-01_PdfEditing.md)
11. [OCR仕様](docs/07_OCR/07-01_OcrSpecification.md)
12. [`.pdfocrproj`仕様](docs/08_ProjectFormat/08-01_ProjectFormat.md)
13. [プラグイン仕様](docs/09_Plugins/09-01_PluginArchitecture.md)
14. [ログ・診断](docs/10_Logging/10-01_LoggingAndDiagnostics.md)
15. [テスト戦略](docs/11_Test/11-01_TestStrategy.md)
16. [運用・配布](docs/12_Operations/12-01_Operations.md)
17. [ロードマップ](docs/13_Roadmap/13-01_Roadmap.md)
18. [ADR索引](docs/90_ADR/README.md)
19. [用語集](docs/99_Glossary/99-01_Glossary.md)

## 要求の表記

- `MUST`: Version 1.0で必須。
- `SHOULD`: 原則実装。技術上の理由で外す場合はADRが必要。
- `MAY`: 任意または将来拡張。
- `TBD`: 利用者確認または技術検証が必要。

文書間で矛盾した場合は、承認済みADR、要求仕様、詳細設計、補足資料の順で優先する。

## 図版

- [メイン画面ワイヤーフレーム](assets/svg/SCR-001_MainWindow_Wireframe.svg)
- [OCR編集モックアップ](assets/svg/SCR-002_OcrEdit_Mockup.svg)
- [プロジェクト診断ワイヤーフレーム](assets/svg/SCR-003_ProjectDiagnostics_Wireframe.svg)

## 文書の状態

初版要求ベースラインに、2026-08-29時点の実装差分を追記した状態。ライブラリの正確なバージョン、PDF適合範囲、未完了のUI・復旧・OCRプロバイダー境界は、実装とADRの更新に合わせて確定する。
