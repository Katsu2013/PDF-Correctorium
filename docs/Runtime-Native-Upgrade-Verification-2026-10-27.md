# .NET 10・PDFium・qpdf更新の検証記録

2026-10-27。最終対象は`Directory.Build.props`のv1.0.0-dev.249 / 1.0.0.249。全19種類の配布版アプリ診断が成功（5,267項目＋文書構成の集約検査）。内訳と配布情報は[リリース記録](releases/v1.0.0-dev.249.md)を参照。

## 更新順と確認範囲

| 段階 | 構成 | 確認結果 |
| --- | --- | --- |
| 診断準備の修復 | .NET 8.0.33、更新前ネイティブ | Release成功、校正69項目成功 |
| PDFium単独更新 | .NET 8、PDFium 158.0.8125、旧qpdf | 使用API82件の宣言差分0、全3アーキテクチャの欠落0、追加セキュリティ40項目成功 |
| qpdf更新 | .NET 8、PDFium 8125、qpdf 12.4.2 | 不正入力125、追加セキュリティ40、出力状態26項目成功 |
| .NET 10移行 | 全7プロジェクト、SDK 10.0.403、実ランタイム10.0.14 | 本体・単独診断2件のRelease成功（警告・エラー0）、契約58、アイコン規約352、版管理はソース25／配布26項目成功 |
| 連続表示修正 | dev.249 | 未修正コードは背景キャッシュ更新の再現試験で失敗。修正後は設定118項目成功。同じページを保持し、回転した古い画像は破棄する |

PDFiumは初めに8111で検証した後、公式公開情報を再確認して2026-10-26公開の8125を採用した。NuGetの公式SHA-512とダウンロードを照合し、同版バイナリアーカイブとx64 DLLの一致を確認した。パッケージ・バイナリ・上流のライセンスを区別し、同版アーカイブの19ファイルを配置・ハッシュ検査へ含めた。qpdfは公式MSVC64 ZIPのdigestを照合し、binの12ファイルを一式で更新した。

## 最終配布と実行条件

配布フォルダー：

`C:\Users\katsu\Documents\Codex\PDF-Correctorium\outputs\PdfCorrectorium-Builds\PdfCorrectorium-v1.0.0-dev.249-win-x64-20261027-133457`

入力指紋：`F78E1E44C0B81F4C043B47E76530418260913B87877C02892F524D0B797A198F`。

EXE SHA-256：`5689F35D122C6597EC4A34DDD8969532B5424CA8816A6B1DE1EAA3CA262A0F4B`。全診断終了後、生成されたcache/config/logs/workspacesを正確なパス・プロセス終了・reparse pointなしを確認して証跡内へ退避。整理後のGetBuildVersionによる54ファイル・版数・runtimeconfig照合と入力指紋の一致も成功。

framework-dependent配布を維持し、.NET 10 Desktop Runtime（x64）の最新サービス版が必要。runtimeconfigはnet10.0、NETCoreとWindowsDesktopを各1件要求し、未検証メジャーへのrollForwardを許可しない。配布照合は54ファイルを対象とする。自作5バイナリはソース診断出力と配布物でバイト単位一致。

旧.NET 8版dev.247と最終.NET 10版dev.249のProjectFormatで、旧版／新版が保存した2プロジェクトを相互に読み、モデルのSHA-256一致と元パッケージ未変更を確認した。すべての過去プロジェクトの検証を意味しない。プロジェクト形式1.12、最小読込dev.243、設定21は維持。

## ネイティブ処理と隔離

最終配布のDLL・qpdf・モデル一式をコピーした専用ホストで、不正入力125、追加セキュリティ40、出力状態26、校正監査25項目（skip 0）が成功。AppContainer証明ではcapability 0、作業内読込成功、外部読書き・ランタイム領域・owner lease操作の拒否を確認。接続可能な制御ソケットを用意した上で、隔離側接続はtimeoutとなった。`SocketAccessDenied`を直接確認したという結果ではない。

単独診断ホストはRID別のdeps.jsonを持つため、検証ホストだけに配布内のx64 PDFiumをルートへ複製し、同一ハッシュを確認した。通常アプリのruntimes配置は変更していない。診断ホストの出力先を誤った初回実行と、中間dev.248の失敗記録は削除していない。

## 証跡と限界

証跡ルート：`outputs/upgrade-dev248-20261027-125750`。

- `pdfium-api-audit-8125.json`、`downloads/`、`qpdf-12.4.1-bin-backup/`
- `phase1-*`、`phase2-*`、`phase3-*`：.NET 8で段階ごとに切り分けた結果
- `continuous-repro-settings/failure.txt`、`continuous-repro-stale.txt`：モデル参照差替えによる破棄の再現
- `dev249-build.log`、`dev249-contracts.log`、`dev249-versioning-*`、`dev249-icons.log`
- `dev249-packaged/results.json`：最終配布の実画面診断
- `dev249-native2-results.json`、`dev249-native2-sandbox/sandbox.json`
- `dev249-source-published-equality.json`、`dev249-cross-net8.0.json`、`dev249-cross-net10.0.json`、`dev249-actual-runtime-modules.json`
- `dev249-runtime-link-stage.json`：最終版の親／子プロセスでCLR・WPF 10.0.14とPDFium 158.0.8125.0を実観測
- `dev249-final-summary.json`、`dev249-final-version.json`、`dev249-generated-state-moves.json`：整理後の最終照合

このPC（Windows build 26220.9739 / 25H2）での確認。.NET 10だけをインストールしたクリーン端末、別OS・DPI・入力機器、任意の大量ページPDF、強制CET環境での実機確認は未実施。API・回帰試験の成功と個別CVEの該当性証明は区別する。署名・Git履歴変更・GitHub公開は行っていない。
