# Background Remover

A free, **fully offline** AI background remover for Windows x64. No uploads, no telemetry, no account, no per-image cost.

Built with .NET 8 / WPF, ONNX Runtime (DirectML GPU with CPU fallback) and the ISNet "general use" model.

## Features

- Open or drag-and-drop images and folders (PNG, JPEG, WebP, BMP, GIF, TIFF); EXIF orientation is applied.
- Batch export with progress and cancel; original resolution is preserved (output size = input size).
- Before/After split, Result, Original and Mask views; zoom (wheel) and pan (right/middle drag).
- Refinement brush (Keep / Remove) with undo, edge feather, edge shrink/grow, and colour-fringe removal (foreground colour estimation near edges).
- Replace background: transparent, solid colour, or image.
- Export PNG, WebP (lossless/lossy) with alpha, or JPEG (flattened onto white if transparent).
- Large images: inference runs at 1024 px and the soft mask is upsampled to full size; images over 100 megapixels are refused to avoid running out of memory.

## Download / run

Get `BackgroundRemover-win-x64.zip` from the [Releases](../../releases) page, unzip, run `BackgroundRemover.exe`. The build is **unsigned**; SmartScreen will warn (More info → Run anyway). Verify the `.sha256` if you wish.

## Build from source

```powershell
dotnet test tests/BgRemover.Tests
# download the model (URL and checksum in THIRD_PARTY_NOTICES.md) to models/isnet-general-use.onnx, then:
dotnet publish src/BgRemover.App -c Release -r win-x64 --self-contained true -o publish
```

CI (`.github/workflows/build.yml`) downloads and checksum-verifies the model, runs the tests, publishes, zips and uploads the artifact; pushing a `v*` tag creates a GitHub release.

## Model and licences

ISNet general-use (DIS, Apache-2.0; ONNX export from rembg, MIT). Non-commercial models such as BRIA RMBG-1.4/2.0 are deliberately **not** used. The licence of the specific trained weights and their training data was not independently audited - verify before commercial redistribution. Full details: [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). App code: MIT ([LICENSE](LICENSE)).

## How accurate is it? (honest numbers)

`tools/BgRemover.Eval` composites 5 CC0/public-domain Wikimedia cut-outs (flowers, a raccoon dog, a puppy; fetched by `eval/fetch-eval-images.ps1`) onto flat-grey, textured and camouflage-like backgrounds and compares the predicted alpha with the cut-out's alpha. Results on an NVIDIA RTX 3060 (DirectML), mean of 15 runs:

| variant | MAE (x1000) | boundary MAE (x1000) | IoU@0.5 |
|---|---|---|---|
| raw model mask, upsampled (**default**) | 11.4 | 79.9 | 0.986 |
| + flip averaging (optional "higher quality") | 12.2 | 78.7 | 0.987 |
| + guided-filter refinement (best of sweep) | 12.6 | 93.9 | 0.986 |

Full table: [docs/EVAL_RESULTS.md](docs/EVAL_RESULTS.md). Caveats:

- Only 5 images, all single objects; this is **not** a benchmark and does not show the app is "very accurate" in general.
- Reference alphas are human-made cut-outs, not pixel-exact ground truth; boundary MAE is dominated by that uncertainty.
- Guided-filter edge refinement (code kept in `Core`, off by default) made results **worse** on this data, so it is disabled.
- No comparison against other matting models was run, and the UI was only smoke-tested (launches and stays responsive), not exhaustively QA'd.

## Known limitations

- Glass, smoke, translucent fabric and reflections come out hard-edged or partly missing.
- Fine hair/fur is approximate: the model predicts at 1024 px, so thin strands are lost or blotchy. Use the brush to fix.
- Foreground and background of similar colour/texture (see the camouflage backgrounds) increase errors; IoU on the raccoon-dog image was 0.97 vs 0.997 on the easiest image.
- Multiple subjects: the model picks what it considers salient; it may keep or drop objects you did not intend.
- The refine brush re-renders the full-resolution result, so very large images feel slower while painting.
- Unsigned binary; GPU path needs Windows 10 1903+ (DirectML), otherwise CPU is used.
