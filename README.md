# PDF Correctorium

[日本語](#japanese) | [English](#english)

<a id="japanese"></a>

## 概要（日本語）

PDF Correctorium（PDFコレクトリウム）は、**ページの並べ替え・回転・結合、復元できない確実な墨消し、しおり（目次）の作成から、OCR透明テキスト（検索用テキスト）の精密な文字修正までを1本で行える、Windows向け完全無料・インストール不要のPDF総合編集・校正ツール**です。

パソコンにインストールせずUSBメモリ等からもすぐに使え、元のPDFファイルを直接上書きしない**元データを壊さない安全設計**を採用しています。編集内容はプロジェクト（`.pdfocrproj`）にいつでも保存して続きから再開でき、作業結果を新しいPDFとして画質を落とさず安全に出力できます。

### こんな用途に便利です
- 📑 **書類整理・ページ向き修正**：サムネイルのマウス操作でページの並べ替え・90度回転・白紙削除。他のPDFからのページ結合も写真や図面の画質を落とさず素早く完了。
- 🛡️ **機密情報・個人情報の確実な墨消し**：黒四角を置くだけの一般的なPDFソフトと異なり、裏側の文字データそのものを完全に消去。背景色に合わせたスポイト機能や、保存後に文字が残っていないかの自動チェックを搭載。
- 🔖 **電子書籍・資料の目次（しおり）作り**：クリックで飛べる階層目次の作成、タイトルや作成者情報、PDFを開いたときの最初の表示（見開き・連続スクロール等）を最適化。
- ✏️ **スキャン文書の検索性向上（OCR文字の校正）**：文字の誤認識の修正、枠の位置や大きさ、文字の間隔、縦書き・読む順番を直して、検索しやすい高品質なPDFに。

### できること

#### 📑 ページの編集・結合・整理
- **直感的なサムネイル操作**：マウスのドラッグ＆ドロップでの並べ替え、90度単位の回転、不要ページの削除。写真やイラストを再圧縮しないため画質が劣化しません。
- **他のPDFからページを差し込み**：別のPDFから必要なページを取り込んで、今の文書へ結合・挿入できます。
- **安心の取り消し・やり直し**：ページの追加・削除・並べ替え・回転を含むすべての操作を何度でもやり直せます。

#### 🛡️ 二度と復元できない確実な墨消し（マスキング）
- **多彩な指定方法**：「PDFの文字を選択」「四角形」「自由な多角形」「フリーハンド（手描き）」から柔軟に範囲を指定。
- **文字だけ消して画質を保つ高度な出力**：文字を選んで墨消しした場合、文字データのみを消去し、写真や図面、範囲外の文字は鮮明なまま残します。
- **安全な画像化マスキング**：手描きなど複雑な形の場合は、ページを高画質のまま1枚の画像にして安全に保護します。
- **スポイト機能 & 出力後の自動チェック**：書類の背景色に合わせた目立たないマスキングが可能。保存後に文字が残っていないかも厳格に自動確認します。

#### 🔖 しおり（目次）・文書情報・コメント
- **しおり（目次）の作成・編集**：新規追加、修正、削除、ジャンプ先の指定、マウス操作による階層（親子関係）の整理。
- **文書プロパティの設定**：タイトル、作成者、文書の言語、PDFを開いたときの初期表示（見開きやスクロール方法）の指定。
- **コメント・タグ・リンク**：文書、ページ、文字枠へのコメントや重要度タグの付与、特定ページへジャンプする内部リンクの作成。

#### ✏️ OCRテキスト（透明文字）の精密校正・修正
- **文字・位置・間隔の修正**：誤認識された文字の打ち直し、枠の位置・大きさ・角度・文字ごとの間隔をマウスや数値で細かく調整。
- **日本語組版への対応**：縦書き・横書きの混在や、文章を読む順番を視覚的に整理。
- **校正・確認モード**：枠の誤ズレを防ぎながら、未確認の文字をキー操作やボタンでテンポよく連続チェック。
- **検索・一括置換 & 品質チェック**：書類全体から同じ間違いを探して一括修正したり、文字枠の異常を自動検知。
- **NDLOCR-Lite 連携**：国立国会図書館の「NDLOCR-Lite」出力（JSON/XML）から高精度な文字枠データを取り込み可能。

#### 💾 プロジェクト管理 & 快適な作業環境
- **2つの保存スタイル**：ファイルサイズを節約する「通常モード」と、元PDFも一緒にまとめて持ち運べる「ポータブルモード」。
- **自動保存 & バックアップ**：作業が止まったときの自動保存や、過去のバックアップからの復元。
- **開いたPDFの特性確認**：フォーム、署名、埋め込みファイルなど、編集・保存時に注意したいポイントを表示。
- **多言語・設定管理**：日本語／英語の即時切替、パネル配置の保存、ショートカットのカスタマイズ。

現在は開発版です。アプリ内で画像から新たにOCRを実行する機能など、未実装の項目もあります。詳しくは「現在の開発状況」と「安全性の修正と残る制限」をご確認ください。

## 現在の開発状況

現在のリポジトリは、開発版`v1.0.0-dev.171`に対応しています。以下を実装しています。

- C# / .NET 8 / WPFによるソリューション構成
- 縦書き・横書きと、文字方向とは独立した回転に対応するOCR領域モデル
- 変更しない元のOCR値と、編集可能な重ね合わせ表示用の値の分離
- 確認状態とPDF出力用の属性
- 状態による絞り込み、ページをまたぐ対象移動、確認済みにして次へ進む操作、位置・サイズの直接編集を防ぐ専用の「校正・確認」モード
- OCR文字列、位置・サイズ、文字ごとの送り幅、読み順、確認状態、検索・置換、複数領域編集の「元に戻す／やり直し」
- ZIP互換の`.pdfocrproj`形式による安全なプロジェクト保存・読み込み・検証（PDFを内包するポータブルモードと、元PDFをコピーしない通常モード。展開件数・容量・圧縮率・埋込PDF整合性の上限検査を含む）
- SHA-256による元PDFの同一性確認
- ポータブル版とインストール版それぞれのデータ保存先の解決
- 構造化された診断ログの基盤
- PDFとプロジェクトを開くWPFアプリケーション画面
- 最近開いたPDF・プロジェクトの一覧から再読み込み、表示件数の設定、履歴クリア
- 再利用可能な別プロセスのPDFiumワーカーによるページ描画・文字抽出・文書情報読込と、スクロール可能なプレビュー
- 編集用プレビューのページ配置（単一ページ／見開き）とスクロール方式（ページ切り替え／連続スクロール）を独立して選択可能。初回はPDFの初期表示指定に従い、意図的に変更した状態はプロジェクトへ保存。見開きのページ切替は移動先の2ページを先に準備して一括表示し、読み込み途中に単一ページへ見えるちらつきを防止。表紙の単独表示有無と左綴じ／右綴じを選べ、連続表示では表示範囲周辺だけを遅延描画してクリックしたページをその位置で編集面へ切り替え
- ページ数表示、非同期サムネイル、前後ページへの移動、ページの挿入・削除・並べ替え、90度単位の回転、および各ページ構成操作のUndo/Redo。削除・並べ替え・回転は元PDFを複製せず論理ページ情報だけを更新し、挿入と最終出力の境界でだけPDFを実体化
- プロジェクト外部のPDFと、プロジェクトに埋め込まれたPDFのプレビュー
- PDFiumによる通常PDFのネイティブ表示と、不可視描画モードまたは透明度ゼロの文字だけを対象にしたOCR編集用の半透明表示
- NDLOCR-Liteの関連ファイル（JSON、XML、TXT、TEI）の自動検出
- NDLOCR-LiteのJSON・XMLからの座標付きOCR領域の取り込みと、手動取り込み
- OCR領域の選択と、文字列・位置・サイズ・回転・文字方向・確認状態・分割／結合・ロック・読み順の編集
- マウスによる移動、8方向のサイズ変更ハンドル、回転操作、整列、文字ごとの送り幅の編集
- 25～400%の表示倍率、幅・高さ・ページ全体・選択範囲に合わせる表示、ツールバー操作、Ctrl＋マウスホイール
- OCR文字列の検索・置換、繰り返し領域への変更反映、文書全体のOCR品質分析
- PDFのしおり、文書情報（タイトル・作者・件名・キーワード・作成アプリ・PDF変換ツール）、文書言語、出力PDFバージョン、PDFを開いたときの表示設定の編集
- 入力PDFのフォーム、署名、JavaScript、添付、レイヤー、増分更新、非埋め込みフォント等の注意事項表示
- 文書・ページ・OCR領域に対するコメント、重要度、解決状態、タグの編集とプロジェクト保存
- 選択OCR領域から別ページへ移動するリンクの設定、戻る／進む、Ctrl+クリックによる移動、出力PDFへのGoToリンク保存
- PDF内の可視／不可視文字を行単位の連続したマーカー状の帯へまとめる文字選択、選択OCR領域、任意矩形、多角形、フリーハンドの墨消し指定、作成後の色変更・Deleteキーによる削除、ページ画像からのスポイト採色、編集表示の半透明／不透明切替、Undo/Redo、プロジェクト保存。通常のPDF文字選択では対象文字を除去して矩形へ置き換え、元の画像・図形・範囲外テキストを保持し、任意範囲や直接編集できない構造ではページ画像化へ安全側フォールバックするPDF出力
- OCR編集／読み順編集／校正・確認／墨消しを排他的に切り替える編集モード。墨消し中はOCR領域の移動・変形・削除を停止し、連続して範囲を指定可能。作成済みの墨消し範囲は直接移動し、8方向のハンドルでサイズ変更可能
- 元PDFとは別のPDFへの安全な出力、出力後の検証、検証後の保存確定
- プロジェクトの自動保存、容量上限付き世代バックアップ、バックアップからの復元、現在ページ周辺だけを保持するページサムネイルキャッシュ
- 「設定 → 表示 → 表示言語」で切り替えられる日本語・英語の画面表示
- ポータブル版・インストール版の両方で、表示言語の選択を次回起動時まで保持
- パネル、サムネイルサイズ、OCRの重ね合わせ表示、編集ハンドル、ショートカット、自動保存、バックアップ保持数を設定できるコンパクトな作業画面
- 外部のテストフレームワークに依存しない契約テスト実行機能

実装済みの範囲は、古い設計資料に記載された初期の基盤段階より広がっています。Version 1.0に向けた未実装項目や既知の不具合は、[実装状況](IMPLEMENTATION_STATUS.md)と設計資料内の実装状況欄で管理しています。上記の機能一覧は、読み込み・保存・再出力時の完全な情報保持や、Version 1.0の全要件の達成を保証するものではありません。

## 安全性の修正と残る制限（dev.171）

dev.171では、文字選択の墨消しを、編集画面で確認・保存したPDF座標の位置と大きさのまま出力します。出力時に字形・基準線・フォントの高さから別の矩形へ作り直さず、文字選択帯へ非表示の描画余白も加えません。このため、「基」「IDE」などを含む全範囲で、半透明／不透明の編集表示と出力PDFの位置・幅・高さが一致します。機密文字の除去は表示矩形とは独立して元の文字命令から行い、出力後も範囲内に抽出可能な文字が残っていないことを検査します。

dev.170では、文字選択のドラッグ方向について、拡張前のポインター範囲に文字中心が入った文字だけを選びます。Y方向へほとんど動かさない操作の行ヒット範囲は維持しつつ、隣文字の外接枠へ触れただけで左隣の文字を選ぶことはありません。PDF出力時の上下端は、選択文字と同じ元文字列・同じ基準線だけから求めます。別フォントや日本語文字の高さが混入しないため、`IDE`の再現範囲は約14.1ポイントから約6.1ポイントへ縮まりました。左右端は選択した字形のPDF実測境界を維持します。

dev.169では、文字を1字ずつ選択しても、同じPDF文字行に属する全文字を基準に上下端とプレビュー余白をそろえます。`g`のようなディセンダー、英大文字、日本語、短い字形を個別に選んでも同じ高さになり、左右端だけが実際に選択した文字へ合います。同じ行で重なる帯は色にかかわらず1本へ統合し、同色で隣接する帯も通常の字間をまたいで1本へ結合します。同じ文字の再選択では最新の色を優先し、矩形を重ねて残しません。編集画面だけでなくPDF出力直前にも整理するため、以前保存したプロジェクトにも適用されます。

dev.168では、プレビュー用の余白付きマーカー帯と、PDF文字の削除判定・出力矩形を分離しました。文字中心が帯に入った文字だけを削除し、出力矩形は対象文字のPDF実座標から締め直すため、字形境界の張り出し、画面座標の丸め、帯の余白で前後の文字まで消したり覆ったりしません。実際の3本の保存済み帯では、29文字だけを除去し、隣接する括弧、`CLI`、助詞などを可視・抽出可能なまま保持しました。

「PDF文字を選択」は、Y方向へほとんど動かさない横ドラッグでもポインター位置の文字行を選べます。縦書きでは同様に細い縦ドラッグを扱います。この補助範囲は文字を見つけるためだけに使い、墨消し帯には含めません。カーソルは十字ではなくIビームになります。

文字選択で作った墨消しはページ画像へフォールバックしません。元の文字命令に付けたマークを保存後に解析し、`Tj`／`TJ`命令内の選択文字の符号だけを除去して、不透明矩形をPDFオブジェクトとして追加します。16進文字列と括弧付き文字列の両方に対応し、範囲外文字のフォント・字幅・位置と、元の画像・図形を維持します。注釈があるだけでは画像化せず、墨消し範囲と重なる注釈だけを除去して範囲外の注釈を保持します。元命令の安全な部分編集ができないフォーム内文字や回転文字などでは、ページを画像化せずPDF出力自体を中止し、理由を表示します。

実際の205ページPDFでは、対象ページの15フォントと8画像・マスクを保持し、新しい全ページ画像を追加せず、7つの元文字命令から選択した29文字だけを除去して、範囲外19文字を元命令のまま維持できることを確認しています。出力後も保護範囲内の抽出文字が0字であることを再検査します。

文字選択中は、通常の大きな選択矩形ではなく、選んだ行に沿うマーカー帯をその場で表示します。帯は前後行や隣文字を隠さないよう、縦余白を文字高の3%（0.5～1.2表示ピクセル）、横余白を0.25～0.75表示ピクセル、PDF出力時の追加安全余白を0.1ポイントとしています。確定後は自動的に選択を解除し、文字に結び付いた帯は移動・サイズ変更できません。帯のクリックで削除対象として再選択でき、ページ余白のクリックまたはEscapeで選択を解除できます。矩形・多角形・フリーハンドの任意範囲は、機密情報を残さないため従来どおり対象ページを高精細画像へ変換します。

dev.163では、墨消しの入力方式を「矩形」「PDF文字を選択」「多角形」「フリーハンド」から選べます。PDF文字選択は元PDF内の可視文字と不可視文字を文字境界単位で取得し、囲んだ文字だけを墨消し候補にします。多角形は各頂点をクリックしてダブルクリックまたはEnterで確定し、フリーハンドはドラッグ軌跡を閉じた図形として確定します。非矩形範囲は外接矩形ではなく保存した輪郭で塗り、透明OCR文字の除去判定と出力後検証にも同じ輪郭を使います。フリーハンド点列は表示精度を保ちながら最大256点へ簡略化し、プロジェクト読込時は安全上4,096点を上限とします。

dev.161では、1本の透明OCR文字行の一部だけに墨消しが重なった場合、行全体ではなく、1ポイントの安全余白を含む墨消し範囲と交差した文字だけを検索・コピー対象から除去します。交差しない行は元のPDFテキストオブジェクトを保ち、部分交差した行は墨消しの左右に残る文字列断片を元フォントと文字境界へ不可視で再構成します。出力検証は各文字境界を再検査し、墨消し範囲内に抽出可能な文字が1字でも残れば保存先へ確定しません。

安全側フォールバックの画質については、墨消しページに画像最適化を先に適用してから再画像化する二重JPEG圧縮を廃止し、元の表示内容から直接300dpi・JPEG品質99で生成します。A4相当では2481×3508画素となります。極端に大きなページでは、安定性のため長辺14,000画素・約96メガピクセルを上限とします。このフォールバックを使ったページでは、既存注釈、構造タグ、ベクター情報は画像化されます。

dev.161では、画像最適化の行・列・内部形状判定を同じ画素走査へ統合し、PDFとは独立した画素配列の判定だけをCPU並列化しました。JPEG品質は従来どおり容量条件を満たす最高値を選びます。中間確定は最大192ページまたは768 MiBの早い方とし、メモリ上限による早期確定は維持しながら不要な全PDF再圧縮を減らします。実際の450ページ、14,275変更領域、443画像最適化、1墨消しを含むプロジェクトで、同一の118,694,840バイト出力を約164秒から約154秒へ短縮しました。処理時間はCPU、ストレージ、原稿画像により変わります。

dev.158では、ポータブルモードのプロジェクトから墨消しPDFを出力すると、別プロセスへ渡す一時プロジェクトの相対パスが欠けて出力前に停止する問題を修正しました。一時プロジェクトには、実際に出力へ使うPDFのファイルサイズとSHA-256を持つ専用参照を保存し、子プロセス側でも同一性を再確認します。大きな元PDFを一時プロジェクトへ重複内包しません。また、進捗と完了状態の同時書込みによる競合、保存先置換後に旧PDFの固定`.bak`が残る問題、後始末の失敗が本来のエラーを隠す問題、指定した墨消し件数の一部が未反映でも完了し得る問題を解消しました。

dev.157では、選択中の墨消し範囲を入力欄以外にフォーカスがある状態からDeleteキーで削除できるようにしました。スポイトを有効にして現在のPDFページをクリックすると、重ね合わせ表示ではなく元のページ画像から色を取得し、選択中の範囲または次に作る範囲へ適用します。スポイトは1回の取得で終了し、Escで中止できます。また、OCR領域も墨消し範囲も選択されていないときは、編集モードにかかわらず通常のホイール操作でワークエリアをスクロールします。Ctrl＋ホイールは従来どおり倍率変更です。

dev.156では、ページ上または一覧で作成済みの墨消し範囲を選び、色入力や色候補からその範囲の色を後から変更できるようにしました。選択中の範囲を明示的に削除でき、色変更・削除はいずれもUndo/Redoできます。編集画面では、範囲の下を確認できる半透明表示と、出力時の見た目に近い不透明表示を切り替えられます。この表示切替は編集画面だけに作用し、PDF出力は保存色で常に不透明です。また、移動用の透明な操作面が既定の白い面を描かないようにし、黒指定が白く見える問題を修正しました。

dev.155では、ページ切り替え方式の見開き表示で次／前ページへ移動するとき、移動先ページと相方ページを先に描画し、準備完了後に同じ画面更新内で差し替えるようにしました。読み込み中は現在の見開きを維持するため、一時的に単一ページ表示へ切り替わって見えることがありません。途中で文書・表示方式・ページ選択が変わった場合は古い準備処理を中止し、不要な結果を表示しません。

dev.154では、作成済みの墨消し範囲をページ上で選択し、範囲内のドラッグで移動、四辺・四隅の8方向ハンドルでサイズ変更できるようにしました。ドラッグ中は軽量な表示座標だけを更新し、完了時に1回だけPDFポイント座標へ反映するため、細かなマウス移動でUndo履歴やプロジェクト更新が増えません。移動・サイズ変更は1操作としてUndo/Redoでき、OCR編集へ戻すと墨消しレイヤーは入力を受け取らなくなります。

dev.153では、墨消しをOCR編集上の一時的な追加状態ではなく、独立した排他的な編集モードへ変更しました。ツールバーの「OCR編集」「墨消し」または編集モード一覧で切り替え、選択状態は常に同期します。墨消し中はページ上のドラッグを範囲指定として扱い、複数範囲を続けて指定できます。OCR領域の追加・削除・移動・変形・回転・文字幅操作は無効になり、右側も墨消し専用プロパティへ切り替わります。EscキーでOCR編集へ戻ります。

dev.152では、選択文字・OCR領域または任意の矩形を墨消し範囲として指定し、`#RRGGBB`色を選択してプロジェクトへ保存できる基礎機能を導入しました。指定はUndo/Redoでき、元PDFは変更しません。当初の確定出力は対象ページ全体の検索・コピーを失う保守的方式でしたが、dev.160で範囲外OCRを保持し、dev.161で部分交差する行も文字境界単位で保持・除去する方式へ更新しています。元PDFを保持するプロジェクト、自動保存、バックアップは機密情報を含み得るため、配布用の墨消し済みPDFとして扱わないでください。

既知の別制限として、再生成した設計書PDFを通常のOCR文字回転診断へ使う探索確認では、既存埋め込みフォントの回転往復判定を満たしませんでした。墨消し経路は同じ設計書PDFで成功していますが、任意PDFにある既存フォント文字の回転編集は保証せず、出力プレビューの確認が必要です。

dev.151では、PDFを最初に開いた際にカタログの`PageLayout`と`ViewerPreferences/Direction`を読み、単一／見開き、ページ切り替え／連続スクロール、表紙位置、左右綴じを編集画面の初期状態へ反映します。指定がないPDFはPDF仕様の既定どおり単一ページ・左から右で開きます。画面上で表示方法を意図的に変更すると、任意項目`EditorViewState`としてそのプロジェクトへ保存され、再度開いたときに文書の初期表示より優先して復元されます。初回状態の適用だけでは未保存扱いにしません。文書プロパティでは連続見開きも選択でき、出力PDFへ`TwoColumnLeft`／`TwoColumnRight`として保存します。

非対話のページ描画診断がUIスレッドを停止させる問題を解消しました。明示的なページ移動と、サムネイル・連続表示・文書走査は別々のPDFワーカーで処理するため、先読み処理が操作を待たせません。複数起動時は起動ログをプロセス別に保存し、設定は各画面が読み込み後に変更した項目だけを最新ファイルへ統合します。ページ編集用の一時セッションは、別プロセスが作成中のフォルダーを放棄済みと誤認して削除しないため、同時起動でも安全に初期化できます。標準の`dotnet test`も実際の27件の契約テストを必ず実行します。

dev.148では「単一ページ／見開き」と「ページ切り替え／連続スクロール」を独立した設定へ変更し、単一ページ＋連続スクロール、見開き＋連続スクロールを含む4通りを選べるようにしました。表紙や末尾に相手ページがない場合、空き側には白紙のページ面や枠線を描かず、背景だけを表示します。連続見開きも全ページ画像を保持せず、各見開きの位置情報と表示範囲周辺の最大12画像だけを保持します。旧設定の「連続ページ」は単一ページ＋連続スクロールへ自動移行します。

dev.147では、見開き表示の「表紙を単独表示」と「左綴じ／右綴じ」を独立して切り替えられるようにしました。左綴じは若いページを左、右綴じは若いページを右へ配置し、表紙を単独表示する場合は1ページ目を左綴じで右側、右綴じで左側へ置きます。

dev.146では、連続ページ表示で文書の先頭から末尾までを切れ目なくスクロールできます。全ページ分の軽量な配置枠だけを用意し、表示範囲の前後1画面に入る画像を遅延描画します。画像キャッシュは最大12ページに制限し、高速スクロールや文書・モード変更では古い描画を中止します。現在ページだけをOCR編集面とし、別ページをクリックするとスクロール位置を保ったまま、そのページが編集可能になります。

dev.143では、ページ削除・並べ替え・回転を、元PDFのページ番号と回転角を持つ論理ページ列として保存する方式へ変更しました。これらの操作やUndo/RedoではPDF全体のコピーを作らず、外部ページの挿入、画像最適化の解析、最終PDF出力など物理PDFが必要な境界でだけ一度実体化します。プレビュー、OCR座標、しおり、コメント、タグ、内部リンクは固定ページIDに追従します。

dev.143で導入したプロジェクト形式1.4以降は、`project.json`と重複していたページ別JSON、元PDF参照JSON、再生成可能なサムネイルを新規保存しません。通常モードの安定した外部PDFはコピーせず、管理対象の一時PDFから変換するときだけ`.assets`へ保存します。参照されなくなった内容ハッシュPDFは、現行・自動保存・バックアップの参照を確認してから整理します。内包PDFの展開キャッシュは最大16件・4 GiB、世代バックアップは設定件数・4 GiB、復旧直前コピーは1件、画面サムネイルは64件かつ現在ページの前後32ページに制限します。固定名`.bak`と世代バックアップの二重作成も廃止しました。

文字送り補正用QDFはファイル全体をメモリに読み込まず、マーク位置を一度の順次走査で索引化して、必要な範囲だけをストリーム置換します。大きなPDFの検索・品質走査用プレビューも低解像度に限定します。

dev.142では、ページ追加・削除・並べ替え・回転で生成する作業PDFに、現在表示中のPDFを含む最大12ファイル・合計1 GiBの上限を追加しました。上限を超えると、OCR編集とページ編集が混在するUndo/Redoの時系列を途中で切断しないよう、古い側の連続した履歴を破棄して未参照PDFを直ちに回収します。現在表示中のPDFは単体で上限を超える場合も保持し、追加PDFを必要としない直近のOCR履歴は可能な限り残します。

dev.141では、OCR文字を領域ごとにまとめて描画し、文字セルの計算結果を再利用することで描画負荷を軽減しました。領域サイズ変更ハンドルも選択中だけ生成します。文字編集、検索強調、ロック、読み順番号の表示は維持しています。検証方法と残る改善候補は[OCR描画の軽量化](OCR-RENDERING.md)を参照してください。

dev.140では、読み順番号をOCR領域や選択枠とは独立した最前面レイヤーへ表示するようにしました。左上のサイズ変更ハンドルや隣接する領域の枠が番号を隠さず、番号は不透明な背景と白い輪郭で判読できます。表示だけの変更で、読み順データやプロジェクト形式は変わりません。

dev.139では、プロジェクトのPDF保存方式の表示名を「ポータブルモード」と「通常モード」へ統一しました。ポータブルモードはPDFを`.pdfocrproj`内へ保存し、通常モードは元PDFを現在位置から相対参照します。文書プロパティ、ステータスバー、別名保存時の保存設定画面で同じ名称を使用します。内部データの`Embedded`／`Relative`は変更していないため、既存プロジェクトとの互換性は変わりません。

dev.138では、アプリ自体のデータ保存モードと、開いているプロジェクトのPDF保存方式を画面上で明確に分離しました。アプリデータ保存モード（ポータブル／インストール）は「編集 → 設定 → 管理」だけに表示します。文書のプロパティにはプロジェクトの「PDF保存方式」として「ポータブルモード」または「通常モード」を表示し、ステータスバーにも`PDF保存: ポータブルモード`または`PDF保存: 通常モード`と表示します。文書未読込時にはステータス表示を隠し、別名保存で方式を切り替えた直後にも表示を更新します。

dev.137では、通常の元PDFを通常モードで保存したときに、PDFを隣接`.assets`へ複製していた動作を修正しました。元PDFの現在位置をプロジェクト保存先からの相対パスで記録し、PDFのコピーも`.assets`の作成も行いません。`.assets`を作るのは、ポータブルモードから通常モードへ変換するとき、またはページ編集で生成した作業PDFを永続化するときだけです。別ドライブ間では相対パスを作れないため、コピーへ自動変更せず、ポータブルモードまたは同じドライブへの保存を案内します。

dev.136では、「プロジェクトを別名で保存」でポータブルモード（PDF内包）と通常モードを選択できる保存設定画面を追加しました。新しく開いたPDFの初期値はポータブル動作でポータブルモード、インストール動作で通常モードですが、保存時に切り替えられます。上書き保存はプロジェクトに記録された方式を維持します。

dev.135では、プロジェクト形式1.2の最小対応アプリ版を、この形式を導入した`1.0.0-dev.133`へ固定しました。保存に使用したアプリ版は引き続き別項目へ記録するため、dev.135で保存しても、内容が形式1.2の範囲ならdev.133以降で開けることを正しく表します。

dev.134では一時的に動作モードから保存方式を固定しましたが、dev.136で利用者が明示的に切り替えられる仕様へ改めました。dev.137では通常モードの意味を整理し、通常の元PDFは現在位置のまま参照するようにしています。

dev.133では両方式を実装しましたが、動作モードは保存画面の初期選択にしか反映されず、ポータブル動作でも利用者の選択や既存プロジェクトの方式によってリンク型のまま保存できました。これは「ポータブルならPDFを内包する」という仕様を満たしていなかったため、dev.134で修正しました。

同時に、入力PDF特性の注意事項、対象別コメント／タグ、OCR領域からのページ内リンクを追加しました。特性検査は注意喚起を目的とした限定的・ヒューリスティックな検査で、電子署名の真正性検証や悪意あるPDFの安全性保証ではありません。ページリンクは本アプリで設定したOCR領域を対象とし、既存PDFリンクの編集、ページ番号の自動認識、任意矩形の作成は未対応です。

dev.132では、文書情報辞書を持たないPDFへしおりと文書情報を同時追加した際に、両者の内部オブジェクト番号が衝突する不具合を修正しました。

dev.131では、qpdfを12.4.1、PDFiumを154.0.8035へ更新しました。すべてのqpdf経路とPDF出力ワーカーに期限・標準出力量上限・プロセスツリー終了を適用し、PDFiumワーカーを含む外部PDF処理はWindowsジョブでプロセス数とメモリ量を制限します。OCR付随ファイル、しおり交換ファイル、ワーカーのJSON・PNG・通信行、圧縮済みプロジェクトにも上限を設け、XMLのDTDと許可外一時出力先を拒否します。ネイティブ依存物は`DEPENDENCIES.lock.json`のSHA-256と照合し、配布記録にも含めます。dev.130で追加したZIP展開上限、危険なエントリ名・重複・内包PDF整合性検証、一時PDF回収、別プロセス化も引き続き有効です。

Windowsジョブは同一利用者権限で動く処理の停止・資源制限であり、AppContainerや権限縮小を行う完全なOSサンドボックスではありません。悪意ある入力に対する敵対的コーパス／ファジング、資源使用量の画面表示、修復・救出UIは引き続き未実装です。

dev.122の監査で再現した5件を修正しました。文書を切り替える際に保存・破棄・キャンセルを確認し、読み込み失敗時は現在の文書を保持します。意図的に空にした文字列をプロジェクトの保存・再読み込みとPDF出力で保持します。親領域・フィット・出力関連の属性を保持し、校正・確認モードでの文字幅補正を制限し、一括置換した領域を要再確認にします。

自動保存は設定した間隔、または約30秒間入力がない場合に実行します（5秒ごとに判定）。一度も保存していないプロジェクトは、元PDFを埋め込んだ復旧用ファイルを`workspaces/recovery/<project-id>.autosave.pdfocrproj`に保存します。復旧するには、このファイルを明示的に開いてください。起動時に復旧データを自動検出する機能は未実装です。復旧用ファイルへの保存だけでは、プロジェクトを保存済み扱いにはしません。

**プロジェクトの互換性：** 新しく保存する形式は1.6です。現行dev.171は形式1.0～1.6を読み込めます。形式1.6は墨消し輪郭と入力方式を追加するため、dev.162以前では開けません。旧版との互換性が必要な場合は、元のバックアップを保持してください。プロジェクト内の管理情報には、保存に使用したアプリのビルドバージョンも記録します。

外部サービス連携／アプリ内でのOCR実行、ルビ、差分、階層別の進捗、修復・救出画面、パネルのドッキングなど、Version 1.0の要件には未実装のものがあります。[残る実装項目](IMPLEMENTATION_STATUS.md#remaining-version-10-gaps)を参照してください。この改訂で残りの全機能が完成したわけではありません。

## 最近開いたファイル

「ファイル → 最近開いたファイル」から、PDFやプロジェクトを新しい順の一覧から開けます。「編集 → 設定 → 管理」で表示件数（既定10件、0～30件）と履歴クリアを設定できます。0件では表示・記録を停止し、履歴クリアは「保存」で確定します。PDFやプロジェクト自体は削除しません。履歴は設定の書き出しに含めません。詳しくは[操作ガイド](RECENT-FILES.md)を参照してください。

## プロジェクトの保存方式

「プロジェクトを別名で保存」で保存先を指定した後、保存設定画面から方式を選択します。「ポータブルモード」は現在の元PDFを`.pdfocrproj`へ内包するため、プロジェクトファイル1つを移動して利用できます。「通常モード」は通常、元PDFをコピーせず、その現在位置をプロジェクトからの相対パスで参照します。元PDFまたはプロジェクトを単独で移動すると参照できなくなるため、位置関係を維持してください。埋め込みPDFからの変換時や外部ページ挿入後など、管理対象の一時PDFを永続化するときだけ隣接`.assets`へ書き出します。回転・削除・並べ替えだけでは作成しません。未参照の内容ハッシュPDFは、復旧用プロジェクトを含む参照確認後に自動整理します。上書き保存は現在の方式を維持し、別名保存で方式を変更すると相互に変換できます。

## 設定の持ち運び・配置プリセット

「編集 → 設定 → 管理」から、設定とショートカットのJSON書き出し・取り込み、パネル幅・表示状態の名前付きプリセットを利用できます。最大20件のプリセットを登録・更新・削除できます。取り込みや配置の適用は「保存」で確定し、「キャンセル」で破棄します。PDFの編集内容や手動倍率は変更しません。詳しくは[操作ガイド](SETTINGS-WORKSPACES.md)を参照してください。

## 表示言語

画面表示は日本語（`ja-JP`）と英語（`en-US`）に対応しています。設定画面の「表示」タブで変更できます。メイン画面、メニュー、ツールチップ、プロパティの項目名、ページ名、確認状態や文字方向の選択肢、主要なダイアログへ即座に反映され、次回起動時も選択した言語を使用します。

言語の切り替え対象はアプリの画面表示だけです。元PDF、取り込んだOCRデータ、コメント、しおり、プロジェクト内容の文字列を翻訳・書き換えすることはありません。

## キーボード操作

アクセスキーは連番ではなく、Open=O、Save=S、Save As=Aなど操作名に基づいて割り当てます。ファイルメニューの操作例はAlt+F → O（PDFを開く）です。設定画面の保存はAlt+Sで実行できます。

入力項目・ボタン・タブの末尾に表示する英数字はAltアクセスキーです。Tab／Shift+Tabで部品を移動し、メイン画面ではF6／Shift+F6で主要な領域を移動できます。OK・キャンセルにはアクセスキーを追加しません。設定で変更可能な8種類の編集ショートカットは、現在のキーまたは割り当てなしをツールチップへ表示します。詳しくは[キーボード操作ガイド](KEYBOARD-ACCESSIBILITY.md)を参照してください。

## 校正・確認モード

ツールバーのモード選択で「校正・確認」を選びます。右側の一覧に、現在のページで条件に合う領域を読み順で表示します。初期の絞り込み条件は「未確認・要再確認」で、未確認のみ・要再確認のみ・全状態も選択できます。削除した領域は確認対象に含めません。それ以外のOCR領域も、周囲の文脈を確認できるようプレビューに表示します。

「前の対象」「次の対象」は、ページ順、その中で読み順に対象を移動し、必要な場合だけ別のページを読み込みます。文書の端から反対側の端へは循環しません。「確認済みにして次へ」は、選択中の単一領域を確認済みにして次の対象へ移動します。文字列を直すとその領域は修正済みになり、絞り込み条件から外れても編集欄は開いたままです。確認済み・修正済み・対象外・保留の領域を再確認する場合は全状態を選んでください。対象の検索はキャンセルでき、モード・絞り込み条件・ページ・文書を変更した場合も、処理中の移動をキャンセルします。

文字列、単語の読み方、確認状態は編集できます。一方、位置の直接移動、サイズ変更、回転、整列、文字幅調整、領域の作成・削除・分割・結合は無効になります。既存の位置・サイズのロック設定は書き換えません。品質分析からの補正にも同じ制限を適用します。ただし、文字列の修正には通常の文字枠調整規則を使用するため、文字の追加・削除に必要なレイアウト変更は発生します。レイアウトを直接調整する場合はOCR編集モードへ戻してください。確認状態と修正した文字列は、空文字や領域属性を含め、プロジェクト保存と「元に戻す／やり直し」に対応しています。絞り込み条件と選択中のモードは一時的な画面状態であり、文書情報としては保存しません。

確認対象の一覧から選択した場合や前後の対象へ移動した場合は、対象が見える位置へプレビューをスクロールします。プレビュー上のOCR領域を直接クリックした場合は、スクロール位置を変えません。現在のページの対象件数は、文書全体や階層別の確認進捗を表すものではありません。コメントとタグは対象単位で編集できますが、階層別集計、差分、監査履歴は未実装です。

## ビルド

### バージョン管理方針

アプリの版番号は`Directory.Build.props`だけで定義します。開発リビジョン171では、ソリューション全体の製品バージョンが`1.0.0-dev.171`、アセンブリ／ファイルバージョンが`1.0.0.171`になります。タイトルバー、バージョン情報、起動ログ、保存プロジェクト内の管理情報もこのビルドバージョンを使用します。必須の改訂・検証手順は[VERSIONING.md](VERSIONING.md)、今後の作業に適用するルールは[AGENTS.md](AGENTS.md)を参照してください。

変更したアプリのソースやビルドツールを配布する前に、`DevelopmentRevision`を増やします。ポータブル版の発行処理は、版番号の不一致、ローカルでのリビジョン巻き戻し、検証済みリビジョンを変更済みの入力で再利用する操作を拒否し、実際のEXE／DLLの版情報を検査して`build-info.json`を記録します。同一ソースの再検証ビルドではリビジョンを維持できますが、出力先は毎回新しい日時付きフォルダーにします。プロジェクトの保存形式は1.5、読み込みに必要な最小版はdev.152です。ローカルのビルド記録はGit履歴の代わりにはならないため、配布前にGitのコミットと送信結果も個別に確認します。

### 前提条件と実行手順

Version 1.0の互換性基準として実行対象を.NET 8に設定し、Visual Studio 2026 / .NET 10の開発ツールでビルドしています。

Windowsのファイル関連付けから`.pdf`や`.pdfocrproj`を開くことに対応しています。アプリはファイル引数を1つ受け取り、「ファイル」メニューと同じ読み込み処理で先頭ページを表示します。日本語名、空白を含む名前、相対パス、大文字の拡張子も扱えます。起動のたびに新しいウィンドウを開き、すでに起動中のアプリへ要求を転送することはありません。ファイルが存在しない場合や破損している場合は、読み込み成功と扱わずエラーを表示します。

ポータブル版には、`Icons/PdfDocument.ico`、`Icons/PdfCorrectoriumProject.ico`と、引用符付き起動コマンド・アイコンの割り当てを説明する[FILE-ASSOCIATIONS.md](FILE-ASSOCIATIONS.md)を同梱します。Windowsの関連付けや既定のアプリを自動変更することはありません。

`global.json`は.NET SDK `10.0.302`と`latestFeature`へのロールフォワードを指定しているため、インストール済みの.NET 10 SDK 10.0.400でもリポジトリ直下から通常のコマンドを実行できます。アプリの実行対象は.NET 8のままです。

```powershell
$env:DOTNET_CLI_HOME="$PWD\.dotnet-cli"
$env:DOTNET_CLI_TELEMETRY_OPTOUT="1"
dotnet build PdfCorrectorium.sln
dotnet run --project tests/PdfCorrectorium.ContractTests
```

既存のビルドを上書きせず、新しい日時付きフォルダーへポータブル版を発行するには、以下を実行します。配布前のReleaseビルドと検証の必須手順は[VERSIONING.md](VERSIONING.md)を参照してください。

```powershell
.\tools\BuildPortable.ps1
```

出力先は`outputs/PdfCorrectorium-Builds`配下で、フォルダー名は`PdfCorrectorium-<version>-win-x64-<yyyyMMdd-HHmmss>`です。ソリューションのビルドで依存パッケージを復元済みの場合、オフラインでは`-NoRestore`を指定できます。

dev.122で発生していた起動スモークテストの設定不一致はdev.123で修正済みです。現在の検証結果は[テスト戦略](outputs/PdfCorrectorium-Documentation/docs/11_Test/11-01_TestStrategy.md)に記録し、過去の監査結果は履歴として残しています。

ステータスバーの表示倍率スライダーは、中央の目印を100%としています。左半分は25～100%、右半分は100～400%をそれぞれ線形に割り当てます。軸をクリックするとその位置へ直接移動します。矢印キーは1パーセントポイント、PageUp／PageDownは10パーセントポイントずつ変更し、ドロップダウンとツールバーの表示も同期します。

ステータスバーの倍率ボタンは枠なしで、マウスを重ねたときや押したときは背景で反応を示し、キーボード操作時はフォーカスを表示します。ツールバーは余白を詰め、ボタンの内側余白を2 DIP、外側余白を1 DIPとしています（DIPは表示倍率に依存しない画面上の単位です）。保存したサイズ設定とアイコン寸法を維持しつつ、メインボタンの外寸を4 DIP縮小し、最小24 DIPとします。区切り線やツールバー自体の余白も狭くしています。

文書の有無に応じた画面状態の専用テストは、ウィンドウを表示せずに実行します。PDFを開く前のメニュー・コマンドの無効化、ダイアログやショートカットからの操作制限、PDF／プロジェクト読み込み後の再有効化、読み込み失敗、ページ数・倍率の制限、単色のスライダー軸と中央100%の左右別スケール、スライダー・ツールバー・ドロップダウン・手入力による倍率表示の同期を確認します。毎回新しい出力フォルダーを指定してください。テストは2ページの検証用PDF、画面画像、`checks.txt`を作成し、失敗時はゼロ以外の終了コードを返します。

```powershell
$uiTestOutput = Join-Path $PWD ("outputs/.verification/document-ui-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
Start-Process -FilePath ".\src\PdfCorrectorium.App\bin\Release\net8.0-windows7.0\PdfCorrectorium.exe" -ArgumentList @("--document-ui-test", ('"' + $uiTestOutput + '"')) -WindowStyle Hidden -Wait -PassThru
```

2026-08-30のこの専用テストでは136項目すべてに合格しました。倍率文字列のデータ連携の維持、25～400%の相互変換、中央位置と目印の一致、中央をまたぐキー操作、フラットボタンの状態、サイズ設定28・36・64におけるラベル表示あり／なしのコンパクトなツールバー寸法を含みます。これは従来の起動スモークテストとは別のテストであり、スモークテスト側の設定不一致はdev.123で修正しました。

ファイルからの起動に関する結合テストでは、新しいアプリのプロセスを13個起動し、先頭ページのプレビュー、プロジェクトデータ、埋め込み／外部の元PDF、エラー処理、入力ファイルが変更されないこと、同梱アイコンの解像度を確認します（67項目）。

```powershell
$launchTestOutput = Join-Path $PWD ("outputs/.verification/file-launch-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
Start-Process -FilePath ".\src\PdfCorrectorium.App\bin\Release\net8.0-windows7.0\PdfCorrectorium.exe" -ArgumentList @("--file-launch-tests", ('"' + $launchTestOutput + '"')) -WindowStyle Hidden -Wait -PassThru
```

校正・確認モードの専用テストは、2ページのPDFとプロジェクトを自ら作成し、非表示のWPF画面を描画します。2026-08-30には、絞り込み、読み順、ページをまたぐ移動、確認操作、文字修正、元に戻す／やり直し、保存・再読み込み、キャンセル、位置・サイズ関連のコマンドと処理の制限、言語別のドロップダウン表示と選択状態について、69項目に合格しました。

```powershell
$reviewTestOutput = Join-Path $PWD ("outputs/.verification/review-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
Start-Process -FilePath ".\src\PdfCorrectorium.App\bin\Release\net8.0-windows7.0\PdfCorrectorium.exe" -ArgumentList @("--review-mode-test", ('"' + $reviewTestOutput + '"')) -WindowStyle Hidden -Wait -PassThru
```

子プロセス用の`--startup-file-test <new-report-path>`は、ウィンドウと操作を止めるエラーダイアログの表示だけを抑え、読み込み後の文書状態を記録します。通常起動と同じファイル読み込み処理を呼び出します。これらのテストはWindowsのファイル関連付けを変更しません。

dev.123で修正する前の2026-08-30の監査では、文書UI 136項目、ファイル起動67項目、校正・確認69項目、契約13項目の合計285項目を再実行し、すべて合格しました。それでも別の人工データによる検査では、今回修正した5件の問題を再現し、従来の起動スモークテストも失敗していました。これらの件数は確認した経路を表すもので、全体の受け入れ完了を意味しません。検証範囲の不足と必要な回帰テストは[テスト戦略](outputs/PdfCorrectorium-Documentation/docs/11_Test/11-01_TestStrategy.md)を参照してください。

常設の`--persistence-test <new-output-directory>`では、文書切り替え・キャンセル・失敗時の保持、プロジェクト保存とPDF出力での空文字、読み込み済み／未表示領域の属性、校正モードの制限、一括処理後の状態、無操作時の自動保存、元PDFを埋め込んだ復旧データを追加検証します。他の診断モードと同様に、新しい出力フォルダーを指定して実行してください。

`--page-history-test <new-output-directory>`では、生成したPDFを使ってページ追加・削除・並べ替え・90度回転をUndo/Redoし、削除・並べ替え・回転で作業PDFを作らないこと、論理回転を最終出力へ一度だけ反映すること、挿入時の作業PDF回収、OCRページ・選択状態・操作前のOCR履歴、Redo後のプロジェクト保存・再読込、PDFiumワーカー分離・資源制限、外部処理の期限・出力量、OCR／しおり取込上限、および32個の同時セッション初期化を39項目で検証します。

```powershell
$pageHistoryOutput = Join-Path $PWD ("outputs/.verification/page-history-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
Start-Process -FilePath ".\src\PdfCorrectorium.App\bin\Release\net8.0-windows7.0\PdfCorrectorium.exe" -ArgumentList @("--page-history-test", ('"' + $pageHistoryOutput + '"')) -WindowStyle Hidden -Wait -PassThru
```

## ドキュメント

[設計資料の目次](outputs/PdfCorrectorium-Documentation/README.md)から、仕様の正本となるMarkdownと更新済みの12点の図版を参照できます。図版には校正・確認、文書プロパティ、プロジェクトPDF保存方式表示、保存方式選択・入力注意・文書注釈、見開きの表紙・左右綴じ配置、墨消し、OCR編集／墨消しモード切替、墨消し範囲の移動・サイズ変更・色変更・削除・スポイト採色を含みます。`PDF-Correctorium-Design-Documentation.pdf`もdev.171のMarkdownと図版から再生成しています。

## 開発体制・クレジット

本プロジェクトの設計、実装、テスト、およびドキュメント作成は、OpenAI の **ChatGPT Codex** を活用したAIペアプログラミング・共同開発によって行われています。

## ライセンス

Apache License 2.0です。第三者コンポーネントは`THIRD-PARTY-NOTICES.md`で管理しています。安定版の配布前に、ソフトウェア部品表（SBOM）を追加する予定です。

[Englishへ移動](#english) | [日本語の先頭へ戻る](#japanese)

---

<a id="english"></a>

## Overview (English)

PDF Correctorium is a **free, portable Windows application for comprehensive PDF editing, secure irreversible redaction, bookmark and metadata management, and precision invisible OCR text proofreading**.

It requires no installation, leaves no registry traces, and runs directly from any folder or USB drive. Built on a **safe non-destructive editing architecture**, it never overwrites your original PDF files. Work is preserved in dedicated projects (`.pdfocrproj`) and can be exported at any time to clean, sanitized, high-quality PDFs without unintended visual degradation.

### Ideal For
- 📑 **Organizing documents & fixing orientations**: Reorder pages via drag-and-drop thumbnails, rotate 90 degrees, remove blank pages, or merge pages from other PDFs with zero image quality loss.
- 🛡️ **Sanitizing confidential & personal data**: Unlike simple annotation boxes that merely cover up text, PDF Correctorium permanently eliminates character codes at the PDF operator level. Features eyedropper color sampling and post-export automated verification (guaranteeing 0 extractable characters).
- 🔖 **Creating bookmarks & configuring reading views**: Build multi-level table-of-contents bookmarks, adjust document properties, and configure initial viewing preferences (facing pages, right-to-left binding, continuous scrolling).
- ✏️ **Fixing scanned PDF searchability (OCR correction)**: Correct misrecognized characters, bounding box misalignments, individual character advances, vertical writing, and reading orders for high-accuracy searchable PDFs.

### What you can do

#### 📑 Page Editing, Merging & Reorganization
- **Intuitive thumbnail operations**: Drag-and-drop reordering, 90-degree rotation, and blank or unwanted page deletion. Operates on logical pages without re-encoding existing page images.
- **External page insertion & merging**: Insert pages from other PDF documents into the current file.
- **Full Undo/Redo**: All page additions, deletions, reordering, and rotations can be undone and redone effortlessly.

#### 🛡️ Secure Irreversible Redaction (Sanitization)
- **Flexible input modes**: "Select PDF Text", Rectangle, Polygon, and Freehand selection modes.
- **Vector-preserving output**: When selecting text, target character draw operators are eliminated while preserving surrounding images, vector artwork, and non-redacted text with native sharpness.
- **300 DPI high-definition raster fallback**: Arbitrary polygons and freehand regions safely rasterize the page image while maintaining unredacted OCR text.
- **Eyedropper color matching & automatic export verification**: Sample background colors directly from the page. Exports are automatically inspected to confirm zero extractable characters remain in the protected zones.

#### 🔖 Bookmarks, Document Properties & Annotations
- **Bookmark hierarchy editing**: Add, edit, remove, set destinations, and organize parent-child bookmark hierarchies via drag-and-drop.
- **Document property management**: Edit title, author, subject, keywords, document language, PDF version, and initial display mode (single page, facing spreads, reading direction).
- **Comments, tags & internal links**: Add comments (with severity and resolution status) and tags to documents, pages, or OCR regions. Create clickable in-document GoTo links.

#### ✏️ Precision OCR Text Proofreading & Correction
- **Text, position & advance editing**: Correct mistyped characters, and fine-tune box bounds, rotation, and per-character advances using 8-directional handles or numerical sliders.
- **Japanese typography support**: Supports mixed vertical/horizontal text blocks and visual reading-order sorting.
- **Dedicated Proofreading Mode**: Inspect unreviewed text sequentially according to reading order while preventing accidental box displacement.
- **Search, bulk replace & OCR quality analysis**: Document-wide search/replace and automated detection of suspicious character spans and anomalous bounding boxes.
- **NDLOCR-Lite integration**: Automatically detect and import high-precision character boundary data from National Diet Library NDLOCR-Lite (JSON/XML).

#### 💾 Project Management & Comfortable Workspace
- **Dual project storage modes**: "Normal mode" (relative reference for large files) and "Portable mode" (self-contained with embedded PDF).
- **Autosave & generation backups**: Configurable automatic saves on idle and multi-generation backup recovery.
- **Input characteristic warnings**: Detect and notify about forms, digital signatures, JavaScript, attachments, layers, and non-embedded fonts.
- **Multi-language & workspace customization**: Instant Japanese/English UI switching, workspace panel presets, and shortcut customization.

This is a development version. Some features, including running new OCR on images within the application, are not yet implemented. See "Current milestone" and "Safety fixes and remaining limitations" below for details.

## Current milestone

The current repository snapshot corresponds to the `v1.0.0-dev.171` development line. It includes:

- C# / .NET 8 / WPF solution structure
- Core OCR region model with vertical/horizontal writing and independent rotation
- Immutable original OCR values and editable overlay values
- Review states and output attributes
- Dedicated proofreading/review mode with status filters, cross-page target navigation, verify-and-next, and protection from direct geometry edits
- Undo/redo for OCR text, geometry, character advances, reading order, review state, search/replace, multi-region edits, and page insertion/deletion/reordering/rotation
- Safe ZIP-compatible `.pdfocrproj` save/open/validation with self-contained embedded-PDF and non-copying relative-reference modes, plus bounded entries, expansion, compression ratios, JSON, thumbnails, and embedded PDFs
- Source PDF SHA-256 fingerprinting
- Portable and installed data-path resolution
- Structured diagnostic log foundation
- WPF application shell for opening PDFs and projects
- Reusable out-of-process PDFium worker for page rendering, text extraction and document-property inspection, with a scrollable preview
- Independent page layout (single page / facing pages) and flow (page by page / continuous scrolling) controls. A PDF's viewer preferences provide the initial state, while an intentional override is saved in the project. Page-by-page facing navigation prepares both destination pages before replacing the visible spread, avoiding a temporary single-page frame. Facing pages support an optional separate cover and left- or right-bound placement; continuous flow lazily renders only the viewport neighborhood and promotes a clicked page without losing the scroll position
- Page count, asynchronous thumbnail navigation, previous/next controls, page insertion/deletion/reordering, and 90-degree page rotation; deletion, reordering, and rotation update logical page references without copying the source PDF, while insertion and final export materialize once at the operation boundary
- External and embedded project source-PDF preview support
- Semi-transparent OCR text overlays extracted from PDF text objects, including invisible render mode and zero-alpha text
- Automatic NDLOCR-Lite companion discovery for JSON, XML, TXT, and TEI files
- Coordinate-based overlay import from NDLOCR-Lite JSON and XML, with a manual import fallback
- Selectable OCR regions with text, position, size, rotation, writing-direction, review-state, split/merge, lock, and reading-order editing
- Mouse movement, eight-direction resize handles, rotation controls, alignment, and character-level advance editing
- 25-400% zoom, fit-width, fit-height, fit-page, fit-selection, toolbar controls, and Ctrl+mouse-wheel
- OCR search/replace, repeated-region propagation, and whole-document OCR quality analysis
- Editable PDF bookmarks, document metadata (title, author, subject, keywords, creator, and producer), document language, output PDF version, and viewer preferences
- Input-PDF notices for forms, signatures, JavaScript, attachments, layers, incremental updates, and non-embedded fonts
- Targeted comments with importance, resolved state, and tags on documents, pages, and OCR regions
- App-authored OCR-region links with back/forward navigation, Ctrl+click following, and exported PDF GoTo annotations
- Redaction of selected characters, OCR regions, or arbitrary rectangles with post-creation color changes, Delete-key removal, page-image eyedropper sampling, translucent/opaque editor previews, Undo/Redo, project persistence, high-resolution flattened visible content, and retained transparent OCR outside redacted ranges
- Mutually exclusive OCR editing, reading-order, review, and redaction modes; redaction mode blocks OCR geometry changes, stays active for consecutive marking, and lets existing marks be moved or resized with eight handles
- Safe isolated export to a separately saved PDF, followed by validation and output commit
- Project autosave, byte-bounded versioned backups, backup restoration, and a current-page-neighborhood thumbnail cache
- Japanese and English UI, switchable from `Settings > Display > Display language`
- Persistent UI-language selection for both portable and installed operation
- Compact preview workspace with configurable panels, thumbnail size, overlay appearance, edit handles, shortcuts, autosave, and backup retention
- Dependency-free contract test runner

The implemented application is broader than the original foundation milestone described in older design snapshots. Remaining Version 1.0 gaps and known defects are tracked in [Implementation status](IMPLEMENTATION_STATUS.md) and in the implementation-status sections of the design documentation. Features listed above are not a guarantee of complete round-trip preservation or of meeting every Version 1.0 requirement.

## Safety fixes and remaining limitations (dev.171)

Dev.171 paints every PDF-text redaction at the exact PDF-coordinate position and size reviewed and stored by the editor. Export no longer substitutes glyph-, baseline-, or font-derived geometry and adds no hidden paint padding to text-selection bands, so the editor's translucent/opaque preview and the exported PDF agree for ranges such as the blue `基`, `IDE`, and the orange title. Confidential character codes are still removed independently from the original text commands, and post-export validation still rejects extractable text inside the protected band.

Dev.170 selects characters whose centres fall inside the original pointer range along the drag direction. The expanded perpendicular hit area still supports an almost zero-height drag, but touching the adjacent glyph box no longer selects the glyph to the left. PDF output derives vertical bounds only from the selected characters' original text object and baseline, so a neighbouring Japanese run or taller font cannot enlarge a short selection such as `IDE`. On the reproduced page-10 case, the affected band decreased from about 14.1 points to 6.1 points while keeping the selected glyphs' measured PDF horizontal bounds.

Dev.169 normalizes the top, bottom, and preview padding of separately selected characters against their complete source-PDF text line. Capitals, short glyphs, Japanese glyphs, and descenders such as `g` therefore produce equal-height marks when selected one at a time; only the horizontal extent follows the selected characters. Overlapping same-line marks are consolidated regardless of colour, and same-colour marks separated only by normal glyph spacing are joined. Re-selecting a character keeps one range and gives the newest selection colour precedence. This cleanup runs in the editor and again immediately before export, so it also repairs previously saved projects.

Dev.163 adds four redaction input methods: rectangle, PDF-text selection, polygon, and freehand. PDF-text selection reads both visible and invisible source-PDF character bounds and creates marks only for characters enclosed by the marquee. Polygon input finishes on double-click or Enter; freehand input closes the dragged trace. Shaped redactions use their persisted outline—not merely their bounding box—for paint, searchable-text exclusion, and post-export validation. Freehand traces are simplified to at most 256 points for responsive editing, while package validation rejects paths over 4,096 points.

Dev.161 removes only the individual searchable characters whose bounds intersect a redaction plus its one-point safety margin. Unaffected invisible OCR lines retain their original PDF text objects; partially intersecting lines are rebuilt as invisible text fragments on the left and right of the protected range using their source font and character bounds. Post-export validation rejects the result if any extractable character remains inside a protected range.

Image optimization now derives row, column, and internal-shape occupancy in one pixel pass and parallelizes only this managed pixel analysis, leaving PDF object edits serialized. The checkpoint ceiling is 192 pages with the existing 768 MiB early memory threshold. On the 450-page, 14,275-region, 443-image, one-redaction reference project, the same 118,694,840-byte output improved from about 164 seconds to about 154 seconds; results vary by CPU, storage, and source images.

Redacted pages are now rendered directly from the pre-optimization page at 300 DPI and JPEG quality 99, avoiding the previous optimize-then-flatten double JPEG encoding. An A4-equivalent page is stored at 2481 by 3508 pixels; unusually large pages remain bounded to 14,000 pixels on the long edge and approximately 96 megapixels. Existing annotations, structure tags, and vector content on a redacted page are still flattened.

Dev.159 fixes large exports that could retain superseded PDFium page resources until the isolated worker reached its 2 GiB safety limit. The exporter now checkpoints, compacts, and reopens the working PDF after at most 96 pages or when private memory reaches 768 MiB, whichever comes first. A dedicated progress phase remains visible during each checkpoint, character-spacing progress advances for both successful and fallback lines, and repeated fallback warnings are summarized by count and representative samples instead of expanding tens of thousands of messages into the UI, state file, and logs. A production-equivalent run with 450 pages, 14,275 modified regions, 444 image-optimized pages, and one redaction completed under the 2 GiB job limit in approximately 158 seconds.

Dev.158 fixes isolated redaction export from Portable-mode projects. The temporary worker project now references the exact prepared PDF with a size and SHA-256 fingerprint instead of becoming an invalid relative project with no path; the worker verifies that identity before editing, without embedding another copy of a large source PDF. It also serializes progress and completion-state writes, removes operation-owned replacement backups after success, preserves the original export exception when cleanup fails, and rejects an output if any requested redaction was not applied.

Dev.157 lets Delete remove the selected redaction whenever focus is outside a text-entry control. The eyedropper samples the underlying current-page preview rather than editor overlays and applies the color to the selected redaction or the next new mark; it is one-shot and Escape cancels it. When neither OCR nor redaction content is selected, an ordinary mouse-wheel action over the workspace scrolls it in every editor mode, while Ctrl+wheel continues to change zoom.

Dev.156 lets you select an existing redaction on the page or in the list, then change its color or delete it; both operations participate in Undo/Redo. The editor can show redactions translucent for placement checks or opaque for an output-like preview. This preview choice does not affect the project or export: exported redactions are always opaque in their saved color. The move surface is now visually transparent, fixing the white interior that could cover a black redaction.

Dev.155 stages both pages of the destination spread before committing previous/next navigation in page-by-page facing mode. The current spread remains visible while preparation is in progress, and both slots are replaced in one dispatcher turn so the UI no longer flashes as a single-page preview. Stale preparation is canceled when the document, page selection, layout, flow, cover rule, or binding direction changes.

Dev.154 makes existing redaction marks adjustable on the page: drag inside a selected mark to move it, or use its four edge and four corner handles to resize it. Pointer movement updates only the lightweight overlay, then commits PDF-point geometry and one Undo entry when the drag completes. Leaving redaction mode makes this overlay non-interactive so it cannot obstruct OCR selection.

Dev.153 makes redaction a distinct, mutually exclusive editor mode instead of a temporary state layered over OCR editing. Users can switch from the toolbar or the editor-mode selector, whose states remain synchronized. While redaction is active, page dragging marks consecutive redaction areas; OCR-region creation, deletion, movement, resize, rotation, and character-width operations are disabled, and the right pane shows only redaction properties. Escape returns to OCR editing.

Dev.152 introduced persistent, undoable redaction marks for selected characters, OCR regions, and arbitrary rectangles with `#RRGGBB` colors. Its original conservative export removed search and copy from the whole affected page. Dev.160 retained invisible OCR outside protected ranges, and dev.161 additionally preserves the safe parts of partially intersecting lines at character-boundary granularity. Source-bearing projects, autosaves, and backups can still contain the confidential source and must not be distributed as redacted output.

As a separate known limitation, an exploratory normal OCR-rotation diagnostic against the regenerated design PDF did not satisfy the rotation round-trip assertion for its existing embedded font. Redaction succeeds on that same PDF, but rotation edits of arbitrary pre-existing PDF fonts are not guaranteed and require output-preview review.

Dev.151 reads Catalog `/PageLayout` and `/ViewerPreferences /Direction` when a PDF is first opened and uses them for the editor's layout, flow, cover placement, and binding. Missing hints use the PDF defaults of Single Page and L2R. An intentional editor-view change is stored in the optional project `EditorViewState` and restored ahead of the document hint; merely applying the initial or restored state does not mark the project dirty. Document Properties now includes Continuous Facing Pages and exports it as `/TwoColumnLeft` or `/TwoColumnRight`.

The non-interactive page-render diagnostic no longer blocks the UI dispatcher. Explicit page navigation uses a foreground PDF worker while thumbnails, continuous-view neighbors and document scans use a separate background worker. Startup logs are process-specific, concurrent application instances merge only preferences changed since their own load, and page-working sessions cannot mistake another process's newly created directory for an abandoned session. Standard `dotnet test` always executes the 28 dependency-free contract checks.

Dev.148 separated page layout (Single Page / Facing Pages) from flow (Page by Page / Continuous Scrolling), allowing all four combinations, including continuous facing spreads. When a cover or final page has no partner, the empty side shows only the canvas background—no artificial white page, border, or shadow. Continuous facing view retains spread geometry and at most 12 nearby page images rather than every rendered page. The legacy Continuous Pages preference migrates to Single Page plus Continuous Scrolling.

Dev.147 added independent Facing Pages preferences for a separate cover and left- or right-bound placement. Left binding places the earlier page on the left and a separate page-1 cover on the right; right binding mirrors both rules.

Dev.146 makes Continuous Pages a seamless, full-document scroll from the first page through the last. It creates only lightweight layout slots for all pages, lazily renders the viewport plus a one-screen buffer, retains at most 12 preview images, and cancels stale work during fast scrolling or a document/mode change. The current page remains the sole interactive OCR surface; clicking another page promotes it in place without losing the scroll position.

Dev.143 stores page deletion, reordering, and rotation as a logical sequence of immutable source-page numbers, stable page IDs, and rotation values. These edits and their Undo/Redo history no longer generate full working-PDF copies. A physical PDF is materialized once only where required, such as external-page insertion, image-optimization analysis, or final export. Preview, OCR geometry, bookmarks, comments, tags, and internal links follow the stable page IDs.

Project format 1.4, introduced in dev.143 and retained by later formats, stops writing redundant per-page JSON, source-reference JSON, and regenerable thumbnails alongside canonical `project.json`. A stable external PDF in Normal mode remains in place; only a transient managed PDF is persisted to `.assets`. Unreferenced content-hash assets are reclaimed after scanning current, autosave, and backup projects. Embedded-source materializations are limited to 16 files / 4 GiB, versioned backups to the configured count / 4 GiB, pre-recovery copies to one, and in-memory thumbnails to 64 within 32 pages of the current page. New saves also stop duplicating the same previous project into both fixed `.bak` and versioned backups.

Character-spacing QDF updates now index markers in one sequential pass and stream unchanged ranges instead of loading the entire QDF into managed memory. Search and quality-analysis previews use a bounded low resolution.

Dev.142 bounds page-edit working PDFs to 12 files and 1 GiB in total, including the current working PDF. When either limit would be exceeded, it removes one contiguous prefix from the oldest side of the shared OCR/page Undo timeline and immediately reclaims the now-unreferenced PDFs. The current PDF remains usable even when it alone exceeds the byte budget, and recent OCR-only entries remain available when they require no additional PDF.

dev.141 draws OCR characters in one layer per region, reuses current cell geometry, and creates region resize handles only while selected. Character editing, search highlights, locks, and foreground reading-order badges are preserved. See [OCR rendering notes](OCR-RENDERING.md) for verification and remaining work.

Dev.140 renders reading-order number badges in a dedicated foreground layer above OCR regions and selection chrome. The upper-left resize handle and neighboring region borders no longer obscure a number; each badge uses an opaque fill and contrasting white outline. This is a presentation-only change and does not alter reading-order data or the project format.

Dev.139 consistently labels project PDF storage as Portable mode or Normal mode. Portable mode embeds the PDF in `.pdfocrproj`; Normal mode leaves the source PDF in place and references it by a relative path. Document Properties, the status bar and Save Project As use the same labels. The persisted `Embedded` / `Relative` values are unchanged, so existing project compatibility is unaffected.

Dev.138 separates the application's data-location mode from the open project's PDF storage form in the UI. Portable/installed application-data storage is shown only under Edit > Settings > Manage. Document Properties identifies the project value as `PDF storage` and shows Embedded / self-contained or Relative reference; the status bar shows the same project value and is hidden while no document is open. The value updates immediately after Save As converts the project storage form.

Dev.137 stops duplicating an ordinary source PDF into an adjacent `.assets` directory when Relative Reference is selected. It records the source's current location relative to the project and creates neither a PDF copy nor `.assets`. An `.assets` directory is now used only when converting an embedded project to relative storage or when a page-edited working PDF must be made durable. A cross-drive relative path is impossible on Windows, so the save fails with guidance to use Embedded or save on the same drive instead of silently copying.

Dev.136 adds a project-save options window to Save Project As, allowing explicit selection between embedded and relative-reference storage. A newly opened PDF defaults to embedded storage in portable operation and relative storage in installed operation, but the choice can be changed when saving. Ordinary overwrite saves preserve the mode recorded in the project.

Dev.135 keeps the minimum reader for project format 1.2 fixed at `1.0.0-dev.133`, the build that introduced the format. The saving application version is recorded separately, so a project saved by dev.135 correctly remains compatible with dev.133 and later when it uses only format-1.2 data.

Dev.134 temporarily made the application mode authoritative; dev.136 replaces that behavior with explicit per-Save-As selection. Dev.137 clarifies relative storage so an ordinary source PDF remains in its current location.

Dev.133 introduced both storage forms, but the application mode only selected the dialog default. A portable run could therefore remain linked through a user choice or an existing project. That did not meet the portable self-containment requirement and is corrected in dev.134.

It also adds input-PDF characteristic notices, targeted comments/tags, and app-authored page links. The characteristic scan is bounded and heuristic: it is not signature-authenticity validation or a security guarantee. Page-link authoring currently targets selected OCR regions; existing PDF links, automatic page-number recognition, and arbitrary rectangle creation are not edited.

Dev.132 fixes an object-number collision when adding both bookmarks and a new document-information dictionary to a PDF that had no existing `/Info` dictionary.

Dev.131 updates qpdf to 12.4.1 and PDFium to 154.0.8035. Every qpdf path and the PDF-export worker now has a deadline, bounded captured output and whole-process-tree termination. Windows Job Objects cap process count and memory for external PDF processing, including the reusable PDFium worker. NDLOCR companions, bookmark exchange files, worker JSON/PNG/protocol lines and the compressed project archive are bounded; XML DTDs and worker output paths outside the designated temporary root are rejected. `DEPENDENCIES.lock.json` pins native-file SHA-256 values and publication records include the verified native files. Dev.130's ZIP expansion limits, unsafe/duplicate-name rejection, embedded-PDF integrity checks, temporary-file reclamation and process separation remain in force.

Job Objects provide termination and resource containment under the same user account; they are not AppContainer or reduced-privilege isolation. Hostile-input corpora/fuzzing, resource telemetry and repair/rescue UI remain future work.

The five issues reproduced in the dev.122 audit are now addressed: document switching prompts to save/discard/cancel and preserves the current document on failed loads; intentional empty text survives project save/reload and PDF export; parent/fit/output metadata is retained; review-mode width correction is guarded; bulk replacements receive needs-review status.

Autosave runs at the configured interval or after about 30 seconds without input (checked every 5 seconds). Never-saved projects receive a source-embedded recovery package under `workspaces/recovery/<project-id>.autosave.pdfocrproj`. Open that file explicitly to recover; automatic recovery discovery at startup is not implemented. Recovery writes do not mark the project saved.

**Project compatibility:** new saves use format 1.6. Current dev.171 reads formats 1.0 through 1.6. Format 1.6 adds persisted redaction outlines and input kinds, so dev.162 and earlier cannot open newly saved projects. Keep a backup when older-build compatibility is needed. The manifest records the saving build version.

External/in-app OCR, ruby, diffs, hierarchical progress, repair/rescue UI, docking and other full Version 1.0 requirements remain unfinished. See [implementation status](IMPLEMENTATION_STATUS.md#remaining-version-10-gaps). This increment is not completion of every remaining feature.

## Recent files

Use File > Recent Files to reopen PDFs and projects in most-recently-opened order. Edit > Settings > Manage provides the display count (default 10, range 0–30) and Clear History. Zero stops displaying and recording history; clearing takes effect only on Settings Save, and never deletes documents. File paths are excluded from settings export. See the [usage guide (Japanese)](RECENT-FILES.md).

## Project storage modes

After choosing a path in Save Project As, select Portable mode or Normal mode in the project-save options window. Portable mode stores the source PDF inside `.pdfocrproj`, allowing one-file movement. Normal mode normally leaves the source PDF in place and records its path relative to the project; moving either file alone may break the reference. Only embedded-to-Normal conversion and transient managed PDFs, such as a source created by external-page insertion, are written to adjacent `.assets`; rotation, deletion, and reordering alone do not create one. Unreferenced content-hash assets are removed only after current and recovery projects have been scanned. Overwrite preserves the current mode, while Save As can convert either way.

## Settings transfer and workspace presets

Use Edit > Settings > Manage to export/import settings and custom shortcuts as JSON, and register up to 20 named panel-layout presets. Import/apply updates the dialog draft; Save commits changes and Cancel discards them. Document edits and manual zoom are preserved. Storage information is now a collapsible section in Manage. See the [usage and format guide (Japanese)](SETTINGS-WORKSPACES.md).

## Display language

The application UI supports Japanese (`ja-JP`) and English (`en-US`). Change the language from the Display tab in the application settings window. The main window, menus, tooltips, property labels, page names, status choices, writing-direction choices, and principal dialogs update immediately; the selection is restored the next time the application starts.

Localization affects application chrome only. It never translates or rewrites text contained in the source PDF, imported OCR data, comments, bookmarks, or project content.

## Keyboard navigation

Mnemonics follow operation names, such as Open=O, Save=S and Save As=A, rather than alphabetical allocation. Menu mnemonics are scoped to their popup: use Alt+F, then O to open a PDF. Settings uses Alt+S for Save.

The letters or digits shown after input labels, actions, and tab captions are Alt access keys. Use Tab/Shift+Tab to move between controls and F6/Shift+F6 to move between main workspace panes. OK/Cancel do not receive new mnemonics. All eight customizable editing commands show their current shortcut or unassigned state in tooltips. See the [keyboard operation guide (Japanese)](KEYBOARD-ACCESSIBILITY.md) for details and limitations.

## Proofreading / review mode

Choose `校正・確認` in the toolbar mode selector. The right pane lists matching regions on the current page, in reading order. The default filter is `未確認・要再確認`; unreviewed-only, needs-review-only, and all-status filters are also available. Deleted regions are never review targets. Other OCR overlays remain visible for context.

`前の対象` / `次の対象` move through matching regions in page order, then reading order, loading additional pages only when necessary. They do not wrap at the document ends. `確認済みにして次へ` marks the single selected region verified and moves to the next target. A text correction marks the region modified; its editor remains open even if it no longer matches the filter. Choose all statuses to revisit verified, modified, excluded or deferred regions. Target search can be canceled, and changing the mode, filter, page or document cancels pending navigation.

Text, word readings, and review status remain editable. Ordinary direct movement, resize, rotation, alignment, character-width adjustment, region creation/deletion and split/merge commands are disabled in this mode; existing geometry-lock settings are not rewritten. The quality-analysis correction path is also guarded. Text corrections still use the normal character-cell reconciliation rules, including layout changes needed for inserting/removing text. Return to OCR editing for direct layout adjustments. Review states and corrected text support project save and Undo/Redo, including intentional empty text and preserved region metadata. Review filters and the selected mode are temporary UI state, not saved document metadata.

Selecting a review-list entry or using target navigation scrolls the preview to reveal the target. Ordinary selection by clicking an OCR region in the preview leaves the scroll position unchanged. The current-page target count is not a document-wide or hierarchical review-progress report. Targeted comments and tags are available, but aggregate reports, diffs, and persistent audit history remain unimplemented.

## Build

### Version policy

`Directory.Build.props` is the sole source of application version inputs. Development revision 171 produces product version `1.0.0-dev.171` and assembly/file version `1.0.0.171` across the solution. The title bar, About dialog, startup log and saved project manifest use the build version. [VERSIONING.md](VERSIONING.md) defines the mandatory revision-increment and verification rules; [AGENTS.md](AGENTS.md) applies them to future repository work.

Before delivering changed source/build tools, advance `DevelopmentRevision`. Portable publication rejects version mismatches, local revision rollback and changed inputs reusing a certified revision, checks the actual EXE/DLL metadata, and writes `build-info.json`. Same-source verification rebuilds may retain a revision but always use a new timestamped folder. The project data format is 1.6 and its minimum reader is dev.163. Local build records are not a substitute for Git history, so commit and push results are verified separately before distribution.

### Commands and prerequisites

The application targets .NET 8 for the Version 1.0 compatibility baseline and is built with the Visual Studio 2026 / .NET 10 toolchain.

Opening a `.pdf` or `.pdfocrproj` through a Windows file association is supported: the application consumes the single file argument and loads the first page, using the same loader as the File menu. Japanese names, spaces, relative paths, and uppercase extensions are supported. Each launch creates a new window; existing instances do not receive forwarded requests. Missing/corrupt files report an error without silently claiming a successful open.

The portable build includes `Icons/PdfDocument.ico`, `Icons/PdfCorrectoriumProject.ico`, and [FILE-ASSOCIATIONS.md](FILE-ASSOCIATIONS.md) with the quoted launch command and icon mapping. No Windows association or default application is changed automatically.

`global.json` specifies .NET SDK `10.0.302` with `latestFeature` roll-forward, allowing the normal repository-root commands to use installed .NET 10 SDK 10.0.400. The runtime target remains .NET 8.

```powershell
$env:DOTNET_CLI_HOME="$PWD\.dotnet-cli"
$env:DOTNET_CLI_TELEMETRY_OPTOUT="1"
dotnet build PdfCorrectorium.sln
dotnet run --project tests/PdfCorrectorium.ContractTests
```

Create a portable build in a new timestamped folder without overwriting an earlier build:

```powershell
.\tools\BuildPortable.ps1
```

Builds are stored under `outputs/PdfCorrectorium-Builds` using the name
`PdfCorrectorium-<version>-win-x64-<yyyyMMdd-HHmmss>`. After a solution build has already restored dependencies, use `-NoRestore` when working offline.

The earlier dev.122 smoke-test settings mismatch is fixed in dev.123. Current verification is recorded in the [test strategy](outputs/PdfCorrectorium-Documentation/docs/11_Test/11-01_TestStrategy.md); the old audit results remain historical evidence.

The status-bar zoom slider places 100% at its center marker: the left half maps linearly from 25% to 100%, and the right half from 100% to 400%. Clicking the track moves directly to that position. Arrow keys still change zoom by 1 percentage point, and PageUp/PageDown by 10; the dropdown and toolbar stay synchronized.

Status-bar zoom buttons are frameless, with background-only hover/press feedback and a keyboard-focus indicator. Toolbar spacing is compact: button padding is 2 DIP and margins are 1 DIP. The saved size preference and icon dimensions are preserved; the main button's outer size is reduced by 4 DIP (minimum 24 DIP), with tighter separators and toolbar padding.

The focused document-availability UI test runs without displaying a window. It checks disabled menus/commands before opening a PDF, dialog/shortcut guards, re-enabling after PDF/project loading, failed loads, page/zoom limits, the single-color status-bar slider track and centered two-scale mapping, and zoom-display synchronization after slider/toolbar operations, dropdown selections, and manual input. Supply a new output folder; the test writes its own two-page PDF fixture, screenshots, and `checks.txt`, and returns a nonzero exit code on failure:

```powershell
$uiTestOutput = Join-Path $PWD ("outputs/.verification/document-ui-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
Start-Process -FilePath ".\src\PdfCorrectorium.App\bin\Release\net8.0-windows7.0\PdfCorrectorium.exe" -ArgumentList @("--document-ui-test", ('"' + $uiTestOutput + '"')) -WindowStyle Hidden -Wait -PassThru
```

On 2026-08-30 this focused test passed all 136 checks, including preservation of the zoom-text binding, conversion round-trips across 25–400%, midpoint/marker alignment, keyboard steps across the midpoint, flat status-button states, and compact toolbar sizing with labels on/off at size preferences 28, 36, and 64. It is separate from the legacy smoke test, whose settings mismatch was corrected in dev.123.

File-launch integration tests start 13 fresh application processes and verify the first-page preview, project data, embedded/external sources, error handling, unchanged input files, and packaged icon resolutions (67 checks):

```powershell
$launchTestOutput = Join-Path $PWD ("outputs/.verification/file-launch-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
Start-Process -FilePath ".\src\PdfCorrectorium.App\bin\Release\net8.0-windows7.0\PdfCorrectorium.exe" -ArgumentList @("--file-launch-tests", ('"' + $launchTestOutput + '"')) -WindowStyle Hidden -Wait -PassThru
```

The dedicated review test creates its own two-page PDF/project fixture and renders the hidden WPF view. On 2026-08-30 it passed 69 checks covering filters, reading order, cross-page navigation, confirmation, text correction, Undo/Redo, save/reload, cancellation, geometry command/handler guards, and localized dropdown labels/selections:

```powershell
$reviewTestOutput = Join-Path $PWD ("outputs/.verification/review-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
Start-Process -FilePath ".\src\PdfCorrectorium.App\bin\Release\net8.0-windows7.0\PdfCorrectorium.exe" -ArgumentList @("--review-mode-test", ('"' + $reviewTestOutput + '"')) -WindowStyle Hidden -Wait -PassThru
```

The child-test option `--startup-file-test <new-report-path>` suppresses only windows/modal error dialogs and writes the resulting document state; it invokes the same startup file-opening path as normal launches. These tests do not modify Windows file associations.

Before the dev.123 fixes, the 2026-08-30 audit re-ran the 136 document-UI, 67 file-launch, 69 review-mode and 13 contract checks: all 285 passed. Separate synthetic-data probes nevertheless reproduced the five now-addressed audit problems, and the legacy smoke test still failed. These counts describe the tested paths, not complete acceptance. See the [test strategy](outputs/PdfCorrectorium-Documentation/docs/11_Test/11-01_TestStrategy.md) for coverage gaps and required regression cases.

The permanent `--persistence-test <new-output-directory>` additionally checks switching/cancel/failure preservation, empty OCR text through project and PDF output, loaded/unvisited region metadata, review restrictions, bulk status, idle autosave and embedded recovery. Run it like the other diagnostic modes, with a new output directory.

`--page-history-test <new-output-directory>` uses generated PDFs to exercise Undo/Redo for page insertion, deletion, reordering and 90-degree rotation. Its 39 checks verify that deletion, reordering, and rotation create no working PDF; that logical rotation is materialized exactly once at final export; that insertion working files are reclaimed; and that 32 working-file sessions can initialize concurrently. It also covers PDFium worker isolation/resource limits, external-process deadline/output caps, OCR/bookmark import caps, OCR history continuity, selection state, and project save/reload after redo.

```powershell
$pageHistoryOutput = Join-Path $PWD ("outputs/.verification/page-history-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
Start-Process -FilePath ".\src\PdfCorrectorium.App\bin\Release\net8.0-windows7.0\PdfCorrectorium.exe" -ArgumentList @("--page-history-test", ('"' + $pageHistoryOutput + '"')) -WindowStyle Hidden -Wait -PassThru
```

## Documentation

The [design documentation index](outputs/PdfCorrectorium-Documentation/README.md) links the normative Markdown and twelve updated diagrams, including review, document properties, project PDF storage/annotations, facing-page cover/binding layouts, redaction, the OCR/redaction mode switch, and redaction move/resize/color/delete/eyedropper controls. `PDF-Correctorium-Design-Documentation.pdf` has also been regenerated from the dev.171 Markdown and diagrams.

## Development & Credits

This project is designed, implemented, tested, and documented with the assistance of OpenAI's **ChatGPT Codex** through collaborative AI pair-programming.

## License

Apache License 2.0. Third-party components are tracked in `THIRD-PARTY-NOTICES.md`; an SBOM will be added before a stable distribution.
