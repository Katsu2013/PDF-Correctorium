# 12-01 運用・配布

## 現行配布状況（dev.122）

Windows 11 x64向けPortable配布物があり、`portable.marker`、PDFium、qpdf、LICENSE、NOTICE、THIRD-PARTY-NOTICES、qpdfライセンスを同梱する。インストーラー、自動更新、SBOM、脆弱性検査結果、`CONTRIBUTING.md`、`SECURITY.md`、`CODE_OF_CONDUCT.md`、正式なリリース自動化は未整備である。

## 配布

初回公開はWindows 11 x64 Portable ZIPを主とし、インストーラーを後続または同時提供できる構造にする。成果物にはLICENSE、NOTICE、THIRD-PARTY-NOTICES、依存ライセンス、SBOMを含める。

## 設定モード

- 実行フォルダーに`portable.marker`がある場合はPortable。
- Portableは`config/`、`logs/`、`cache/`、`workspaces/`を実行フォルダー配下へ置く。
- Installedは設定をAppData、キャッシュ・ログ・作業領域をLocalAppDataへ置く。
- 実行フォルダーが書込不可の場合は明確に案内し、勝手にモードを変えない。

## 更新

Version 1.0の目標は更新確認と公式リリースページへの案内である。現行実装に更新確認機能はなく、自動置換も後続候補である。

## バックアップと復旧

- プロジェクト保存前の世代バックアップ
- 重要処理前のスナップショット
- 異常終了時の作業領域検出
- 元PDF移動時の再リンク
- 修復前の保全コピー

## サポート情報

現行の「バージョン情報」はアセンブリ版とApache-2.0表示に限られる。アプリ版、ランタイム、CPU、PDF/OCRエンジン、依存ライセンス、ログ場所をまとめた表示と、Issue報告テンプレートは目標仕様である。

## OSSリポジトリ

```text
LICENSE
NOTICE
THIRD-PARTY-NOTICES.md
README.md
CONTRIBUTING.md
SECURITY.md
CODE_OF_CONDUCT.md
docs/
src/
tests/
```

## セキュリティ

脆弱性報告窓口を`SECURITY.md`に記載する。外部OCR、プラグイン、PDF解析は非信頼入力として扱い、タイムアウト、サイズ上限、パス検証、ZIP Slip対策を実装する。
