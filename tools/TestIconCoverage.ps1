param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
# Explicit adopted placements; dialog footer placement takes precedence over the operation's icon.
# Labels are only test locators; runtime rendering uses stable ActionIcon keys.
$targets = @(
    @{ File='ApplicationSettingsWindow.xaml'; Type='Button'; Label='既定値に戻す(_D)'; Key='Reset'; Count=1 }
    @{ File='ApplicationSettingsWindow.xaml'; Type='Button'; Label='選択を適用(_A)'; Key='Apply'; Count=1 }
    @{ File='BatchCharacterAdjustmentProgressWindow.xaml'; Type='Button'; Label='中止(_S)'; Key='Stop'; Count=1 }
    @{ File='DocumentBatchWindow.xaml'; Type='Button'; Label='候補を作成(_C)'; Key='Search'; Count=1 }
    @{ File='DocumentBatchWindow.xaml'; Type='Button'; Label='処理を中止(_S)'; Key='Stop'; Count=1 }
    @{ File='DocumentBatchWindow.xaml'; Type='Button'; Label='選択した候補を適用(_A)'; Key='Apply'; Count=1 }
    @{ File='DocumentBatchWindow.xaml'; Type='Button'; Label='適用可能な候補をすべて選択(_L)'; Key='SelectAll'; Count=1 }
    @{ File='DocumentBatchWindow.xaml'; Type='Button'; Label='すべて解除(_U)'; Key='Deselect'; Count=1 }
    @{ File='DocumentImageOptimizationWindow.xaml'; Type='Button'; Label='すべて選択(_A)'; Key='SelectAll'; Count=1 }
    @{ File='DocumentImageOptimizationWindow.xaml'; Type='Button'; Label='選択解除(_D)'; Key='Deselect'; Count=1 }
    @{ File='DocumentImageOptimizationWindow.xaml'; Type='Button'; Label='選択ページを順に確認(_R)'; Key='ReviewList'; Count=1 }
    @{ File='DocumentImageOptimizationWindow.xaml'; Type='Button'; Label='選択ページを一括適用(_P)'; Key='Apply'; Count=1 }
    @{ File='DocumentPropertiesWindow.xaml'; Type='Button'; Label='適用(_A)'; Key='Apply'; Count=1 }
    @{ File='ImageOptimizationPreviewWindow.xaml'; Type='Button'; Label='すべて置換(_R)'; Key='Replace'; Count=1 }
    @{ File='ImageOptimizationPreviewWindow.xaml'; Type='Button'; Label='すべて保持(_K)'; Key='Keep'; Count=1 }
    @{ File='ImageOptimizationPreviewWindow.xaml'; Type='Button'; Label='この内容で実行(_E)'; Key='Apply'; Count=1 }
    @{ File='InternalLinkWindow.xaml'; Type='Button'; Label='適用(_A)'; Key='Apply'; Count=1 }
    @{ File='MainWindow.xaml'; Type='MenuItem'; Label='文書のプロパティ...(_D)'; Key='DocumentProperties'; Count=1 }
    @{ File='MainWindow.xaml'; Type='MenuItem'; Label='終了(_X)'; Key='Exit'; Count=1 }
    @{ File='MainWindow.xaml'; Type='MenuItem'; Label='元に戻す(_U)'; Key='Undo'; Count=1 }
    @{ File='MainWindow.xaml'; Type='MenuItem'; Label='やり直す(_R)'; Key='Redo'; Count=1 }
    @{ File='MainWindow.xaml'; Type='MenuItem'; Label='透明テキストを検索...(_F)'; Key='Search'; Count=1 }
    @{ File='MainWindow.xaml'; Type='MenuItem'; Label='透明テキストを置換...(_H)'; Key='Replace'; Count=1 }
    @{ File='MainWindow.xaml'; Type='MenuItem'; Label='コメントとタグ...(_C)'; Key='Comments'; Count=1 }
    @{ File='MainWindow.xaml'; Type='MenuItem'; Label='設定...(_P)'; Key='Settings'; Count=1 }
    @{ File='MainWindow.xaml'; Type='MenuItem'; Label='選択領域の編集を他ページへ反映...(_R)'; Key='Propagate'; Count=1 }
    @{ File='MainWindow.xaml'; Type='MenuItem'; Label='OCR品質の異常候補...(_E)'; Key='Warning'; Count=1 }
    @{ File='MainWindow.xaml'; Type='MenuItem'; Label='ページを抽出...(_X)'; Key='PageExtract'; Count=1 }
    @{ File='MainWindow.xaml'; Type='MenuItem'; Label='PDFを分割...(_S)'; Key='PdfSplit'; Count=1 }
    @{ File='MainWindow.xaml'; Type='MenuItem'; Label='PDF全体の画像を分析・最適化...(_O)'; Key='ImageOptimize'; Count=1 }
    @{ File='MainWindow.xaml'; Type='MenuItem'; Label='ページ番号・ヘッダー／フッター・文字透かし...(_H)'; Key='Decoration'; Count=1 }
    @{ File='MainWindow.xaml'; Type='MenuItem'; Label='現在のプロジェクトを検証(_V)'; Key='Validate'; Count=1 }
    @{ File='MainWindow.xaml'; Type='MenuItem'; Label='バックアップから復旧(_R)'; Key='Restore'; Count=1 }
    @{ File='MainWindow.xaml'; Type='MenuItem'; Label='ログフォルダーを開く(_L)'; Key='OpenFolder'; Count=1 }
    @{ File='MainWindow.xaml'; Type='MenuItem'; Label='バージョン情報(_A)'; Key='Info'; Count=1 }
    @{ File='MainWindow.xaml'; Type='ToggleButton'; Label='サムネイル'; Key='Thumbnail'; Count=1 }
    @{ File='MainWindow.xaml'; Type='MenuItem'; Label='選択ページを抽出...(_X)'; Key='PageExtract'; Count=1 }
    @{ File='MainWindow.xaml'; Type='MenuItem'; Label='編集(_E)'; Key='Edit'; Count=1 }
    @{ File='MainWindow.xaml'; Type='MenuItem'; Label='選択を解除(_D)'; Key='Deselect'; Count=1 }
    @{ File='MainWindow.xaml'; Type='MenuItem'; Label='選択セルへ複数文字を入力(_M)'; Key='MultiCharacterInput'; Count=1 }
    @{ File='MainWindow.xaml'; Type='MenuItem'; Label='横書き(_H)'; Key='HorizontalWriting'; Count=1 }
    @{ File='MainWindow.xaml'; Type='MenuItem'; Label='縦書き(_V)'; Key='VerticalWriting'; Count=1 }
    @{ File='MainWindow.xaml'; Type='MenuItem'; Label='未確認(_U)'; Key='Unreviewed'; Count=1 }
    @{ File='MainWindow.xaml'; Type='MenuItem'; Label='修正済み(_M)'; Key='Edit'; Count=1 }
    @{ File='MainWindow.xaml'; Type='MenuItem'; Label='要再確認(_R)'; Key='Warning'; Count=1 }
    @{ File='MainWindow.xaml'; Type='MenuItem'; Label='OCR対象外(_E)'; Key='Excluded'; Count=1 }
    @{ File='MainWindow.xaml'; Type='MenuItem'; Label='保留(_D)'; Key='Pending'; Count=1 }
    @{ File='MainWindow.xaml'; Type='Button'; Label='操作ガイド'; Key='Help'; Count=1 }
    @{ File='MainWindow.xaml'; Type='Button'; Label='行全体'; Key='EstimateCharacterAdvances'; Count=1 }
    @{ File='MainWindow.xaml'; Type='Button'; Label='選択文字以降'; Key='EstimateCharacterSuffix'; Count=1 }
    @{ File='MainWindow.xaml'; Type='Button'; Label='確認(_W)'; Key='Warning'; Count=2 }
    @{ File='MainWindow.xaml'; Type='Button'; Label='コメント・タグ'; Key='Comments'; Count=1 }
    @{ File='MainWindow.xaml'; Type='ToggleButton'; Label='スポイトで取得(_I)'; Key='Eyedropper'; Count=1 }
    @{ File='MainWindow.xaml'; Type='Button'; Label='検索を中止'; Key='Stop'; Count=1 }
    @{ File='OcrQualityAnalysisWindow.xaml'; Type='Button'; Label='文書全体を分析(_A)'; Key='Analysis'; Count=3 }
    @{ File='MainWindow.xaml'; Type='MenuItem'; Label='OCR作業文字（TXT）...(_T)'; Key='Export'; Count=1 }
    @{ File='MainWindow.xaml'; Type='MenuItem'; Label='コメント一覧（CSV）...(_C)'; Key='Export'; Count=1 }
    @{ File='MainWindow.xaml'; Type='MenuItem'; Label='完成ページ画像（PNG／JPEG）...(_I)'; Key='Export'; Count=1 }
    @{ File='OcrQualityAnalysisWindow.xaml'; Type='Button'; Label='キャンセル'; Key='Stop'; Count=1 }
    @{ File='OcrSearchReplaceWindow.xaml'; Type='Button'; Label='検索(_S)'; Key='Search'; Count=1 }
    @{ File='OcrSearchReplaceWindow.xaml'; Type='Button'; Label='すべて置換(_A)'; Key='Replace'; Count=1 }
    @{ File='OcrSearchReplaceWindow.xaml'; Type='Button'; Label='キャンセル'; Key='Stop'; Count=1 }
    @{ File='OcrSearchReplaceWindow.xaml'; Type='Button'; Label='1件置換(_E)'; Key='Replace'; Count=1 }
    @{ File='PageExtractionWindow.xaml'; Type='Button'; Label='保存先へ進む(_N)'; Key='PageExtract'; Count=1 }
    @{ File='PageTextDecorationsWindow.xaml'; Type='Button'; Label='ページ番号を追加'; Key='PageNumber'; Count=1 }
    @{ File='PageTextDecorationsWindow.xaml'; Type='Button'; Label='ヘッダーを追加'; Key='Header'; Count=1 }
    @{ File='PageTextDecorationsWindow.xaml'; Type='Button'; Label='フッターを追加'; Key='Footer'; Count=1 }
    @{ File='PageTextDecorationsWindow.xaml'; Type='Button'; Label='文字透かしを追加'; Key='TextWatermark'; Count=1 }
    @{ File='PageTextDecorationsWindow.xaml'; Type='Button'; Label='表紙を除き、2ページ目から1で始める(_B)'; Key='PageNumber'; Count=1 }
    @{ File='PageTextDecorationsWindow.xaml'; Type='Button'; Label='適用'; Key='Apply'; Count=1 }
    @{ File='PdfSplitWindow.xaml'; Type='Button'; Label='出力先へ進む(_N)'; Key='PdfSplit'; Count=1 }
    @{ File='ProjectCommentsWindow.xaml'; Type='Button'; Label='新規(_N)'; Key='CommentsAdd'; Count=1 }
    @{ File='ProjectCommentsWindow.xaml'; Type='Button'; Label='適用(_A)'; Key='Apply'; Count=1 }
    @{ File='RepeatedRegionCandidateWindow.xaml'; Type='Button'; Label='すべて選択(_A)'; Key='SelectAll'; Count=1 }
    @{ File='RepeatedRegionCandidateWindow.xaml'; Type='Button'; Label='すべて解除(_D)'; Key='Deselect'; Count=1 }
    @{ File='RepeatedRegionCandidateWindow.xaml'; Type='Button'; Label='選択した候補へ反映(_R)'; Key='Apply'; Count=1 }
    @{ File='RepeatedRegionPropagationOptionsWindow.xaml'; Type='Button'; Label='候補を検索(_S)'; Key='Search'; Count=1 }
    @{ File='RepeatedRegionSearchProgressWindow.xaml'; Type='Button'; Label='中止(_S)'; Key='Stop'; Count=1 }
)
$checks = 0
$placements = 0
$footerExclusions = 0
$iconNamespace = 'clr-namespace:PdfCorrectorium.App.Resources'
foreach ($group in $targets | Group-Object -Property { $_.File }) {
    $path = Join-Path $RepositoryRoot ('src/PdfCorrectorium.App/' + $group.Name)
    [xml]$source = Get-Content -LiteralPath $path -Raw -Encoding UTF8
    $namespaces = New-Object System.Xml.XmlNamespaceManager($source.NameTable)
    $namespaces.AddNamespace('x', 'http://schemas.microsoft.com/winfx/2006/xaml')
    foreach ($target in $group.Group) {
        $matches = @($source.SelectNodes("//*[local-name()='$($target.Type)']") | Where-Object {
            $attribute = if ($target.Type -eq 'MenuItem') { 'Header' } else { 'Content' }
            $_.GetAttribute($attribute) -eq $target.Label -and
                ($target.Type -ne 'MenuItem' -or @($_.SelectNodes("./*[local-name()='MenuItem']")).Count -eq 0)
        })
        if ($matches.Count -ne $target.Count) { throw "Placement count changed: $($target.File): $($target.Label)" }
        foreach ($control in $matches) {
            if ($control.SelectSingleNode("ancestor-or-self::*[@x:Name='DialogFooter']", $namespaces)) {
                if ($control.GetAttribute('ActionIcon.Key', $iconNamespace)) {
                    throw "Dialog footer gained an icon: $($target.File): $($target.Label)"
                }
                $footerExclusions++; $checks++
                continue
            }
            if ($control.GetAttribute('ActionIcon.Key', $iconNamespace) -ne $target.Key) {
                throw "Missing/wrong icon: $($target.File): $($target.Label)"
            }
            $placements++; $checks++
        }
    }
    foreach ($control in $source.SelectNodes("//*[local-name()='Button' and @IsCancel='True']")) {
        if ($control.GetAttribute('ActionIcon.Key', $iconNamespace)) { throw "Ordinary cancel gained an icon: $path" }
        $checks++
    }
    foreach ($control in $source.SelectNodes("//*[local-name()='MenuItem']")) {
        if (@($control.SelectNodes("./*[local-name()='MenuItem']")).Count -gt 0 -and
            $control.GetAttribute('ActionIcon.Key', $iconNamespace)) {
            throw "Category header gained an icon: $path"
        }
        $checks++
    }
}
# Inspect every dialog, including older placements and hidden progress controls.
# New dialogs must identify the actual bottom action area, independently of translated captions.
$dialogs = 0
$footerButtons = 0
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $RepositoryRoot 'src/PdfCorrectorium.App') -Filter '*Window.xaml') {
    if ($file.Name -eq 'MainWindow.xaml') { continue }
    [xml]$source = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
    $namespaces = New-Object System.Xml.XmlNamespaceManager($source.NameTable)
    $namespaces.AddNamespace('x', 'http://schemas.microsoft.com/winfx/2006/xaml')
    $footers = @($source.SelectNodes("//*[@x:Name='DialogFooter']", $namespaces))
    # The compact reader is closed through standard window chrome and has no bottom action area.
    if ($file.Name -eq 'ReadAloudWindow.xaml') {
        if ($footers.Count -ne 0) { throw 'The compact reader must not reserve a dialog footer.' }
        $checks++
        continue
    }
    if ($footers.Count -ne 1) { throw "Dialog must identify exactly one footer: $($file.Name)" }
    $dialogs++; $checks++
    foreach ($node in $footers[0].SelectNodes('descendant-or-self::*')) {
        if ($node.GetAttribute('ActionIcon.Key', $iconNamespace) -or
            $node.NamespaceURI -eq $iconNamespace -and $node.LocalName -eq 'ActionIcon') {
            throw "Dialog footer contains an icon: $($file.Name)"
        }
        if ($node.LocalName -eq 'Button') {
            if (!$node.GetAttribute('Content') -or $node.HasAttribute('ContentTemplate')) {
                throw "Dialog footer must use plain captions: $($file.Name)"
            }
            $footerButtons++; $checks++
        }
    }
    foreach ($button in $source.SelectNodes("//*[local-name()='Button' and @IsCancel='True']")) {
        if (!$button.SelectSingleNode("ancestor-or-self::*[@x:Name='DialogFooter']", $namespaces)) {
            throw "Dialog dismissal is outside the identified footer: $($file.Name)"
        }
        $checks++
    }
}
# Explicit icon-only actions retain semantic captions for automation/localization,
# with visible tooltip hints and real access-key registration rather than hidden text.
$iconOnlyActions = @(
    'RapidOcrReviewWindow|ZoomOutButton|ZoomOut|O'
    'RapidOcrReviewWindow|ZoomInButton|ZoomIn|I'
    'RapidOcrReviewWindow|FitImageButton|FitPage|F'
    'RapidOcrReviewWindow|ActualSizeButton|ActualSize|Z'
    'RepeatedRegionCandidateWindow|PreviousCandidateButton|PreviousPage|P'
    'RepeatedRegionCandidateWindow|NextCandidateButton|NextPage|N'
    'RepeatedRegionCandidateWindow|ZoomOutButton|ZoomOut|O'
    'RepeatedRegionCandidateWindow|ZoomInButton|ZoomIn|I'
    'RepeatedRegionCandidateWindow|FitPreviewButton|FitPage|F'
    'OcrSearchReplaceWindow|SearchButton|Search|S'
)
foreach ($action in $iconOnlyActions) {
    $file, $name, $key, $mnemonic = $action.Split('|')
    [xml]$source = Get-Content -LiteralPath (Join-Path $RepositoryRoot ('src/PdfCorrectorium.App/' + $file + '.xaml')) -Raw -Encoding UTF8
    $ns = New-Object System.Xml.XmlNamespaceManager($source.NameTable)
    $ns.AddNamespace('x', 'http://schemas.microsoft.com/winfx/2006/xaml')
    $button = $source.SelectSingleNode("//*[@x:Name='$name']", $ns)
    if (!$button -or $button.GetAttribute('Style') -ne '{StaticResource IconOnlyActionButtonStyle}' -or
        $button.GetAttribute('ActionIcon.Key', $iconNamespace) -ne $key -or !$button.GetAttribute('Content') -or
        !$button.GetAttribute('ToolTip') -or
        $button.GetAttribute('KeyboardAccess.Key', 'clr-namespace:PdfCorrectorium.App.Services') -ne $mnemonic -or
        $button.SelectSingleNode("ancestor::*[@x:Name='DialogFooter']", $ns)) {
        throw "Icon-only action lost its icon, semantics, tooltip or access key: $action"
    }
    $checks++
}
[xml]$appSource = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'src/PdfCorrectorium.App/App.xaml') -Raw -Encoding UTF8
$ns = New-Object System.Xml.XmlNamespaceManager($appSource.NameTable)
$ns.AddNamespace('x', 'http://schemas.microsoft.com/winfx/2006/xaml')
$onlyStyle = $appSource.SelectSingleNode("//*[@x:Key='IconOnlyActionButtonStyle']", $ns)
if (!$onlyStyle.SelectSingleNode("*[local-name()='Setter' and @Property='Width' and @Value='32']") -or
    !$onlyStyle.SelectSingleNode("*[local-name()='Setter' and @Property='Height' and @Value='32']")) { throw 'Icon-only hit size changed.' }
$checks++
$onlyTemplate = $appSource.SelectSingleNode("//*[@x:Key='ActionIconOnlyTemplate']", $ns)
if (!$onlyTemplate.SelectSingleNode("*[local-name()='ActionIcon' and @Width='18' and @Height='18']") -or
    $onlyTemplate.SelectSingleNode(".//*[local-name()='AccessText']")) { throw 'Icon-only template gained text or lost its icon size.' }
$checks++
$customization = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'src/PdfCorrectorium.App/MainWindow.ToolbarCustomization.cs') -Raw
if ($customization -notmatch 'ActionIcon.SetKey\(reset, "Reset"\)') { throw 'Dynamic toolbar reset lost its icon.' }
$checks++
if ($placements + $footerExclusions -ne 81) { throw 'The dev.236 placement registry changed unexpectedly.' }
[xml]$updateSource=Get-Content -LiteralPath (Join-Path $RepositoryRoot 'src/PdfCorrectorium.App/MainWindow.xaml') -Raw -Encoding UTF8
$updateNode=$updateSource.SelectSingleNode("//*[local-name()='MenuItem' and @Header='更新を確認(_U)']")
if(!$updateNode-or$updateNode.GetAttribute('ActionIcon.Key',$iconNamespace)-cne'Reset'){throw 'Update action lost its shared Material circular-arrow icon.'}
$checks++
if ($dialogs -lt 21 -or $footerButtons -lt 47) { throw 'Dialog footer coverage unexpectedly decreased.' }
Write-Output "PASS: $placements retained dev.210 icons, $footerExclusions reclassified footer placements; $dialogs dialogs/$footerButtons plain footer buttons; $checks coverage/exclusion checks."
