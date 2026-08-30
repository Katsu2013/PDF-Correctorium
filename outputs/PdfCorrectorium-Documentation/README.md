# PDF Correctorium 開発ドキュメント

本書群は、PDF Correctorium Version 1.0 の実装・レビュー・OSS公開に用いる設計基準である。

dev.124では版番号を共通ビルド設定へ一元化し、数値版・画面表示・保存情報・配布名を揃えた。[版管理ルール](../../VERSIONING.md)に今後の更新・検証手順を定めている。以下のdev.122/123の記録は、各更新時点の履歴である。

2026-08-30の現行ソース（dev.123へ更新する前の配布物dev.122）との整合性監査を反映した。要求文はVersion 1.0の目標を表し、実装済みであることを意味しない。各文書の「現行実装」または「実装状況」を実装判定の根拠とし、全体の既知問題はリポジトリ直下の[実装状況](../../IMPLEMENTATION_STATUS.md#known-implementation-defects)で管理する。

Markdownファイルを正本とする。`PDF-Correctorium-Design-Documentation.pdf`は2026-08-09時点の発行スナップショットであり、今回の追記を含まない。最新仕様として配布するには、Markdownからの再生成と全ページの確認が別途必要である。

## 2026-08-30の反映内容

- 機能要求・UX・画面設計: 校正・確認モードの対象一覧、状態絞り込み、ページをまたぐ移動、確認済みにして次へ、選択時のスクロールと操作制限。階層別レビュー集計等は未実装として区別した。
- 画面設計: 文書プロパティ、共通タブ・入力部品、しおり一覧、文書未読込時の無効化、同期する倍率表示、中央100%の2段階スライダー、フラットボタンとツールバー余白。
- テスト戦略: 契約13件、画面136件、ファイル起動67件、校正69件の実績と実行方法、および未検出だった5件の回帰テスト課題。
- 運用・配布: ファイル引数からの起動、関連付け用アイコン、毎回別の日時付きフォルダーにビルドする運用と自動保存の制限。
- 既知問題: 文書切替時の未保存編集消失、空文字編集の復活、再保存時の領域属性欠落、校正モードの幅補正経路、一括置換後の状態不一致を未修正として記載した。

上記は本文追記時点の記録。その後dev.123で図版5件を整備し、上記5件を修正、操作停止後の自動保存と未保存プロジェクトの復旧用保存を追加した。現在の結果は[実装状況](../../IMPLEMENTATION_STATUS.md)と[テスト戦略](docs/11_Test/11-01_TestStrategy.md)を参照する。未実装の大規模機能は引き続き残件である。

新規保存はプロジェクト形式1.1となり、旧ビルドでは開けない。dev.123は旧形式1.0も読み込める。旧ビルドを使う場合はバックアップを保持する。

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

2026-08-30に現行画面の模式図を更新した。スクリーンショットではなく、主要部品と操作の配置を説明する図である。未実装機能は現行画面へ混ぜず、将来案に明示する。本文の既知制限も併せて参照する。

- [メイン画面ワイヤーフレーム](assets/svg/SCR-001_MainWindow_Wireframe.svg)
- [OCR編集モックアップ](assets/svg/SCR-002_OcrEdit_Mockup.svg)
- [校正・確認モード](assets/svg/SCR-009_ReviewMode_Wireframe.svg)
- [文書プロパティ](assets/svg/SCR-010_DocumentProperties_Wireframe.svg)
- [プロジェクト診断・修復（将来案・未実装）](assets/svg/SCR-003_ProjectDiagnostics_Wireframe.svg)

## 文書の状態

初版要求ベースラインに、2026-08-30時点の実装差分と既知問題を追記した状態。ライブラリの正確なバージョン、PDF適合範囲、未完了のUI・復旧・OCRプロバイダー境界は、実装とADRの更新に合わせて確定する。要求を現行の不具合に合わせて緩和したものではない。
