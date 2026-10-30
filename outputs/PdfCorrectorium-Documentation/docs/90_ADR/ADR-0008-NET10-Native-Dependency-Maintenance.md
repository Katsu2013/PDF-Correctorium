# ADR-0008 .NET 10とネイティブ依存関係の保守

- Status: Accepted（2026-10-27、dev.248で着手、dev.249で最終検証）
- Supersedes: ADR-0001のランタイム採用部分。C# / WPF / MVVMと既存の編集・隔離境界は維持。
- Context: .NET 8の2026-11-10サポート終了を踏まえ、LTSの.NET 10へ移す。同時にPDFiumとqpdfの保守更新が必要。変更をまとめて入れて原因を不明にせず、旧環境で診断を直してから依存関係、ランタイムの順に確認する。
- Decision: 全7プロジェクトを.NET 10に統一し、framework-dependent配布を維持。PDFium 158.0.8125とqpdf 12.4.2を固定する。TFMで切り替わる管理DLL・日本語資源と同版ライセンスもハッシュ照合する。版管理はruntimeconfigの要求TFM、NETCore / WindowsDesktop、未検証メジャーへのrollForwardを検査する。
- Consequences: 利用者は.NET 10 Desktop Runtime（x64）の最新サービス版が必要。起動ログで実ランタイムとWPFロード元を確認できる。CLR更新だけで保存形式を上げず、形式1.12／最小読込dev.243／設定21を維持する。API宣言照合・回帰試験とCVEの該当性判断は別に扱う。クリーン端末・別OS・多様な入力機器・大規模文書の全条件を認定したものではない。

ライブラリはリリース前および定期的に情報を確認し、影響する重要な修正を優先して検証する。利用者の起動時に未検証版へ自動更新しない。過去のADRや旧配布物の記録は保持する。

根拠：[Microsoftサポートポリシー](https://dotnet.microsoft.com/en-us/platform/support/policy)、リポジトリのdocs/NET10-Compatibility-Audit-2026-10-27.mdとdocs/Runtime-Native-Upgrade-Plan-2026-10-27.md。
