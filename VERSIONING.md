# バージョン・リビジョン管理

## 一元管理

版番号の定義元は[Directory.Build.props](Directory.Build.props)の`ApplicationVersionPrefix`と`DevelopmentRevision`だけとする。各プロジェクトで別々に設定しない。

| 項目 | dev.128の値 | 参照・反映先 |
|---|---|---|
| 製品バージョン | `1.0.0-dev.128` | Version、InformationalVersion、タイトルバー、ヘルプ→バージョン情報、起動ログ、保存プロジェクトのapplicationVersion |
| 数値の版番号 | `1.0.0.128` | ソリューション内の全プロジェクトのAssemblyVersion・FileVersion、EXE/DLLのファイル情報、生成するWindowsアプリケーションマニフェスト |
| 配布名 | `PdfCorrectorium-v1.0.0-dev.128-win-x64-日時` | 実際のMSBuild設定から取得。文書の文字列を命名元にしない |

数値版番号の4番目が開発リビジョンである。`.sln`の`VisualStudioVersion`はソリューションを扱う開発ツールの版であり、アプリの版番号ではない。`app.manifest`内の`1.0.0.0`はテンプレート値であり、ビルド時に中間フォルダーへコピーして数値版番号を書き込み、その生成版をEXEへ埋め込む。

Git情報が利用できる場合、SDKがInformationalVersion末尾へ`+コミットID`を付加することがある。表示用の開発リビジョンは変わらず、バージョン情報画面と起動ログには完全な文字列を表示する。

## 必須の更新手順

1. 利用者へ変更したアプリまたはビルド処理を渡す前に、`DevelopmentRevision`を前回より増やす。コンパイルごとの自動加算はしない。数値の上限は65534である。
2. READMEの現行版、実装状況の現行スナップショット、今回の変更・検証記録を更新する。過去の検証記録や図版の基準版を一括置換してはいけない。
3. リポジトリ直下でReleaseビルド、契約テスト、版番号の回帰検証、変更対象のアプリ診断を実行する。
4. `tools/BuildPortable.ps1`で新しい日時付きフォルダーへ発行し、版番号検査と`build-info.json`の生成まで成功したものだけを配布する。既存フォルダーを上書きしない。
5. 利用者には正確な版番号と出力フォルダーを案内する。

```powershell
dotnet build PdfCorrectorium.sln -c Release
dotnet run --project tests/PdfCorrectorium.ContractTests -c Release --no-build
.\tools\TestVersioning.ps1
.\tools\BuildPortable.ps1 -NoRestore
```

同じソースの再検証ビルドは同じリビジョンでもよいが、日時付きフォルダーは毎回新しくする。版番号を上げずに過去の配布物を変更したり、検査を通すために過去の記録を書き換えたりしない。

## 自動チェックと証跡

- [Directory.Build.targets](Directory.Build.targets)は通常のソリューションビルドと発行で版番号の整合性を検査する。版の上書きや不正なリビジョンをエラーにする。
- [BuildPortable.ps1](tools/BuildPortable.ps1)は、実装状況の現行版の一致、リビジョンの巻戻し、同じリビジョンでのビルド入力変更を検出する。`-BuildLabel`は実際の版と同じ値しか受け付けない。
- ビルド入力の指紋にはソース、リソース、ビルド設定、配布ツール、同梱qpdfとライセンス類を含め、生成物と変更履歴文書は含めない。発行前後の指紋も一致させる。
- 発行後に主要EXE/DLLの製品・ファイル・アセンブリ版番号とWindowsマニフェストを検査する。[GetBuildVersion.ps1](tools/GetBuildVersion.ps1)へ`-PublishDirectory`を渡せば再確認できる。
- `build-info.json`に版番号、生成日時、SDK、入力指紋、主要バイナリーのSHA-256を保存する。取得できる場合のみGitコミットと未コミット変更の有無も記録し、取得不能ならnullとする。

入力の再利用検査はローカルの配布フォルダーと`build-info.json`を根拠にする。dev.123以前の記録のない成果物、別PCでの履歴、削除済みフォルダーは完全には照合できない。初回や履歴のない環境では人が前回のリビジョンを確認する。Gitのコミット・タグ管理を代替する仕組みではない。

## データ形式との区別

アプリの版を進めただけでは、プロジェクト形式を変更しない。dev.128の保存形式は引き続き1.1、必要最小アプリ版は形式1.1へ対応した`1.0.0-dev.123`である。`applicationVersion`だけを実行中の版に追随させる。

## dev.123からの是正

dev.123では製品文字列だけが`1.0.0-dev.123`で、数値版番号は`1.0.0.0`、従来のバージョン情報画面は`1.0.0`と表示されていた。dev.124で上記の一元化と検査を導入した。この作業フォルダーは確認時点でGitリポジトリとして認識されなかったため、Git履歴の継続性は確認できていない。既存のGit情報の再作成・コミット・pushは実施していない。
