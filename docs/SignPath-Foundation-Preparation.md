# SignPath Foundation 申請・連携準備

2026-10-28。直接配布MSIXにはSignPath FoundationのOSS向け署名を採用する方針。**未申請・未承認・連携未実装**。本書と[Code signing policy](../CODE_SIGNING.md)をローカルで準備した段階で、GitHubへのコミット・公開、申請送信、証明書設定は行っていない。アプリとビルドツールの版数はdev.255のまま。

## 申請前に確認すること

| 項目 | 現在の確認結果・残作業 |
|---|---|
| リポジトリ | originは `https://github.com/Katsu2013/PDF-Correctorium.git` |
| 自作コードのライセンス | ローカルのLICENSEはApache License 2.0 |
| 同梱物 | 第三者ランタイム・PDF/OCRライブラリ・モデル・フォントを含む。Foundation条件との適合を構成全体で確認する。自作コードのライセンスだけで審査通過とは判断しない |
| 署名対象形式の公開実績 | 現在READMEが案内する公開物はdev.199のZIP。Foundationは署名したい形式で既にリリース済みであることを条件にしている。MSIXの公開実績は未確認のため、申請前に確認し、必要なら対象形式の扱いを問い合わせる。未署名MSIXを一般向け正式版と案内しない |
| 公開ソースと成果物の一致 | 作業ツリーには多数の変更・未追跡ファイルがある。申請対象のソース、依存物、ビルド定義を確定して公開する作業は別途必要 |
| 署名方針・プライバシー | CODE_SIGNING.mdを準備。担当者案と実際の権限を所有者が確認し、申請前にホームページとダウンロード案内から参照可能にする |
| GitHub / SignPathの多要素認証 | 未確認。両方で有効化が必要 |
| GitHub Actions | 現在この作業ツリーに `.github` のワークフローはない。Foundation向けは署名要求に至るビルドをGitHub-hosted runnerで実行する必要がある |
| Package Name / Publisher | 未確定。承認された証明書の正確なSubjectにPublisherを合わせ、直接配布の最初の公開前に確定する |
| 署名用アカウント情報 | Organization ID、Project slug、Artifact configuration、Signing policy、API tokenは承認・構成後に取得する。架空の値や秘密鍵をアプリに同梱しない |
| 更新配信先 | HTTPSの固定`.appinstaller` URLと、版ごとのMSIX URLを確定する。固定フィードは現行クライアントが受け付ける直接200応答が必要。GitHub Pages等のMIME型・配信ヘッダー・キャッシュ・Windowsからの取得を検証する |

Foundationの条件には審査、プロジェクトの実績、各リリースの手動承認も含まれる。無料枠の利用が確約された状態ではない。[公式条件](https://signpath.org/terms)、[申請ページ](https://signpath.org/apply.html)。

## 採用後の配布工程

1. 公開ソースの版を固定し、GitHub ActionsでReleaseビルド・必要な契約テスト・版管理検査・配布検証を行う。公開CIでは個人のPDFやプロジェクトファイルを使用しない。
2. 確定したPackage Name／Publisherと埋込App Installer設定で未署名MSIXを作り、CIの内部成果物として保管する。
3. 公式SignPath Actionで成果物と出自を照合できる形で署名を依頼し、担当者が承認する。トークンはGitHub Actionsの保護されたシークレットとして扱い、信頼できないPRへ渡さない。
4. 返却されたMSIXの署名・タイムスタンプ・発行元を検証する。コンテナー内部のペイロード、版数、宣言、更新設定が提出した内容と一致することも確認する。
5. 版別の署名済みMSIXをGitHub Releasesへ公開してダウンロードを確認した後、固定フィードを更新する。ZIPは従来どおり別途配布する。
6. クリーン環境で署名済み旧版→新版、起動中・未保存文書あり・通信失敗時、設定や復旧データの継承、アンインストールを確認する。

手動承認は**開発者がリリースを署名する際の操作**。承認・署名・公開後の利用者向け自動更新はWindows App Installerが管理する。利用者に署名用のアカウントや証明書登録を求める設計にはしない。

## 現行ツールとの差分

`tools/BuildDistributions.ps1` のDirect経路は、Windowsの証明書ストアのThumbprintを指定してSignToolを実行する方式。SignPathのAPI tokenをその引数に入れることはできない。

連携実装では、内部用のパッケージ生成と外部署名済み成果物の受領・検証・公開を分ける必要がある。既存の「署名なしDirect公開の拒否」「ソースとペイロードの一致」「版数の巻戻し拒否」を取り除いて回避しない。第三者バイナリに自作の署名を付けず、MSIXコンテナーと対象を限定した自作成果物だけを署名する。

ワークフローとビルドツールを実装する際は、Directory.Build.propsの現行値を読み、必須のリビジョン更新・Releaseビルド・契約テスト・版検査・配布検証を行う。今回は文書のみを準備し、再ビルドや版数更新はしていない。

## 英文申請説明の下書き

下記は提出前に実際の公開状況・担当者・ビルド構成を反映して修正する。現在CIや署名が稼働しているという説明にはしない。

> PDF Correctorium is an Apache-2.0-licensed Windows desktop PDF editor focused on correcting invisible OCR text, including NDLOCR-Lite output, and on local PDF editing and redaction. The repository is https://github.com/Katsu2013/PDF-Correctorium.
>
> We plan to use SignPath Foundation for our direct-distribution MSIX packages. We intend to build the packages from the public repository using GitHub-hosted GitHub Actions runners and require explicit maintainer approval for each release signing request. This integration has not yet been configured. Third-party components will retain their applicable licenses and existing signatures and will not be individually re-signed using the project's signing access.
>
> Document and OCR processing takes place locally. Signing requests will contain application build artifacts and build-origin metadata, not users' PDF documents or project files. Our proposed signing and privacy policy is prepared in CODE_SIGNING.md; its public URL, role assignments and release-artifact availability will be confirmed before submission.

提出時に追加する情報：署名方針の実際の公開URL、署名対象の既存リリースURL、承認担当者、プロジェクトの利用実績。審査・連携が済んでいない段階で、Foundationの提供を受けている旨のクレジットや署名済みバッジを有効にしない。

参照：[Foundation条件](https://signpath.org/terms)、[GitHub連携](https://docs.signpath.io/trusted-build-systems/github)、[MSIX署名の設定](https://docs.signpath.io/artifact-configuration/reference)、[現行の更新動作](Installer-Automatic-Updates.md)。
