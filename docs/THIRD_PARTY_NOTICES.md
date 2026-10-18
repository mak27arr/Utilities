# Third-Party Notices

## TesseractOcrMaui 1.5.2

The Android app uses [TesseractOcrMaui](https://www.nuget.org/packages/TesseractOcrMaui/1.5.2), licensed under Apache License 2.0. The package wraps Tesseract 5.5.1 on Android and is distributed as a 76.33 MB NuGet download. The package maintainer notes that bundled native dependencies may carry separate licenses; review those notices when preparing a release.

## English OCR model

`src/Utilities.Mobile/Resources/Raw/eng.traineddata` is the English model from the official [tessdata_fast repository](https://github.com/tesseract-ocr/tessdata_fast). It is licensed under Apache License 2.0. The full upstream license text is in [TESSERACT_MODEL_LICENSE.txt](TESSERACT_MODEL_LICENSE.txt).

The model is packaged with the app. Recognition uses local image bytes and has no model download step or network service.
