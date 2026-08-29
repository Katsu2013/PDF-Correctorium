# ADR-0002 ZIP互換`.pdfocrproj`を採用

- Status: Accepted
- Context: PDF外の編集状態、履歴、コメント、復旧情報を単一ファイルで長期保存したい。
- Decision: 人間可読JSON中心のZIP互換コンテナとし、拡張子を`.pdfocrproj`とする。
- Consequences: 移動と調査が容易。頻繁な全ZIP書換えを避けるため編集中は作業領域へ展開する。安全保存と形式移行が必要。
