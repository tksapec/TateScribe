# Third-party components

| Component | Fixed version | License | Purpose |
| --- | --- | --- | --- |
| .NET | 8.0 target | MIT | Windows application runtime |
| OpenCvSharp | 4.13.0.20260627 | Apache-2.0 | local image preprocessing |
| Microsoft.Data.Sqlite | 9.0.7 | MIT | project persistence |
| Open XML SDK | 3.3.0 | MIT | DOCX generation |
| PaddlePaddle | 3.2.0 | Apache-2.0 | local OCR runtime |
| PaddleOCR | 3.7.0 | Apache-2.0 | primary OCR adapter |
| Tesseract | separately bundled | Apache-2.0 | optional vertical Japanese OCR adapter |

Production packaging must include model files, their upstream license notices, and SHA-256 checksums in the release manifest. The worker never downloads models or sends user data to a network service.
