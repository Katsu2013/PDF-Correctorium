# 公開・Wiki同期の手順

## 正本と公開先

| 情報 | 正本 | 公開先 |
|---|---|---|
| ソース・リリース説明 | mainとdocs/releases | GitHub Releases |
| 操作ガイド | リポジトリ内wiki/ | GitHub Wikiへ同期 |
| 過去の監査 | reportsとdocs/archive | 日付付きの履歴 |
| Webサイト | docs/index.html | Pagesが実公開・確認できるまで公式入口にしない |

## 公開用ビルド

1. 指摘対応・版・ドキュメントをレビューしてコミットする。
2. クリーンなチェックアウトで`InvokeReleaseVerification.ps1 -RequireCleanGit`を実行する。
3. CIと同じSDKで、ロック済みパッケージから復元・発行する。
4. 新しい出力先のビルド・検証・全ZIP照合の記録をレビューする。
5. 対応するコミットにタグを付け、同じ検証済み成果物を公開する。
6. リリース説明の版・ハッシュを実ファイルから転記する。古い版の表を流用しない。
7. Wiki同期を行い、公開ページと入口を確認する。

- `BuildPortable.ps1 -RequireCleanGit`は発行前後にHEADと未コミット変更を検査する。
- 通常のローカル検証ビルドは未コミットでも作成できる。その成果物をクリーンな公開ビルドと偽って扱わない。
- 過去の公開ZIPは変更しない。旧ビルドのgitDirty・元コミットも書き換えない。
- クリーンな出所はビット単位の再現可能性や全OSでの動作保証を意味しない。

## ランタイムパックの復元

- `RestoreLockedPackages.ps1`でSHA-256を検証した固定アーカイブを用意する。
- SDKの暗黙のフレームワーク参照の解決を許可する。NuGet.Configの復元元は引き続きローカルの固定アーカイブのみ。
- `DisableTransitiveFrameworkReferenceDownloads=true`を指定しない。SDK 10.0.403ではWindowsDesktopの推移的参照にNETSDK1185を発生させる。
- デスクトップ発行では未使用のASP.NETパックを候補から除外する。ASP.NETの実参照が追加された場合は、黙って省略せずロック見直しを要求して停止する。
- 発行後に固定版.NETランタイムとバイナリのハッシュを照合する。ランタイム版を勝手に最新版へ浮動させない。

## Wikiを公開前に確認する

```powershell
python tools/CheckPublicDocumentation.py
python tools/PrepareWikiPublication.py --output outputs/wiki-publication-<新しい日時>
```

- 内部ページリンクは公開Wiki形式へ、リポジトリ側の資料はGitHubのmainへ向ける。
- 出力したMarkdownをレビューする。manifestは確認用で、Wikiへ公開するのはMarkdownのみ。
- mainへ未公開の資料を指すリンクがないか、ソース公開後に確認する。
- 同期はPDF-Correctorium.wiki.gitの既存チェックアウトへコピーして差分を確認する。他のページを一括削除しない。
- コミット・pushとIssue返信は利用者の明示承認後に行う。準備ツールは投稿・Git操作を行わない。

## 診断スイッチを使う場合

```powershell
$env:PDFCORRECTORIUM_ENABLE_DIAGNOSTICS = '1'
# このPowerShellから起動した診断プロセスだけに継承される。
# 診断完了後は通常起動へ戻す。
Remove-Item Env:PDFCORRECTORIUM_ENABLE_DIAGNOSTICS
```

- 未指定・`true`などでは診断を実行せず終了コード2で終了する。
- 通常のファイル起動と、PDF出力に必要な隔離ワーカーは有効化不要。
- これは診断コードの別バイナリ化ではなく、明示的な開発者用起動制限である。
- 通常利用者向けの操作画面やシステム全体の環境変数へ設定する必要はない。

## 公開文書の検査

- 個人のWindowsプロファイル絶対パスを公開用テキストに残さない。
- 現在のソース文書には`<repo>`などの表記を使う。過去の版・検証件数は保持する。
- 公開済みの履歴・過去PDFに記録された情報をGitの履歴改変で消す処理は行わない。
- プレリリースの導線はReleases一覧を使う。安定版専用の`/releases/latest`へ依存しない。
- API最低条件（Windows 10 2004以降）と、実機検証（Windows 11 x64）と、MicrosoftのOSサポートを区別する。

## 音声の検証範囲

| プロファイル | 使用場所 | 確認範囲 |
|---|---|---|
| NativePlayback（既定） | 音声出力可能なローカルWindows | 実WAV、実再生、進行キュー、一時停止・再開、取消、速度差 |
| SynthesisOnly | GitHub CI | 同じWindows音声生成処理で実WAV、速度差、取消と再利用。実再生は確認済みと扱わない |

- CIの生成専用プロファイルも、段落・読み方・PDF出力とUIの論理検証を行う。
- 検証JSONのspeechVerificationModeとnativeSpeechPlaybackVerifiedを必ず区別する。CIの成功だけで実機の音声出力を確認済みと表現しない。
