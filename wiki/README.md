# PDF Correctorium GitHub Wiki ソース

このフォルダーには、[GitHub Wiki](https://github.com/Katsu2013/PDF-Correctorium/wiki) 用の Markdown ソースファイル一式が格納されています。

---

## 📂 ファイル一覧

* `_Sidebar.md` : 全ページ共通の右側目次ナビゲーション
* `Home.md` : Wikiのトップページ
* `Getting-Started.md` : クイックスタート・起動方法
* `Ui-Overview.md` : 画面の見方と基本操作
* `Project-Files.md` : プロジェクト形式 (.pdfocrproj) の仕様
* `Ocr-Editing.md` : 文字修正・位置調整
* `Vertical-Text.md` : 縦書き・横書き・読み順
* `Review-Mode.md` : 校正・確認モードの使い方
* `Search-Replace.md` : 検索と一括置換
* `Ndlocr-Integration.md` : NDLOCR-Lite 連携
* `Redaction-Guide.md` : 安全な不可逆墨消し
* `Page-Editing.md` : ページ編集・回転・並べ替え
* `Bookmarks-Metadata.md` : しおり・文書情報の編集
* `Comments-Tags.md` : コメント・タグ・内部リンク
* `Keyboard-Shortcuts.md` : キーボードショートカット一覧
* `Settings-Workspaces.md` : 表示言語・設定プリセット
* `FAQ.md` : よくある質問（FAQ）

---

## 🚀 GitHub Wiki への反映方法

### 方法1: Web画面から手動でコピペ（数ページの場合）
1. [リポジトリのWiki](https://github.com/Katsu2013/PDF-Correctorium/wiki) を開きます。
2. ページ名に合わせてファイルの内容を貼り付けて保存します。

### 方法2: Gitで一括プッシュ（推奨）
GitHub Wiki は独立した Git リポジトリとしても管理されています。

```bash
# 1. Wiki専用リポジトリをクローン（URL末尾に .wiki.git）
git clone https://github.com/Katsu2013/PDF-Correctorium.wiki.git

# 2. この wiki フォルダの中身をすべてクローン先へコピー

# 3. コミットしてプッシュ
cd PDF-Correctorium.wiki
git add .
git commit -m "Initial wiki documentation"
git push origin master
```