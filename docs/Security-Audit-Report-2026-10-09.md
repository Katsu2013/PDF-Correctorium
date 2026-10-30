# PDF-Correctorium ソースコード・セキュリティ診断レポート (2026-10-09)

- **診断実施日**: 2026年10月9日
- **対象プロダクト**: PDF-Correctorium
- **対象ソースコード**: `src/` 配下全プロジェクト (`PdfCorrectorium.App`, `PdfCorrectorium.Core`, `PdfCorrectorium.Infrastructure`, `PdfCorrectorium.ProjectFormat`)
- **診断種別**: 静的コード解析、アーキテクチャ・脆弱性診断 (ホワイトボックス)

---

## 1. エグゼクティブサマリ

PDF-Correctorium のソースコード全体を対象に、信頼境界、入力検証、外部ツール・ネイティブライブラリ連携、プロセス分離、メモリ保護、データ機密性の観点からセキュリティ監査を実施しました。

### 総合評価
全体として**極めて高いセキュリティ意識と多層防御（Defense-in-Depth）**が設計・実装されています。
特に以下の点は、商用・オープンソースのデスクトップPDFアプリケーションと比較しても模範的です：
1. **堅牢なプロセス分離**: ネイティブPDF処理（PDFium）をUIプロセスから完全に分離し、Windows Job Object を用いた厳格なメモリ上限設定（2GB/3GB）とハングアップ時の強制ツリー終了を実装。
2. **安全な墨消し（Redaction）**: レンダリングビットマップへの物理的ピクセル焼付け、QPDFによる不要ストリームの完全再圧縮・除去、さらに出力後のPDFからテキストが抽出不可であることをPDFiumで再検証する機構を実装。
3. **XML/XXE防御の徹底**: 全XML読み込み処理で `DtdProcessing = Prohibit`, `XmlResolver = null`, `MaxCharactersInDocument` を明示指定。
4. **外部リンクの厳格なホワイトリスト**: PDF内リンクおよびプロジェクト定義のURIに対し、`http` / `https` のみを許可し、危険なスキーム（`file://`, `javascript:`, `cmd:` 等）を完全に遮断。

一方で、**ZIPヘッダ改ざん時のZip Bomb対策**、**巨大OCR XMLファイルのインメモリ展開によるメモリ圧迫**、**外部プロセス呼び出し時の引数・パス検証の改善点**など、主にサービス妨害（DoS）耐性と入力検証の観点で改善が推奨される潜在的脆弱性が確認されました。

---

## 2. 検出された潜在的脆弱性と改善提案

### 【中リスク】 1. ZIPヘッダ偽装による Zip Bomb・メモリ枯渇 (DoS)
- **該当箇所**: [`ProjectPackageService.cs`](../src/PdfCorrectorium.ProjectFormat/ProjectPackageService.cs#L612-L620), [`ReadThumbnailCacheAsync`](../src/PdfCorrectorium.ProjectFormat/ProjectPackageService.cs#L308-L314)
- **CWE**: CWE-409 (Improper Handling of Highly Compressed Data / Zip Bomb)

#### 詳細とメカニズム
`ProjectPackageService.EnsureArchiveWithinLimits` では、`entry.Length`（非圧縮サイズ）や `entry.CompressedLength`（圧縮サイズ）をもとに展開量と圧縮率（`Limits.MaximumCompressionRatio`）を検証しています。
しかし、**`ZipArchiveEntry.Length` は ZIP ファイルのヘッダ（セントラルディレクトリ）に記録されている値であり、攻撃者が任意の値に改ざん可能**です。

ヘッダ上の `Length` を小さな値（例: 100バイト）に偽装した悪意あるDeflateストリームが含まれていた場合：
1. `ReadJsonAsync`: ヘッダチェックを通過後、`entry.Open()` で得られた展開ストリームがそのまま `JsonSerializer.DeserializeAsync` に渡されるため、ストリーム終端（または数GBの展開後）まで制限なく読み込みが続行します。
2. `ReadThumbnailCacheAsync`: 同様に `input.CopyToAsync(buffer)` で展開サイズ無制限でバッファへコピーされます。

※同クラス内の [`MaterializeEmbeddedSourceAsync`](../src/PdfCorrectorium.ProjectFormat/ProjectPackageService.cs#L517) では安全な `CopyWithLimitAsync`（実読込バイト数カウント）が使われていますが、JSONおよびサムネイル展開部には適用されていません。

#### 影響
悪意を持って作成された `.pdfocrproj` を開いた際、メモリが枯渇し `OutOfMemoryException` またはプロセスのハングアップ（DoS）が発生します。

#### 推奨対策
`ReadJsonAsync` および `ReadThumbnailCacheAsync` においても、展開時の実際の読み取りバイト数を追跡し、上限を超えた時点で即時中断するラッパーストリーム（または `BoundedStream`）を適用してください。

```csharp
// 改善イメージ: ReadJsonAsync で実読み取りバイト数を制限
private async Task<T> ReadJsonAsync<T>(ZipArchive archive, string name, CancellationToken cancellationToken)
{
    var entry = archive.GetEntry(name) ?? throw new InvalidDataException($"Missing entry: {name}");
    if (entry.Length <= 0 || entry.Length > Limits.MaximumJsonEntryBytes)
        throw new InvalidDataException($"JSON entry exceeds the allowed size: {name}");

    await using var input = entry.Open();
    using var limitedStream = new MemoryStream();
    // ヘッダではなく、実際の展開ストリームのバイト数を上限でチェックしながらコピー
    await CopyWithLimitAsync(input, limitedStream, Limits.MaximumJsonEntryBytes, cancellationToken);
    limitedStream.Position = 0;

    return await JsonSerializer.DeserializeAsync<T>(limitedStream, JsonOptions, cancellationToken)
        ?? throw new InvalidDataException($"Empty JSON entry: {name}");
}
```

---

### 【中リスク】 2. NDL OCR XML インポート時の過大サイズ許容とインメモリDOM展開 (DoS)
- **該当箇所**: [`NdlOcrCompanionService.cs`](../src/PdfCorrectorium.App/Services/NdlOcrCompanionService.cs#L89, #L301-L311)
- **CWE**: CWE-400 (Uncontrolled Resource Consumption)

#### 詳細とメカニズム
`NdlOcrCompanionService` の `MaximumImportBytes` は既定で **256MB** に設定されています。
`ReadXmlAsync` では以下の処理が実行されます：
1. `File.ReadAllTextAsync` で 256MB の文字列オブジェクトをメモリ上に確保。
2. 構文エラー時のフォールバック処理で `Regex.Replace` および文字列結合を行い、さらに 256MB の文字列を複製。
3. `ParseXmlAsync` でインメモリDOMツリーである `XDocument`（LINQ to XML）に展開。

.NET の `XDocument` は、XMLの要素・属性・テキストノードごとに多数のオブジェクトをヒープに生成するため、元のXMLサイズの 5〜10 倍以上のメモリを消費します。256MB の XML を `XDocument` に展開すると、容易に 1.5GB〜2.5GB 以上のメモリを消費し、GC の高負荷や 64bit プロセスであっても一時的なメモリ圧迫・クラッシュを引き起こすリスクがあります。

#### 影響
巨大な OCR XML ファイルをインポート、または自動探索により読み込んだ際に、著しいメモリ消費と UI フリーズ、またはアプリの強制終了が発生します。

#### 推奨対策
1. OCR 付随ファイルの特性上、実用上 256MB の単一 XML は想定されないため、`MaximumImportBytes` を 16MB〜32MB 程度に引き下げる。
2. 必要に応じて、全ツリーをメモリに載せる `XDocument` ではなく、`XmlReader` による前方読み取り（ストリーミングパース）を採用する。

---

### 【低リスク】 3. エクスプローラー呼び出し時のコマンド引数分割
- **該当箇所**: [`MainWindow.xaml.cs:1080-1081`](../src/PdfCorrectorium.App/MainWindow.xaml.cs#L1080-L1081)
- **CWE**: CWE-88 (Improper Neutralization of Argument Delimiters in a Command)

#### 詳細とメカニズム
ログフォルダを開く処理で、以下のように記述されています：
```csharp
private void OpenLogFolderMenuItem_OnClick(object sender, RoutedEventArgs e) =>
    Process.Start(new ProcessStartInfo("explorer.exe", _applicationPaths.LogDirectory) { UseShellExecute = true });
```
Windows の `explorer.exe` に `UseShellExecute = true` で引数を渡す際、パス文字列が二重引用符で囲まれていない場合、パスに空白が含まれていると（例: `C:\Users\John Doe\AppData\...`）、`explorer.exe` が空白で引数を分割して認識します。

#### 影響
空白を含むユーザー環境において、意図したフォルダが開かない、あるいは別のフォルダ（ドキュメントフォルダ等）が開いてしまう誤作動が発生します。

#### 推奨対策
引数全体を明示的に二重引用符で囲んで渡します：
```csharp
Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_applicationPaths.LogDirectory}\"") { UseShellExecute = true });
```

---

### 【低リスク】 4. QPDF 呼び出し時のオブジェクト参照番号の型検証漏れ
- **該当箇所**: [`PdfInputStructureService.cs:52-56, 86-90`](../src/PdfCorrectorium.App/Services/PdfInputStructureService.cs#L52-L56)
- **CWE**: CWE-20 (Improper Input Validation)

#### 詳細とメカニズム
[`PdfSourceLinkService.TryReference`](../src/PdfCorrectorium.App/Services/PdfSourceLinkService.cs#L261-L267) では、`parts[0]` および `parts[1]` が数値であるかを `int.TryParse` で検証した上で QPDF 引数に渡しています。
しかし、`PdfInputStructureService.cs` では以下のように `parts[2] == "R"` のみを確認しています：
```csharp
var parts = catalogReference.Split(' ', StringSplitOptions.RemoveEmptyEntries);
if (parts.Length != 3 || parts[2] != "R")
    throw new InvalidDataException("PDFカタログ参照が不正です。");
var catalog = await RunAsync(qpdfPath,
    ["--json-output=2", $"--json-object={parts[0]},{parts[1]}", pdfPath, "-"], cancellationToken);
```
`ExternalProcessRunner` は `ArgumentList.Add` を使用しているため、シェルメタ文字等による任意のコマンドインジェクションには至りませんが、`catalogReference` 内に不正な文字列が含まれていた場合、QPDF の `--json-object=...` オプションの引数として不正な文字列がそのまま渡されます。

#### 推奨対策
`PdfSourceLinkService` と同様に、`int.TryParse(parts[0], out _) && int.TryParse(parts[1], out _)` による数値検証を追加してください。

---

### 【低リスク】 5. `PdfPageManagementService` におけるファイルパス未正規化によるフラグ誤認リスク
- **該当箇所**: [`PdfPageManagementService.cs:74-93`](../src/PdfCorrectorium.App/Services/PdfPageManagementService.cs#L74-L93)
- **CWE**: CWE-88 (Improper Neutralization of Argument Delimiters in a Command)

#### 詳細とメカニズム
`PdfPageManagementService.MaterializeAsync` において、`sourcePdfPath` および `outputPath` が `Path.GetFullPath` による絶対パス正規化を経由せずに QPDF の引数リストへ追加されています。
万が一、ハイフンで始まる相対パス（例: `--output.pdf`）が渡された場合、CLI ツールである `qpdf` がファイル名オペランドではなくコマンドラインオプションと誤認して異常終了または予期しない引数解釈を行うリスクがあります。

#### 推奨対策
引数リストに追加する前に `Path.GetFullPath(sourcePdfPath)` および `Path.GetFullPath(outputPath)` を適用し、必ずドライブレターから始まる絶対パスに変換してください。

---

### 【情報】 6. 環境変数 `PDFOCR_QPDF_PATH` によるバイナリ実行経路の優先
- **該当箇所**: [`PdfBookmarkService.cs:944`](../src/PdfCorrectorium.App/Services/PdfBookmarkService.cs#L944)
- **CWE**: CWE-426 (Untrusted Search Path)

#### 詳細と影響
`ResolveQpdfPath()` では、環境変数 `PDFOCR_QPDF_PATH` が定義されている場合、配布フォルダに同梱された `components\qpdf\qpdf.exe` よりも優先してそのパスを実行します。
マルチユーザー環境や制限付き環境において、別プロセスによってユーザー環境変数が改変された場合、意図しない外部実行ファイルが呼び出される可能性があります。

#### 推奨対策
本番リリースビルドにおいては同梱バイナリを最優先とし、環境変数はデバッグ・開発時のみ有効化するか、実行ファイルのデジタル署名やハッシュチェックを行う構成を推奨します。

---

### 【情報】 7. 本番実行ファイルにおける診断・テスト引数の混入とファイル名競合
- **該当箇所**: [`App.xaml.cs:49-70, 112-250`](../src/PdfCorrectorium.App/App.xaml.cs#L49)
- **CWE**: CWE-489 (Active Debug Code)

#### 詳細と影響
`App.xaml.cs` には `--smoke-test`, `--render-test`, `--editor-test`, `--isolated-pdf-export` など多数のテスト用引数ハンドラが実装されており、`#if DEBUG` 等で分離されず本番バイナリに含まれています。
関連付け起動等で偶然 `--smoke-test` 等の名前を持つファイルをダブルクリックした場合、文書を開かずにテスト処理が走ってアプリが終了する挙動となります。

#### 推奨対策
コマンドライン引数の解析時に、テスト用フラグとファイルパス（既存ファイルの有無）を明確に区別するか、テスト専用のハンドラを条件付きコンパイル（`#if DEBUG`）で分離することを推奨します。

---

## 3. 評価すべき優良なセキュリティ実装（防御機構）

本監査において、脆弱性の混入を防ぐための高度な防御機構が多数確認されました。

| 防御項目 | 実装箇所 | 防御メカニズム |
|---|---|---|
| **プロセス分離 & ジョブオブジェクト** | [`PdfNativeWorkerClient.cs`](../src/PdfCorrectorium.App/Services/PdfNativeWorkerClient.cs)<br>[`WindowsProcessJob.cs`](../src/PdfCorrectorium.App/Services/WindowsProcessJob.cs) | PDFium ネイティブ呼び出しを別プロセスに完全分離。Windows Job Object でプロセスメモリ上限（2GB/3GB）とハング時のツリー終了を強制し、不正PDFによるOOMやクラッシュからメイン画面を保護。 |
| **確実な墨消し（物理的焼付け & 事後検証）** | [`PdfExportService.cs`](../src/PdfCorrectorium.App/Services/PdfExportService.cs#L3128, #L5973) | 墨消し矩形をビットマップ画像へ物理的に直接塗りつぶし、QPDFで不要ストリームを再圧縮・除去。出力後、PDFiumで該当領域内に文字データが残存していないかを再検査し、抽出可能な文字がある場合は出力を即時中止。 |
| **徹底された XXE / XML爆弾防御** | [`NdlOcrCompanionService.cs`](../src/PdfCorrectorium.App/Services/NdlOcrCompanionService.cs#L407)<br>[`PdfBookmarkService.cs`](../src/PdfCorrectorium.App/Services/PdfBookmarkService.cs#L411)<br>[`WorkspaceDockLayout.cs`](../src/PdfCorrectorium.App/Services/WorkspaceDockLayout.cs#L16) | すべての `XmlReaderSettings` において `DtdProcessing = Prohibit`, `XmlResolver = null`, `MaxCharactersInDocument` を明示設定。外部実体参照および数十億笑い攻撃（Billion Laughs）を完全に遮断。 |
| **URLスキームのホワイトリスト検証** | [`MainWindowViewModel.ProjectFeatures.cs`](../src/PdfCorrectorium.App/ViewModels/MainWindowViewModel.ProjectFeatures.cs#L291)<br>[`PdfSourceLinkService.cs`](../src/PdfCorrectorium.App/Services/PdfSourceLinkService.cs#L151) | 外部リンク起動時に `uri.Scheme is ("http" or "https")` のみを許可。`file://`, `javascript:`, `cmd:`, `powershell:` などの危険なプロトコルハンドラ呼び出しを確実に防止。 |
| **Zip Slip（ディレクトリトラバーサル）対策** | [`ProjectPackageService.cs`](../src/PdfCorrectorium.ProjectFormat/ProjectPackageService.cs#L637-L642) | ZIP内のエントリ名に対して、ルートパス、ドライブレター、`..` や空文字セグメントを含むものを包括的に拒絶。 |
| **ログにおけるプライバシー保護** | [`DiagnosticLog.cs`](../src/PdfCorrectorium.Infrastructure/DiagnosticLog.cs#L35)<br>[`StartupDiagnostics.cs`](../src/PdfCorrectorium.App/StartupDiagnostics.cs#L99) | 診断ログおよび起動ログの出力時に、Windows ユーザー名（`Environment.UserName`）を自動的に `<user>` へ置換してマスキング。 |

---

## 4. 推奨対応ロードマップ

| 優先度 | 対応項目 | 対象ファイル | 想定工数 |
|:---:|---|---|:---:|
| **高** | ZIP解凍時の実ストリーム長制限の適用（Zip Bomb対策） | [`ProjectPackageService.cs`](../src/PdfCorrectorium.ProjectFormat/ProjectPackageService.cs) | 小 |
| **中** | NDL OCR XML インポート時の最大サイズ上限の縮小（256MB → 32MB程度） | [`NdlOcrCompanionService.cs`](../src/PdfCorrectorium.App/Services/NdlOcrCompanionService.cs) | 極小 |
| **低** | ログフォルダ起動時の引数引用符の追加 | [`MainWindow.xaml.cs`](../src/PdfCorrectorium.App/MainWindow.xaml.cs) | 極小 |
| **低** | QPDF呼び出し引数におけるオブジェクト番号の整数型検証の追加 | [`PdfInputStructureService.cs`](../src/PdfCorrectorium.App/Services/PdfInputStructureService.cs) | 極小 |
| **低** | ページ操作時の入出力パスの `Path.GetFullPath` 正規化 | [`PdfPageManagementService.cs`](../src/PdfCorrectorium.App/Services/PdfPageManagementService.cs) | 極小 |
| **情報** | リリースビルドにおける QPDF パス探索順序の固定化 | [`PdfBookmarkService.cs`](../src/PdfCorrectorium.App/Services/PdfBookmarkService.cs) | 小 |
| **情報** | テスト・診断用起動スイッチの条件付きコンパイル分離 | [`App.xaml.cs`](../src/PdfCorrectorium.App/App.xaml.cs) | 中 |
