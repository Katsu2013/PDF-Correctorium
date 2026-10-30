# バージョン・リビジョン管理

## dev.274のコミット前照合

現行候補はdev.199からの累積変更を対象にする。全件schema 2の配布一覧を用い、認証済みフォルダーを改変せず、新しい公開用ステージとZIPを照合する。Gitの改行規則と除外設定も入力指紋へ含め、上流のロック対象ファイルは元のバイトを維持する。ステージした独自ソースと作業ツリーのバイト、ライブラリのロック値を照合し、Gitの登録による別入力化を防ぐ。

コミット・push・タグ・公開は今回行わない。コミット前の候補はgitDirty=trueであり、正式公開のクリーンな記録・署名・独立レビューに代わらない。署名やGitHub Actions、MSIX導入／更新の未実施を成功として記録しない。dev.271の未実行とdev.272の検証済み結果は、その版の履歴として下記およびリリース文書へ保持する。

## 一元管理

版番号の定義元は[Directory.Build.props](Directory.Build.props)の`ApplicationVersionPrefix`と`DevelopmentRevision`だけとする。各プロジェクトで別々に設定しない。

| 項目 | dev.129の値 | 参照・反映先 |
|---|---|---|
| 製品バージョン | `1.0.0-dev.129` | Version、InformationalVersion、タイトルバー、ヘルプ→バージョン情報、起動ログ、保存プロジェクトのapplicationVersion |
| 数値の版番号 | `1.0.0.129` | ソリューション内の全プロジェクトのAssemblyVersion・FileVersion、EXE/DLLのファイル情報、生成するWindowsアプリケーションマニフェスト |
| 配布名 | `PdfCorrectorium-v1.0.0-dev.129-win-x64-日時` | 実際のMSBuild設定から取得。文書の文字列を命名元にしない |

数値版番号の4番目が開発リビジョンである。`.sln`の`VisualStudioVersion`はソリューションを扱う開発ツールの版であり、アプリの版番号ではない。`app.manifest`内の`1.0.0.0`はテンプレート値であり、ビルド時に中間フォルダーへコピーして数値版番号を書き込み、その生成版をEXEへ埋め込む。

Git情報が利用できる場合、SDKがInformationalVersion末尾へ`+コミットID`を付加することがある。表示用の開発リビジョンは変わらず、バージョン情報画面と起動ログには完全な文字列を表示する。

## 必須の更新手順

### dev.250の自己完結型・MSIXコンテナー

アプリの版入力とVersion／AssemblyVersion／FileVersionの導出は変更しない。StoreがMSIXの第4成分を0に予約するため、MSIXコンテナー版だけは`<prefixのMajor>.<prefixのMinor>.<DevelopmentRevision>.0`へ一元導出する。開発リビジョンはprefix変更時にもリセットしない。prefixのPatchを含むアプリ完全版はbuild-infoとdistribution-infoへ保存し、異なるアプリ入力で同じパッケージ版を再使用しない。パッケージ履歴の巻戻し・同じ版での入力差替えも拒否する。手動のMSIX版指定は提供しない。

BuildPortableはロックした.NET Core／Windows Desktop Runtime 10.0.14を同梱する。GetBuildVersionはincludedFrameworksとプライベートランタイムの実ファイルを検査し、共有ランタイム方式を自己完結型として通さない。BuildDistributionsは認証済みの現行ペイロードからZIP／MSIXを作り、アプリ版・コンテナー版・入力指紋・パッケージとZIPのSHA-256を対応付ける。未署名Development MSIXは正式公開可能と扱わない。詳細は[配布手順](docs/Distribution-Operations.md)。

入力指紋にはpackaging内のテンプレートも含める。WPFコンパイラーがsrc内へ一時生成する`*_wpftmp.csproj`はビルド出力として除外し、コンパイル中の作成・削除で指紋を不安定にしない。通常のcsprojやソースは引き続き対象にする。

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

### dev.271の公開用資源の照合

BuildPortableの `build-info.json` schema 2は、主要バイナリーの従来の記録に加え、build-info自身を除く配布ファイル全件のパス・サイズ・SHA-256を保存する。NewReleaseStageはその一覧だけを別フォルダーへコピーし、利用者の設定・キャッシュ・PDFを元フォルダーに保持する。使用後の旧ビルドへ全件一覧を後付けしない。梱包・正式公開ゲートと完成ZIPの照合で同じ規則を使う。

入力指紋にはテストのソースとCI定義も含める。相対パスをOrdinal順で整列し、PowerShell 5.1／7のカルチャー依存差を避ける。これは新しいビルド入力なのでdev.271へ進め、過去の指紋と認証済みフォルダーを変更しない。アプリや修正済み診断のビルド・配布認証はユーザー承認後に実施し、このソース修正だけで完了したと扱わない。

- [Directory.Build.targets](Directory.Build.targets)は通常のソリューションビルドと発行で版番号の整合性を検査する。版の上書きや不正なリビジョンをエラーにする。
- [BuildPortable.ps1](tools/BuildPortable.ps1)は、実装状況の現行版の一致、リビジョンの巻戻し、同じリビジョンでのビルド入力変更を検出する。`-BuildLabel`は実際の版と同じ値しか受け付けない。
- ビルド入力の指紋にはソース、リソース、ビルド設定、配布ツール、`DEPENDENCIES.lock.json`、ロック対象のローカルNuGetパッケージ、同梱qpdfとライセンス類を含め、生成物と変更履歴文書は含めない。発行前後の指紋も一致させる。
- `tools/TestDependencyLock.ps1`でリポジトリ内と配布先のPDFium/qpdfをSHA-256照合する。`build-info.json`には主要な管理バイナリーだけでなく、検証したネイティブ依存ファイルも記録する。
- 発行後に主要EXE/DLLの製品・ファイル・アセンブリ版番号とWindowsマニフェストを検査する。[GetBuildVersion.ps1](tools/GetBuildVersion.ps1)へ`-PublishDirectory`を渡せば再確認できる。
- `build-info.json`に版番号、生成日時、SDK、入力指紋、主要バイナリーのSHA-256を保存する。取得できる場合のみGitコミットと未コミット変更の有無も記録し、取得不能ならnullとする。

入力の再利用検査はローカルの配布フォルダーと`build-info.json`を根拠にする。dev.123以前の記録のない成果物、別PCでの履歴、削除済みフォルダーは完全には照合できない。初回や履歴のない環境では人が前回のリビジョンを確認する。Gitのコミット・タグ管理を代替する仕組みではない。

### 指紋計算の実行環境に関する既知の制約（dev.209検証時）

dev.270までの入力一覧は`Sort-Object FullName`で整列するため、Windows PowerShell 5.1とPowerShell 7系では句読点を含むファイル名の順序が異なる場合がある。dev.209では同じ246項目・同じファイル別SHA-256でも総合指紋が異なることを確認した。旧版はソース変更と即断せず、ビルドと再照合を同じ実行環境で行う。当時は`powershell -NoProfile -ExecutionPolicy Bypass -File ...`でビルド・版管理試験・指紋照合を統一し、記録との一致を確認した。dev.271の新しい指紋順序は上記を参照する。

dev.209では、将来の改訂でOS・言語・PowerShell版に依存しないOrdinal順の入力一覧と交差実行環境テストを導入する課題を残した。dev.271のソースでその順序を実装し、PowerShell 5.1／7の一致を確認する。旧指紋記録は上書きしない。

## データ形式との区別

### dev.248以降のランタイム照合

全7プロジェクトを.NET 10で確認する。dev.270までは単独のExportDiagnostics / TextDiagnosticsが.slnに含まれず個別のReleaseビルドが必要だった。dev.271からはソリューションに含め、Windows APIターゲットと投影パックをアプリへ揃える。

GetBuildVersion.ps1はsrc/testsの対象TFMを確認し、配布先のPdfCorrectorium.runtimeconfig.jsonをAssertRuntimeConfiguration.ps1で検査する。TFM、NETCore / WindowsDesktopの要求メジャー・マイナー、フレームワークの欠落・重複、未検証のメジャーへのrollForwardを確認する。TestVersioning.ps1には正常系と7種類の異常系を含む。

BuildPortable.ps1のbuild-info.jsonにはtargetFrameworkとrequiredFrameworksを記録する。起動ログのstartup.runtimeで親・ワーカーの実ランタイムとWPFのロード元も確認する。これらはクリーン端末や別OSでの実機試験の代わりではない。

アプリの版を進めただけでは、プロジェクト形式を変更しない。dev.129の保存形式は引き続き1.1、必要最小アプリ版は形式1.1へ対応した`1.0.0-dev.123`である。`applicationVersion`だけを実行中の版に追随させる。

## dev.123からの是正

dev.123では製品文字列だけが`1.0.0-dev.123`で、数値版番号は`1.0.0.0`、従来のバージョン情報画面は`1.0.0`と表示されていた。dev.124で上記の一元化と検査を導入した。この作業フォルダーは確認時点でGitリポジトリとして認識されなかったため、Git履歴の継続性は確認できていない。既存のGit情報の再作成・コミット・pushは実施していない。
