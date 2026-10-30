# 追加セキュリティ確認（2026-10-12）

対象: 現在のソース 1.0.0-dev.218 / 数値版 1.0.0.218。

前回のGemini指摘への是正後、プロジェクト参照、ページ正規化、OCR JSON、画像キャッシュ、ワーカー起動を追加確認した。今回、アプリケーション／ビルドコードは変更していない。監査用プログラムとこの記録のみ追加した。第三者ライブラリ全体の脆弱性調査や全機能の網羅的監査ではない。

## 検証対象と証跡

- 現ソースのfingerprint: `D2E91D1154F884BC632DC437875DDF3D8956E3EE845D57ACD415350B5025CEA2`。Windows PowerShellで取得し、dev.218配布物のbuild-info.jsonと一致した。
- 再現プログラム: `outputs/SecurityReview-20261012/Program.cs`、`Probe.csproj`。
- 実行記録: `outputs/SecurityReview-20261012/probe-results.txt`。
- 調査した主要ソースのSHA-256: `outputs/SecurityReview-20261012/source-hashes.json`。
- 使用DLLは前回の検証用配布コピー内のApp/Core/ProjectFormat。3つとも正式dev.218配布物とSHA-256一致を確認した。`binary-hashes.json`に保存。
- コマンド: リポジトリルートから `dotnet run --project outputs/SecurityReview-20261012/Probe.csproj -c Release --configfile NuGet.Config`。終了コード0。
- テストは合成データのみ。10万ページ参照の生成と2048×2048画像に抑え、OOM、巨大画像、実際の外部共有への接続・認証は実施していない。

## 1. 旧形式のページ数から、ファイル容量に見合わない配列を生成する（優先度: 高）

**条件:** 攻撃者が作った旧形式プロジェクトを利用者が開く。形式1.1で再現した。

`ProjectPackageService.OpenAsync`（338–341行）は `SourcePdf.PageCount` を採用する。`ProjectPageSequence.Normalize`（22–28行）はその値を `Enumerable.Range(...).Select(...).ToArray()` に渡す。ページ数の実用上限がなく、`ValidatePageSequence`（865行）は形式1.4未満では検査を省略する。UIはValidateAsync後にOpenAsyncを呼ぶため、通常の開く操作から到達する。

**再現:** 1,112バイトのZIPで `PageCount=100000`、空のPages/PageSequenceを指定。ValidateAsyncは有効と判定し、OpenAsyncは10万件のページ参照を生成した。その呼び出しの累積マネージ割当量は4,815,200バイトだった。これはピークメモリではない。

**影響:** 小さなプロジェクト内の整数だけで大量の配列・オブジェクト生成を要求できる。より大きな値によるメモリ枯渇・操作不能が懸念される。巨大値でのクラッシュ自体は実行していない。

**是正:** 旧形式も含め、正規化前にページ数・OCRページ番号の最大値・ページ列件数の上限を検査する。可能な段階で実PDFページ数とも照合する。正規化の途中でキャンセルできるようにする。

## 2. 旧形式の絶対パスから、確認前にネットワーク共有へアクセスし得る（優先度: 高、環境依存）

**条件:** 形式1.0/1.1のプロジェクトにUNCパスを含め、SMB等への接続が可能なWindowsで開く。

旧形式の `AbsolutePathHint` は保持される。`EnsureSourceReferenceIsSafe` はサイズとSHA-256の書式だけを検査する。`ResolveExternalSourcePath`（1046行）は絶対パスに対して `File.Exists` を呼ぶ。`MainWindowViewModel.LoadProjectAsync`（2004行）は元PDF再選択の確認画面より前に `VerifySourceAsync` を呼び、このパス解決へ到達する。

**確認:** `AbsolutePathHint=\\audit.invalid\share\document.pdf` を持つ形式1.1プロジェクトがValidateAsyncを通過した。外部パス解決は呼んでいない。実際の接続や認証情報送信を確認したとの意味ではない。

**影響:** ソース経路から、開くだけで攻撃者指定先へのネットワーク接続を試みる可能性があると判断する。OSの認証・ファイアウォール設定によってはNTLM認証の応答送信につながり得る。平文パスワードが必ず漏れるとの主張ではない。Microsoftも、悪意あるSMBサーバーへのNTLM要求を防ぐためのブロック機能を説明している: [SMB NTLM blocking](https://learn.microsoft.com/en-us/windows-server/storage/file-server/smb-ntlm-blocking)。

**是正:** 未信頼プロジェクトのパスをFile.Exists等へ渡す前に、UNC・デバイスパス等を分類する。ネットワーク参照は明示的な利用者操作で選択した場合のみ許可する。旧形式の互換性は、読み込み時の自動接続よりPDF再選択で維持する。

## 3. サムネイルの圧縮後サイズだけを制限している（優先度: 中）

**条件:** サムネイルキャッシュを含むプロジェクトを開き、ページ一覧のサムネイルを表示する。

パッケージ読取時の上限は1件4MiB等だが、`MainWindowViewModel.DecodeThumbnail`（3750–3759行）は画素数・寸法を調べず、`BitmapDecoder.Create(..., OnLoad)` で主プロセスに読み込む。デコード時の縮小指定もない。拡張子が.jpgでもデコーダーは内容を判定する。

**再現:** `thumbnails/page-1.jpg` に16,400バイトのPNGを格納し、実際のキャッシュ読取とDecodeThumbnailを実行。戻り値は2048×2048のままで、CopyPixelsで16,777,216バイト（16MiB）の画素を取得できた。

**影響:** ZIP内のバイト上限を守る画像でも、デコード後のメモリ消費が大きい。複数のキャッシュ画像や過大寸法の画像によるメモリ圧迫・操作不能が懸念される。異常寸法でのクラッシュや画像デコーダーの任意コード実行は検証していない。

**是正:** 幅・高さ・総画素数とデコード後の合計メモリに上限を設ける。表示用の寸法に縮小して読み込む。古いキャッシュは拒否して元PDFから再生成できるため、厳しい制限を導入しやすい。

## 4. OCR JSONにはXML相当の構造・総文字数制限が適用されていない（優先度: 中）

**条件:** 不正なOCR JSONを手動で取り込む、または同名PDFの付随JSONとして自動取り込みする。

`NdlOcrCompanionService.ReadJsonAsync`（238行）は最大256MiBをJsonDocumentとして全量解析する。`MaximumTotalTextCharacters` はXML経路でのみ検査される。JSONの行数は有効な座標を持つ行だけがカウントされる（259–263行）。さらに、pages配列とboundingBoxの点数・各点の要素数にも個別上限がない。

**再現:**

- `MaximumTotalTextCharacters=8` に設定しても、12文字のJSON OCR行が正常に取り込まれた。
- `MaximumLineCount=2` に設定し、無効な行 `{}` を10万件含む300,016バイトのJSONを入力すると、拒否されず空ページとして取り込まれた。呼び出しの累積マネージ割当量は8,890,720バイト。ピークメモリではない。

**影響:** 実バイト数制限は機能しているが、DOM構築と配列展開のメモリ増幅、および構造に比例するCPU消費は残る。前回XMLを改善したが、JSONにも同様の資源制限が必要。デフォルト上限でのOOMは再現していない。

**是正:** JSONも逐次読み込みで構造上限を検査し、無効な行を含む入力レコード数、点数、総本文文字数、全体の処理量を制限する。XMLとJSONで同じ意味の上限を適用する。

## 防御上の補足（新たな侵入手段を実証したものではない）

PDFium/qpdfの別プロセス化とWindows Job Objectは、主に障害・時間・メモリ量を制御する。現在の起動にはAppContainerや制限トークンによる権限分離がなく、ネイティブライブラリに任意コード実行の欠陥があった場合のファイル／ネットワークアクセスを防ぐ境界にはなっていない。Job Objectの資源制限とAppContainerによるアクセス制限は別の仕組みである: [Job Objects](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects)、[Launch an AppContainer](https://learn.microsoft.com/en-us/windows/win32/secauthz/implementing-an-appcontainer)。

今回、PDFium/qpdfの未修正CVEや悪用可能な任意コード実行は確認していない。ワーカーの権限分離は中長期の防御改善候補として扱う。

是正は、1と2を先に、3と4を次に進めるのが妥当。今回の結果は前回の修正が無効という意味ではなく、前回の対象外だった入力経路・展開後資源の追加確認である。

## 後続の是正実装

上記はdev.218の監査時点の結果。4件は[dev.220のリリース記録](releases/v1.0.0-dev.220.md)のとおり是正した。dev.219を中間検証済み成果物として保持し、直接選択したPDFの内包保存とパスに限定したセッション許可を仕上げる際にリビジョンを進めた。最終版の契約38件、ソース／配布セキュリティ85件、配布ページ履歴44件、ソース／配布永続化61件、配布版管理18件が通過した。監査時の再現プログラム・旧版DLL・記録は書き換えていない。
