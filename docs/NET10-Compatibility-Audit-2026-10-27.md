# .NET 10 互換性監査（2026-10-27）

> 以下はdev.247を対象にした移行前監査の記録です。その後、dev.248で更新に着手し、dev.249で診断・連続表示の修正と最終配布検証を行っています。現在の採用版・結果は[dev.249の記録](releases/v1.0.0-dev.249.md)を参照してください。本文中の「現行」は監査時点を指します。

## 結論

現行 dev.247 は、限定的な修正で .NET 10 へ移行できる見込みです。アプリの処理方式や保存形式を作り直す必要は、今回の検証では見つかりませんでした。ただし、そのまま対象フレームワークを変えるだけでは、契約テストのコンパイルと配布DLLのハッシュ検証で失敗します。

現行ソースと配布済みアプリは変更していません。実験は独立したコピーで行いました。校正モードの既存診断の失敗は、追跡調査で画面初期化不足と判明しました。.NET 8の診断コピーでは準備処理を整えて69項目が2回成功しましたが、現行ソースへの正式反映と.NET 10での再確認は今後の作業です。「全機能の .NET 10 対応が完了した」という判定ではありません。

.NET 8 のサポート終了は **2026-11-10**、.NET 10 LTS は **2028-11-14** です。移行先は .NET 10 が適切です。[Microsoft サポートポリシー](https://dotnet.microsoft.com/en-us/platform/support/policy)

## 対象と検証環境

| 項目 | 確認内容 |
| --- | --- |
| 現行リポジトリ | `C:\Users\katsu\Documents\Codex\PDF-Correctorium` |
| 現行版 | `Directory.Build.props` の dev.247 / 1.0.0.247 |
| 現行対象 | 本体・診断2件が net8.0-windows7.0、共通3ライブラリ・契約テストが net8.0 |
| SDK | global.json は 10.0.302 / latestFeature、実際の選択は **10.0.403** |
| 実行ランタイム | 現行の比較試験は **8.0.33**、移行実験は **10.0.14** |
| OS | このPCの Windows build 26220.9739 / 25H2。別OS・別ハードウェアでは未実施 |
| 調査範囲 | 7プロジェクト、C# 209ファイル、XAML 30ファイル、PowerShell 10ファイル、ルート構成、依存ロック、配布物、設計・利用案内 |
| 診断コピー | `outputs/net10-compatibility-audit-20261027-114925/probe` |
| 実験版の識別 | コピー内だけ dev.248。正式な次版・認証済み配布物ではない |
| 診断用発行先 | `outputs/net10-compatibility-audit-20261027-114925/publish` |

SDK 10 でビルドしていることと、アプリが Runtime 10 を使用することは別です。現行の runtimeconfig は NETCore / WindowsDesktop の 8.0.0 を要求しており、SDK導入だけでは移行されません。[バージョンの選択規則](https://learn.microsoft.com/en-us/dotnet/core/versions/selection)

実験版の runtimeconfig は両フレームワークの 10.0.0 を要求しています。メイン画面と AppContainer 内の子プロセスで、実際に `10.0.14/coreclr.dll` がロードされたことも記録しました。WPFネイティブ部品も WindowsDesktop 10.0.14 でした。

## 移行時に修正が必要な箇所

### F1：対象フレームワークを7プロジェクトで統一する

| ファイル | 現行 → 移行候補 |
| --- | --- |
| src/PdfCorrectorium.App/PdfCorrectorium.App.csproj | net8.0-windows7.0 → net10.0-windows7.0 |
| src/PdfCorrectorium.Core/PdfCorrectorium.Core.csproj | net8.0 → net10.0 |
| src/PdfCorrectorium.Infrastructure/PdfCorrectorium.Infrastructure.csproj | net8.0 → net10.0 |
| src/PdfCorrectorium.ProjectFormat/PdfCorrectorium.ProjectFormat.csproj | net8.0 → net10.0 |
| tests/PdfCorrectorium.ContractTests/PdfCorrectorium.ContractTests.csproj | net8.0 → net10.0 |
| tests/PdfCorrectorium.ExportDiagnostics/PdfCorrectorium.ExportDiagnostics.csproj | net8.0-windows7.0 → net10.0-windows7.0 |
| tests/PdfCorrectorium.TextDiagnostics/PdfCorrectorium.TextDiagnostics.csproj | net8.0-windows7.0 → net10.0-windows7.0 |

共通ライブラリを net8.0 のまま net10.0 アプリから使用すること自体は可能ですが、今回の「全体移行」では検証基準を統一する方が管理しやすいです。

**後ろの診断2プロジェクトは現在の .sln に含まれていません。** ソリューションビルド成功だけでは、その2件の互換性を確認できません。個別の Release ビルドを検証手順へ明記するか、ソリューションの管理対象へ加える必要があります。今回は個別ビルドも実施しました。

global.json は既に .NET 10 を選ぶ構成で、SDKのメジャー変更は不要です。実験では Windows のターゲット接尾辞を維持したままコンパイルできました。`windows7.0` はAPIのターゲット情報であり、Windows 7で動作・サポートできるという意味ではありません。接尾辞や古い supportedOS 宣言の整理は、OSサポート方針と合わせて扱います。

### F2：契約テストの CA2022 を修正する

場所：`tests/PdfCorrectorium.ContractTests/Program.cs:1289`

キャンセルを確認する試験で、`ReadAsync` の戻り値を無視しています。.NET 10 では CA2022 が既定で警告となり、このリポジトリの TreatWarningsAsErrors により **Releaseソリューションビルドが1エラーで停止**しました。[CA2022 の規則](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca2022)

修正候補は、キャンセル試験の意図を保ち、次のように完全読込APIを使用することです。

```csharp
try { await cancelled.ReadExactlyAsync(new byte[1], cancellation.Token); }
catch (OperationCanceledException) { cancellationObserved = true; }
```

コピー内だけでこの修正を行い、**警告0・エラー0、契約テスト58件成功**を確認しました。全体の警告抑制は不要です。これは製品側で読込欠落が発生したという検出ではなく、試験コードの意図を解析規則に合わせて明示する修正です。

### F3：選択されるDLLに合わせて依存ロックを更新する

パッケージ版を変更しなくても、TFMによって選択DLLが変わります。

| パッケージ | 固定版 | .NET 8での資源 | .NET 10での資源 | 対応 |
| --- | --- | --- | --- | --- |
| Dirkster.AvalonDock | 4.74.1 | lib/net5.0-windows7.0 | lib/net10.0-windows7.0 | 本体DLLの配布ハッシュを更新 |
| RapidOcrNet | 4.2.0 | lib/net8.0 | lib/net10.0 | 配布ハッシュを更新 |
| System.Numerics.Tensors | 9.0.0 | lib/net8.0 | lib/net9.0 | 配布ハッシュを更新 |
| AvalonDock 日本語資源 | 上記パッケージ内 | net5.0側の ja DLL | net10.0側の ja DLL | 本体と同じTFMから配布・確認 |

現行ロックのまま `GetBuildVersion.ps1 -PublishDirectory` を実行すると、**Published dependency hash mismatch: RapidOcrNet.dll** で失敗することを再現しました。

修正対象は `DEPENDENCIES.lock.json` の published file ハッシュです。NuGetアーカイブの版・ハッシュを理由なく変更する必要はありません。コピー内で3つのハッシュを正しい採用DLLに合わせると、GetBuildVersion と published TestVersioning（18件）は通りました。

`ja/AvalonDock.resources.dll` もバイト列が変わっています。現行ロックはこのファイル自体のハッシュを列挙していないため、移行時には日本語資源も追加の確認対象、できれば明示的なロック対象とします。本体と古い言語DLLを混在させず、**新規出力フォルダへ発行**してください。SatelliteResourceLanguages=ja と現在の配置は維持できます。

Tensors 9.0.0 の net9.0 DLL が選ばれることは、.NET 9 Runtime の別途インストールを意味しません。実際のOCRは Runtime 10 で動作しました。ただし、互換性の確認とパッケージ自体の保守・脆弱性レビューは別であり、正式移行時に保守版への更新要否を確認します。

### F4：利用条件・設計書・リリース案内を更新する

必須の更新対象は、READMEの .NET 8 バッジと互換性基準の説明、wiki/Home.md の開発基盤、新規リリースノートの起動要件、関連する配布・プラットフォーム設計です。

framework-dependent 配布を維持する場合、利用者には **.NET 10 Desktop Runtime（x64）** が必要になります。一般の .NET Runtime や ASP.NET Runtime の案内だけでは不足します。旧 dev.199 のリリース文書など、歴史的に .NET 8 が正しい箇所は書き換えません。

現在の「Windows 10 / 11」という広い記述も、サポート中のOS・版・エディションを踏まえて明確にします。Microsoftの .NET 10 対応表はOSライフサイクルに依存しており、すべてのWindows 10を一括してサポート対象とする説明は避けます。[公式対応OS](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)

`app.manifest` の Windows 7 / 8 / 8.1 宣言と csproj のプラットフォーム情報も、実際の対応OSと区別して整理します。現時点では、これらが .NET 10 のビルドを妨げる問題は出ていません。Windows SDK依存が増える接尾辞変更を、移行のついでに無検証で行う必要はありません。

### F5：検証にRuntimeの一致確認を追加する

`GetBuildVersion.ps1` は版数・マニフェスト・DLLのハッシュを確認しますが、runtimeconfig の TFM や要求する NETCore / WindowsDesktop のメジャーを検証していません。

移行後は、次を自動検証するのが適切です。

- 全プロジェクトが意図したTFMであること。
- 配布物の runtimeconfig が net10.0、および両フレームワークの 10.0.x を要求すること。
- PDF / OCR ワーカーも実際に Runtime 10 で起動すること。
- .NET 8が無く、.NET 10 Desktop Runtimeのみの通常ユーザー環境で起動できること。

今回、前3項目は構成読取・実ビルド・実行モジュールで確認しました。最後の「Runtime 10のみ」のクリーン環境は未検証です。自動検証の追加は今後の実装候補であり、現行の本体スクリプトには追加していません。

### F6：校正モードの既存診断を修復する

場所：`src/PdfCorrectorium.App/App.ReviewDiagnostics.cs:139`

画面ルート配下から、ItemsSourceが一致するComboBoxを `Single` で探す試験が、**Sequence contains no matching element** で停止しました。現行 dev.247 の認証済みバイナリを別フォルダへコピーし、.NET 8でも同じ行の失敗を再現しました。

当初、名前による直接取得とApplyTemplateだけを試した追加実験では、後続の「閉じたドロップダウンのローカライズ表示」確認にも失敗しました。

その後の.NET 8での追跡調査により、診断がWindow.Show / Loaded処理を通さず、未生成のドッキング内の描画部品を検査していたことを確認しました。失敗時も選択値・6件の選択肢・Bindingは正常でした。名前での取得とApplyTemplateだけでは測定・子要素生成が終わらず、表示文字を確認できていませんでした。

通常と同じ画面初期化と診断用設定の隔離だけを整えた.NET 8コピーでは、期待値を緩和せず**69項目が2回すべて成功**しました。今回の失敗は診断の準備不足であり、校正機能本体の不具合はこの試験では再現しませんでした。現行ソースは未変更で、修正を正式反映した後の.NET 10での再確認は未実施です。詳細は[校正診断の原因調査](Review-Mode-Diagnostic-Investigation-2026-10-27.md)に記録しました。旧失敗の証跡も保持しています。

## 現行の版数・構成で.NET 10の試験を通った範囲

以下は.NET 10上での互換性についての結果です。ライブラリの保守更新は別途判断します。PDFium・qpdfの更新候補は末尾の補足に記録しました。

| 領域 | 確認結果 |
| --- | --- |
| PDFium 154.0.8035 | 同じネイティブDLL。実際の描画・文字抽出・リンク・出力試験を通過 |
| qpdf 12.4.1 | 同じネイティブDLL/EXEと配置。隔離実行・子プロセス・出力検証を通過 |
| ONNX Runtime Managed / Native 1.29.0 | 同じ採用資源。モデルの実推論・再利用を通過 |
| SkiaSharp / Win32 Native 3.119.1 | 同じ採用資源。画像切り出しとOCRを通過 |
| Clipper2 2.0.0 | netstandard2.0資源を継続使用。関連出力試験を通過 |
| 日本語OCRモデル・辞書・NotoSansJP | ハッシュ・配置の変更不要。認識と出力試験で使用 |
| NDLOCRデータの取り込み | .NET専用エンジンの同梱ではなくデータ読取。XML/JSONの境界・文字コード等のセキュリティ試験を通過 |
| AppContainer / Job Object | 通常権限へのフォールバックを追加せず、Runtime 10でPDF/OCRを実行できた |
| バックグラウンド処理 | 非同期・取消・プロセス再利用の既存経路を維持。方式変更の必要は未検出 |
| 保存形式 | formatVersion 1.12 / minimum reader dev.243 / settings 21 を、この移行だけで上げる必要は未検出 |
| 配布構成 | components / runtimes / ja の現在の配置を維持できる |
| バージョン導出 | Directory.Build.props / targetsの運用は継続可能。実装する際はその時点の値から新リビジョンへ進める |

net8.0向けDLLを.NET 10から使用しても、実行ランタイムが .NET 8になるわけではありません。今回ONNX・Skiaの実処理でも確認しています。全パッケージを一斉に更新する理由はありません。

## .NET 9 / 10 の破壊的変更との照合

.NET 8からの移行では .NET 9の変更も含めて確認しました。[.NET 9変更一覧](https://learn.microsoft.com/en-us/dotnet/core/compatibility/9.0)、[.NET 10変更一覧](https://learn.microsoft.com/en-us/dotnet/core/compatibility/10.0)

| 変更 | ローカル確認・判定 |
| --- | --- |
| WPFの空Grid定義がビルドエラーになる | XAML30件をXMLとして確認。空のRowDefinitions / ColumnDefinitionsは0件。実コンパイルも通過。[公式説明](https://learn.microsoft.com/en-us/dotnet/core/compatibility/wpf/10.0/empty-grid-definitions) |
| 不正なDynamicResourceの型が実行時エラーになる | ColorにColorKey、BrushにBrushKeyを使う現行の参照を確認。画面・アイコン・主要ダイアログの診断で関連クラッシュ未検出。[公式説明](https://learn.microsoft.com/en-us/dotnet/core/compatibility/wpf/10.0/dynamicresource-crash) |
| GetXmlNamespaceMapsの型変更 | 製品ソースに直接使用なし。AvalonDockはNET10向けアセットでドッキング診断を通過。[公式説明](https://learn.microsoft.com/en-us/dotnet/core/compatibility/wpf/9.0/xml-namespace-maps) |
| DLL探索規則の変更 | AssemblyDirectoryだけを指定する独自探索コードなし。PDFium / ONNX / Skiaを実ロード。現行は単一ファイル配布ではない。[公式説明](https://learn.microsoft.com/en-us/dotnet/core/compatibility/interop/10.0/search-assembly-directory) |
| CET対応apphost | .NET 10 apphostとネイティブライブラリの試験でプロセス異常終了は未検出。CET強制端末は別途必要。セキュリティを下げる設定は追加しない。[公式説明](https://learn.microsoft.com/en-us/dotnet/core/compatibility/interop/9.0/cet-support) |
| BinaryFormatterの無効化 | 製品ソースに使用なし。互換パッケージを追加する必要なし |
| System.Text.Jsonのメタデータ名衝突検証 | JsonPolymorphic / JsonDerivedType / ReferenceHandlerの独自設定は未使用。プロジェクト・設定・IPCの実試験を通過。[公式説明](https://learn.microsoft.com/en-us/dotnet/core/compatibility/serialization/10/property-name-validation) |
| ZIPフラグ・UTF-8名の扱い変更 | 契約の不正ZIP・上限検査と、プロジェクトの両ランタイム読込を確認。ZIP全体のバイト一致を互換性条件にはしない |
| 環境変数によるruntime設定の優先 | ワーカーはDOTNET_ / CORECLR_ / COR_の継承を除去する現行方針を維持。取消・隔離の実試験を通過 |
| 浮動小数点→整数変換、オーバーロード等 | 7プロジェクトを警告エラー化のままコンパイル。座標・レンダリング・不正寸法の既存試験で移行固有問題は未検出 |
| WinForms / System.Drawing / ASP.NET / EF等 | 現行プロジェクトに該当フレームワーク・直接利用なし。今回の移行対象外 |
| NuGet pruning / deps.jsonの省略 | System.Memoryが解決結果から省かれるが現行配布に実DLLはなく、起動・推論・検証は成功。deps.jsonを旧版から手で持ち込まない |
| NuGetAuditModeの既定all | 実際の復元設定でもall。オフラインソースだけでは最新脆弱性情報の照会完了を保証しないため、正式公開時は別途レビューが必要。[公式説明](https://learn.microsoft.com/en-us/dotnet/core/compatibility/sdk/10.0/nugetaudit-transitive-packages) |

.NET 10の既知の問題も確認しました。10.0.12の64コア超端末に関するGC問題は10.0.13で修正予定とされており、今回の10.0.14より前の条件です。新規配布案内では最新サービス版を使用し、古い10.0.0のままでよいとは案内しません。構成バインダーやLinuxコンテナ等の既知問題は、このWPF構成には直接該当しません。[公式の既知問題](https://github.com/dotnet/core/blob/main/release-notes/10.0/known-issues.md)

## 実行した検証と結果

### ビルド・配布検証

| 検証 | 結果 |
| --- | --- |
| TFM変更のみのRelease solution build | 失敗：CA2022が1件 |
| キャンセル試験1行を直したRelease solution build | 警告0・エラー0 |
| ExportDiagnostics / TextDiagnostics個別Release build | ともに警告0・エラー0 |
| dotnet testの契約ランナー | 58件成功。0件テストの見かけの成功ではない |
| 診断用framework-dependent publish | 成功。通常のBuildPortable配布作成は未実施 |
| 元の依存ロックによる配布検証 | 想定どおり失敗。TFM変更によるDLL差分を検知 |
| コピー内の3ハッシュ更新後のGetBuildVersion | 成功 |
| TestVersioning | source 17件、published 18件成功 |
| アイコン・footer規則 | 現行ソース側352件成功。移行のための再デザイン不要 |

### アプリの診断

同じ.NET 10診断バイナリで19系統を順番に実行しました。**18系統成功、校正モード1系統失敗**です。記録された成功assertionは5,196件で、文書構成の集約PASSはこの件数に含めていません。

| 系統 | 成功チェック数 / 結果 |
| --- | --- |
| OCRプロパティ | 92 |
| 文書UI・ダイアログ | 208 |
| キーボード・アクセスキー | 3,581 |
| OCR注意事項・一覧 | 142 |
| RapidOCRの実認識・辞書・適用 | 186 |
| 類似語・共有辞書 | 103 |
| 設定 | 116 |
| パネルの閉じる・再表示 | 89 |
| AvalonDock | 18 |
| プロジェクト保存・復元 | 61 |
| ページ履歴 | 44 |
| OCR描画・文字だけ表示・該当箇所表示 | 41 |
| 文書構成・日本語装飾・復旧発見 | 集約PASS / result.txt |
| TXT・CSV・ページ画像等 | 44 |
| リンク編集・自動リンク・一括墨消し等 | 277 |
| 画像による文字境界調整 | 38 |
| 最近のファイル | 76 |
| 起動引数・ファイル起動 | 80 |
| 校正モード | 失敗。F6参照。NET8でも同一地点で失敗 |

### ネイティブ処理とセキュリティ境界

| 検証 | 結果 |
| --- | --- |
| AppContainerの実プローブ | 成功。能力0、許可ファイル読取可、外部ファイル・runtime書込・所有者記録変更不可 |
| ネットワーク境界 | 制御接続成功後、隔離側接続はtimed-out。プローブ成功だがAccessDeniedエラーを観測したという意味ではない |
| 不正入力・OCR XML/JSON等 | 125件成功 |
| 追加セキュリティ・墨消し残留等 | 40件成功 |
| 出力状態の共有競合・並列出力等 | 26件成功 |
| 監査追加回帰 | 25件成功、skip 0 |

セキュリティ回帰の成功は、第三者監査・最新CVEレビュー・正式公開の承認を代替しません。

### 保存形式の両方向確認

現行.NET 8 / dev.247の実DLLと、検証.NET 10の実DLLを参照する小さな比較プログラムで、次の3ファイルを両ランタイムから読みました。

- dev.247のRapidOCR診断で保存したプロジェクト。
- .NET 10の保存・復元診断で保存したプロジェクト。
- .NET 10のRapidOCR診断で保存したプロジェクト。

3ファイルすべてで復元モデルをJSONにしたSHA-256が一致し、元のプロジェクトのハッシュも変わっていません。つまり、この試験範囲では **旧版→新ランタイム、新ランタイム保存→旧版** の読取が成立しています。任意の過去の全プロジェクトについて保証した結果ではありません。

## 残る確認と方針の判断

| 項目 | なぜ残るか / 次の確認 |
| --- | --- |
| 校正モードの正式な再診断 | F6。原因は判明し、NET8コピーは69項目が2回成功。現行ソースへの反映とNET10再実行が必要 |
| .NET 10だけのクリーン端末 | このPCには.NET 8/9も存在。ロードされたRuntime 10は確認済みだが、クリーンインストール試験は別途必要 |
| Windows 10の継続サポート範囲 | 対応版・エディション・ライフサイクルと実機試験を決める必要がある |
| マルチDPI・タッチ・ペン・スリープ復帰 | 自動試験は単一端末が中心。100/125/150/200%、画面間移動、浮動ツールバーの反復、IMEと復帰を実機確認 |
| CET等を組織ポリシーで強制した端末 | この端末の通過だけで全ポリシー条件は証明できない |
| 長大文書の速度・メモリ | 同じ450ページ資料等でcold/warm open、ページ移動、リンク枠、再OCR、保存・出力を同条件で複数回比較する |
| 最新の依存アドバイザリ | ローカル復元成功とハッシュ一致はCVE情報の最新性を保証しない。ONNX・PDFium・qpdf・Tensors等の保守レビューを別途行う |
| 正式な配布パイプライン | 最終変更後の新リビジョンでBuildPortable、版数・依存ロック・指紋・実配布診断を再実施 |

測定中は別の診断も走らせているため、保存したms値は性能比較には使いません。.NET 10化だけで読み込みが速くなるとの約束はできません。既存のページ優先読込・バックグラウンド処理・OCRプロセス再利用を維持し、同条件の比較を優先します。

配布容量については、キャッシュ等を除いた共通67ファイルの比較で約0.78 MB増でした。PDBや長い検証パスなどの影響も含むため、最終ZIP容量の予測値ではありません。今回の実験ではランタイム自体は同梱していません。単一ファイル化・AOT・trimming・self-contained化は今回の移行に混ぜず、必要なら別にワーカー配置・ネイティブ探索・容量上限を設計します。

## 実装する場合の推奨順序

1. 現行の値を読み直してリビジョンを進め、7プロジェクトのTFMとF2を変更する。
2. 採用されたDLL・日本語資源を確認し、依存ロックを更新する。
3. F6の校正診断を修復し、他の既存診断を維持する。
4. runtimeconfig / worker runtimeの検証を加え、Release・全診断・保存互換性を確認する。
5. Desktop RuntimeとOSの利用条件を設計書・README・新規リリースノートに反映する。
6. 実機のDPI・入力・ドッキング、および長大文書での性能を確認する。
7. 新しいフォルダへBuildPortableで発行し、その**正確な出力先**にGetBuildVersionと配布後診断を実施する。

## 補足：PDFium・qpdfの保守更新（2026-10-27確認）

| 対象 | 同梱版 | 配布元で確認した最新候補 | 判断 |
| --- | --- | --- | --- |
| bblanchon.PDFium.Win32 | 154.0.8035 | 158.0.8111（2026-10-19公開） | 優先して更新を検証する。最新という理由だけで採用完了とはしない |
| qpdf | 12.4.1 | 12.4.2（リリースノート日付2026-09-26） | パッチ更新候補。CLIと出力の回帰確認後に採用する |

確認元：[PDFiumのNuGet公開履歴](https://www.nuget.org/packages/bblanchon.PDFium.Win32/158.0.8111)、[qpdf 12.4.2リリース](https://github.com/qpdf/qpdf/releases/tag/v12.4.2)。この追記では新しいパッケージのダウンロード・差し替え・新版による実行試験は行っていません。

PDFiumは、このアプリで使用している行列取得・設定やコンテントマーク等にExperimental APIを含みます。上流はExperimental APIを予告なく変更・削除できると定めています。採用候補の公開ヘッダー・DLLエクスポートと、P/Invokeの型・呼出規約・所有権を照合します。[API安定性の方針](https://pdfium.googlesource.com/pdfium/+/refs/heads/main/CONTRIBUTING.md)、[公開編集API](https://pdfium.googlesource.com/pdfium/+/main/public/fpdf_edit.h)

Googleの2026-10-13告知にはPDFiumのHigh脆弱性（CVE-2026-110155）の修正が含まれています。この告知はChromeを対象としており、現在の同梱DLLの該当性や候補版への修正取り込みは、修正コミットとビルド設定の照合が必要です。今回「現行版が安全」「候補版で全問題が解消」とする判定はしていません。AppContainer隔離も更新の代わりにはしません。[Googleの告知](https://chromereleases.googleblog.com/2026/10/stable-channel-update-for-desktop_01668083673.html?amp=1)

qpdf 12.4.2には不正なオブジェクトID等の検査改善、線形化時のメモリ削減・出力修正、数値引数の厳密化が含まれます。現行コードはJSONバージョン2を明示しており、--linearizeは使用していません。そのため、線形化の速度改善がこのアプリにもそのまま現れるとは判断しません。QDFを加工する出力経路、JSON読取、終了コード・警告の扱いは実ファイルで再検証します。[公式リリースノート](https://qpdf.readthedocs.io/en/stable/release-notes.html)

更新検証は、現行ランタイムでPDFium単独、qpdf単独、両者の組み合わせ、その後.NET 10との組み合わせを比較する順が適切です。描画だけでなく、日本語の透明テキスト・位置・読み順、リンク・しおり、墨消し後の本文とActualText/Alt・構造ツリー残留、隔離・取消・性能を対象にします。

更新時の管理対象は、csproj、local-packages、tools/qpdf/bin一式、DEPENDENCIES.lock.json、ライセンス・配布元情報、設計・検証記録です。qpdf.exeだけを交換せず同じ配布物のDLL一式を使用します。tools/qpdf/README.mdにも旧アプリ名・旧配置・Releaseでは無効な環境変数指定の説明が残っているため、更新時に現在のcomponents/qpdf配置とRelease/Debugの実際の動作に合わせて修正します。

通常は月次およびリリース前に依存関係を確認し、影響する重要なセキュリティ修正は優先して検証・反映します。起動時の無検証な自動更新は採らず、検証した版とハッシュを固定する運用を維持します。

## 証跡と現行ソースの保全

証跡ルート：

`C:\Users\katsu\Documents\Codex\PDF-Correctorium\outputs\net10-compatibility-audit-20261027-114925`

主なファイル：

- build-net10.log / build-net10-probe-fixed.log / contracts-net10.log
- build-export-diagnostics-net10.log / build-text-diagnostics-net10.log
- app-diagnostics-results.json / security-diagnostics-results.json
- actual-runtime-modules.json / asset-selection.json / dependency-differences.json
- all-common-file-differences.json / matched-file-sizes.json
- version-gate-before-lock-update.log / version-gate-after-probe-lock-update.log
- versioning-source.log / versioning-published.log
- cross-runtime-net8.0.json / cross-runtime-net10.0.json
- baseline-review-mode/failure.txt / diagnostics/review-mode/failure.txt
- review-mode-fixed-locator/failure.txt / review-locator-followup.txt
- audited-source-inventory.txt / main-probe-binary-hashes.json
- baseline-source-fingerprint.txt / final-original-source-fingerprint.txt

現行本体のビルド入力指紋は監査前後で一致：

`8EBC7F737D6EF2AA5677E133C01B7A5F7C598519E07CDFEDE673A15F89EB1A7A`

ルートのリビジョンはdev.247のままです。診断コピー以外のアプリコード・ビルド設定は変更せず、本監査文書だけを追加しました。認証済み旧配布フォルダの変更、Gitのコミット・履歴変更、正式リリースは行っていません。

