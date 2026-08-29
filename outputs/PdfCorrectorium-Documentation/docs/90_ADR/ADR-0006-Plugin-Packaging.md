# ADR-0006 専用プラグインパッケージ

- Status: Proposed
- Context: NuGet直接導入は依存競合、サイズ、安全性、一般利用者の理解に難がある。
- Decision: 開発時のNuGet利用は許容し、利用者向けはZIP互換`.pdfocrplugin`を候補とする。
- Consequences: 独自manifest、互換性検査、権限表示、更新方式が必要。Version 1.0では契約設計を優先する。
