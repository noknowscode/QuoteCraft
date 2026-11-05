# UI evidence (re-captured after the A pass, 2026-11-04)

Debug desktop build (`net10.0-desktop`) under Xvfb on Linux with the software renderer (`QC_X11_RENDERER=software`),
window height 640, live-resized through 360, 599, 600, 800, 1080 and 1920 px. One sheet per page and theme:

| Sheet | What it shows |
|---|---|
| `quotes-*` | Quotes list (stats, filters, list) |
| `dashboard-*` | Quotes with a quote open (one pane below 1080, list and detail from 1080, side column from 1400) |
| `editor-*` | Quote editor opened from the list |
| `editor-totals.jpg` | (2026-11-02) The editor below the fold at 360 and 1280 px, Light and Dark: line items, the subtotal, tax and total card, and the status actions, reached with Tab (keyboard focus on Mark Sent); window height 800 |
| `flows-round2-light.jpg` | (2026-11-02) Runtime checks of the round-2 fixes at 1280 × 800: the item dialog refusing "1,5" (its "aterials" is the synthetic typing starting before the field had focus), an edited item keeping its place with the 8.25 % totals, the expired banner with Reopen, and Send reporting that no email app opened |
| `clients-*`, `catalog-*` | With a client or category open |
| `settings-*` | Settings (live preview from 1400) |
| `onboarding-*`, `onboarding-step3-*` | First run: welcome, and the quote settings step |
| `fault-retry.jpg` | Error states and Retry (2026-11-04, 1280 × 800, `--qc-fault=load`: the database fails for the first 20 s). Top row: Quotes, Clients, Catalog and Settings in their error states. Middle row, after the window: Settings after its Retry, then Quotes, Clients and Catalog opened again, loaded. Bottom row (Dark): Catalog's error state, then its own Retry loading the categories |
| `wasm-create-and-pdf.jpg` | WebAssembly (Release `browserwasm`, headless Chromium, 1280 × 800, 2026-11-04): first run, New Quote, a line item, Save, the detail, Preview and Download PDF (`QC-2026-0001.pdf`) |
| `wasm-valid-until-and-pdf.jpg` | WebAssembly (Release `browserwasm`, headless Chromium, 1280 × 800, A pass 2, 2026-11-04): a new quote's Valid Until picker open on the Settings default (Nov 18), the editor after picking Nov 25 and adding a line item, the detail ("Valid until Nov 25, 2026") and the preview after Download PDF (the downloaded `QC-2026-0001.pdf` reads "Valid Until: Nov 25, 2026") |
| `editor-valid-until-and-photo.jpg` | Desktop (Debug, Xvfb, A pass 2, 2026-11-04): a past valid-until day refused on Mark Sent (Dark, 1280 px), and a photo thumbnail opened enlarged in the `!PhotoViewer` dialog (Light, 1280 × 1290) |
| `pdf-export.jpg` | The PDF that Export PDF wrote on desktop (2026-11-03, SkiaSharp `SKDocument`, D12), rendered with `pdftoppm` at 90 dpi: QC-2026-0001 from the sample data with 24 more line items, a logo and three photos added; two quote pages (the table breaks after 18 rows and repeats its header row) and the photo appendix |

Reproduce: run the app with `--qc-data=<empty folder> --qc-seed=sample --qc-theme=light|dark --qc-route=<route>`
(Debug only; routes `Dashboard[:detail]`, `QuoteEditor`, `Clients:detail`, `Catalog:detail`, `Settings`,
`Onboarding[:step3|:complete]`; onboarding sheets use no seed, so the first run shows it).

Not covered here: Android, iOS and WinAppSDK at runtime (CI builds them); touch input; screen readers; dialogs and
flyouts (opened by hand on desktop only).
