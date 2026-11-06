**Windows x64, self-contained** - download the single file `BackgroundRemover.exe` and run it (the model is embedded and extracted to `%LOCALAPPDATA%\BackgroundRemover` on first use), or use the ZIP. No install, no account, no network access needed; images never leave your PC.

- The app is **unsigned**, so Windows SmartScreen will show "Windows protected your PC": choose *More info → Run anyway*. Verify the download against the attached `.sha256` file.
- Download is large (~300 MB unpacked) because it bundles the .NET runtime, ONNX Runtime and the 170 MB ISNet model.
- Uses the GPU through DirectML when available, otherwise the CPU.
- Model: ISNet "general use" (DIS, Apache-2.0, ONNX from rembg, MIT). See `THIRD_PARTY_NOTICES.md` for licence caveats.
- Accuracy is measured only on a small set of 5 public-domain cut-outs; see the README for numbers and known limitations (glass, fine hair, similar colours). The UI was only smoke-tested (it launches and responds), not fully QA'd.
