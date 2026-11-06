# Third-party notices

## Model: ISNet "isnet-general-use"

- Architecture / training code: **DIS** (Highly Accurate Dichotomous Image Segmentation), Xuebin Qin et al., <https://github.com/xuebinqin/DIS> - **Apache License 2.0**.
- The ONNX export used here is distributed by the **rembg** project (<https://github.com/danielgatis/rembg>, MIT) as release asset `isnet-general-use.onnx`:
  `https://github.com/danielgatis/rembg/releases/download/v0.0.0/isnet-general-use.onnx`
  (SHA-256 `60920E99C45464F2BA57BEE2AD08C919A52BBF852739E96947FBB4358C0D964A`, 178,648,008 bytes).
- **Verification caveat:** the upstream repositories are permissively licensed, but the trained weights' own terms (including the training-data terms of the DIS5K dataset) were not independently audited by this project. If you plan to redistribute this app commercially, verify the model licence and dataset terms yourself.
- Deliberately **not** used: BRIA RMBG-1.4 / RMBG-2.0 (non-commercial licence).

The model file is downloaded by the build (CI) from the URL above and its checksum is verified; it is not stored in this git repository.

## Libraries

| Component | Licence |
|---|---|
| Microsoft.ML.OnnxRuntime.DirectML (ONNX Runtime) | MIT |
| DirectML (system component on Windows 10 1903+) | Microsoft, shipped with Windows |
| SkiaSharp (and bundled Skia) | MIT / BSD-3-Clause |
| .NET 8 runtime / WPF | MIT |
| xUnit (tests only) | Apache-2.0 |

## Evaluation images

Only CC0 / public-domain Wikimedia Commons cut-outs are used by the evaluation harness; see `eval/images/ATTRIBUTION.md` after running `eval/fetch-eval-images.ps1`.
