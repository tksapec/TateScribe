# TateScribe

Windows向けの、縦書き電子書籍スクリーンショットを編集可能な横書きDOCXへ変換するローカルOCRアプリです。画像・OCR結果・本文を外部へ送信しません。

## 基本の使い方

1. 本ごとにフォルダーを作り、その直下へキャプチャ画像（PNG/JPEG/WebP）をまとめます。
2. TateScribeでその本のフォルダーを選びます。初回は画像を撮影時刻順で取り込み、`project.db` に管理情報を保存します。
3. 必要に応じてページの順序を「上へ」「下へ」で修正し、向きが異なる画像は「左へ90度」「右へ90度」で調整します。4辺の不要な領域、表示形式、ページ種別、印刷ページ番号をページ単位で設定できます。元画像は変更されません。
4. `全ページをOCR` を選ぶと、登録済みの画像を順番に認識します。処理を止める場合は `OCRを中止` を選びます。完了済みページの結果は保持されます。
5. `校正用パッケージを出力` で、既定10ページ単位のZIPまたはフォルダーを作成します。パッケージをChatGPTへ添付し、同梱の`instructions.md`に従って校正します。TateScribeはChatGPT APIへ接続しません。
6. 校正結果は、TateScribeが出力した`[[TATESCRIBE_FORMAT]]`、プロジェクトID、バッチID、`[[PAGE]]`マーカーを残したUTF-8テキスト／Markdown、またはZIP内の`proofread.txt`・`proofread.md`として`校正済みテキストを取り込む`から読み込みます。保存前にプロジェクト、範囲、順序、文字数差を検証します。
7. 必要に応じて選択ページのOCR本文を編集して保存します。確定本文、手動修正、補正候補、PaddleOCR下書きの順で本文を採用します。
8. `DOCXへ出力` を選ぶと、同じフォルダーに `フォルダー名.docx` を出力します。未校正ページを含む場合は件数を確認してから続行します。

DOCXは横書きです。元画像の縦書き配置やスクリーンショット境界は再現せず、画像境界による改ページも作りません。

## 開発時の実行

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
dotnet run --project .\src\TateScribe.App
```

OCRワーカーの依存導入は `scripts/setup-ocr.ps1` を使います。リリース時にはOCRモデルをローカル同梱し、実行時のダウンロードを禁止します。依存固定とライセンスは [THIRD_PARTY.md](THIRD_PARTY.md) を参照してください。

`scripts/package.ps1` は自己完結の `artifacts/win-x64` と、配布用の `artifacts/TateScribe-win-x64.zip` を生成します。OCRランタイムを同梱するため、ZIPは大きくなります。

## EXEから起動する

自己完結版は次のEXEをダブルクリックして起動できます。.NET SDKやPowerShellを起動する必要はありません。

```text
artifacts\TateScribe-win-x64\TateScribe.App.exe
```

OCRも利用する場合は、EXEと同じフォルダーにある `ocr-runtime` と `ocr-worker` フォルダーを削除・移動しないでください。
