# TateScribe

Windows向けの、縦書き電子書籍スクリーンショットを編集可能な横書きDOCXへ変換するローカルOCRアプリです。画像・OCR結果・本文を外部へ送信しません。

## 基本の使い方

1. 本ごとにフォルダーを作り、その直下へキャプチャ画像（PNG/JPEG/WebP）をまとめます。
2. TateScribeでその本のフォルダーを選びます。初回は画像を撮影時刻順で取り込み、`project.db` に管理情報を保存します。
3. 必要に応じてページの順序を「上へ」「下へ」で修正し、向きが異なる画像は「左へ90度」「右へ90度」で調整します。4辺の不要な領域、表示形式、ページ種別、印刷ページ番号をページ単位で設定できます。元画像は変更されません。
4. `選択ページをOCR`、`未完了ページから再開`、または `全ページを再OCR` を選びます。処理を止める場合は `OCRを中止` を選びます。中止までに完了した結果は保持され、アプリを開き直さず未完了ページから再開できます。
5. `本文校正用パッケージを出力` で、既定10ページ単位のZIPまたはフォルダーを作成します。`ChatGPTへの指示`で「本文校正」を選び、パッケージと指示文をChatGPTへ渡します。TateScribeはChatGPT APIへ接続しません。
6. 校正結果は、TateScribeが出力した`[[TATESCRIBE_FORMAT]]`、プロジェクトID、バッチID、`[[PAGE]]`マーカーを残したUTF-8テキスト／Markdown、またはZIP内の`proofread.txt`・`proofread.md`として`校正済みテキストを取り込む`から読み込みます。保存前にプロジェクト、範囲、順序、文字数差を検証します。
7. 必要に応じて選択ページのOCR本文を編集して保存します。現行の確定本文、手動修正、補正候補、PaddleOCR下書きの順で本文を採用します。確定後に手動保存した場合は、古い確定版を履歴に残したまま新しい手動版を現行本文にします。
8. `ルビ確認用パッケージを出力` で、確定本文、原画像、OCRルビ候補、JSON Schemaを含むフォルダーを作ります。既定のルビ方針は「原画像に存在するルビのみ」です。
9. `ChatGPTへの指示`で「ルビ確認」を選び、ルビ確認用パッケージと指示文をChatGPTへ渡します。ChatGPTは本文を変更せずJSONだけを返します。
10. `ルビJSONを取り込んで確認`でJSONを読み込みます。プロジェクト、バッチ、本文ハッシュ、段落、UTF-16範囲、親文字、根拠ページ等を検証し、エラー時はDBを変更しません。
11. ルビ確認画面で原文・根拠・座標・注意を確認し、候補を個別に確定または却下します。`ImageConfirmed`は対象本文範囲の元ページにある、読み・親文字・OCR信頼度・リンク信頼度が一致する画像候補を意味します。注釮信頼度0.70未満、OCR信頼度0.70未満、リンク信頼度0.60未満、元ページ不一致、Body側へ戻した候補は警告となり、一括確定されません。画像を目視した個別確定は可能です。
12. `保存済みルビ候補を確認`では、注釈を持つ最新バッチが選択された履歴画面から過去バッチを開けます。履歴には状態別件数と、現行本文に対して古いバッチかを表示します。本文を変更すると、対応する確定ルビはStaleになり、出力へ使われません。
13. `DOCXへ出力` を選ぶと、同じフォルダーに `フォルダー名.docx` を出力します。1段落の複数ルビを含め、確定済みのルビだけをTateScribeが生成します。
14. `でんでん用データを出力`では、書名・著者・文字方向等を入力し、出力先の`README.txt`と`upload/`を生成します。`upload/`には`book.md`（または章分割Markdown）、公式形式の`ddconv.yml`、`default.css`、必要な画像と`ruby.csv`を置きます。アップロード時は`upload/`内の全ファイルを選択し、ルートの`README.txt`は選択しません。挿絵は既定OFFで、明示的に有効にした場合だけ`PageRole=Illustration`の画像を出力します。EPUBとZIPは生成しません。

DOCXは横書きです。元画像の縦書き配置やスクリーンショット境界は再現せず、画像境界による改ページも作りません。

## Word ruby compatibility and review

The DOCX Word ruby offset uses a **3pt default** and accepts whole numbers from 0 through 20. TateScribe calculates the XML raise provisionally in half-points: `hpsRaise = rubyFontSizeHalfPoints + wordOffsetPoints * 2`. This metric is not a direct equivalence with Word's displayed offset, so a changed offset requires a new DOCX export and visual review in Word. The generated XML is diagnostic evidence, not a substitute for opening the exported file in Word: manual Word visual verification and B/C comparison remain required when Word-saved reference files are available.

Ruby review supports Ctrl/Shift extended selection, and Ctrl+Enter confirms the selected rows after pending edits commit. The individual Confirm and Reject buttons affect only the current selection; rejection is button-only, never a Delete-key shortcut. Image-based and text-based bulk confirmation remain separate. Their result dialog reports examined, newly confirmed, already confirmed, wrong-source, and excluded counts, plus each exclusion reason. The selection summary always includes the selected-count.

This compatibility and review work adds no SQLite schema migration and does not create a Release ZIP.

## 開発時の実行

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
dotnet run --project .\src\TateScribe.App
```

OCRワーカーの依存導入は `scripts/setup-ocr.ps1` を使います。リリース時にはOCRモデルをローカル同梱し、実行時のダウンロードを禁止します。依存固定とライセンスは [THIRD_PARTY.md](THIRD_PARTY.md) を参照してください。

通常の配布内容確認は `scripts/package.ps1 -SkipArchive` を使い、自己完結の `artifacts/win-x64` だけを生成します。配布用の `artifacts/TateScribe-win-x64.zip` は明示的に必要な場合だけ `scripts/package.ps1` で作成してください。OCRランタイムを同梱するため、ZIP作成には時間がかかります。

## EXEから起動する

自己完結版は次のEXEをダブルクリックして起動できます。.NET SDKやPowerShellを起動する必要はありません。

```text
artifacts\TateScribe-win-x64\TateScribe.App.exe
```

OCRも利用する場合は、EXEと同じフォルダーにある `ocr-runtime` と `ocr-worker` フォルダーを削除・移動しないでください。

## 校正ワークフロー

校正用パッケージはページごとに現行の `ConfirmedText`、`ManualText`、`SuggestedText`、座標付き Raw PaddleOCR 復元本文の順で本文を選び、`manifest.json` の `textSource` に根拠を記録します。新規パッケージは formatVersion 2 で、取込み対象は `[[TEXT_BEGIN]]` と `[[TEXT_END]]` の内側だけです。各ページの `[[JOIN_TO_NEXT:...]]` は必須で、欠落や本文ブロック内への混入はエラーになります。判読不能箇所と修正報告は `[[REPORT_BEGIN]]` / `[[REPORT_END]]` に分離され、最終ページ本文へ混入しません。旧 formatVersion 1 も引き続き取り込めます。

パッケージ出力後に画像、OCR、手動本文、順序、切り抜き、回転、ページ種別、表示形式が変わると取込み時に警告またはエラーになります。警告ページは取込み前・取込み後・色分けしたインライン差分を確認してページ単位に採用でき、画像変更・削除・除外ページは保存できません。非常に大きい本文はメモリを保護するため変更範囲をまとめて表示します。

再OCRは ManualText と ConfirmedText を削除しません。校正後にOCRが更新されたページは「Stale（校正済みだがOCR更新後）」として表示されます。「本文履歴・ルビ候補」では Manual/Confirmed の過去版を復元し、ルビ候補を Body に戻したり下書きへの採否を変更できます。

## DOCX出力

Illustration と Blank は本文出力から除外します。Other は本文があれば既定で含め、出力前に確認します。スクリーンショット境界では改ページせず、format 2 の `JOIN_TO_NEXT` に従って直接連結、空白連結、段落、場面転換を保持します。DOCXには Normal、Heading1、Heading2、Heading3、SectionNumber、SceneBreak を明示定義します。

確定本文は安定した段落ID、本文ハッシュ、元ページ範囲を持つ構造化スナップショットとして保存します。ルビは本文とは別に `Proposed`、`Confirmed`、`Rejected`、`Stale` で管理し、`Confirmed`だけをDOCXとでんでん用データへ出力します。詳細は [ChatGPTワークフロー](docs/CHATGPT_WORKFLOWS.md)、[ルビJSON形式](docs/RUBY_JSON_FORMAT.md)、[でんでん出力](docs/DENDEN_EXPORT.md)を参照してください。

DOCXとでんでん用データは同じ出力前確認を使用し、未校正、Other本文、空本文、未確定／Proposed／Staleルビ、挿絵、既存出力先を表示します。未確定、Proposed、Staleルビは最終出力へ含めません。でんでん用画像は、全体・無回転のPNG/JPEG/GIFを保持し、WebP/BMP/TIFF等と変形済み挿絵は決定論的にPNGへ変換します。変換後の1画像が3 MiBを超える場合、または`upload/`内の総ファイル数が100件を超える場合は出力先を作成せず停止します。
