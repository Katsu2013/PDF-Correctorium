# 紹介ページ・Store画像の公開手順

2026-11-07。紹介ページのソース準備段階。GitHubへのcommit/push、Pagesの有効化、Store提出はこの作業では行っていない。

## 配置と共用

| 場所 | 用途 |
|---|---|
| `site/index.html` | 日本語の紹介ページ |
| `site/privacy.html` | ローカル処理・通信・保存データ・公開サポートの説明 |
| `site/assets/screenshots/` | READMEとサイトで共用する実機画像4枚 |
| `.github/workflows/pages.yml` | `site/`だけをPagesへ配信するワークフロー |
| `outputs/Store-Presentation-2026-11-07/` | ローカルのStoreアップロード用フォルダ（Git管理外） |
| `outputs/Store-Presentation-2026-11-07.zip` | 同フォルダをまとめたZIP。Storeへは解凍したPNGをアップロード |

元画像は無加工でコピーし、画素・サイズを維持。画像一覧と撮影上の留意点は[こちら](../site/assets/screenshots/README.md)。アプリの画面表示はdev.283であり、画像が公開済みリリースと同一であるとは案内しない。

## 公開する手順

1. 紹介ページとプライバシー説明を確認する。プライバシーのStore申告は、最終MSIXの権限・実装と照合する。
2. 所有者のcommit/push許可後、今回のREADME・site・pages.yml・このガイドだけを選んでcommitする。既に作業中のdev.283修正やセキュリティ報告書を混ぜない。
3. リポジトリの **Settings → Pages → Build and deployment → Source → GitHub Actions** を選ぶ。Environmentsの`github-pages`はmainのみの配信を許可する。
4. mainへのpush、またはActionsの **Publish introduction to GitHub Pages → Run workflow** で配信する。
5. 成功後、`https://katsu2013.github.io/PDF-Correctorium/` を確認。スマートフォン幅、画像拡大、ダウンロード先、プライバシーへのリンクを確認する。
6. StoreのWebサイトには上記トップURL、プライバシーには末尾`privacy.html`、サポートにはGitHub IssuesのURLを設定する。公開成功前に到達可能とは案内しない。

`docs/`全体やアプリ出力・監査資料をPages配信元にしない。公開は`site/`に限定する。リリースリンクは`/releases`を使用し、未公開dev.283やStoreの未公開製品へ直接ダウンロードさせない。

画像と文書・静的サイトの変更のため、アプリのリビジョン変更や再ビルドは不要。配信ワークフローはアプリのビルド・リリース・署名を実行しない。

参考：[GitHub Pagesの公開元設定](https://docs.github.com/en/pages/getting-started-with-github-pages/configuring-a-publishing-source-for-your-github-pages-site)。

## 紹介ページのデザイン更新（2026-11-07）

- 濃いグレーの冒頭・赤のアクセント・白背景の機能紹介で情報の強弱を整理。実機画像を主役にし、校正・リンク・読み上げを交互のレイアウトで紹介。
- 外部フォントやJavaScriptを追加せず、既存の無加工画像を維持。画像の遅延読み込みと寸法指定、キーボードのフォーカス表示、動きを減らす設定を維持。
- 一時ローカルサーバー経由でブラウザー表示を確認。1440px／390px幅で横方向のはみ出しなし。画像4枚の読み込み、画面紹介への移動、HTMLの入れ子・内部リンクの検査に成功。
- 確認画像はローカルの`outputs/pages-preview-2026-11-07/desktop.png`と`mobile.png`。GitHub Pagesへの公開は未実施。
