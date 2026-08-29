# ADR-0003 非破壊OCR編集レイヤーを採用

- Status: Accepted
- Context: 編集中のPDF破損を防ぎ、Undo、自動保存、差分を成立させたい。
- Decision: 変更はOCR編集レイヤーへ保存し、PDF生成は明示保存時のみ行う。
- Consequences: 安全性とレビュー性が上がる。画面プレビューと最終PDF生成の一致を検証する必要がある。
