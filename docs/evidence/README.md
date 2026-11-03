# UI evidence (Phase B, 2026-11-02)

Debug desktop build (`net10.0-desktop`) under Xvfb on Linux, window height 640, live-resized through 360, 599, 600,
800, 1080 and 1920 px. One sheet per page and theme:

| Sheet | What it shows |
|---|---|
| `quotes-*` | Quotes list (stats, filters, list) |
| `dashboard-*` | Quotes with a quote open (one pane below 1080, list and detail from 1080, side column from 1400) |
| `editor-*` | Quote editor opened from the list (re-captured 2026-11-02 after the round-2 fixes: the tax rate reads "13", not "13.0") |
| `editor-totals.jpg` | The editor below the fold at 360 and 1280 px, Light and Dark: line items, the subtotal, tax and total card, and the status actions, reached with Tab (keyboard focus on Mark Sent); window height 800 |
| `flows-round2-light.jpg` | Runtime checks of the round-2 fixes at 1280 × 800: the item dialog refusing "1,5" (its "aterials" is the synthetic typing starting before the field had focus), an edited item keeping its place with the 8.25 % totals, the expired banner with Reopen, and Send reporting that no email app opened |
| `clients-*`, `catalog-*` | With a client or category open |
| `settings-*` | Settings (live preview from 1400) |
| `onboarding-*`, `onboarding-step3-*` | First run: welcome, and the quote settings step |
| `pdf-export.jpg` | The PDF that Export PDF wrote on desktop (2026-11-03, SkiaSharp `SKDocument`, D12), rendered with `pdftoppm` at 90 dpi: QC-2026-0001 from the sample data with 24 more line items, a logo and three photos added; two quote pages (the table breaks after 18 rows and repeats its header row) and the photo appendix |

Reproduce: run the app with `--qc-data=<empty folder> --qc-seed=sample --qc-theme=light|dark --qc-route=<route>`
(Debug only; routes `Dashboard[:detail]`, `QuoteEditor`, `Clients:detail`, `Catalog:detail`, `Settings`,
`Onboarding[:step3|:complete]`; onboarding sheets use no seed, so the first run shows it).

Not covered here: Android, iOS, WebAssembly and WinAppSDK at runtime (the WebAssembly PDF download was checked in
headless Chromium; see D12 in [the decisions](../DECISIONS-production-readiness.md)); touch input; screen readers;
dialogs and flyouts (opened by hand on desktop only).
