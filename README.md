# TateScribe

Windows向けの、縦書き電子書籍スクリーンショットを編集可能な横書きDOCXへ変換するローカルOCRアプリです。画像・OCR結果・本文を外部へ送信しません。

## 基本の使い方

1. 本ごとにフォルダーを作り、その直下へキャプチャ画像（PNG/JPEG/WebP）をまとめます。
2. TateScribeでその本のフォルダーを選びます。初回は画像を撮影時刻順で取り込み、`project.db` に管理情報を保存します。
3. 必要に応じてページの順序を「上へ」「下へ」で修正し、OCR本文を編集します。
4. `DOCXへ出力` を選ぶと、同じフォルダーに `フォルダー名.docx` を出力します。

DOCXは横書きです。元画像の縦書き配置やスクリーンショット境界は再現せず、画像境界による改ページも作りません。

## 開発時の実行

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
dotnet run --project .\src\TateScribe.App
```

OCRワーカーの依存導入は `scripts/setup-ocr.ps1` を使います。リリース時にはOCRモデルをローカル同梱し、実行時のダウンロードを禁止します。依存固定とライセンスは [THIRD_PARTY.md](THIRD_PARTY.md) を参照してください。
