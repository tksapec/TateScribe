# Proofreading Workflow Hardening Design

## Goal

TateScribe の非破壊 OCR と手動 ChatGPT 校正運用を維持しながら、校正本文の選択、取込み境界、陳腐化検出、OCR/校正状態、履歴、ページ検証、DOCX 出力を安全にする。

## Constraints

- PaddleOCR の座標付き原本、Tesseract 生結果、Suggested/Manual/Confirmed の各本文を上書き統合しない。
- ChatGPT API へ直接接続しない。
- 既存 DB と proofreading format 1 の読込み互換性を維持する。
- スクリーンショット境界だけを理由に DOCX を改ページ・改段落しない。
- Illustration と Blank は画像も本文も原則出力せず、Other の本文は既定で含める。
- ローカル書籍画像や OCR 本文をテスト資産として追加しない。

## Architecture

既存の Core / Infrastructure / App 境界を保ち、MainWindow に集中している処理だけを小さなサービスへ移す。Core にフォーマット、差分、境界接続、検証の純粋ロジックを置き、Infrastructure に SQLite、ZIP、画像メタデータ、Open XML、ワーカー通信を置く。App は入力、サービス呼出し、確認ダイアログ、結果表示のみを担当する。

## Data model

- `OcrStatus`: OCR 実行状態を表し、校正状態から分離する。
- `ProofreadingStatus`: 旧値を読める互換マッピングを持ち、校正済み本文を保持したまま OCR 更新時に `Stale` を表現する。
- `BoundaryJoinType`: `DirectJoin`, `SpaceJoin`, `ParagraphBreak`, `SceneBreak`, `Uncertain`。
- `page_text_versions`: Manual/Confirmed を追記し、同種・同内容の連続版を抑止する。
- `proofreading_export_pages`: 元画像、本文、切り抜き、回転、役割、表示方式、順序、OCR run のスナップショットを保持する。
- `ocr_failures`: ページ、ファイル、処理段階、例外型、メッセージ、取消区分、発生時刻を保持する。
- `review_items`: ページ番号、OCR、ルビ、マージ提案を共通の要確認項目として保持する。
- `ocr_run_words`: 自動分類と手動上書きを区別し、role / included_in_draft の変更を再表示・再OCR後も保持する。

スキーマ変更は `schema_version` を使う明示的な段階マイグレーションとして、単一トランザクション内で実行する。列の存在は `PRAGMA table_info` で確認し、想定外の SQLite 例外は再送出する。

## Proofreading package format 2

新規 `ocr.txt` は次の構造とする。

```text
[[TATESCRIBE_FORMAT:2]]
[[PROJECT_ID:<guid>]]
[[BATCH_ID:<guid>]]

[[PAGE:0001]]
[[TEXT_BEGIN]]
本文
[[TEXT_END]]
[[JOIN_TO_NEXT:DirectJoin]]

[[REPORT_BEGIN]]
判読不能箇所一覧
主な修正箇所一覧
[[REPORT_END]]
```

ページ本文は Confirmed, Manual, Suggested, RawPaddle の順に選択し、manifest に `textSource` と基準ハッシュを記録する。取込みは `TEXT_BEGIN` と `TEXT_END` の間だけを本文とし、構文上の直後・直前改行だけを除く。本文内の通常の `[[...]]`、全角空白、半角空白、空行は保持する。文書全体を包む単一 Markdown コードフェンスだけを安全に除去する。format 1 は従来パーサーで読み込む。

## Import safety and diff

取込み前に export snapshot と現在値を比較する。元画像変更、ページ削除、除外はエラーとして保存不可にする。順序、OCR/Manual、PageRole、crop、rotation、DisplayProfile の変更はページ警告として利用者の明示採用を要求する。差分モデルは追加、削除、置換、変更文字数、変更段落数を保持し、UI で前後本文と色分け差分、全選択/全解除、警告のみ絞込みを提供する。保存件数には `AcceptedMarkers.Count` を使う。

## OCR and page review

Paddle エンジンは Python ワーカー内でモデル設定単位に遅延初期化し、同一設定では再利用する。失敗は構造化応答と DB の失敗履歴へ残し、ほかのページの処理を続行する。再OCRは Manual/Confirmed を削除せず、校正基準 OCR run と異なる場合だけ校正状態を `Stale` にする。

印刷ページ番号検証は `PageValidationService` に集約し、プロジェクトを開く、追加、並べ替え、使用変更、番号保存、DisplayProfile変更の各時点で ReviewItem を更新する。ルビ候補は座標付きで一覧表示し、Body/RubyCandidate と draft 採否を変更できる。

## DOCX

ページ本文は `BoundaryJoinType` に従って組み立てる。明示された `[[CHAPTER:...]]` と `[[TITLE:...]]` を優先し、ChapterTitle の先頭短行列は1つの見出しにまとめる。Open XML の StylesPart に Normal, Heading1/2/3, SectionNumber, SceneBreak を定義し、本文の1字下げ、見出し・節番号・場面転換の字下げ/中央揃え、章前改ページをスタイルで表現する。OpenXmlValidator で検証する。

## Error handling and verification

構造マーカー欠落・重複・入れ子は取込み前にエラーにする。DB 更新はトランザクション化し、キャンセルまたはエラーページを保存しない。Core の純粋ロジック、Repository migration、Python ワーカー、Open XML を自動テストし、最後に build/test/package スクリプトと主要な手動経路を確認する。
