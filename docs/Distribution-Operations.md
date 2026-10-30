# .NET同梱・ZIP・MSIX・更新の運用

2026-10-30、現行ソースdev.274。全7プロジェクトRelease・契約・21種類のアプリ診断・全件ZIP照合は完了。dev.255までの更新機構に加え、配布全件の記録と利用者データを混ぜない公開用ステージを採用する。[更新の採用仕様と制限](Installer-Automatic-Updates.md)。開発版の生成とStore審査・正式公開は区別する。

## dev.271からの公開用ステージ

BuildPortableは新しいフォルダーで生成した全配布ファイルの一覧を `build-info.json` schema 2へ記録する。使用後の `cache`、`config`、`logs`、`workspaces` を削除して再利用するのではなく、認証時の一覧から新しい公開用ステージへ取り出す。

```powershell
# アプリをビルドするコマンド。ユーザーとのビルド確認後に実行する。
powershell.exe -NoProfile -File tools/BuildPortable.ps1

# 上で得た正確な新規ビルドを指定する。元の設定・キャッシュ等は削除しない。
powershell.exe -NoProfile -File tools/NewReleaseStage.ps1 `
  -PublishDirectory '<認証された新規ビルドの絶対パス>' `
  -OutputDirectory '<存在しない公開用ステージの絶対パス>'
```

ステージは一覧外のファイル、欠落、改変、再解析ポイント等を拒否する。文書や秘密鍵なども配布物として認めない。schema 1しかない旧ビルドに、使用後のファイル一覧を後付けして認証し直してはならない。dev.270の利用中フォルダーはそのまま保管する。dev.271追加当時の未ビルド記録は履歴に残し、最終dev.274では新しい出力先で検証を完了した。[結果・正確なパス・ZIPハッシュ](releases/v1.0.0-dev.274.md)。

BuildDistributionsへは、このステージを渡す。ZIPは一覧に記録されたファイルだけで生成し、完成ZIPの全エントリ・サイズ・ハッシュも確認する。公開用ステージでアプリを起動したり、診断ツールや結果を追加したりしない。検証には別コピーを使う。

`InvokeReleaseVerification.ps1 -OutputDirectory <新しい検証先>` は、全7プロジェクトのReleaseビルド、契約・安全性・隔離・保存・履歴等の診断、版検査、クリーンなステージとZIPの生成・照合をまとめる。CIも同じコマンドを使う。生成した試験データのみを用い、署名や外部公開は行わない。dev.271で追加したCI定義はGitHub上では未実行。現行dev.274の新しい診断ビルドと同じコマンドのローカル実行は成功した。コミット前のためGit記録はdirtyであり、署名・依存性の勧告確認・独立レビューも未完了。正式公開の判定とコミット直前のローカル確認を分ける。

正式公開ゲートは、全件照合に加えて、build-infoが記録したGitコミットと現在のクリーンな作業ツリーの一致を要求する。署名・依存レビュー・独立レビュー・公開窓口等の条件は維持する。開発用ZIPの生成を正式公開の承認と扱わない。

## 利用者向け

- 自己完結型ZIPは別途.NETインストール不要。書込可能な新しいフォルダーへ展開して起動する。
- ZIPの「配布ページを開く」はGitHubを開く。古い実行フォルダーへ上書きせず、新しいフォルダーへ展開する。資料PDF／プロジェクトは元の場所に保管する。
- Store版の更新はStore、直接配布MSIXの更新はWindowsのApp Installerが管理する。dev.255の直接配布MSIXには更新設定を埋め込むため、対応するWindowsではMSIX単体から導入しても更新先を登録する。旧来の埋込なしMSIXは`.appinstaller`から導入する必要がある。
- 直接配布版の「更新を確認」は実際にHTTPSで版を照合する。新版ありで続行を選ぶと通常の保存確認へ進み、実際の終了後だけWindowsの更新画面を開く。作業中の強制終了、EXEの上書き、別アプリのインストールは行わない。
- 現在生成するDevelopment MSIXは未署名で、通常端末にそのままインストールできる正式版ではない。自己署名証明書の信頼登録や開発者モード有効化を自動で行わない。
- Store登録・公開が済むまでStoreに掲載済みと案内しない。PDFの既定アプリを自動で変更しない。

## ビルド担当者向け

Windows PowerShell 5.1でリポジトリ直下から実行する。

```powershell
dotnet build PdfCorrectorium.sln -c Release
dotnet run --project tests/PdfCorrectorium.ContractTests -c Release --no-build
powershell.exe -NoProfile -File tools/TestVersioning.ps1
powershell.exe -NoProfile -File tools/BuildPortable.ps1
```

BuildPortableは、ロック済みのランタイムパックをオフラインNuGetソースから復元し、`win-x64`、自己完結型、複数ファイル、非トリミングで発行する。ASP.NETなど使用しないフレームワークの推移的なパックダウンロードを抑える。将来の.NETサービス更新は公式パッケージのハッシュ・実行資源・ライセンスと診断を更新してから再発行する。

ONNXの依存DLLを静的にも確認し、公式qpdf MSVC64一式のMSVC 14.51.36247.0の8 DLLをアプリ直下へも配置する。既存のツール内配置とライセンス通知を維持し、両方をロックする。.NET同梱だけでVCランタイムの前提が消えると扱わず、実導入時も確認する。

認証済みフォルダーを正確に指定して、まず未署名の検証用コンテナーとZIPを作成する。

```powershell
powershell.exe -NoProfile -File tools/BuildDistributions.ps1 `
  -PublishDirectory '<BuildPortableが生成した正確な絶対パス>' `
  -Channel Development
```

検証用の設定・cache/logs/workspaces等がある入力は拒否する。先に配布コンテナーを作り、その後に実画面診断を実施する。自己完結型ペイロードとMSIX/ZIPの各SHA-256、MakeAppxの版とハッシュ、アプリ版とMSIX版の対応を記録する。署名時はWindowsの証明書ストアを使用し、PFX秘密鍵やパスワードをログ・設定へ書かない。

正式なPackage Name／Publisher／PublisherDisplayNameがPartner Centerから得られた後、`-Channel Store`とその値を指定して提出用MSIXを生成できる。これは提出処理や公開承認を実行するものではない。Store向けMSIXの提出自体にCA署名証明書は不要で、Store側が審査後に再署名する。

直接配布は`-Channel Direct`に加え、固定HTTPSの`-AppInstallerUri`、版別MSIXを指す`-PackageUri`、有効な`-SigningCertificateThumbprint`、`-TimestampServer`が必要。信頼されるタイムスタンプ付き署名が成功してから更新用ファイルを作る。自己署名を一般利用者へ登録させる方法を正式版の既定にしない。

## 版数と更新の保全

アプリの版数は従来どおりDirectory.Build.propsだけから導出する。MSIXコンテナー版は`Major.Minor.DevelopmentRevision.0`。開発リビジョンは製品prefixを変更してもリセットしない。完全なアプリ版はdistribution-infoに残す。更新では同じPackage Name／Publisherを維持し、Store／直接配布の相互移行は署名・ユーザーデータを実機で確認してから案内する。

App Installerは24時間間隔の起動時確認とOSの背景確認を宣言し、更新を起動の必須条件にしない。署名不正や失敗時にアプリ側で強制上書きしない。安定版配信元に開発版を混ぜない。Store／OS／組織ポリシーにより更新が遅延・停止する場合がある。

## 検証と未完了事項

直接配布の署名手段はSignPath Foundationを採用する方針にした。[署名方針](../CODE_SIGNING.md)と[申請・外部署名連携の準備](SignPath-Foundation-Preparation.md)は文書として準備済みだが、未申請・未承認で連携も未実装。現行のローカル証明書向けDirect経路にSignPathのトークンを指定して動く構造ではない。公開CI、対象形式の公開実績、Package Name／Publisher、役割・多要素認証の確認が残る。

`TestDistributions.ps1`は、Store版数、XMLエスケープ、宣言、関連付け、更新設定、危険なURL、コンテナー内の全ペイロードの一致、ZIPの自己完結性を確認する。MSIX作成ではMicrosoftの署名が有効なMakeAppxを使用し、スキーマ検証を無効化しない。

既存の開発者モードは無効、Windows Sandbox実行環境も未用意だったため、OSの信頼設定を変えずに実インストールを行う検証はできていない。同梱ランタイムの実行、共有.NET探索先の無効化、DLL読込先、AppContainer・OCR・保存等を別々に確認する。これは.NET未導入のクリーンOSでの実機試験の代替ではない。

Storeアカウント取得、正式な識別子・掲載条件・プライバシー案内の確定、Store審査、直接配布の署名手段・配信先、実インストール／更新／アンインストール、独立レビューを含む既存の正式公開条件は残る。`readyForPublicRelease`をパッケージ生成だけでtrueにしない。
