# UI evidence (Phase B, 2026-11-02)

Debug desktop build (`net10.0-desktop`) under Xvfb on Linux, window height 640, live-resized through 360, 599, 600,
800, 1080 and 1920 px. One sheet per page and theme:

| Sheet | What it shows |
|---|---|
| `quotes-*` | Quotes list (stats, filters, list) |
| `dashboard-*` | Quotes with a quote open (one pane below 1080, list and detail from 1080, side column from 1400) |
| `editor-*` | Quote editor opened from the list |
| `clients-*`, `catalog-*` | With a client or category open |
| `settings-*` | Settings (live preview from 1400) |
| `onboarding-*`, `onboarding-step3-*` | First run: welcome, and the quote settings step |

Reproduce: run the app with `--qc-data=<empty folder> --qc-seed=sample --qc-theme=light|dark --qc-route=<route>`
(Debug only; routes `Dashboard[:detail]`, `QuoteEditor`, `Clients:detail`, `Catalog:detail`, `Settings`,
`Onboarding[:step3|:complete]`; onboarding sheets use no seed, so the first run shows it).

Not covered here: Android, iOS, WebAssembly and WinAppSDK at runtime; touch input; screen readers; dialogs and flyouts
(opened by hand on desktop only).
