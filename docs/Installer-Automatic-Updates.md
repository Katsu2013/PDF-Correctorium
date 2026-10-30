# Store・直接配布MSIXの自動更新（dev.255）

2026-10-28。利用者が選択済みのMSIX＋Store＋ZIP構成を維持する。直接配布の自動更新はWindowsのApp Installerへ委ね、独自の常駐EXE、サービス、スケジュールタスク、強制終了を追加しない。

## 利用者の操作

| 配布経路 | 自動更新 | アプリの更新メニュー |
|---|---|---|
| ZIP | 対象外 | 「配布ページを開く」。版確認や上書きを行わない |
| Store MSIX | Storeが管理 | Storeの更新画面を開く |
| 直接配布MSIX | WindowsのApp Installerが管理 | HTTPSの更新情報を実際に取得し、導入済み版と照合する |
| 未署名Development MSIX | 正式な更新経路なし | 未設定と表示。更新可能と案内しない |

直接配布版の手動確認は非同期で、編集中の画面をブロックしない。更新なし、通信／設定の失敗、新版ありを区別する。新版ありで続行を選ぶと、通常の終了処理へ進む。未保存の変更は保存／破棄／キャンセルを選べ、保存失敗やキャンセルなら更新も中止する。実際にウィンドウが閉じた後だけ、検査済みのローカル `.appinstaller` をWindowsで開く。Windowsがパッケージの署名・信頼・導入を検査する。別のアプリインスタンスへ終了を強制しない。

通常の自動更新はアプリのメニューとは独立してWindowsが行う。起動時確認の最小間隔24時間、背景確認、`ShowPrompt=false`、`UpdateBlocksActivation=false`を宣言する。背景確認はWindows標準では8時間間隔で、OS／組織ポリシーやネットワーク条件により遅延・停止する。アプリを強制的に閉じるAPIや、更新を起動の必須条件にする設定は使わない。

## 実装と検査

- `.NET`ターゲットと依存パッケージは変更しない。導入済みのPackage Name／Publisher／Windows版番号は、保護された現在プロセスのパッケージIDをWin32 APIで取得する。ZIP横の設定ファイルを更新指示として信用しない。
- 直接配布MSIXの保護された`distribution.json`だけが、固定のHTTPS `.appinstaller` 配信先を指定する。
- 確認は最大20秒、解凍後256KiBまで。HTTPS、資格情報なし、リダイレクトなし。XMLのDTDと外部参照を禁止する。
- `.appinstaller`の宣言URI、主パッケージの名前・発行元・x64・4要素の版番号を照合する。別アプリ、別発行元、EXE、平文URL、追加パッケージ、代替配信先、起動必須化、ダウングレード許可は受け付けない。旧版と同版はインストーラーへ渡さない。
- 更新確認の失敗を「最新版」と表示しない。重複確認を抑え、終了中の確認は取り消す。アプリの起動／PDF読込へネットワーク待ちを追加しない。
- 引き渡すのは検査済みXMLだけで、固有のキャッシュフォルダへ新規作成する。シンボリックリンク等へ転送された配置を拒否する。アプリ自身はEXEやDLLを置換しない。
- 直接配布の包装時は同一の`.appinstaller`をMSIX内と外に用意し、`uap13:AutoUpdate`で参照する。これにより対応するWindowsでMSIX単体から導入しても更新先を登録できる。現在のMSIXのWindows 11最小版はこの機能に対応する。
- Store／Developmentへ直接配布用の更新情報を埋め込まない。Windowsの信頼ストアや更新ポリシーを変更しない。

## 公開前の必須作業

Storeは開発者登録・正式なIdentity・提出が必要。直接配布は信頼される署名手段、同じPackage Name／Publisher、固定HTTPS配信先、版別の署名済みMSIXが必要。証明書の秘密鍵やパスワードをチャットや設定ファイルへ保存しない。

現時点ではアカウント・署名・公開配信先が未用意。制御した更新情報による実画面の確認、XML生成、MSIXスキーマ検証と、実際の署名付き旧版→新版への自動導入試験を区別する。後者が終わるまで「自動更新を実機検証済み」「正式公開可能」と表示しない。クリーン環境、通信失敗、無効な署名、別発行元、起動中の更新、設定・復旧データの引継ぎ、アンインストールも最終公開時に確認する。

直接配布の署名はSignPath Foundationを採用する方針。[署名方針](../CODE_SIGNING.md)と[申請・連携準備](SignPath-Foundation-Preparation.md)を用意した段階であり、申請・承認・GitHub Actions連携・実署名は未完了。Storeの署名とは別に扱う。

参照：[Windows標準の更新設定](https://learn.microsoft.com/en-us/windows/msix/app-installer/update-settings)、[埋込App Installer](https://learn.microsoft.com/en-us/windows/msix/app-installer/how-to-embed-an-appinstaller-file)、[GetCurrentPackageId](https://learn.microsoft.com/en-us/windows/win32/api/appmodel/nf-appmodel-getcurrentpackageid)。
