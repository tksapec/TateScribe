# TateScribe User Guide

1. プロジェクトを開き、画像を追加します。撮影順はファイル名日時、EXIF撮影日時、作成日時、更新日時、自然順ファイル名の順で判定します。
2. 表示形式・ページ種別・印刷ページ番号を保存します。FixedPageVertical の重複・逆行・欠落候補・非数値は常時「要確認」として保存されます。
3. OCRを実行します。ページ単位の失敗は「本文履歴・ルビ候補」の「OCR失敗履歴」で段階と理由を確認でき、ほかのページは続行します。
4. 必要なら本文を手動修正して保存します。同じ内容の連続保存は履歴を増やさず、以前の Manual/Confirmed 版は「本文履歴・ルビ候補」から復元できます。Confirmed 取込み後に手動保存・Manual版復元を行った場合は、その手動本文が現行版になります。
5. ルビ誤判定は同画面で分類を Body / RubyCandidate に変更し、「下書きに含める」を切り替えて保存します。赤枠が現在のルビ候補です。
6. 校正用パッケージを出力し、ChatGPTへ画像、`ocr.txt`、`instructions.md`、`manifest.json`、`review-items.json`を添付します。ChatGPTには本文を各 `TEXT_BEGIN` / `TEXT_END` 内、報告を `REPORT_BEGIN` / `REPORT_END` 内へ返してもらいます。
7. 校正済みテキストを取り込みます。赤い「取込み前」と緑の「取込み後」、変更字数・段落数、警告を確認し、ページごとに採用します。エラーページは採用できず、キャンセル時はDBを変更しません。
8. DOCXへ出力します。未校正ページと本文のある Other ページは確認されます。Illustration/Blankは除外され、スクリーンショット境界だけでは改ページされません。「章タイトルの前で改ページ」はChapterTitle/構造マーカーの見出しにだけ作用します。

再OCR後に「校正済みですが、その後OCRが更新されています」と表示された場合、Confirmed/Manual本文は保持されています。新しいOCR根拠と比較して再確認してください。

1. Choose a book folder and import images. Check page order, rotation, inclusion, display profile, page role, and all four crop edges.
2. Run OCR. PaddleOCR words with coordinates, auxiliary Tesseract text, and a non-destructive suggested text are retained separately.
3. Export a proofreading package for roughly ten pages. Choose ZIP or folder and optionally include cropped review images.
4. Attach the package to ChatGPT. Follow `instructions.md`; keep the provenance and `[[PAGE:xxxx]]` markers in the returned text.
5. Import UTF-8 `.txt`, `.md`, or a package ZIP containing `proofread.txt`/`proofread.md`. Review validation and the character-count/diff summary, then explicitly save accepted pages.
6. DOCXを出力します。TateScribeは現行のConfirmedText、ManualText、SuggestedText、PaddleOCR復元下書きの順で採用し、未確定ページを含む場合は確認します。本文中の意図的な空行は空段落として保持します。

TateScribe does not send images, OCR results, or text to external services. The ChatGPT exchange is performed manually by the user.
