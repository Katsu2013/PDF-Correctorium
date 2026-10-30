# 読み上げの段落連結と自然音声の制約（dev.256）

dev.261で、既存の単語読み方を入力・登録・一覧編集するUIと、PDF編集画面だけの確認用ルビを追加した。[操作・適用範囲](Word-Readings-And-Ruby.md)を参照する。

2026-10-28。行ごとに音声合成を終了・再開すると、折返し位置へ不自然な間が入る。読み上げ時だけ隣接する折返し行を段落としてまとめ、同じ合成要求に渡す。OCRの文字、領域、保存形式、未保存状態は変更しない。

## 連結と区切り

- 現在ページの明示された読み順を維持し、全PDFを先読みしない。横書きは行間・高さ・端・字下げ、縦書きは列間隔・幅・上端を使い、異なる親段落IDをまたがない。
- 新しい字下げ、空行、別の段、文字サイズの異なる見出し、箇条書き、番号付き項目、同じ高さの表セル、コードらしい行、書字方向の違いは区切る。回転した行は自動連結しない。
- 日本語の折返しに空白を挿入せず、欧文の単語どうしには空白を補う。既存のハイフンは保持し、綴りや誤認識を推測して変更しない。
- 部分的な文字選択は行どうしを連結しない。削除・読み上げ対象外・墨消し等で除外した行は境界とし、その前後を直結しない。元／編集後の枠・墨消し元IDによる除外を維持する。
- 句読点を保持し、文中の間は音声エンジンへ委ねる。合成要求は従来同様約2,000 UTF-16コード単位までで、結合文字やサロゲートペアを途中で切らない。

推定は幾何情報によるため、字下げ・空き・親IDのない段落境界等は区別できない場合がある。

## 読み上げ位置

連結した文字位置と各行IDを対応付け、音声の進捗で（dev.260以降はWindowsMediaの時刻付きキュー）読み上げ中の行へ一時マーカーを移す。通常のOCR枠を隠していても表示する。進捗イベントのない音声は合成要求の開始行を表示する。終了・停止・文書変更後や、前の段落から遅れて届くイベントは破棄する。枠だけの移動は同じ領域IDへ追従する。

## Microsoft Nanami / Keita（Natural）

利用者の指定はWindowsナレーター設定から追加した自然音声。ナレーターでは提供されるが、現在のSystem.Speech / SAPIの声とは異なり、この環境の通常SAPI登録にも存在しない。OneCore列挙へ変えるだけで使えるとも判断しない。

調査時点で、一般アプリからこれらを直接利用する公開された公式ローカルAPIを確認できていない。非公式アダプターの作者も抽出した鍵の利用と将来の互換性の制約を説明している。本アプリには鍵抽出、非公開API、レジストリ改変、アダプター導入を追加しない。利用できない名前だけを音声一覧へ追加しない。

Azure SpeechのNanamiNeural / KeitaNeuralは、本文の外部送信、アカウント、料金、秘密情報の保管を伴う別の採用判断になる。今回はクラウド音声を追加せず、音声ピッカーのツールチップに標準APIで利用可能な声だけを表示する旨を追記した。

参照：[Microsoftのナレーター音声](https://support.microsoft.com/en-au/accessibility/windows/narrator/appendix-a-supported-languages-and-voices)、[Windows音声合成API](https://learn.microsoft.com/en-us/uwp/api/windows.media.speechsynthesis.speechsynthesizer)、[非公式アダプターの制約説明](https://github.com/gexgd0419/NaturalVoiceSAPIAdapter#will-it-work-on-future-versions-of-windows)、[Azureの音声一覧](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/language-support?tabs=text-to-speech)。

