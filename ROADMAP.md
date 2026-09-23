# PDF Correctorium 開発ロードマップ (Development Roadmap)

[日本語](#japanese) | [English](#english)

<a id="japanese"></a>

## 🎯 プロジェクトのビジョン

**PDF Correctorium（PDFコレクトリウム）の目標は、「日常業務で高価な市販ソフトの代わりに使える、実用的なオープンソースPDF総合エディタ」の実現です。**

現在、PDFの編集や加工を行うためには、高額な年間サブスクリプションや有料の市販ソフトを購入するか、あるいは利便性の高いWeb上のオンライン編集ツールを利用するのが一般的です。

しかし、個人利用・ビジネス利用を問わず、以下のような課題が存在します：
1. **サブスクリプションの費用負担**: 毎年費用がかかり続ける。
2. **クラウドへの情報流出の懸念**: オンラインツールは手軽な反面、**契約書・請求書・個人情報・機密書類が外部サーバーへ送信される**というセキュリティ上の懸念がある。
3. **無料ツールの制限**: ファイル容量上限、1日の処理回数制限、出力ファイルへの広告・透かし混入など。

PDF Correctorium は、**「完全無料 (OSS)」「完全ローカル処理（外部に送信しない安心設計）」「ポータブル（インストール不要）」「元ファイルを壊さない安全設計」** を核とし、安心して手軽に使えるPDFエディタを目指します。

---

## 📊 一般的なデスクトップツールとの機能比較（現状と課題）

本プロジェクトは現在開発途上であり、日常業務の完全な代替ソフトとなるにはまだ多くの機能が不足しています。
現状の到達点（できること）と今後の課題（未対応の機能）を客観的・第三者目線で比較した一覧は以下の通りです。

### 1. 一般的な市販・単機能デスクトップツールとの比較

| 機能・項目 | 一般的な高額商用ソフト | 一般的な有料買い切りソフト | 一般的な単機能無料ツール | **PDF Correctorium (v1.0.0-dev.171)** |
| :--- | :---: | :---: | :---: | :---: |
| **ライセンス・費用** | 高額サブスク（年間数万円） | 有料（買い切り/更新） | 無料 (OSS等) | **完全無料 (Apache 2.0)** |
| **導入形態・ポータブル性** | 要インストール | 要インストール | 要インストール（一部例外あり） | **完全ポータブル（解凍のみ・USB動作可）** |
| **元ファイル保護** | 上書き保存が基本 | 上書き保存が基本 | 上書き保存が基本 | **非破壊プロジェクト管理（.pdfocrproj）** |
| **ページ並べ替え・回転・結合** | ◎ | ◎ | ◎ | **◎（画質無劣化・元ファイル非破壊）** |
| **しおり（目次）作成・編集** | ◎ | ◎ | △（一部対応） | **◎（階層化・プロパティ・表示先設定）** |
| **安全な墨消し（情報消去）** | ◎（上位版のみ） | ◯（有償版） | ✕ | **◎（文字消去＋画像化＋自動検証）** |
| **OCR透明テキストの精密校正** | ✕（枠・送り幅調整不可） | ✕ | ✕ | **◎（文字枠・文字送り幅・日本語縦書き微調整）** |
| **本文テキストの直接編集・修正** | ◎ | ◯〜◎ | ✕ | **✕ 未対応（計画中）** |
| **埋め込み画像の編集・差し替え** | ◎ | ◯〜◎ | ✕ | **✕ 未対応（計画中）** |
| **標準PDF注釈（ハイライト/印鑑/メモ）** | ◎ | ◎ | ✕〜△ | **✕ 未対応（計画中）** |
| **ページの分割・抽出** | ◎ | ◎ | ◎ | **△ 専用機能なし（手動結合・削除で代用可 / 計画中）** |
| **パスワード保護・暗号化設定** | ◎ | ◎ | ◎ | **✕ 未対応（計画中）** |
| **アプリ単体でのOCR文字認識** | ◎（標準内蔵） | ◯〜◎（標準内蔵またはアドオン） | ✕ | **△ 単体実行不可（外部NDLOCR連携のみ / 計画中）** |

### 2. Web上のオンライン編集ツールとの比較

| 比較項目 | Web上のオンライン編集ツール | **PDF Correctorium** |
| :--- | :--- | :--- |
| **利用の手軽さ・対応端末** | **◎ ブラウザのみで即利用可能**。スマホ・タブレット・Mac・Linux問わず動作 | **△ Windows専用**のデスクトップアプリ。ダウンロードが必要 |
| **情報セキュリティ・プライバシー** | ⚠️ ファイルを外部サーバーへアップロードするため、機密情報・個人情報の流出リスクがある | **🛡️ 完全ローカル処理**。PC内で完結するため、社外秘・契約書も安全に処理 |
| **ファイル容量・回数制限** | ⚠️ 「100MBまで」「1日3回まで」等の制限や、有料会員への誘導がある | **⚡ 容量・回数の制限なし**。大きなPDFでも制限なく何度でも利用可能 |
| **ネットワーク依存** | ⚠️ 通信環境が必須。巨大ファイルのアップロード・ダウンロードに時間がかかる | **🌐 完全オフライン動作**。ネットのない環境や隔離されたセキュア環境でも快適に動作 |
| **画質の劣化** | ⚠️ サーバー側で自動圧縮され、写真や図面が劣化することがある | **💎 画質をそのまま維持**。写真やイラストを再圧縮せずきれいなまま保持 |

---

## 🗺️ 開発ロードマップ（計画・検討中の機能）

日常業務で市販ソフトの代わりに使える総合エディタを目指し、以下の機能拡張を計画・検討しています。  
*※各機能の実装順序やリリース時期は、利用ニーズやフィードバック等を踏まえて柔軟に決定・対応していく方針です。*

### 現在の実装機能 (v1.0.0-dev.171)
* [x] **画質無劣化のページ編集**: ドラッグ＆ドロップでの並べ替え、90度回転、不要ページ削除、別PDFからのページ差し込み
* [x] **復元できない安全な墨消し**: PDF描画命令からの文字コード消去（ベクター保持）＋任意形状の高精細画像化＋出力後自動検証
* [x] **しおり（目次）・文書情報編集**: 階層しおりの作成、文書プロパティ、初期表示設定（見開き・連続スクロール）
* [x] **OCR透明テキスト精密校正**: 文字修正、位置・サイズ・角度・送り幅調整、日本語縦書き・読み順整理、校正モード、NDLOCR-Lite連携
* [x] **安心のプロジェクト保存**: ZIP互換形式（.pdfocrproj）、通常モード／ポータブルモード、自動保存・世代バックアップ

---

### 今後の機能拡張・実装候補

#### 📑 文書整理・分割・セキュリティ機能
* [ ] **ページの抽出（切り出し）**: 指定したページ範囲（例: 1-5, 8ページ）だけを新しいPDFとしてワンクリック保存
* [ ] **PDFの分割**: 「1ページずつすべて分割」「指定ページで区切って分割」
* [ ] **白紙ページの挿入**: 任意の場所に空の白紙ページを追加
* [ ] **パスワード保護・暗号化（セキュリティ）**:
  * 閲覧パスワード（文書を開く際のパスワード設定）
  * 権限パスワード（印刷の禁止、テキスト・画像のコピー禁止、編集禁止）
  * 暗号化基盤（qpdf）を活用した強固なセキュリティ
* [ ] **ページ番号・ヘッダー・フッター印字**: 文書の天や地に「1 / 10」などの通しページ番号を一括追加

#### ✏️ 標準PDF注釈・レビュー機能
* [ ] **テキスト注釈（蛍光ペン・下線）**: ハイライト、アンダーライン、取り消し線
* [ ] **テキストボックス・吹き出し（コールアウト）**: 文書上に直接メモを書き込む枠や、矢印付きコメント
* [ ] **電子印鑑・ビジネススタンプ**: 「承認」「回覧」「マル秘」「日付印」などのスタンプ押印
* [ ] **図形描画・フリーハンドペン**: 矢印、長方形、楕円、手描きメモ
* [ ] **注釈の一覧表示とエクスポート**: 追加した注釈の一覧管理、他のPDFソフトとの完全な互換性

#### 🔤 本文テキスト・画像の直接編集
* [ ] **本文テキストの直接修正**: 既存の文字をクリックし、フォント属性を保ったまま誤字脱字を直接打ち直し
* [ ] **画像の差し替え・抽出・リサイズ**: PDF内に埋め込まれたロゴや写真を新しい画像に差し替え、あるいは高画質のまま抽出
* [ ] **透かし（ウォーターマーク）の追加**: 「社外秘」「SAMPLE」などの背景透かしを斜め一括配置

#### 🔍 アプリ内蔵OCRによるワンクリック検索可能PDF化
* [ ] **軽量・高精度OCRエンジンの内蔵**: Tesseract や NDLOCR-Lite 等のエンジンを同梱
* [ ] **ワンクリック検索可能PDF化**: スキャン画像PDFを読み込み、1クリックで透明テキストレイヤーを生成
* [ ] **自動傾き補正・ノイズ除去**: スキャン画像の傾きを自動検知して水平補正

#### ⚡ 最適化・構造修復・フォーム機能
* [ ] **PDFファイルサイズ最適化・軽量化**: 重複オブジェクト統合、高圧縮ストリーム化、画像の指定DPIダウンサンプリング
* [ ] **破損PDFの自動修復**: 壊れたXREFや構文エラーをqpdf自己修復パーサーで再構築して救出
* [ ] **Web表示最適化（リニアライズ）**: ブラウザで1ページ目から瞬時に開けるFast Web View構造化
* [ ] **フォーム（AcroForm）の平坦化**: 入力済み申請書や請求書を固定描画化し、改ざんや文字消失を防止

#### 🛠️ 業務支援・高度な文書操作機能
* [ ] **PDFの比較・差分検証（Diff）**: 2つのPDFの同一ページをピクセルおよび文字単位で比較し変更箇所をハイライト
* [ ] **添付ファイル（Embedded Files）管理**: PDF内への関連データ（Office/ZIP/インボイスXML等）の埋め込み・抽出
* [ ] **N-up（割り付け集約）＆ 小冊子面付け**: 2/4ページを1枚に集約印刷、または中綴じ印刷用ページ並べ替え
* [ ] **見開きページの左右分割**: A3見開きスキャンPDFを無劣化でA4単ページ×2枚に自動分割
* [ ] **メタデータの完全消去（サニタイズ）**: 作成者・編集履歴・XMPメタデータを完全除去（不可逆墨消しと連携）
* [ ] **パスワード保護の一括解除**: 既知パスワードを持つ暗号化PDFの一括復号化・平文保存
* [ ] **スキャン画像の傾き補正（Deskew）**: 手動スライダー/2点基準線指定・自動検出＋無劣化マトリクス回転（CTM）
* [ ] **ページ余白調整・用紙リサイズ**: CropBox調整による無劣化トリミング・自動余白カット、用紙サイズ拡張・とじしろマージン追加

#### 🖼️ 画像処理・ベクター変換・外部連携機能
* [ ] **PDF内の埋め込み画像抽出**: ページ全体ではなく、埋め込まれた写真・ロゴを生データのまま無劣化で一括抽出
* [ ] **ページの画像変換（連番画像書き出し）**: 指定DPI（72〜600）での高品質ラスタライズ（PNG/JPEG/TIFF/WebP）
* [ ] **ページのSVG（ベクター画像）変換**: 拡大しても線や文字がボケないベクターSVG出力（Web埋め込み・CAD/デザイン流用）
* [ ] **画像の差し替え・外部エディタ連携編集**: レイアウト（Matrix）を崩さない画像置換、Photoshopやペイント等との自動同期編集
* [ ] **画像・SVGからのページインポート**: 複数画像（PNG/JPEG等）のドラッグ＆ドロップ一括PDF化、無劣化JPEGパススルー、SVGピュアベクター変換差し込み

#### 📑 Office文書へのエクスポート機能
* [ ] **Office形式への変換・エクスポート**: PDFの文字・表・スライド構造を解析し、再編集可能な Word（.docx）、Excel（.xlsx）、PowerPoint（.pptx）へ完全ローカル・オフライン変換

#### ♿ アクセシビリティ・タグ付きPDF・音声読み上げ機能
* [ ] **タグ付きPDF（Tagged PDF / PDF/UA）作成**: 見出し・段落・表・ヘッダー/フッター除外（Artifact）の論理構造タグ付け
* [ ] **ルビ（ふりがな）タグと発音設定**: `<Ruby>` タグによる親文字・ルビのペアリング、`/ActualText` による難読漢字の読み方指定と2重読み防止
* [ ] **Windows標準音声合成（TTS）連携＆追従ハイライト**: 新世代WinRT自然音声（Ayumi/Haruka/Ichiro/Natural Voice）による朗読、現在読み上げ文のカラオケ風ハイライト・自動スクロール

---

<a id="english"></a>

## 🎯 Project Vision (English)

**The goal of PDF Correctorium is to become a practical, open-source alternative to expensive commercial PDF editors.**

Today, working with PDFs typically means paying for expensive commercial subscriptions, purchasing proprietary desktop tools, or relying on web-based online services.

However, users face significant challenges:
1. **Costly Subscriptions**: Continuous, high annual costs.
2. **Cloud Privacy Risks**: Uploading contracts, financial records, personal data, and confidential business documents to third-party servers presents security and compliance concerns.
3. **Arbitrary Limitations**: File size ceilings, daily usage quotas, and watermarking on free tiers.

PDF Correctorium provides a **100% free (Apache 2.0), fully offline (local privacy), portable, non-destructive** solution for desktop users.

---

## 📊 Functional Comparison with General Tools (Current Status & Gaps)

PDF Correctorium is actively under development. To become a viable daily replacement for commercial PDF software, many common features are still missing. Below is an honest, third-party comparison showing current capabilities alongside areas currently unaddressed.

### 1. vs Commercial & Basic Desktop Software

| Feature / Aspect | Expensive Commercial Suites | Paid Desktop Editors | Basic Free Tools | **PDF Correctorium (v1.0.0-dev.171)** |
| :--- | :---: | :---: | :---: | :---: |
| **License & Pricing** | Expensive subscription (annual) | Paid (one-time or maintenance) | Free (OSS / freeware) | **100% Free (Apache 2.0)** |
| **Portability** | Requires installation | Requires installation | Mostly requires installation | **Fully portable (extract & run, USB ready)** |
| **Original File Safety** | Direct file overwrite | Direct file overwrite | Direct file overwrite | **Non-destructive project packages (.pdfocrproj)** |
| **Page Reordering / Rotation / Merge** | ◎ | ◎ | ◎ | **◎ (Lossless & non-destructive)** |
| **Bookmark (TOC) Editing** | ◎ | ◎ | △ (Limited) | **◎ (Hierarchical, properties, fit views)** |
| **Irreversible Redaction (Sanitization)** | ◎ (High-tier editions only) | ◯ (Paid editions) | ✕ | **◎ (Operator removal + raster fallback + auto verify)** |
| **Invisible OCR Text Proofreading** | ✕ (Boxes/advances uneditable) | ✕ | ✕ | **◎ (Precision boxes, advances & vertical text)** |
| **Direct In-Place Text Editing** | ◎ | ◯–◎ | ✕ | **✕ Not yet supported (Planned)** |
| **Embedded Image Editing / Replacement** | ◎ | ◯–◎ | ✕ | **✕ Not yet supported (Planned)** |
| **Standard Annotations (Highlights/Stamps)** | ◎ | ◎ | ✕–△ | **✕ Not yet supported (Planned)** |
| **Page Splitting / Burst Extraction** | ◎ | ◎ | ◎ | **△ No dedicated tool yet (Planned)** |
| **Password Protection & Encryption** | ◎ | ◎ | ◎ | **✕ Not yet supported (Planned)** |
| **Standalone OCR Engine** | ◎ (Built-in) | ◯–◎ (Built-in / add-on) | ✕ | **△ Requires external NDLOCR (Planned)** |

### 2. vs Web-Based Online Tools

| Comparison Aspect | Web-Based Online Tools | **PDF Correctorium** |
| :--- | :--- | :--- |
| **Accessibility & Platform** | **◎ Zero install, runs in any browser** on Windows, Mac, Linux, and mobile | **△ Windows desktop application only**, requires downloading |
| **Data Privacy & Security** | ⚠️ Uploads documents to external third-party cloud servers (privacy risk) | **🛡️ 100% Local Execution**. Documents never leave your PC |
| **Quotas & Limitations** | ⚠️ Daily caps, file size ceilings (e.g. 100MB), queues, or paywalls | **⚡ Unlimited Usage**. No size caps, page count ceilings, or artificial delays |
| **Network Dependency** | ⚠️ High-speed internet required; upload/download latency for large files | **🌐 Fully Offline**. Operates immediately in air-gapped or secure environments |
| **Image Compression** | ⚠️ Frequently recompresses files, reducing image/drawing sharpness | **💎 Lossless Quality**. Existing raster and vector streams are preserved |

---

## 🗺️ Development Roadmap & Planned Feature Areas

The project plans to explore and implement the following feature areas to evolve into a practical everyday PDF editor.  
*Note: Implementation sequence and release schedules will be adapted flexibly based on technical feasibility and user needs.*

### Current Features (v1.0.0-dev.171)
* [x] **Lossless Page Editing**: Reorder, 90-degree rotate, delete, and merge pages without image degradation.
* [x] **Irreversible Redaction**: Text stream operator removal + high-DPI rasterization fallback + post-export verification.
* [x] **Bookmark & Document Properties**: Hierarchical bookmarks, document metadata, and initial view preferences.
* [x] **Invisible OCR Text Proofreading**: Precise bounding boxes, character advances, vertical flow, and NDLOCR-Lite import.
* [x] **Project Package Management**: ZIP-compatible (.pdfocrproj) format, normal/portable modes, and autosave.

---

### Planned Feature Candidates

#### 📑 Document Organization, Splitting & Security
* [ ] **Page Extraction**: Extract selected page ranges directly into new PDFs.
* [ ] **PDF Splitting**: Split by page count or custom boundary ranges.
* [ ] **Blank Page Insertion**: Insert blank pages anywhere in the document.
* [ ] **Password Protection & Encryption**: Open passwords, permission restrictions (printing, copying), and qpdf-based encryption.
* [ ] **Bates & Page Numbering**: Batch-apply headers, footers, and continuous page numbers.

#### ✏️ Standard PDF Annotations & Review
* [ ] **Text Annotations**: Highlights, underlines, strikethroughs.
* [ ] **Text Boxes & Callouts**: Floating text notes and arrow callout boxes.
* [ ] **Business Stamps & Seals**: Approval, circulation, confidential, and date stamps.
* [ ] **Freehand & Geometric Shapes**: Arrows, rectangles, ellipses, and hand-drawn pen notes.
* [ ] **Annotation Listing & Export**: Full `/Annots` standard compatibility.

#### 🔤 Direct Text & Image Editing
* [ ] **Direct Text Editing**: Edit existing text while maintaining font attributes.
* [ ] **Image Replacement & Extraction**: Replace logos/images or extract original quality bitmaps.
* [ ] **Watermarks**: Batch-apply background diagonal watermarks (e.g., "CONFIDENTIAL").

#### 🔍 Standalone Built-in OCR
* [ ] **Embedded Offline OCR Engine**: Bundle lightweight OCR engines (Tesseract / NDLOCR-Lite).
* [ ] **One-Click Searchable PDF**: Automatically generate invisible text layers from scanned PDFs.
* [ ] **Automated Deskew & Clean-up**: Detect page orientation skew and auto-straighten scans.

#### ⚡ Optimization, Repair & Forms
* [ ] **PDF File Size Optimization**: Deduplicate objects, generate object streams, and downsample high-DPI images.
* [ ] **Corrupted PDF Auto-Repair**: Salvage damaged xref tables and malformed streams using qpdf recovery parsing.
* [ ] **Web Streaming Optimization (Linearize)**: Fast Web View linearization for instant in-browser display.
* [ ] **Form (AcroForm) Flattening**: Permanently flatten interactive form fields to prevent tampering and missing fonts.

#### 🛠️ Workflow, Comparison & Advanced Utilities
* [ ] **Visual & Text PDF Comparison (Diff)**: Compare two PDF versions with pixel-level and text-coordinate difference highlighting.
* [ ] **Embedded File Attachments**: Attach or extract supplementary files (Office docs, ZIP, Factur-X/ZUGFeRD invoice XML).
* [ ] **N-up Page Layout & Booklet Imposition**: Print 2/4 pages per sheet or rearrange pages for saddle-stitch booklet binding.
* [ ] **Spread Page Split**: Losslessly split 2-page scans (e.g. A3) into two single pages (A4) via CropBox adjustment.
* [ ] **Metadata Sanitization & Anonymization**: Completely strip authors, editing history, and XMP metadata (paired with redaction).
* [ ] **Batch Password Removal (Decryption)**: Batch-decrypt password-protected PDFs with known credentials.
* [ ] **Scan Deskew (Orientation Leveling)**: Manual slider / 2-point guideline calibration & auto-deskew with lossless Matrix (CTM) rotation.
* [ ] **Page Margins & Sheet Resizing**: Lossless CropBox trimming, auto-white-margin crop, and sheet expansion / binding gutter margins.

#### 🖼️ Image Utilities, Vector Conversion & External Editing
* [ ] **Embedded Image Extraction**: Extract raw photos and logos at original resolutions without quality loss.
* [ ] **Page to Image Rendering**: Export pages as high-resolution images (PNG, JPEG, TIFF, WebP) at custom DPI.
* [ ] **Page to Vector SVG Conversion**: Infinitely scalable SVG vector conversion for web embedding and CAD/illustration reuse.
* [ ] **Image Replacement & External Editor Round-Trip**: In-place image swapping preserving layout matrices, plus auto-sync editing with external tools (Photoshop, Paint, etc.).
* [ ] **Image & SVG Page Import**: Drag-and-drop batch conversion of images (PNG, JPEG passthrough) and crisp SVG vectors into native PDF pages.

#### 📑 Office Document Export
* [ ] **Office Export (Word / Excel / PowerPoint)**: Reconstruct text flow, tabular data, and slide geometry into editable .docx, .xlsx, and .pptx files fully offline.

#### ♿ Accessibility, Tagged PDF & Audio Reading (TTS)
* [ ] **Tagged PDF (PDF/UA) Creation**: Logical structural tagging for Headings (H1–H6), Paragraphs, Tables, and Header/Footer suppression (Artifacts).
* [ ] **Ruby (Furigana) & Phonetic ActualText**: `<Ruby>` structural tags and `/ActualText` pronunciation mapping to prevent duplicate vocalization.
* [ ] **Windows Native TTS & Karaoke Highlight**: Modern WinRT Natural Voice integration (Ayumi/Haruka/Ichiro/Neural) with real-time sentence highlighting and auto-scrolling.
