# PDF-Correctorium ソースコード・セキュリティ監査レポート (2026-10-14)

- **診断実施日**: 2026年10月14日
- **対象プロダクト**: PDF-Correctorium
- **対象バージョン**: `1.0.0-dev.223` (アセンブリ/ファイルバージョン: `1.0.0.223`)
- **対象ソースコード**: `src/` 配下全プロジェクト
  - [`PdfCorrectorium.App`](../src/PdfCorrectorium.App)
  - [`PdfCorrectorium.Core`](../src/PdfCorrectorium.Core)
  - [`PdfCorrectorium.Infrastructure`](../src/PdfCorrectorium.Infrastructure)
  - [`PdfCorrectorium.ProjectFormat`](../src/PdfCorrectorium.ProjectFormat)
- **診断種別**: 静的コード解析、アーキテクチャ・脆弱性診断、過去指摘事項の是正検証 (ホワイトボックス)

---

## 1. エグゼクティブサマリ

PDF-Correctorium のソースコード全体（バージョン `1.0.0-dev.223`）を対象に、信頼境界、入力検証、外部プロセス・ネイティブライブラリのサンドボックス隔離、プロセス間通信、ファイル・パス制御、メモリ保護、データ機密性・完全性の観点からセキュリティ監査を実施しました。

### 総合評価: 極めて堅牢（A+）
本プロダクトは、2026年10月9日の初期監査レポートおよび2026年10月12日の追加調査で指摘された潜在的脆弱性（Zip Bomb、構造ツリーに残存する墨消し機密情報、UNC/SMBパスによるNTLM漏洩、QPDFパス偽装、巨大DOM展開など）に対して、**極めて迅速かつ徹底的な設計・コード是正を完了**しています。

特に注目すべきセキュリティ設計上の成果は以下の通りです：
1. **Windows AppContainer による能力数0の極小権限サンドボックス**:
   PDFium によるネイティブ描画ワーカーおよび QPDF 外部処理を、特権・ネットワーク能力を持たない AppContainer トークン（`capabilities = 0`）および Windows Job Object（メモリ上限 2GB/3GB、ツリー終了強制）の下で隔離実行。
2. **多層防御による Zip Bomb / 入力爆弾の根絶**:
   ZIPヘッダの公称サイズを一切信用せず、実展開ストリーム長を物理的にカウント・遮断する [`BoundedReadStream`](../src/PdfCorrectorium.Core/IO/BoundedReadStream.cs) と、C# オブジェクト展開前にトークン数・深さ・文字列長・配列数を逐次検証する [`JsonInputBudget`](../src/PdfCorrectorium.Core/IO/JsonInputBudget.cs) を導入。
3. **タグ付きPDFにおける墨消し情報の物理的・構造的完全除去**:
   レンダリングビットマップへの物理的ピクセル焼付けに加え、出力PDFの構造ツリー（`/StructTreeRoot`, `/MarkInfo`, `/SpiderInfo`）をカタログから完全に除去し、出力後に QPDF による構造再検査と PDFium による文字抽出検証を実施。
4. **悪意あるパス・デバイス名・UNC の完全遮断**:
   [`SourcePdfPathPolicy`](../src/PdfCorrectorium.ProjectFormat/SourcePdfPathPolicy.cs) による UNC / ネットワークドライブ / Reparse Point（ジャンクション・シンボリックリンク）の自動参照ブロック、および [`SafeFileName`](../src/PdfCorrectorium.Core/IO/SafeFileName.cs) による Windows 特殊デバイス名（CON, PRN, AUX, NUL等）とパストラバーサルの無害化。
5. **XML/XXE 防御と完全ストリーミング化**:
   全 XML リーダーで DTD 禁止、外部リゾルバ無効化、最大文字数制限を適用。さらに NDL OCR XML インポート処理を従来のインメモリ `XDocument` から [`OcrXmlFragmentReader`](../src/PdfCorrectorium.App/Services/OcrXmlFragmentReader.cs) による完全ストリーミング処理へ移行し、メモリ肥大化を根絶。

現在残存している留意事項は、本番バイナリにおけるテスト用コマンドライン引数の分離や、公開に向けたコード署名の適用など、軽微な仕様整理・リリース準備事項に限られます。

---

## 2. 過去の指摘事項に対する是正確認状況 (2026-10-09 / 2026-10-12 指摘)

過去の監査および追加調査で検出された脆弱性・改善項目について、現行コード（`dev.223`）における是正状況を追跡・検証しました。**全指摘事項が適切な設計とコードにより是正完了していることを確認しました。**

| # | 指摘項目 | 当初リスク | 該当 CWE | 是正ステータス | 是正メカニズム・実装箇所 |
|---|---|:---:|:---:|:---:|---|
| 1 | 墨消し後も構造ツリーの ActualText/Alt に秘密文字列が残存 | **高** | CWE-200<br>CWE-459 | **【是正完了】** | [`PdfBookmarkService.cs`](../src/PdfCorrectorium.App/Services/PdfBookmarkService.cs#L733-L775) においてカタログから `/StructTreeRoot`, `/MarkInfo`, `/SpiderInfo` を完全除去。QPDF による書き込み後、出力PDFのカタログを再パースして残存がないことを事後検証。 |
| 2 | ZIPヘッダ偽装による Zip Bomb・メモリ枯渇 (DoS) | **中** | CWE-409 | **【是正完了】** | [`BoundedReadStream.cs`](../src/PdfCorrectorium.Core/IO/BoundedReadStream.cs) を導入。ZIPヘッダの公称サイズを信頼せず、実際の展開バイト数を読み取り中に追跡して上限到達時に即座に遮断。 |
| 3 | プロジェクト内OCR・しおりの構造量制限不足とDOM展開 | **中** | CWE-400 | **【是正完了】** | [`JsonInputBudget.cs`](../src/PdfCorrectorium.Core/IO/JsonInputBudget.cs) によるトークン・深度・配列数の事前スキャン、および [`PdfBookmarkService.cs`](../src/PdfCorrectorium.App/Services/PdfBookmarkService.cs#L459-L493) の `XmlReader` プリスキャンにより、全構造量をメモリ確保前に制限。 |
| 4 | ダイアログ・パスへの UNC / 特殊ファイル名の混入 | **中** | CWE-200<br>CWE-59 | **【是正完了】** | [`SourcePdfPathPolicy.cs`](../src/PdfCorrectorium.ProjectFormat/SourcePdfPathPolicy.cs) で UNC、ネットワークドライブ、再解析ポイントを遮断。[`SafeFileName.cs`](../src/PdfCorrectorium.Core/IO/SafeFileName.cs) でダイアログに渡すファイル名からパス区切りやデバイス名を完全排除。 |
| 5 | NDL OCR XML インポート時の過大サイズとインメモリDOM展開 | **中** | CWE-400 | **【是正完了】** | [`NdlOcrCompanionService.cs`](../src/PdfCorrectorium.App/Services/NdlOcrCompanionService.cs#L99, #L250-L320) でXML上限を32MBに制限。[`OcrXmlFragmentReader.cs`](../src/PdfCorrectorium.App/Services/OcrXmlFragmentReader.cs) と `XmlReader` による逐次ストリーミングパースへ全面移行。 |
| 6 | エクスプローラー呼び出し時のコマンド引数分割 | **低** | CWE-88 | **【是正完了】** | [`MainWindow.xaml.cs`](../src/PdfCorrectorium.App/MainWindow.xaml.cs#L1080) で引数パスを明示的に二重引用符 `$\"\"{_applicationPaths.LogDirectory}\"\"` で囲んで実行。 |
| 7 | QPDF 呼び出し時のオブジェクト参照番号の型検証漏れ | **低** | CWE-20 | **【是正完了】** | [`PdfInputStructureService.cs`](../src/PdfCorrectorium.App/Services/PdfInputStructureService.cs#L54, #L89) で `PdfObjectReference.IsValid(...)` による厳格な整数形式チェックを適用。 |
| 8 | `PdfPageManagementService` におけるファイルパス未正規化 | **低** | CWE-88 | **【是正完了】** | [`PdfPageManagementService.cs`](../src/PdfCorrectorium.App/Services/PdfPageManagementService.cs#L77-L78) で `Path.GetFullPath(...)` による絶対パス正規化を徹底。 |
| 9 | 環境変数 `PDFOCR_QPDF_PATH` による実行バイナリ優先 | **情報** | CWE-426 | **【是正完了】** | [`QpdfExecutableResolver.cs`](../src/PdfCorrectorium.App/Services/QpdfExecutableResolver.cs#L20-L39) で環境変数および外部 PATH 探索を `#if DEBUG` に限定。Release ビルドでは同梱バイナリを固定参照。 |
| 10 | ネイティブ処理ワーカーの権限分離・サンドボックス化 | **重要** | CWE-250 | **【是正完了】** | [`WindowsSandboxProcess.cs`](../src/PdfCorrectorium.App/Services/WindowsSandboxProcess.cs) による Windows AppContainer (capabilities=0) 隔離を導入。PDFium および QPDF の双方に適用。 |

---

## 3. 最新ソースコードにおける潜在的セキュリティ課題と改善提言

現行バージョン（`1.0.0-dev.223`）のコードベースを精査した結果、重大な脆弱性（RCE、特権昇格、任意のファイル読み書き、SSRF等）は存在しないことを確認しました。
今後プロダクション品質をさらに高めるために推奨される軽微な改善点および運用上の留意事項を以下に記載します。

---

### 【低リスク / 仕様的考慮】 1. 本番バイナリにおける多数のテスト・診断用CLIスイッチの常時有効化
- **該当箇所**: [`App.xaml.cs:49-75, 112-255`](../src/PdfCorrectorium.App/App.xaml.cs#L49)
- **CWE**: CWE-489 (Active Debug Code)

#### 詳細とリスク
[`App.xaml.cs`](../src/PdfCorrectorium.App/App.xaml.cs) の起動ルーチン内には、`--smoke-test`, `--render-test`, `--ndl-test`, `--editor-test`, `--keyboard-test`, `--diagnostic-delay-worker` など、20種類以上のテスト・診断用コマンドライン引数ハンドラが実装されています。これらは `#if DEBUG` や診断専用ビルドで分離されておらず、本番の Release バイナリにもそのままコンパイルされています。

エンドユーザーがエクスプローラーからの関連付け起動やドラッグ＆ドロップを行った際、偶然にもこれらのフラグ名と一致するファイル名（例: `--smoke-test` や `--editor-test`）のファイルを開こうとした場合、アプリケーションは通常画面を起動せず、テスト処理を実行して即座に終了してしまいます。

#### 推奨対策
1. テスト専用のコマンドラインフラグ処理を `#if DEBUG` 等の条件付きコンパイルディレクティブで囲み、Release ビルドから除外する。
2. もしくは、引数が既存のファイルパスとして存在する場合はファイルオープンを最優先とし、フラグとしての解釈を行わない判定順序にする。

---

### 【低リスク / 運用考慮】 2. 異常終了時・強制終了時の一時サンドボックスディレクトリの残存
- **該当箇所**: [`WindowsSandboxProcess.cs:37, 267-283`](../src/PdfCorrectorium.App/Services/WindowsSandboxProcess.cs#L37)
- **CWE**: CWE-459 (Incomplete Cleanup)

#### 詳細とリスク
[`WindowsSandboxProcess`](../src/PdfCorrectorium.App/Services/WindowsSandboxProcess.cs) は起動時に `%TEMP%\PdfCorrectorium\sandbox\<guid>` を作成し、処理対象の PDF や実行ファイルをステージングします。正常終了時は `Dispose()` メソッド内で AppContainer プロファイルの削除およびステージングディレクトリの完全削除が行われます。

しかし、親プロセスがタスクマネージャーから強制終了された場合や、ブルースクリーン、電源断などの異常停止が発生した場合、`Dispose()` は呼び出されず、ステージングされた一時 PDF ファイルや作業データが一時フォルダ内に残存します。機密文書を扱っている場合、端末の共有ユーザーや後続のプロセスから一時フォルダ内のデータが参照されるリスクが残ります。

#### 推奨対策
アプリケーション起動時（[`StartupDiagnostics`](../src/PdfCorrectorium.App/StartupDiagnostics.cs) やメイン初期化時）に、過去のセッションで残された孤立した `%TEMP%\PdfCorrectorium\sandbox` 配下の古い一時ディレクトリ（例: 24時間以上経過したもの、または現在起動中のプロセスに属さないもの）を安全に巡回・削除するクリーンアップ機構を設けることを推奨します。

---

### 【低リスク / 可用性考慮】 3. 外部プロセスによる一時状態ファイルの長期排他ロック時のタイムアウト
- **該当箇所**: [`IsolatedPdfExportService.cs:393-413`](../src/PdfCorrectorium.App/Services/IsolatedPdfExportService.cs#L393-L413)
- **CWE**: CWE-667 (Improper Locking)

#### 詳細とリスク
`dev.223` において、隔離ワーカーが出力状態を通知する [`ExportWorkerStateWriter`](../src/PdfCorrectorium.App/Services/IsolatedPdfExportService.cs#L370) は、Windows の共有競合（エラーコード 5, 32, 33）に対して最大 5 秒間（25ms 間隔）の再試行を行う実装へと強化されました。これにより、親プロセスによる短時間の状態読み取りとワーカーによるアトミックな `File.Move` の競合は解消されました。

しかし、エンドユーザー環境において、リアルタイムアンチウイルスソフトやデスクトップバックアップエージェントなどが、一時ファイル（`export.state.json`）を 5 秒以上にわたって排他ロックし続けた場合、再試行が上限に達し `ExportWorkerStateWriter.WriteAsync` が失敗します。
現在、状態書き込み失敗時も元の例外が stderr 経由で親プロセスに伝達され、無限ハングアップは回避されていますが、出力処理自体が失敗扱いとなります。

#### 推奨対策
現行のエラーハンドリングはフェイルセーフとして正しく機能していますが、進捗通知用の状態書き込み失敗が直ちに PDF 出力全体の致命的エラーとならないよう、進捗更新処理と最終完了通知（`completed`）を分離し、進捗通知の失敗はスキップして処理を継続する設計を維持・徹底することを推奨します。

---

### 【情報 / ガバナンス】 4. 配布バイナリのコード署名（Authenticode）およびリリース準備ゲート
- **該当箇所**: [`tools/TestVersioning.ps1`](../tools/TestVersioning.ps1), `release-readiness.json`
- **CWE**: CWE-347 (Improper Verification of Cryptographic Signature)

#### 詳細
`dev.221` 以降、配布パイプラインには正式公開前の確認ゲート（`release-readiness.json`）が導入され、署名・テスト・依存関係レビュー・独立監査が完了するまで `readyForRelease = false` として外部公開をブロックする安全機構が実装されています。

現在、ローカル開発ビルド（`1.0.0-dev.223`）は未署名（未提供のコード署名証明書待ち）の状態です。Windows SmartScreen や企業エンドポイント防御による誤検知を防止し、バイナリの改ざん検知を保証するため、正式な外部リリースに際しては EV コード署名証明書による Authenticode 署名およびタイムスタンプの付与が不可欠です。

---

### 【情報 / 設計トレードオフ】 5. 墨消し出力における構造ツリー削除とアクセシビリティ（PDF/UA）の両立
- **該当箇所**: [`PdfBookmarkService.cs:733-775`](../src/PdfCorrectorium.App/Services/PdfBookmarkService.cs#L733-L775)
- **CWE**: なし（仕様上のトレードオフ）

#### 詳細
タグ付き PDF において墨消しされたテキストが構造ツリー（`/StructTreeRoot` 内の `/ActualText` や `/Alt`）に残存する情報漏洩（CWE-200）を防ぐため、現行の実装では墨消し適用時に文書全体の構造ツリー（タグ情報）を一律に除去する安全策が採られています。

このアプローチは**機密情報の漏洩を100%確実に防止する観点において完全に正しく、最も安全な設計**です。一方で、構造ツリーが除去されることにより、スクリーンリーダー等によるアクセシビリティ情報（PDF/UA 準拠）は失われます。
UI において「墨消しを行うとタグ構造が除去される」旨の警告が利用者に正しく明示されていることを確認しました。将来的により高度なタグ追跡（墨消し対象外の構造要素のみを再構築する機能）を導入する際は、再構成後の構造ツリーに対する機密情報漏洩検査の継続が必要です。

---

## 4. 評価すべき優良なセキュリティ設計（多層防御アーキテクチャ）

本監査において確認された、PDF-Correctorium の模範的なセキュリティ設計・防御機構を整理します。

```
[UI / メインプロセス] (WPF, 完全整合性)
   │
   ├─► 入力検証レイヤー
   │     ├─ SourcePdfPathPolicy: UNC/ネットワークドライブ/ReparsePointを完全遮断
   │     ├─ SafeFileName: デバイス名(CON,PRN等)・パストラバーサルをサニタイズ
   │     ├─ BoundedReadStream: 実読み取りバイト数制限 (Zip Bomb根絶)
   │     └─ JsonInputBudget: トークン/深さ/オブジェクト数/文字列長の事前逐次スキャン
   │
   ├─► [AppContainer サンドボックス] (WindowsSandboxProcess, Capabilities = 0, Low Integrity)
   │     ├─ WindowsProcessJob: メモリ上限 (2GB/3GB) & ツリー強制終了
   │     ├─ レンダリングワーカー: PDFium による描画処理
   │     └─ QPDF 外部ツール: 構文解析・圧縮・ストリーム更新
   │
   └─► 出力確定・検証レイヤー
         ├─ 墨消し事後検証: PDFium による文字残存ゼロ検証
         ├─ 構造ツリー除去 & 検証: StructTreeRoot/ActualText/Alt の完全パージと再確認
         └─ PdfOutputFileCommitter: アトミック置換 & 占有時バックアップ保護
```

1. **Windows AppContainer による能力数0の極小権限サンドボックス ([`WindowsSandboxProcess.cs`](../src/PdfCorrectorium.App/Services/WindowsSandboxProcess.cs))**:
   - `PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES` を使用し、ネットワーク接続や特権を一切持たない AppContainer トークンを生成。
   - `PROC_THREAD_ATTRIBUTE_HANDLE_LIST` により、子プロセスへの不要な OS ハンドル継承を完全に遮断。
   - 環境変数から `DOTNET_`, `CORECLR_`, `COR_` 等のランタイム・プロファイラ・スタートアップフック変数を自動除去し、CLR インジェクションを抑止。
   - 専用の作業領域（`work`）とランタイム領域（`runtime`）のみに厳格な ACL と Low Integrity ラベルを付与し、作業領域外のファイル読み書きおよび実行バイナリの上書きを OS カーネルレベルで拒否。
2. **物理的ストリーム制限とメモリ保護 ([`BoundedReadStream.cs`](../src/PdfCorrectorium.Core/IO/BoundedReadStream.cs), [`JsonInputBudget.cs`](../src/PdfCorrectorium.Core/IO/JsonInputBudget.cs))**:
   - `ZipArchiveEntry.Length` 等の偽装可能なヘッダ情報を信用せず、展開ストリームの実読み取りバイト数を追跡。
   - JSON デシリアライズ前に `Utf8JsonReader` を用いた事前バジェット検査を行い、悪意ある巨大トークン、深いネスト（最大64）、過剰な配列・オブジェクトを DOM 化前にリジェクト。
3. **確実な墨消しと多重検証 ([`PdfExportService.cs`](../src/PdfCorrectorium.App/Services/PdfExportService.cs), [`PdfBookmarkService.cs`](../src/PdfCorrectorium.App/Services/PdfBookmarkService.cs))**:
   - 墨消し領域をレンダリング画像へ物理的にピクセル描画（塗りつぶし）した上で、QPDF により不要な本文テキストストリームを再圧縮・パージ。
   - 出力後、PDFium を用いて該当領域からテキストが抽出できないことを再検証。
   - 構造ツリー内の代替テキスト（`/ActualText`, `/Alt`）もカタログレベルで完全に削除し、QPDF で事後確認。
4. **徹底した XXE・XML爆弾防御 ([`NdlOcrCompanionService.cs`](../src/PdfCorrectorium.App/Services/NdlOcrCompanionService.cs), [`PdfBookmarkService.cs`](../src/PdfCorrectorium.App/Services/PdfBookmarkService.cs))**:
   - すべての XML 処理において `DtdProcessing = Prohibit`, `XmlResolver = null`, `MaxCharactersInDocument` を設定。
   - NDL OCR XML インポート処理は完全なストリーミング前方パース（`OcrXmlFragmentReader`）を採用し、DOM ツリー展開に伴うメモリ爆発を根本的に防止。
5. **安全なファイル確定と排他制御 ([`PdfOutputFileCommitter.cs`](../src/PdfCorrectorium.App/Services/PdfOutputFileCommitter.cs))**:
   - `File.Replace` によるアトミックな置換を行い、同一ボリューム内の一時ファイル退避を活用。
   - Acrobat 等による占有で置換できない場合でも、生成済み PDF を失わないよう別名退避（リカバリ保存）を行い、利用者に安全に通知。
6. **プライバシー保護ログ ([`DiagnosticLog.cs`](../src/PdfCorrectorium.Infrastructure/DiagnosticLog.cs))**:
   - ログ出力時に Windows ユーザー名（`Environment.UserName`）を自動的に `<user>` へ置換し、サポートログ等への個人情報混入を防止。

---

## 5. 推奨アクションロードマップ

| 優先度 | 項目 | 対象ファイル / 領域 | 推奨対応 |
|:---:|---|---|---|
| **低** | テスト・診断用CLIスイッチの分離 | [`App.xaml.cs`](../src/PdfCorrectorium.App/App.xaml.cs) | Release ビルドにおいてテスト用引数判定を `#if DEBUG` で除外、またはファイル存在判定を先行させる。 |
| **低** | 孤立した一時サンドボックスのクリーンアップ | [`WindowsSandboxProcess.cs`](../src/PdfCorrectorium.App/Services/WindowsSandboxProcess.cs) | 起動時に過去の異常終了で残された `%TEMP%\PdfCorrectorium\sandbox` 配下の古い孤立ディレクトリを安全に削除。 |
| **情報** | 正式公開向けコード署名と最終ゲート検証 | 配布パイプライン、`release-readiness.json` | EV コード署名証明書による Authenticode 署名とタイムスタンプの組み込み、推移依存関係の最新アドバイザリ照会。 |

---

## 6. 結論

PDF-Correctorium（バージョン `1.0.0-dev.223`）は、デスクトップ PDF アプリケーションにおいて最もリスクが高いとされる**ネイティブコードの脆弱性悪用**および**細工された悪意あるファイルによる DoS / メモリ枯渇攻撃**に対して、極めて高度かつ洗練された多層防御（AppContainer 隔離、Job Object、ストリーム制限、事前バジェット検査、物理的墨消し、パス検証）を実装しています。

過去の監査で検出された課題はすべて根本的かつ安全に解消されており、現在のコードベースには**悪用可能な重大・高リスクのセキュリティ脆弱性は認められませんでした**。残る課題は本番リリースに向けた CLI 引数の整理およびコード署名の適用という運用・ガバナンス上のステップのみであり、極めて高いセキュリティ品質を備えていると評価します。
