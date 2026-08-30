using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace PdfCorrectorium.App.Services;

/// <summary>
/// アプリケーションの表示言語を管理し、WPF 画面内の固定文字列を翻訳します。
/// </summary>
/// <remarks>
/// 内部データや PDF 内の文字列には影響を与えず、画面に表示する文言だけを変換します。
/// 未登録の文言は原文を維持するため、機能追加時にも画面が空欄になりません。
/// </remarks>
public static class LocalizationService
{
    public const string JapaneseLanguage = "ja-JP";
    public const string EnglishLanguage = "en-US";

    private static readonly Dictionary<string, string> JapaneseToEnglish = new(StringComparer.Ordinal)
    {
        ["設定した間隔に加え、操作停止から約30秒後にも保存します。通常保存前の文書は元PDFを含む復旧用プロジェクトとして作業フォルダーのrecoveryへ保存します。"] = "Autosave also runs about 30 seconds after input stops. Documents not yet saved are stored with their source PDF in a recovery project under the workspace recovery folder.",
        ["ファイル(_F)"] = "_File",
        ["編集(_E)"] = "_Edit",
        ["表示(_V)"] = "_View",
        ["しおり(_B)"] = "_Bookmarks",
        ["OCR(_O)"] = "_OCR",
        ["ページ(_P)"] = "_Page",
        ["検証(_A)"] = "V_alidate",
        ["ヘルプ(_H)"] = "_Help",
        ["PDFを開く"] = "Open PDF",
        ["PDF出力"] = "Export PDF",
        ["上書き保存"] = "Save",
        ["OCR読込"] = "Load OCR",
        ["OCRデータを読み込む"] = "Load OCR Data",
        ["OCR表示"] = "OCR Display",
        ["OCRオーバーレイを表示"] = "Show OCR Overlay",
        ["プロジェクトを開く"] = "Open Project",
        ["プロジェクトを上書き保存"] = "Save Project",
        ["プロジェクトを別名で保存"] = "Save Project As",
        ["PDFをエクスポート"] = "Export PDF",
        ["終了"] = "Exit",
        ["元に戻す"] = "Undo",
        ["やり直す"] = "Redo",
        ["検索"] = "Find",
        ["透明テキストの検索と置換"] = "Find and Replace Invisible Text",
        ["選択領域の編集を他ページへ反映"] = "Propagate Selected Region Edits to Other Pages",
        ["定型領域の編集を他ページへ反映"] = "Propagate Repeated Region Edits",
        ["候補を検索"] = "Find Candidates",
        ["ヘッダー／フッターの編集を繰り返す"] = "Repeat Header / Footer Edits",
        ["現在ページで選択した領域の位置と文字列を基準に、他ページの似た領域を探します。候補を確認してから、分割・削除・文字送りをまとめて反映できます。"] = "Finds similar regions on other pages using the position and text selected on the current page. Review candidates before propagating splits, deletion, and character advances.",
        ["対象ページ"] = "Target Pages",
        ["ページ一覧で選択したページ"] = "Pages Selected in the Page List",
        ["ページを指定"] = "Specify Pages",
        ["全ページ（参照ページを除く）"] = "All Pages (Except Reference Page)",
        ["反映内容"] = "Changes to Propagate",
        ["分割・位置・寸法・文字送りを反映"] = "Propagate Splits, Position, Size, and Character Advances",
        ["一致した領域を削除"] = "Delete Matching Regions",
        ["各ページの文字列を維持し、分割・文字幅比率だけ反映する"] = "Preserve each page's text and propagate only splits and character-width ratios",
        ["参照ページで途中の文字を削除した状態まで同じように複製する場合は、チェックを外してください。"] = "Clear this option to reproduce the reference page exactly, including characters deleted from the middle.",
        ["候補の厳しさ"] = "Candidate Sensitivity",
        ["最低一致度"] = "Minimum Similarity",
        ["数字だけ異なるページ番号などは同じ文字として比較します。候補が多すぎる場合は値を上げてください。"] = "Numbers such as page numbers are normalized during comparison. Raise this value if too many candidates are found.",
        ["反映候補の確認"] = "Review Propagation Candidates",
        ["反映するページを確認してください"] = "Review the Pages to Change",
        ["チェックした候補だけを変更します。固定済みの領域は安全のため対象外です。"] = "Only checked candidates will be changed. Locked regions are excluded for safety.",
        ["一覧で候補を選ぶと、右側に実際のページ画像と反映対象の位置を表示します。チェックした候補だけを変更し、固定済みの領域は対象外にします。"] = "Select a candidate to show the actual page and target position on the right. Only checked candidates will be changed; locked regions are excluded.",
        ["すべて選択"] = "Select All",
        ["すべて解除"] = "Clear All",
        ["選択した候補へ反映"] = "Apply to Selected Candidates",
        ["反映"] = "Apply",
        ["一致度"] = "Similarity",
        ["現在の文字列"] = "Current Text",
        ["位置"] = "Position",
        ["状態"] = "Status",
        ["候補ページのプレビュー"] = "Candidate Page Preview",
        ["前の候補"] = "Previous Candidate",
        ["次の候補"] = "Next Candidate",
        ["候補を選択するとページ画像を表示します。"] = "Select a candidate to display its page image.",
        ["このページをメイン画面で確認して閉じる"] = "Show This Page in the Main Window and Close",
        ["反映元の選択を保つため、メイン画面で確認する場合はこの候補画面を閉じます。確認後、必要に応じて一括反映をもう一度開いてください。"] = "To preserve the source selection safely, opening a candidate in the main window closes this dialog. After reviewing it, reopen propagation if necessary.",
        ["固定済み（対象外）"] = "Locked (Excluded)",
        ["反映可能"] = "Ready",
        ["定型領域を検索"] = "Find Repeated Regions",
        ["他ページから同じ領域を検索しています"] = "Searching Other Pages for Matching Regions",
        ["検索を準備しています..."] = "Preparing search...",
        ["検索を中止"] = "Cancel Search",
        ["分析を中止"] = "Cancel Analysis",
        ["PDFを開いています..."] = "Opening PDF...",
        ["プロジェクトを検証して開いています..."] = "Validating and opening project...",
        ["プロジェクトを保存・検証しています..."] = "Saving and validating project...",
        ["現在ページの画像を解析しています..."] = "Analyzing the current page image...",
        ["PDF全体の画像を解析しています..."] = "Analyzing images throughout the PDF...",
        ["OCR付随データを読み込んでいます..."] = "Loading accompanying OCR data...",
        ["しおりを読み込んでいます..."] = "Importing bookmarks...",
        ["しおりを書き出しています..."] = "Exporting bookmarks...",
        ["ページを再構成しています..."] = "Rebuilding pages...",
        ["ページを回転して再構成しています..."] = "Rotating and rebuilding pages...",
        ["定型領域検索の進捗"] = "Repeated region search progress",
        ["候補 0 件"] = "0 candidates found",
        ["中止"] = "Cancel",
        ["現在のページ検索が終わり次第、中止します..."] = "Canceling after the current page search...",
        ["前後の文字列"] = "Context",
        ["一致位置"] = "Match Location",
        ["一致文字列"] = "Matched Text",
        ["行ブロック全体に一致"] = "Match Entire Line Block",
        ["正規表現を使用"] = "Use Regular Expressions",
        ["正規表現では、置換文字列に $1 などのグループ参照を使用できます。"] = "Regular-expression replacements may use group references such as $1.",
        ["検索で一致した文字"] = "Search Match",
        ["1件置換"] = "Replace One",
        ["不可視テキストのみ"] = "Invisible Text Only",
        ["検索と置換"] = "Find and Replace",
        ["設定"] = "Settings",
        ["文書のプロパティ"] = "Document Properties",
        ["透明テキストを表示"] = "Show Invisible Text",
        ["ページ一覧を表示"] = "Show Page List",
        ["OCRプロパティを表示"] = "Show OCR Properties",
        ["ステータスバーを表示"] = "Show Status Bar",
        ["アイコン一覧"] = "Icon Gallery",
        ["行編集"] = "Line Editing",
        ["段落編集"] = "Paragraph Editing",
        ["文字編集"] = "Character Editing",
        ["OCR編集"] = "OCR Editing",
        ["読み順編集"] = "Reading Order",
        ["校正・確認"] = "Proofreading / Review",
        ["文字と確認ステータスを編集できます。枠の移動・変形操作は無効です。"] = "Edit text and review status. Direct frame movement and transformation are disabled.",
        ["確認対象の絞り込み"] = "Filter Review Targets",
        ["このページに該当する領域はありません。前／次の対象で別のページも検索できます。"] = "No matching regions on this page. Previous / Next also searches other pages.",
        ["前の対象"] = "Previous Target",
        ["次の対象"] = "Next Target",
        ["確認済みにして次へ"] = "Verify and Next",
        ["選択中の1領域を確認済みにして、ページ順・読み順で次の対象へ移動します。"] = "Verify the selected region and move to the next target in page and reading order.",
        ["検索を中止"] = "Cancel Search",
        ["OCR領域を確認済みに変更"] = "Mark OCR Region as Verified",
        ["確認対象を読み込めませんでした。"] = "Could not load review targets.",
        ["ページ"] = "Pages",
        ["しおり"] = "Bookmarks",
        ["ページ一覧"] = "Page List",
        ["ページ全体"] = "Whole Page",
        ["ページ幅"] = "Page Width",
        ["ページ高さ"] = "Page Height",
        ["幅に合わせる"] = "Fit Width",
        ["100%で表示"] = "Actual Size (100%)",
        ["実際のサイズ（100%）で表示"] = "Show Actual Size (100%)",
        ["前へ"] = "Previous",
        ["次へ"] = "Next",
        ["ページを挿入..."] = "Insert Pages...",
        ["選択ページを削除"] = "Delete Selected Pages",
        ["選択ページの後へPDFページを追加"] = "Insert PDF Pages After Selection",
        ["左へ90度回転"] = "Rotate 90° Left",
        ["右へ90度回転"] = "Rotate 90° Right",
        ["選択ページを左へ90°回転"] = "Rotate Selected Pages 90° Left",
        ["選択ページを右へ90°回転"] = "Rotate Selected Pages 90° Right",
        ["サムネイル"] = "Thumbnails",
        ["追加"] = "Add",
        ["子を追加"] = "Add Child",
        ["子しおりを追加"] = "Add Child Bookmark",
        ["現在のページにしおりを追加"] = "Add Bookmark for Current Page",
        ["選択中のしおりの下に追加"] = "Add Below Selected Bookmark",
        ["選択中のしおりの子として追加"] = "Add as Child of Selected Bookmark",
        ["選択したしおりを削除"] = "Delete Selected Bookmark",
        ["選択したしおりのページへ移動"] = "Go to Selected Bookmark",
        ["このしおりへ移動"] = "Go to This Bookmark",
        ["しおりをインポート"] = "Import Bookmarks",
        ["しおりをエクスポート"] = "Export Bookmarks",
        ["同じ階層内で上へ"] = "Move Up Within Level",
        ["同じ階層内で下へ"] = "Move Down Within Level",
        ["削除"] = "Delete",
        ["移動"] = "Move",
        ["インポート"] = "Import",
        ["エクスポート"] = "Export",
        ["OCRプロパティ"] = "OCR Properties",
        ["説明"] = "Help",
        ["選択中のOCR領域"] = "Selected OCR Region",
        ["確認ステータス"] = "Review Status",
        ["文字方向"] = "Writing Direction",
        ["横書き"] = "Horizontal",
        ["縦書き"] = "Vertical",
        ["行全体"] = "Full Line",
        ["変更前"] = "Before",
        ["変更後"] = "After",
        ["選択中の文字"] = "Selected Character",
        ["文字の送り幅"] = "Character Advance",
        ["文字幅の調整"] = "Character Width Adjustment",
        ["単語ごとの読み方"] = "Word Readings",
        ["領域の位置・サイズを固定"] = "Lock Region Position and Size",
        ["X"] = "X",
        ["Y"] = "Y",
        ["幅"] = "Width",
        ["高さ"] = "Height",
        ["回転角度（度）"] = "Rotation (degrees)",
        ["未確認"] = "Unreviewed",
        ["確認済み"] = "Reviewed",
        ["修正済み"] = "Modified",
        ["要再確認"] = "Needs Review",
        ["OCR対象外"] = "Excluded from OCR",
        ["保留"] = "On Hold",
        ["表示"] = "Display",
        ["編集"] = "Editing",
        ["ショートカット"] = "Shortcuts",
        ["保存場所"] = "Storage",
        ["言語"] = "Language",
        ["表示言語"] = "Display language",
        ["画面レイアウト"] = "Layout",
        ["ツールバー"] = "Toolbar",
        ["ツールバー表示"] = "Toolbar display",
        ["ツールバーサイズ"] = "Toolbar size",
        ["アイコンのみ"] = "Icons only",
        ["アイコンと説明"] = "Icons and text",
        ["アイコン＋説明"] = "Icons and text",
        ["ボタンサイズ"] = "Button size",
        ["プロパティの説明文を表示"] = "Show property help text",
        ["プロパティ領域に詳しい説明を表示する"] = "Show detailed help in the properties panel",
        ["ページ一覧パネルを表示"] = "Show page list panel",
        ["ページ一覧を表示する"] = "Show page list",
        ["プロパティパネルを表示"] = "Show properties panel",
        ["OCRプロパティを表示する"] = "Show OCR properties",
        ["ステータスバーを表示"] = "Show status bar",
        ["ページ一覧の幅"] = "Page list width",
        ["プロパティの幅"] = "Properties width",
        ["プロパティ領域の幅"] = "Properties panel width",
        ["ページのサムネイルを表示"] = "Show page thumbnails",
        ["ページ一覧にサムネイルを表示する"] = "Show thumbnails in the page list",
        ["OCRオーバーレイ"] = "OCR overlay",
        ["色"] = "Color",
        ["不透明度"] = "Opacity",
        ["未選択の文字枠を表示"] = "Show unselected character borders",
        ["文字編集時、未選択のOCR行にも文字枠を表示する"] = "Show character boxes on unselected OCR lines while editing characters",
        ["文字編集枠の太さ"] = "Character box thickness",
        ["表示倍率にかかわらず、画面上では同じ太さになるように表示します。"] = "Keeps character box borders visually consistent at every zoom level.",
        ["文字送り調整ハンドル"] = "Character advance handle",
        ["文字幅の調整ハンドル"] = "Character width handle",
        ["太さ"] = "Thickness",
        ["領域のサイズ変更ハンドル"] = "Region resize handles",
        ["塗り色"] = "Fill color",
        ["枠線色"] = "Border color",
        ["大きさ"] = "Size",
        ["文字境界の自動推定"] = "Automatic character boundary estimation",
        ["文字幅の自動推定"] = "Automatic character width estimation",
        ["最小幅／行高さ"] = "Minimum width / line height",
        ["最大幅／行高さ"] = "Maximum width / line height",
        ["均等幅への寄せ方"] = "Equal-width bias",
        ["文字画素の必須率"] = "Required ink coverage",
        ["文字情報への寄せ方"] = "Glyph information bias",
        ["Undo / Redo"] = "Undo / Redo",
        ["Undo／Redo"] = "Undo / Redo",
        ["保存"] = "Save",
        ["キャンセル"] = "Cancel",
        ["閉じる"] = "Close",
        ["OK"] = "OK",
        ["適用"] = "Apply",
        ["既定値に戻す"] = "Restore Defaults",
        ["一般"] = "General",
        ["文書情報"] = "Document Information",
        ["PDFバージョン"] = "PDF Version",
        ["元PDFのバージョン"] = "Source PDF version",
        ["出力PDFのバージョン"] = "Output PDF version",
        ["自動（推奨）"] = "Automatic (recommended)",
        ["PDF 1.4（Acrobat 5.x）"] = "PDF 1.4 (Acrobat 5.x)",
        ["PDF 1.5（Acrobat 6.x）"] = "PDF 1.5 (Acrobat 6.x)",
        ["PDF 1.6（Acrobat 7.x）"] = "PDF 1.6 (Acrobat 7.x)",
        ["PDF 1.7（Acrobat 8.x以降）"] = "PDF 1.7 (Acrobat 8.x or later)",
        ["元PDFより低いバージョンは選択できません。PDF 1.4では、1.5以降専用のオブジェクトストリームを使用せずに出力します。"] = "A version lower than the source PDF cannot be selected. PDF 1.4 output disables object streams, which require PDF 1.5 or later.",
        ["元PDFより低いPDFバージョンは指定できません。元PDFと同じか、より新しいバージョンを選択してください。"] = "The output version cannot be lower than the source PDF. Select the same or a newer version.",
        ["読み上げオプション"] = "Reading Options",
        ["文書の言語"] = "Document language",
        ["PDFカタログの言語タグ（/Lang）へ反映します。候補から選ぶか、BCP 47形式（例: ja-JP、en-US）で入力してください。空欄にすると既存の指定を削除します。"] = "This value is written to the PDF catalog language tag (/Lang). Select a suggestion or enter a BCP 47 tag such as ja-JP or en-US. Leave it blank to remove the existing setting.",
        ["タイトル"] = "Title",
        ["作成者"] = "Author",
        ["件名"] = "Subject",
        ["キーワード"] = "Keywords",
        ["初期表示"] = "Initial View",
        ["単一ページ"] = "Single Page",
        ["連続ページ"] = "Continuous",
        ["見開き"] = "Facing Pages",
        ["左開き"] = "Opens left",
        ["右開き"] = "Opens right",
        ["左綴じ"] = "Left binding",
        ["右綴じ"] = "Right binding",
        ["左綴じ（右開き・左から右）"] = "Left binding (opens right, left-to-right)",
        ["右綴じ（左開き・右から左）"] = "Right binding (opens left, right-to-left)",
        ["右開きは左綴じ（/L2R）で、表紙を単独表示する場合は1ページ目を右側に配置します。左開きは右綴じ（/R2L）で、1ページ目を左側に配置します。PDFの言語設定は綴じ方向を変更しません。この値はPDFビューアへの初期表示の指定であり、ビューア側の設定によって無視される場合があります。"] = "A document that opens to the right is left-bound (/L2R); with a separate cover, page 1 is placed on the right. A document that opens to the left is right-bound (/R2L); page 1 is placed on the left. The PDF language setting does not change binding direction. These are initial-view hints and may be overridden by the viewer.",
        ["表紙を単独表示"] = "Show cover separately",
        ["検索文字列"] = "Find what",
        ["置換文字列"] = "Replace with",
        ["次を検索"] = "Find Next",
        ["検索する文字列"] = "Find what",
        ["置換後の文字列"] = "Replace with",
        ["置換"] = "Replace",
        ["すべて置換"] = "Replace All",
        ["大文字と小文字を区別"] = "Match case",
        ["現在のページ"] = "Current page",
        ["文書全体"] = "Entire document",
        ["ページ画像の最適化"] = "Optimize Page Images",
        ["PDF全体の画像最適化"] = "Optimize Images in Entire PDF",
        ["PDF全体の画像最適化候補"] = "PDF Image Optimization Candidates",
        ["ページ画像の最適化プレビュー"] = "Page Image Optimization Preview",
        ["最適化内容"] = "Optimization Summary",
        ["検出した背景"] = "Detected Background",
        ["プレビューの見方"] = "How to Read the Preview",
        ["検出箇所"] = "Detected Areas",
        ["この内容で実行"] = "Run with These Settings",
        ["前のページ"] = "Previous Page",
        ["次のページ"] = "Next Page",
        ["前の文字へ移動"] = "Previous Character",
        ["次の文字へ移動"] = "Next Character",
        ["縮小"] = "Zoom Out",
        ["拡大"] = "Zoom In",
        ["ページ幅に合わせる"] = "Fit Width",
        ["ページ全体に合わせる"] = "Fit Page",
        ["ページ全体を表示"] = "Fit Page",
        ["全体"] = "Fit",
        ["高さに合わせる"] = "Fit Height",
        ["ページ高さに合わせる"] = "Fit Height",
        ["100%"] = "100%",
        ["プロジェクトを検証"] = "Validate Project",
        ["PDF Correctorium について"] = "About PDF Correctorium",
        ["設定を保存しました。"] = "Settings were saved.",
        ["アプリケーション設定"] = "Application Settings",
        ["ポータブルモード"] = "Portable mode",
        ["インストールモード"] = "Installed mode",
        ["PDFは開かれていません"] = "No PDF is open",
        ["PDFを開くとページを表示します。最初の保存時に安全な .pdfocrproj 作業ファイルを作成します。"] = "Open a PDF to display its pages. A safe .pdfocrproj working file is created when you first save.",
        ["未保存"] = "Not saved",
        ["準備完了"] = "Ready",
        ["ページなし"] = "No pages",
        ["OCRデータ未読込"] = "OCR data not loaded",
        ["PDFプレビューを読み込みました。左側のページ一覧からページを切り替えられます。"] = "The PDF preview was loaded. Use the page list on the left to change pages.",
        ["OCR付随ファイルを検索しています..."] = "Searching for OCR companion files...",
        ["PDFテキストレイヤー"] = "PDF text layer",
        ["PDFプレビューを描画しています..."] = "Rendering PDF preview...",
        ["読込中..."] = "Loading...",
        ["行の文字列"] = "Line text",
        ["段落の文字列（1行につき1つのOCR領域）"] = "Paragraph text (one OCR region per line)",
        ["現在の動作モード"] = "Current mode",
        ["OCRの編集単位"] = "OCR edit unit",
        ["複数領域の整列"] = "Align Multiple Regions",
        ["同じ幅"] = "Same Width",
        ["同じ高さ"] = "Same Height",
        ["左端を揃える"] = "Align Left",
        ["右端を揃える"] = "Align Right",
        ["上端を揃える"] = "Align Top",
        ["下端を揃える"] = "Align Bottom",
        ["左右中央を揃える"] = "Align Horizontal Centers",
        ["上下中央を揃える"] = "Align Vertical Centers",
        ["読み順再計算"] = "Recalculate Reading Order",
        ["読み順を前へ"] = "Move Earlier",
        ["読み順を後へ"] = "Move Later",
        ["透明テキスト領域を追加"] = "Add Invisible Text Region",
        ["選択した透明テキスト領域を削除"] = "Delete Selected Invisible Text Regions",
        ["前処理付き一括自動調整"] = "Batch Auto-adjust with Preprocessing",
        ["OCR品質の異常候補"] = "OCR Quality Anomaly Candidates",
        ["文字数の外れ値"] = "Character Count Outliers",
        ["キーワード幅の補正"] = "Keyword Width Correction",
        ["寸法の許容差（%）"] = "Size tolerance (%)",
        ["最低比較件数"] = "Minimum peers",
        ["文字数比率"] = "Character-count ratio",
        ["文書全体を分析"] = "Analyze Entire Document",
        ["幅・高さ・書字方向が近い領域同士を比較し、文字数だけが極端に少ない／多い箇所を候補として表示します。"] = "Compare regions with similar width, height, and writing direction, then list unusually short or long OCR text as candidates.",
        ["判定"] = "Finding",
        ["文字数"] = "Characters",
        ["標準文字数"] = "Expected",
        ["比較件数"] = "Peers",
        ["方向"] = "Direction",
        ["OCR文字列"] = "OCR text",
        ["選択箇所へ移動"] = "Go to Selection",
        ["まだ分析していません。"] = "Not analyzed yet.",
        ["キーワード"] = "Keyword",
        ["大文字と小文字を区別"] = "Match case",
        ["許容差（%）"] = "Tolerance (%)",
        ["最低出現件数"] = "Minimum occurrences",
        ["同じ語の出現幅を行の高さ（縦書きは行の幅）で正規化し、中央値から外れた箇所を補正候補にします。固定済みの行・文字は変更しません。"] = "Normalize each occurrence by line height (line width for vertical text), use the median as the reference, and list deviations. Locked lines and characters are unchanged.",
        ["現在幅"] = "Current span",
        ["基準幅"] = "Reference span",
        ["差（%）"] = "Difference (%)",
        ["固定"] = "Lock",
        ["選択候補を補正"] = "Correct Selected",
        ["全候補を補正"] = "Correct All",
        ["キーワードを入力してください。"] = "Enter a keyword.",
        ["少なすぎる"] = "Too few",
        ["多すぎる"] = "Too many",
        ["固定済み"] = "Locked",
        ["なし"] = "None",
        ["分析条件を数値で入力してください。"] = "Enter numeric analysis conditions.",
        ["文字数の外れ値候補は見つかりませんでした。"] = "No character-count outliers were found.",
        ["{0}件の候補が見つかりました。"] = "Found {0} candidates.",
        ["OCR文字数を分析できませんでした。"] = "Could not analyze OCR character counts.",
        ["出現件数が{0}件のため、基準を決定できません。"] = "Only {0} occurrences were found, so a reference could not be established.",
        ["全{0}件から基準比率{1:0.00}を求め、{2}件を候補にしました。"] = "Calculated reference ratio {1:0.00} from {0} occurrences and found {2} candidates.",
        ["キーワードの文字幅を分析できませんでした。"] = "Could not analyze keyword widths.",
        ["補正できる候補が選択されていません。"] = "No correctable candidates are selected.",
        ["{0}件を同じキーワードの基準幅へ補正します。よろしいですか？"] = "Correct {0} occurrences to the keyword reference width?",
        ["{0}件を補正しました。"] = "Corrected {0} occurrences.",
        ["文字の一括自動調整"] = "Batch Character Auto-adjust",
        ["前処理（必要な項目だけ選択してください）"] = "Preprocessing (select only the required options)",
        ["上下2行以内で行の高さの誤差が10%未満なら、平均値へ統一する"] = "If line heights within two neighboring lines differ by less than 10%, normalize them to the average",
        ["行頭が開き鍵括弧（「『）なら、行の高さの50%分を行頭側へ広げる"] = "If a line starts with an opening Japanese quote (「 or 『), extend its leading edge by 50% of the line height",
        ["行末が句読点または閉じ鍵括弧（、。,.!?」』など）なら、行の高さの50%分を行末側へ広げる"] = "If a line ends with punctuation or a closing Japanese quote, extend its trailing edge by 50% of the line height",
        ["行頭・行末が細長い文字（・、｜、I、L、：、；など）なら、行の高さの50%分を該当する行端側へ広げる"] = "If a line starts or ends with a narrow character (middle dot, pipe, I, L, colon, semicolon, etc.), extend that edge by 50% of the line height",
        ["すべての行の行頭・行末を、行の高さの5%分だけ外側へ広げて認識余白を確保する"] = "Extend both ends of every line outward by 5% of the line height to provide recognition margin",
        ["処理順: 行の高さを統一 → 行頭・行末へ認識余白を追加 → 特定文字の行端を拡張 → ページ画像から全体の文字送りを自動調整"] = "Order: normalize line heights, add recognition margins, extend edges for special characters, then auto-adjust all character advances from the page image",
        ["固定済みの領域と文字は変更しません。すべての変更は1回の［元に戻す］で取り消せます。"] = "Locked regions and characters are not changed. All changes can be reverted with a single Undo.",
        ["一括実行"] = "Run Batch",
        ["全ページを実行"] = "Run All Pages",
        ["対象ページを実行"] = "Run Target Pages",
        ["対象ページ"] = "Target Pages",
        ["現在のページ"] = "Current Page",
        ["ページ一覧で選択したページ"] = "Pages Selected in the Page List",
        ["ページを指定"] = "Specify Pages",
        ["全ページ"] = "All Pages",
        ["例: 1,3,5-10"] = "Example: 1,3,5-10",
        ["文書全体の文字を自動調整しています"] = "Auto-adjusting characters throughout the document",
        ["対象ページの文字を自動調整しています"] = "Auto-adjusting characters on target pages",
        ["処理を準備しています..."] = "Preparing...",
        ["一括自動調整の進捗"] = "Batch auto-adjustment progress",
        ["調整済み 0 行"] = "Adjusted 0 lines",
        ["中止"] = "Cancel",
        ["全ページを画像解析するため、ページ数によっては時間がかかります。処理中は編集操作を一時停止します。"] = "All pages are analyzed from their images. This may take time for large documents, and editing is temporarily disabled while processing.",
        ["対象ページを画像解析するため、ページ数によっては時間がかかります。処理中は編集操作を一時停止します。"] = "Target pages are analyzed from their images. This may take time for large selections, and editing is temporarily disabled while processing.",
        ["位置・サイズをロック済み（自動調整対象外）"] = "Position and size locked (excluded from auto-adjustment)",
        ["文字送りを等分"] = "Equalize Character Advances",
        ["文字送りを戻す"] = "Restore Character Advances",
        ["文字送りを推定"] = "Estimate Character Advances",
        ["文字サイズを等分"] = "Equalize Character Sizes",
        ["画像から各文字サイズを自動調整"] = "Auto-fit Character Sizes from Image",
        ["OCR取込時の文字サイズへ戻す"] = "Restore Imported OCR Character Sizes",
        ["行内の全文字サイズ"] = "All Character Sizes in Line",
        ["選択した1文字を複数文字へ分割"] = "Split Selected Character into Multiple Characters",
        ["選択を解除"] = "Clear Selection",
        ["選択解除"] = "Clear Selection",
        ["選択文字以降を推定"] = "Estimate from Selected Character",
        ["選択文字の位置と送り幅をロック"] = "Lock Selected Character Position and Advance",
        ["選択文字のロックを解除"] = "Unlock Selected Characters",
        ["領域の位置・サイズを固定／解除"] = "Toggle Region Position and Size Lock",
        ["回転を戻す"] = "Reset Rotation",
        ["左へ90°回転"] = "Rotate 90° Left",
        ["右へ90°回転"] = "Rotate 90° Right",
        ["文書名"] = "Document name",
        ["元PDF"] = "Source PDF",
        ["元PDFのSHA-256"] = "Source PDF SHA-256",
        ["プロジェクトファイル"] = "Project file",
        ["OCRデータソース"] = "OCR data source",
        ["設定ファイル"] = "Settings file",
    };

    private static readonly Dictionary<string, string> EnglishToJapanese =
        JapaneseToEnglish
            .GroupBy(pair => pair.Value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Key, StringComparer.Ordinal);

    public static string CurrentLanguage { get; private set; } = JapaneseLanguage;

    public static bool IsEnglish => string.Equals(CurrentLanguage, EnglishLanguage, StringComparison.OrdinalIgnoreCase);

    /// <summary>指定された言語を現在の表示言語として設定します。</summary>
    public static void SetLanguage(string? language)
    {
        CurrentLanguage = string.Equals(language, EnglishLanguage, StringComparison.OrdinalIgnoreCase)
            ? EnglishLanguage
            : JapaneseLanguage;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(CurrentLanguage);
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo(CurrentLanguage);
    }

    /// <summary>現在の表示言語に合わせて固定文言を翻訳します。</summary>
    public static string Translate(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        if (IsEnglish)
        {
            if (JapaneseToEnglish.TryGetValue(text, out var english)) return english;
            if (TryTranslateWithTrailingEllipsis(text, JapaneseToEnglish, out english)) return english;
            return TranslateJapanesePattern(text);
        }
        if (EnglishToJapanese.TryGetValue(text, out var japanese)) return japanese;
        return TryTranslateWithTrailingEllipsis(text, EnglishToJapanese, out japanese) ? japanese : text;
    }

    /// <summary>
    /// 「設定...」のように、辞書登録済みの文言へ省略記号だけを付けたラベルを翻訳します。
    /// </summary>
    private static bool TryTranslateWithTrailingEllipsis(
        string text,
        IReadOnlyDictionary<string, string> dictionary,
        out string translated)
    {
        foreach (var suffix in new[] { "...", "…" })
        {
            if (!text.EndsWith(suffix, StringComparison.Ordinal)) continue;
            var baseText = text[..^suffix.Length];
            if (!dictionary.TryGetValue(baseText, out var translatedBase)) continue;
            translated = translatedBase + suffix;
            return true;
        }

        translated = string.Empty;
        return false;
    }

    /// <summary>ページ数や領域数など、数値を含む代表的な動的表示を英語化します。</summary>
    private static string TranslateJapanesePattern(string text)
    {
        var match = Regex.Match(text, @"^(\d+)\s*/\s*(\d+)\s*ページ$");
        if (match.Success) return $"Page {match.Groups[1].Value} of {match.Groups[2].Value}";

        match = Regex.Match(text, @"^文字領域:\s*([\d,]+)件$");
        if (match.Success) return $"Text regions: {match.Groups[1].Value}";

        match = Regex.Match(text, @"^文字領域:\s*([\d,]+)件（削除予定:\s*([\d,]+)件）$");
        if (match.Success) return $"Text regions: {match.Groups[1].Value} (pending deletion: {match.Groups[2].Value})";

        match = Regex.Match(text, @"^(.+)（付随ファイル:\s*([\d,]+)件）$");
        if (match.Success) return $"{match.Groups[1].Value} (companion files: {match.Groups[2].Value})";

        match = Regex.Match(text, @"^(.+)（このページ:\s*([\d,]+)領域）$");
        if (match.Success) return $"{Translate(match.Groups[1].Value)} (this page: {match.Groups[2].Value} regions)";

        return text;
    }

    /// <summary>翻訳後の書式文字列へ引数を埋め込みます。</summary>
    public static string Format(string format, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, Translate(format), arguments);

    /// <summary>
    /// 指定した WPF 要素以下にある、バインドされていない固定文言を現在の言語へ更新します。
    /// </summary>
    public static void Apply(DependencyObject root)
    {
        var visited = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
        ApplyCore(root, visited);
    }

    private static void ApplyCore(DependencyObject current, ISet<DependencyObject> visited)
    {
        if (!visited.Add(current)) return;

        switch (current)
        {
            case Window window:
                TranslateProperty(window, Window.TitleProperty);
                break;
            case TextBlock textBlock:
                TranslateProperty(textBlock, TextBlock.TextProperty);
                break;
            case Run run:
                TranslateProperty(run, Run.TextProperty);
                break;
            case HeaderedContentControl headeredContent:
                TranslateProperty(headeredContent, HeaderedContentControl.HeaderProperty);
                TranslateProperty(headeredContent, ContentControl.ContentProperty);
                break;
            case HeaderedItemsControl headeredItems:
                TranslateProperty(headeredItems, HeaderedItemsControl.HeaderProperty);
                break;
            case ContentControl contentControl:
                TranslateProperty(contentControl, ContentControl.ContentProperty);
                break;
        }

        if (current is FrameworkElement element)
        {
            TranslateProperty(element, FrameworkElement.ToolTipProperty);
            if (element.ContextMenu is not null) ApplyCore(element.ContextMenu, visited);
        }

        if (current is DataGrid dataGrid)
        {
            foreach (var column in dataGrid.Columns)
                TranslateProperty(column, DataGridColumn.HeaderProperty);
        }

        if (current is ListView { View: GridView gridView })
        {
            foreach (var column in gridView.Columns)
                TranslateProperty(column, GridViewColumn.HeaderProperty);
        }

        foreach (var child in LogicalTreeHelper.GetChildren(current).OfType<DependencyObject>())
            ApplyCore(child, visited);

        try
        {
            var visualChildren = VisualTreeHelper.GetChildrenCount(current);
            for (var index = 0; index < visualChildren; index++)
                ApplyCore(VisualTreeHelper.GetChild(current, index), visited);
        }
        catch (InvalidOperationException)
        {
            // FrameworkContentElement など、Visual ではない論理要素は論理ツリーだけを処理します。
        }
    }

    private static void TranslateProperty(DependencyObject target, DependencyProperty property)
    {
        if (BindingOperations.IsDataBound(target, property)) return;
        if (target.GetValue(property) is not string value || value.Length == 0) return;
        var translated = Translate(value);
        if (!string.Equals(value, translated, StringComparison.Ordinal)) target.SetValue(property, translated);
    }
}
