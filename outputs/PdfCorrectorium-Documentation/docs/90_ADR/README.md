# ADR索引

| ADR | 状態 | 決定 |
|---|---|---|
| [ADR-0001](ADR-0001-WPF-DotNet8.md) | Runtime部分はADR-0008で置換 | 初期採用のC# / .NET 8 / WPF / MVVM |
| [ADR-0002](ADR-0002-Project-Container.md) | Accepted | ZIP互換`.pdfocrproj` |
| [ADR-0003](ADR-0003-NonDestructive-Ocr-Layer.md) | Accepted | 非破壊OCR編集レイヤー |
| [ADR-0004](ADR-0004-License.md) | Accepted | Apache License 2.0 |
| [ADR-0005](ADR-0005-Page-Regeneration.md) | Accepted | 変更ページ再生成を既定 |
| [ADR-0006](ADR-0006-Plugin-Packaging.md) | Proposed | `.pdfocrplugin`、NuGet直接導入なし |
| [ADR-0007](ADR-0007-Logical-Page-Composition.md) | Accepted | ページ削除・並べ替え・回転は論理モデルを正本とする |
| [ADR-0008](ADR-0008-NET10-Native-Dependency-Maintenance.md) | Accepted | .NET 10と検証済みネイティブ依存関係の固定・保守 |

ADRは採用後に書き換えず、変更する場合は新しいADRで置換する。

- [ADR-0009 現行設計と配布候補の扱い](ADR-0009-Release-Documentation-And-Artifact-Scope.md)
