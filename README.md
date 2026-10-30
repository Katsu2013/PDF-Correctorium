# PDF Correctorium

<p align="center">
  <strong>文書をPC内で処理する、OCR校正・墨消し・読み上げに対応したWindows用PDFエディター</strong>
</p>

<p align="center">
  <a href="https://github.com/Katsu2013/PDF-Correctorium/releases"><img src="https://img.shields.io/badge/Release-v1.0.0--dev.199-blue.svg?style=flat-square" alt="Version"></a>
  <img src="https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20(64bit)-0078D6.svg?style=flat-square&logo=windows" alt="Platform">
  <img src="https://img.shields.io/badge/License-Apache%202.0-green.svg?style=flat-square" alt="License">
  <img src="https://img.shields.io/badge/Type-Portable%20(No%20Install)-orange.svg?style=flat-square" alt="Portable">
  <img src="https://img.shields.io/badge/.NET-10%20%2F%20WPF-purple.svg?style=flat-square" alt=".NET 10">
</p>

<p align="center">
  <a href="#japanese">日本語</a> | <a href="#english">English</a> | <a href="https://katsu2013.github.io/PDF-Correctorium/">公式ウェブサイト</a> | <a href="wiki/Home.md">公式Wiki</a>
</p>

---

<a id="japanese"></a>

## 🚀 今すぐダウンロード（無料・インストール不要）

パソコンにインストールせず、フォルダーから起動できます。PDF処理にはWindowsのAppContainer隔離機構を使用します。

| 配布ファイル | 対応OS | 形式 | ダウンロードリンク |
| :--- | :--- | :--- | :--- |
| **PDF Correctorium (最新ポータブル版)** | Windows 10 / 11 (64bit) | ZIPアーカイブ (容量はリリースページに記載) | 👉 **[⬇️ 最新版をダウンロード (.zip)](https://github.com/Katsu2013/PDF-Correctorium/releases/download/v1.0.0-dev.199/PdfCorrectorium-v1.0.0-dev.199-win-x64.zip)** |

### 🔰 3ステップで使えるクイックスタート
1. 上記リンクから ZIP ファイルをダウンロードします。
2. ZIP ファイルをお好みのフォルダーに解凍（展開）します。
3. フォルダー内の **`PdfCorrectorium.exe`** をダブルクリックするだけで即座に起動します！

> [!TIP]
> **完全ローカル処理による安心設計**  
> PDF Correctorium の文書処理はお使いのパソコン内で完結し、クラウドへ文書をアップロードしません。墨消し後は出力PDFを確認してください。**プロジェクトファイルには原本・編集前データが残るため、墨消し済みの共有物として扱わないでください。** 保証範囲は[セキュリティ設計](docs/SECURITY-DESIGN.md)に記載しています。

---

## 📸 アプリケーション画面

<p align="center">
  <img src="outputs/PdfCorrectorium-Documentation/assets/svg/SCR-001_MainWindow_Wireframe-v1.0.0-dev165.svg" alt="PDF Correctorium メイン画面" width="90%">
  <br>
  <em>直感的なサムネイル操作、多彩なツールバー、ドッキング可能なパネルを備えた作業画面</em>
</p>

<div align="center">
  <table>
    <tr>
      <td width="50%" align="center">
        <img src="outputs/PdfCorrectorium-Documentation/assets/svg/SCR-013_Redaction-v1.0.0-dev165.svg" alt="指定範囲を除去して検証する墨消し" width="100%">
        <br>
        <strong>🛡️ 指定範囲の文字を除去する「本物の墨消し」</strong><br>
        <sub>文字描画命令そのものを完全消去。スポイト機能・自動検証付き</sub>
      </td>
      <td width="50%" align="center">
        <img src="outputs/PdfCorrectorium-Documentation/assets/svg/SCR-009_ReviewMode_Wireframe-v1.0.0-dev165.svg" alt="OCR校正・確認モード" width="100%">
        <br>
        <strong>✏️ 精密なOCR文字校正（NDLOCR連携）</strong><br>
        <sub>枠ズレ修正、縦書き・読み順整理、テンポよく確認できる専用モード</sub>
      </td>
    </tr>
  </table>
</div>

---

## 🌟 PDF Correctorium の3つの主な特長

### 1. 🛡️ 対象範囲の表示・本文文字を除去する墨消し
- **指定範囲の文字を除去**: 見た目を塗りつぶすだけでなく、対象の文字データを除去し、範囲内に本文文字が残っていないかを出力時に検査します。別ページ・文書情報・添付ファイル等にある同じ情報は、別途確認が必要です。
- **多彩な指定方法**: 「PDFの文字を選択」「四角形」「自由な多角形」「フリーハンド（手描き）」から柔軟に範囲を指定可能。
- **自動検証機能**: 保存後に対象範囲の本文文字を再検査し、既存の構造ツリーの除去も確認します。文書全体のタグ・代替テキストが失われることを警告します。範囲外に複製された情報や添付ファイルを含む全ての機密情報の除去を保証するものではありません。
- **スポイト機能**: 書類の背景色と同じ色を取得して、目立たず自然にマスキングできます。

### 2. ✏️ 国立国会図書館「NDLOCR-Lite」連携 & 透明OCRテキストの精密校正
- **文字・位置・文字間隔を思い通りに修正**: 誤認識された文字の打ち直しはもちろん、枠の位置・大きさ・角度・文字ごとの間隔をマウスや数値で細かく調整可能。
- **日本語組版に完全対応**: 縦書き・横書きの混在や、文章を読む順番（読み順）を視覚的に整理できます。
- **専用の「校正・確認モード」**: 文字枠の誤ズレを防ぎながら、未確認の文字をキーボード操作やボタンでテンポよく連続チェックできます。
- **NDLOCR-Lite 連携**: 国立国会図書館の「NDLOCR-Lite」出力（JSON/XML）から高精度な文字枠データを取り込めます。

### 3. 📑 ページの並べ替え・結合・しおり作成・画質無劣化
- **ページの自由な整理**: サムネイルのドラッグ＆ドロップで並べ替え、90度回転、不要ページの削除。
- **他のPDFからページ挿入**: 別のPDFから必要なページを取り込んで結合できます。
- **通常のページ整理では元画像を保持**: ページの並べ替え・回転・結合で、既存画像を不要に再圧縮しません。画像最適化や範囲墨消しなど、画像の変換を伴う機能とは区別します。
- **しおり（目次）と文書設定**: クリックで飛べる階層目次の作成、タイトルや作成者情報、PDFを開いたときの表示方法（見開き・左右綴じなど）を自在に設定。
- **元データを壊さない安全設計**: 元PDFを直接上書きせず、プロジェクト（`.pdfocrproj`）にいつでも保存して続きから再開できます。

---

## 💡 他のツールとの違い

| 比較項目 | 一般的な市販PDFソフト (Acrobat等) | Web上のオンライン編集ツール | **PDF Correctorium** |
| :--- | :--- | :--- | :--- |
| **利用料金** | 高額な月額/年額サブスクリプション | 無料枠あり（制限多数・有料誘導） | **完全無料 (Apache 2.0・商用利用可)** |
| **セキュリティ** | ローカル処理 | ⚠️ **外部クラウドへ文書を送信** | 🔒 **文書処理は完全ローカル・PDF／OCR本文の外部送信なし** |
| **容量・回数制限** | なし | 厳しいファイル容量・回数制限あり | **回数・期限・利用料の制限なし（処理の安全上限あり）** |
| **墨消しの安全性** | 有料上位版のみ対応（高額） | 単なる黒塗り（裏文字が残る危険大） | 🛡️ **文字コード完全抹消＋自動検証** |
| **OCR透明テキスト校正** | 大まかな文字修正のみ | 非対応 | ✏️ **枠位置・字幅・縦書き・読み順まで精密校正** |
| **導入形態** | 重いインストーラー・要管理者権限 | ブラウザのみ（要ネット環境） | 🚀 **完全ポータブル（解凍して即使える）** |

---

## 📚 詳しい使い方・ドキュメント (公式Wiki)

文書処理にネット接続は不要です。利用者が開くWebリンクや更新確認では通信します。ローカル処理、処理上限、墨消しと編集用プロジェクトの違いは[具体例を含む説明](docs/Local-Processing-And-Redaction-Scope.md)を参照してください。

さらに詳しい操作方法や活用ガイドは、[公式ドキュメント (Wiki)](wiki/Home.md) をご覧ください。

* 🔰 **[クイックスタート・起動方法](wiki/Getting-Started.md)**：ダウンロードから最初のPDF出力まで
* 🛡️ **[墨消し（マスキング）完全ガイド](wiki/Redaction-Guide.md)**：安全な機密保護の手順と注意点
* ✏️ **[OCRテキストの校正・編集](wiki/Ocr-Editing.md)**：文字枠・字幅の調整
* 🔍 **[校正・確認モードの使い方](wiki/Review-Mode.md)**：未確認テキストの効率的なチェック
* 📑 **[ページ編集・並べ替え・結合](wiki/Page-Editing.md)**：ページ構成の自由な編集
* 🔖 **[しおり・文書プロパティの編集](wiki/Bookmarks-Metadata.md)**：目次作成と表示設定
* 🇯🇵 **[国会図書館 NDLOCR-Lite 連携](wiki/Ndlocr-Integration.md)**：OCR認識結果の取り込み
* ⌨️ **[キーボードショートカット一覧](wiki/Keyboard-Shortcuts.md)**：効率的なキー操作
* ❓ **[よくある質問 (FAQ)](wiki/FAQ.md)**：トラブルシューティング

---

## dev.199からの主な追加機能

<!-- FINAL-LOCAL-VERIFICATION -->
最終のローカル確認：dev.274のRelease・契約・21種類のアプリ診断・ZIP照合は成功。コミット・署名・外部公開はまだ行っていません。詳しい結果は上記／下記の累積リリース文書を参照してください。


- 選択行RapidOCR再認識と資料別OCR辞書、類似語・文字密度の校正。[操作](wiki/RapidOCR.md)
- 既存リンクの編集・整列・ロック、自動リンク候補、一括墨消し。
- Windows読み上げ、手動段落、共通読み方・行の例外と画面確認用ルビ。[操作](wiki/Read-Aloud.md)
- ライト／ダーク／OS追従、独立パネルと左右収納、ツールバー表示の保存。
- TXT／CSV作業データと完成PNG／JPEG、自己完結型.NET 10配布。[出力](wiki/Data-Exports.md)

[累積変更・互換性・制約と検証](docs/releases/v1.0.0-dev.274.md)。この節は次回公開候補のソースを説明します。未OCRページ／PDF全体の新規認識、署名済みMSIX・Store実公開は未完了です。Wiki原稿はリポジトリ内で更新しており、GitHub Wikiへの別途公開は行っていません。

## 🗺️ 開発ロードマップと現在のバージョン

本プロジェクトは、**「日常業務で高価な市販ソフトの代わりに使える、実用的なオープンソースPDF総合エディタ」** の実現を目指して継続的に開発されています。

- **現在のリポジトリSnapshot**: `v1.0.0-dev.274`（次回公開候補。公開状況はReleases参照）
- **今回の比較元となる公開版**: `v1.0.0-dev.199`

[前回公開dev.199からの累積変更](docs/releases/v1.0.0-dev.274.md)：RapidOCRによる行の再認識、共通OCR辞書・品質分析、リンク編集と一括処理、読み上げ・段落・読み方、テーマとドッキング改善、.NET同梱と安全な配布基盤をまとめています。署名・正式公開は別途準備が必要です。

[dev.270](docs/releases/v1.0.0-dev.270.md)：削除済みの旧OCR項目が読み上げ順へ混ざり、手動でまとめた「Chapter」と「1」を分断する不具合を修正します。

[dev.269](docs/releases/v1.0.0-dev.269.md)：原文の検索・コピーを維持する読み上げ方針を確定し、PDF出力後の案内で読み方情報の保存と外部リーダーの発音を区別します。

[dev.268](docs/releases/v1.0.0-dev.268.md)：読み方付きPDFの出力が、行末のハイフンで停止する不具合を修正します。

[dev.267](docs/releases/v1.0.0-dev.267.md)：読み方をPDFの読み上げ用代替テキストに保存します。本文の文字と確認用ルビの扱いを維持し、無変更リンクのページ別確認など出力の重複処理も省きます。[仕様](docs/Pdf-Reading-Export.md)。

[dev.266](docs/releases/v1.0.0-dev.266.md)：横書きのChapterと縦書きの1を、位置や方向を保って同じ段落に指定できます。パネル収納は左右別に一括、固定へ戻すのは個別へ統一し、後から開く読み上げにも収納設定を適用します。[仕様](docs/Chapter-Grouping-And-Side-AutoHide.md)。

[dev.264](docs/releases/v1.0.0-dev.264.md)：段落編集で各段落を薄い外枠で囲み、選択中だけ強調します。既存の枠表示・不透明度設定に連動し、現在ページだけを描画します。[仕様](docs/Paragraph-Outlines.md)。

[dev.263](docs/releases/v1.0.0-dev.263.md)：自動収納タブをアイコン＋文字の約32px幅へ拡大し、展開時に収納前の幅を引き継ぎます。狭すぎる旧保存値を補正し、有効な展開幅は次回起動へ保持します。[仕様](docs/AutoHide-Tab-And-Width.md)。

[dev.262](docs/releases/v1.0.0-dev.262.md)：共通の読み方辞書と行ごとの例外、ルビのダブルクリック編集、読み上げパネルのドッキングと左右端への自動収納に対応します。[操作と保存の互換性](docs/Shared-Readings-And-Docked-Speech.md)。

[dev.259](docs/releases/v1.0.0-dev.259.md)：読み上げウィンドウをコンパクトにし、最大化と下部の「閉じる」を削除。右上の×で閉じ、「音声と速度」を開閉すると高さも追従します。

[dev.258](docs/releases/v1.0.0-dev.258.md)：メインツールバーにヘッドホン形の読み上げボタンを追加。ウィンドウを開き、表示の切り替えはアプリ共通のツールバー設定へ保存します。

[dev.257](docs/releases/v1.0.0-dev.257.md)：読み上げの前後移動と段落／行の単位切替、行を保持する手動段落指定を追加しました。段落編集と読み上げの区切りを共通にし、指定はUndoとプロジェクト保存に対応します。保存形式1.13の読込にはdev.257以降が必要です。[操作・互換性](docs/Speech-Navigation-And-Paragraphs.md)。

[dev.256](docs/releases/v1.0.0-dev.256.md)：読み上げ時に同じ段落の折返し行をつなぎ、行ごとの不自然な間を減らします。マーカーは音声の進行に合わせて各行へ移ります。OCRデータや保存内容は変更しません。ナレーター専用のNanami／Keita（Natural）は、公式ローカルAPIを確認できず未対応です。[仕様・制約](docs/Read-Aloud-Paragraphs.md)。

[dev.255](docs/releases/v1.0.0-dev.255.md)：直接配布MSIXの更新設定を埋め込み、実際の版確認と保存・終了後のWindowsへの引き渡しを追加しました。自動更新はWindows標準のApp Installerへ、StoreはStoreへ委ねます。ZIPは手動更新を維持します。署名・配信先・Store登録と、本物の更新導入試験は別途必要です。[操作と公開前条件](docs/Installer-Automatic-Updates.md)。

[dev.254](docs/releases/v1.0.0-dev.254.md)：文書なしで設定を開けない問題と、Windowsがライトのときのダークメニューを修正しました。OCR読込アイコンはテーマの前景色へ、区切り線・選択ボタン外周・ドッキングの明るい枠と下地は灰色系へ合わせています。既存のアイコン形状とPDF紙面は維持します。

[dev.250](docs/releases/v1.0.0-dev.250.md)：.NET 10.0.14を同梱するWindows x64自己完結型へ変更し、ランタイムの別途インストールが不要なZIPを生成します。MSIX生成・App Installerの更新設定と、導入経路に応じた「更新を確認」メニューも追加しました。MSIXはStore登録・署名前の検証用で、通常端末へ配布する正式インストーラーとしての公開はまだ行っていません。[導入・配布手順](docs/Distribution-Operations.md)を参照してください。

[dev.251](docs/releases/v1.0.0-dev.251.md)：上記の配布基盤を引き継ぎ、背景辞書更新で見開きページの移動が破棄される競合も修正しています。

[dev.252](docs/releases/v1.0.0-dev.252.md)：UIのライト／ダーク／Windows連動と、Windows標準音声による読み上げを追加した中間候補です。最終の実装・検証・配布場所は以下のdev.253を参照してください。

[dev.253](docs/releases/v1.0.0-dev.253.md)：UIのライト／ダーク／Windows連動と、3範囲のWindows音声読み上げを実装・検証しました。PDF紙面の色を保ち、OCR枠の移動前の位置や墨消しとの対応も読み上げ除外へ反映します。契約63件と配布版20系統の回帰診断を通過した自己完結型ZIP／未署名検証用MSIXを生成済みです。操作方法・絶対パス・検証結果は同ノートを参照してください。GitHub／Storeでの正式公開は未実施です。

[dev.249](docs/releases/v1.0.0-dev.249.md)：.NET 10、PDFium 158.0.8125、qpdf 12.4.2へ更新し、校正診断と連続描画を修正した共有ランタイム方式の履歴版です。

[dev.247](docs/releases/v1.0.0-dev.247.md)：編集単位を「段落 → 行 → 文字」の順に整理。表示順と内部の動作を分離し、初期値の行編集は維持します。画像調整の共通の操作不可理由、OCR未選択案内、処理進捗の重複表示も抑制しました。[dev.246のアイコン表示](docs/releases/v1.0.0-dev.246.md)と保存形式1.12は維持しています。

[dev.241](docs/releases/v1.0.0-dev.241.md)：一覧の「詳細列」チェックボックスを、列見出しの右クリックメニューへ変更。列ごとに表示を選べ、既定の列表示にも戻せます。選択・並べ替え・分析結果を維持します。

[dev.240](docs/releases/v1.0.0-dev.240.md)：OCRプロパティの確認ステータス・文字方向を約半幅の横並びにし、画像調整も「行全体／選択文字以降」の短いボタンで横並びに整理。狭いパネルでは縦並びへ戻し、対象範囲・操作不可の理由・既存アイコン・キーボード操作を維持します。

[dev.239](docs/releases/v1.0.0-dev.239.md)：×で閉じたページ一覧・しおり・機能別プロパティを、表示メニューの1回の操作で再表示できるよう修正。チェック状態と保存状態を同期し、元の格納先を失った旧レイアウトにも復元先を用意しました。浮動表示・自動非表示・再起動後も対象です。

[dev.238](docs/releases/v1.0.0-dev.238.md)：候補一覧は作業に必要な情報を基本表示にし、幅・高さ・比較件数・頻度等は「詳細列」で確認する形に整理しました。文字数の外れ値、キーワード幅、類似語、検索、共通領域候補に共通の切替を設け、固定・修正不可・適用時の注意は隠しません。フォント一覧や一括墨消しの不要な表示も整理しています。[一覧別の表示方針](docs/List-Presentation.md)。

[dev.237](docs/releases/v1.0.0-dev.237.md)：文字数の外れ値を、CJK・欧文・半角カナ等の字幅と枠の縦横比を考慮した密度判定へ変更。日本語行に欧文の生の文字数を当てはめず、文字種構成に応じた「目安文字数」「密度比」を表示します。不安定な基準での判定を避け、総当たり比較を廃止しました。[判定式・制約](docs/OCR-Character-Density.md)。

[dev.236](docs/releases/v1.0.0-dev.236.md)：文字数の外れ値一覧に「固定済みの領域も表示」を追加。初期は位置・サイズ固定の候補を非表示にし、再分析なしで切替・固定変更・Undo／Redoへ追従します。未埋め込みフォントの注意はプロパティへ移し、透明文字専用と確認できたフォントは通知しません。署名など他の注意事項は上部に残します。範囲／ページ／PDF全体OCRは[確認済み計画](docs/RapidOCR-New-Recognition-Plan.md)であり、まだ未実装です。

[dev.171からの変更点・互換性・検証結果](docs/releases/v1.0.0-dev.199.md)をご確認ください。

この開発スナップショットでは、元PDFの文書内リンク／HTTP(S)リンクを専用モードで表示・編集でき、文字選択または範囲指定から新しいリンクを作成できます。複数行の文字リンクは一括編集し、出力PDFでは行ごとのクリック範囲になります。ページ番号・ヘッダー／フッター・文字透かし、入力PDFの注意事項表示にも対応しています。日本語印字用フォントを同梱しています。画像透かしや危険なリンク動作の実行には対応していません。

[dev.200のリンク編集機能・互換性・検証状況](docs/releases/v1.0.0-dev.200.md)と、[dev.204の表示ページ優先読み込み・検証状況](docs/releases/v1.0.0-dev.204.md)をご確認ください。公開済みの配布版は上記dev.199です。

[dev.205の確認済みアイコン反映内容](docs/releases/v1.0.0-dev.205.md)もdev.206のビルドへ含めています。公開済みのGitHubリリースを更新したという意味ではありません。

[dev.207の表示修正](docs/releases/v1.0.0-dev.207.md)：リンク表示／編集のツールバーアイコン、対応するメニュー項目のアイコン、リンク枠の位置・サイズ合わせアイコンを追加・統一し、OCRプロパティに残っていた二重の区切り線を修正しました。

[dev.208のプロパティUI統一](docs/releases/v1.0.0-dev.208.md)：全編集モードで「説明」の表示設定を共通化しました。説明を非表示にしても選択件数・リンク先・警告・エラーは残ります。リンクの整列／サイズ合わせはOCRと同じ小型アイコンボタン、ロックは押下状態を示すアイコンとラベルになり、OCR関連・リンク編集で共通するリンク操作は同じ順序・見た目になりました。既存の整列基準とロックの保護範囲は変更していません。

[dev.209の操作性・読込待機修正](docs/releases/v1.0.0-dev.209.md)：一括処理は必要なページを優先して準備し、文書全体の読込完了は待ちません。回転・切り抜き座標と既存リンクを正しく使い、中止は通常の読込を止めません。OCR編集後のリンクロックは画面上の最新位置を固定します。承認済みアイコンも68配置へ追加・共通化しました。新しい図柄の候補は確認待ちです。

[dev.211のダイアログ下部の表示統一](docs/releases/v1.0.0-dev.211.md)：ダイアログ下部のボタンは、保存・適用・既定値に戻す・実行・中止等も含めてアイコンなしに統一しました。画面内やメニュー・ツールバーの操作アイコンは維持しています。

[dev.212の幅・高さ合わせアイコン](docs/releases/v1.0.0-dev.212.md)：承認された「枠＋両矢印」の案に変更し、外側の基準線を省いて枠と矢印の間隔を広げました。OCRとリンクのボタンは同じ形状・描画方式を使用します。他の再検討アイコンは未反映です。

[dev.213のプロパティ分割](docs/releases/v1.0.0-dev.213.md)：OCR・校正／リンク／墨消し／コメント・タグを独立したパネルにし、既定は右側のタブにまとめます。各パネルは移動・分割・切り離し・非表示に対応し、配置はアプリ共通で保存します。

[dev.215の実PDFでの自動調整修正](docs/releases/v1.0.0-dev.215.md)：OCRの点数と画像の点数が異なる目次でも、点線の実範囲を本文・ページ番号の基準にします。本文の推定では局所的な文字境界に合わせて幅の目安を更新し、累積ずれを狭い想定位置へ強制しません。文字列を書き換えず、点数の違いは案内します。行単位の画像コピーは対象領域に限定します。

[dev.224の10月14日監査是正](docs/releases/v1.0.0-dev.224.md)：診断引数より既存ファイルを優先し、所有者を確認できる停止済み一時領域の清掃を追加しました。進捗通知は短時間でスキップ可能にし、最終完了通知は必須のままです。署名証明書がないため、未署名の開発検証版です。

[dev.225の文書データ出力・正規表現検索](docs/releases/v1.0.0-dev.225.md)：ファイルメニューの「作業データ・ページ画像の出力」から、編集後OCRのTXT、独自コメント・タグのCSV、墨消し・装飾反映済みPNG/JPEGを保存できます。TXT/CSVは機密情報を含み得る作業データとして警告します。一括墨消しには確認付きの正規表現候補検索を追加しました。[既存機能との重複整理](wiki/Roadmap.md)と[設計・制約](docs/Document-Exports-and-Regex.md)も更新しました。GitHub公開版は上記dev.199のままです。

[dev.229の候補一覧改善](docs/releases/v1.0.0-dev.229.md)：類似語・誤認識候補の「現在の語」「修正候補」の列見出しによる並べ替えを復旧し、この2列だけ文字を大きくしました。差分強調・コピー・部分修正後の一覧保持は維持します。

[dev.235の類似語修正の継続](docs/releases/v1.0.0-dev.235.md)：類似語を部分修正したあと、PDF上のOCR行を移動・サイズ調整しても候補一覧を維持します。位置だけの変更やそのUndo／Redoでは全体再分析を要求しません。文字・削除・固定状態・ページ構成が変わった場合の古い候補の拒否は維持します。

[dev.230の選択行再認識](docs/RapidOCR-Line-Recognition.md)：日本語モデルを同梱したRapidOCRで横書きの1行を再認識できます。明確で高信頼な文字抜けだけを自動補完し、置換・削除・曖昧な追加は原画像との比較画面で確認します。処理はオフライン・隔離ワーカーで行い、固定状態と元のOCRを保持し、Undoで戻せます。全文OCRや縦書き・ルビの再認識は対象外です。

dev.232以降は、再認識結果の適用時に、同じ行画像から行全体の文字幅・境界も自動調整します。文字列が一致した場合も調整し、文字の訂正とサイズ調整は1回のUndoで戻せます。画像から判定できない場合は元の範囲を保持し、ステータスバーで知らせます。

dev.233で、白余白付きの画像だけで1回認識し、候補を1つに統一しました。参考用の別候補は表示しません。文字抜けの自動補完は単一結果の信頼度・挿入位置・原画像の根拠で判定し、置換・削除は引き続き確認します。

[dev.234の再OCR余白修正](docs/releases/v1.0.0-dev.234.md)：認識用の余白を文字幅へ混入させず、画像中の実文字範囲だけを配置します。切り出しの画素位置を固定し、同じ文字を繰り返し適用する際の行端・文字幅の拡大を防ぎます。上下の読み取り余白と認識時の白余白も広げました。

ソースから開発する場合、新しいクローンでは最初に `powershell -File tools/RestoreLockedPackages.ps1` を実行してください。Gitへ直接格納しない大きなOCR復元用パッケージを公式NuGetから取得し、固定SHA-256を検査します。その後は既存のローカルNuGet設定でオフライン復元・ビルドできます。配布アプリ自体は、この取得手順もネットワーク通信も不要です。

[dev.228の一致箇所マーカー](docs/releases/v1.0.0-dev.228.md)：OCR枠を非表示にしていても、類似語・品質分析・検索から移動したキーワードだけを半透明の黄色マーカーで示します。枠の不透明度0%でも表示し、通常の枠は再表示しません。

[dev.227の表示改善](docs/releases/v1.0.0-dev.227.md)：通常のリンク確認進捗をステータスバーへ移動しました。「表示 → OCR枠を表示」で枠・塗りを非表示にして赤文字だけで編集でき、枠の不透明度はアプリ設定で調整・保存できます。

[dev.226の類似語分析改善](docs/releases/v1.0.0-dev.226.md)：進捗バー、部分修正後の一覧保持、技術用語内の`」`とj/Jの混同候補、相違箇所の強調表示、候補へ移動した際のOCR表示縮小を修正しました。頻度は分析時の値、Undoや他の編集後は再分析が必要です。

[dev.223の説明文・PDF出力安定化](docs/releases/v1.0.0-dev.223.md)：類似語分析の画面説明から具体的な誤認識例を除き、PDF出力の状態読取がワーカーの原子的な更新を妨げないように修正しました。隔離権限と出力PDFの読取制限は維持しています。

[dev.222の類似語による誤認識候補検出](docs/releases/v1.0.0-dev.222.md)を追加しました。OCR品質分析の第3タブで文書全体の英数字用語を比較し、疑わしい語を候補表示します。確認した選択候補だけを修正でき、一括Undoに対応します。[操作・制限](docs/Similar-Word-Analysis.md)を参照してください。dev.221のAppContainer隔離・入力制限・墨消し安全対策を保持しています。未署名・独立レビュー未完了の開発検証版であり、正式公開済み版とは区別します。

[dev.221の設計・セキュリティ是正](docs/releases/v1.0.0-dev.221.md)：当時のRelease・契約38件・配布版セキュリティ125件・ページ履歴44件・永続化61件・版管理18件を検証した記録です。既存構造ツリーの除去に伴うアクセシビリティへの影響と、正式公開前の残条件は[セキュリティ設計](docs/SECURITY-DESIGN.md)を参照してください。

[dev.220の追加是正最終版](docs/releases/v1.0.0-dev.220.md)：4件の対策と、利用者が直接開いたPDFの保存時の互換性を確認しています。

[dev.219の追加セキュリティ是正](docs/releases/v1.0.0-dev.219.md)：旧形式を含むページ数制限、外部PDFのネットワーク参照の明示選択、サムネイルの画素数検査・縮小読込、OCR JSONの逐次解析と構造・本文量制限を追加しました。

[dev.218のセキュリティ是正](docs/releases/v1.0.0-dev.218.md)：ZIPの実読込量を制限し、OCR XMLを逐次解析へ変更しました。XML専用の32MiB上限とノード数・深さ・本文量の制限を追加し、QPDF引数の参照番号・パス検証、同梱ツール優先の探索を統一しています。

[dev.217のOCR操作改善](docs/releases/v1.0.0-dev.217.md)：選択対象・固定状態と画像調整の作用範囲を短く表示し、行全体／選択文字以降を文字付きボタンへ整理しました。読み方・座標・回転は詳細へ、一般説明は操作ガイドへ移しています。比較表示などは後続段階です。

[dev.210のアイコン追加](docs/releases/v1.0.0-dev.210.md)：当時の静的監査142配置を反映しました。そのうちダイアログ下部の配置は、dev.211で利用者の方針に合わせて意図的な除外へ変更しています。

[dev.206の追加機能](docs/releases/v1.0.0-dev.206.md)：リンク枠をCtrl+クリックで複数選択し、最初の矩形を基準に整列・サイズ統一できます。ロック中は移動・サイズ変更・整列・削除を禁止します。目次等の末尾ページ番号からの自動リンク、同一範囲の複数ページへの墨消し、文字列検索による一括墨消しは、候補一覧と元ページ／移動先プレビューを確認してから適用します。

設定で新規文書の初期編集モードを選べ、保存したプロジェクトのモードを優先して復元します。モードだけの変更は未保存扱いにせず、通常保存時に記録します。メイン／ページ表示ツールバーのボタンと入力部品は右クリックで表示を切り替え、アプリ共通で保存します。全非表示でも「表示 → ツールバーボタンの表示」から復元できます。[確定仕様・使い方・制限](docs/Next-Implementation-Plan.md)も参照してください。

PDF／プロジェクトを開くと、最初のページを先に表示し、表示中のページのリンクから優先して読み込みます。準備できたページは、未表示ページの読み込み完了を待たずにリンク枠を表示・編集できます。見開き／連続表示でも表示ページを優先し、ページ移動・スクロール・倍率変更を利用できます。残りのリンクとPDF特性はバックグラウンドで確認し、保存／出力時は完了を待ってリンク情報の欠落を防ぎます。元PDFの同一性検証や埋め込みPDFの展開など、初期表示に必要な処理は省略しません。

ページ番号は表紙を除いて2ページ目から「1」にでき、奇数・偶数で位置を変えられます。設定画面の左右の簡易プレビューで配置を確認し、適用後は編集中のページにも表示します。

> [!NOTE]
> **💡 現バージョンで未対応の機能（今後の実装候補）**  
> 一般的な市販ソフトに備わっている以下の機能は、現バージョンではまだ対応していません：  
> - 既存本文テキストの直接打ち直し（OCR透明テキストではなく既存ベクターフォントの直接編集）  
> - 埋め込み画像の直接差し替え・リサイズ  
> - 蛍光ペン（ハイライト）・下線・電子印鑑・付箋メモなどの標準注釈（`/Annots`）  
> - パスワード設定・印刷禁止等のセキュリティ暗号化  
> - 未OCR範囲・ページ／PDF全体の新規OCR（選択した横書き行のRapidOCR再認識は実装済み）  
> 
> これらの機能拡張計画については [開発ロードマップ (ROADMAP.md)](ROADMAP.md) をご覧ください。

---

<details>
<summary>🛠️ <strong>開発者向け情報・詳細な技術仕様・ビルド手順（クリックで展開）</strong></summary>

### 前提条件とビルド手順
dev.248以降はアプリ・共通ライブラリ・診断を.NET 10に統一し、Visual Studio 2026 / .NET 10 SDKでビルドします。dev.250のBuildPortableは.NET 10.0.14同梱の自己完結型（win-x64、複数ファイル、非トリミング）を生成します。利用者の別途ランタイム導入は不要ですが、ソースを通常ビルドして実行する開発環境にはSDKまたはDesktop Runtimeが必要です。MSIXはWindows 11 x64を対象とし、対応OSのサポート期間にも従います。クリーンOS／実インストールと更新の試験は未完了です。

```powershell
$env:DOTNET_CLI_HOME="$PWD\.dotnet-cli"
$env:DOTNET_CLI_TELEMETRY_OPTOUT="1"
powershell.exe -NoProfile -File tools/RestoreLockedPackages.ps1
dotnet build PdfCorrectorium.sln -c Release
dotnet run --project tests/PdfCorrectorium.ContractTests -c Release --no-build
```

ポータブル版の発行スクリプト：
```powershell
.\tools\BuildPortable.ps1
```

全7プロジェクトと公開用資源の検証は、Windows PowerShell 5.1で `tools/InvokeReleaseVerification.ps1 -OutputDirectory <新しい検証先>` を使用します。このコマンドはビルドを行います。利用済みフォルダーを圧縮する場合も、直接ZIP化せず [公開用ステージの手順](docs/Distribution-Operations.md)に従って利用者データを除外してください。

### バージョン管理方針
アプリの版番号は `Directory.Build.props` だけで定義します。必須の改訂・検証手順は [VERSIONING.md](VERSIONING.md)、今後の作業に適用するルールは [AGENTS.md](AGENTS.md) を参照してください。

### 安全性の修正と技術的詳細
- **文字選択の墨消し (dev.171)**: 編集画面で確認・保存したPDF座標の位置と大きさのまま出力。出力時に別矩形へ作り直さず、文字選択帯へ非表示の描画余白も加えない完全幾何学一致を実現。文字命令から文字符号のみを除去し、画像化フォールバックを行わずベクター構造を保持。
- **リソース管理と隔離プロセス (dev.159, dev.131)**: 大量ページの出力時に不要となったPDFiumのページリソースをチェックポイント方式で解放。qpdf本体およびPDF出力ワーカーにプロセスツリー終了とWindowsジョブオブジェクトによる資源制限（メモリ・プロセス数制限）を適用。
- **プロジェクト保存形式 (1.10)**: 元PDFを相対参照する「通常モード」と、元PDFを内包して単一ファイルで持ち運べる「ポータブルモード」を完備。複数範囲リンクとリンク表示状態も保存します。旧形式1.0～1.9を読み込めますが、1.10の読込にはdev.200以降が必要です。

より詳細なアーキテクチャやテスト戦略は [設計資料の目次](outputs/PdfCorrectorium-Documentation/README.md) を参照してください。

</details>

---

## 開発体制・クレジット

本プロジェクトの設計、実装、テスト、およびドキュメント作成は、OpenAI の **ChatGPT Codex** を活用したAIペアプログラミング・共同開発（Vibe Coding）によって行われています。

## Code signing policy / 署名方針（準備中）

GitHubで直接配布するMSIXは、SignPath FoundationのOSS向け署名を採用する方針です。現在は未申請・未承認で、署名サービスとの連携も未実装です。[Code signing policy・プライバシー方針](CODE_SIGNING.md)と[申請準備・残条件](docs/SignPath-Foundation-Preparation.md)を用意しています。Store版はMicrosoft Store側の署名を利用する方針です。

## ライセンス

[Apache License 2.0](LICENSE) のもとで公開されています。商用・非商用を問わず完全無料でご利用いただけます。第三者コンポーネントのライセンスは [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) をご確認ください。

---

<a id="english"></a>

## Overview (English)

**PDF Correctorium** is a free, portable Windows application for comprehensive PDF editing, secure irreversible redaction, bookmark/metadata management, and precision invisible OCR text proofreading.

<p align="center">
  <a href="https://github.com/Katsu2013/PDF-Correctorium/releases/download/v1.0.0-dev.199/PdfCorrectorium-v1.0.0-dev.199-win-x64.zip"><strong>⬇️ Download Latest Portable Release (v1.0.0-dev.199 .zip)</strong></a>
</p>

### Key Highlights
- **Operator-Level Irreversible Redaction**: Permanently eliminates underlying character codes and image pixels directly from PDF streams. Verifies the selected redaction area before finalizing the output. Copies elsewhere in the document and editable project/cache data need separate review.
- **Precision OCR Proofreading & NDLOCR Integration**: Fine-tune bounding boxes, per-character advances, and vertical reading orders. Import OCR data directly from National Diet Library NDLOCR-Lite (JSON/XML).
- **Page Reorganization Without Quality Loss**: Reorder, rotate, split, extract, and merge PDF pages without recompressing existing images.
- **Local document processing**: Documents are processed locally without cloud upload. Verify the exported PDF before sharing; editable projects retain original and pre-edit data. See [security scope and release conditions](docs/SECURITY-DESIGN.md).
- **Fully Portable & Open Source**: No installation required. Runs directly from a USB flash drive or folder under the Apache 2.0 license.

See the [English Section in our Wiki](wiki/Home.md) for full documentation and user guides.
