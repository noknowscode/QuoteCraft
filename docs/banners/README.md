# QuoteCraft gallery banners

Banners for the Uno Platform Studio samples gallery, made from real captures of **QuoteCraft** (quoting and invoicing
for contractors) running as a Release WebAssembly build in headless Chromium. Nothing in them is redrawn: the UI, data
and copy are the app's own seeded sample data (Northside Plumbing & Heating, five clients, seven quotes, the
65-item catalog). Styling sits only around and on top of the captures: device frames drawn in CSS, crop, perspective,
falloff masks, light, and backdrops tinted from the app's palette.

The look is the contractor's drafting desk: slate blueprint grounds with drafting grids, warm and cool paper grounds,
and amber light where the call to action is. The app's own brand motif, a slate band over a thin amber rule, returns as
the split line of `stylized-split` and the rule under the title.

Open `index.html` for the contact sheet.

## Finals

All in `finals/`: every banner at 1x (1200 × 630) and 2x (2400 × 1260), and two heroes at 1920 × 1080.

| Banner | Family | What it shows | Source captures (state · theme · viewport · DPR) |
|---|---|---|---|
| `showcase-desk` | showcase | Desktop window with the Quotes page, Kitchen Renovation open beside the list, and two phones over its right edge: the quote preview and the catalog; warm paper | `desk-quotes-light` (Quotes, Kitchen Renovation open · Light · 1440×900 · 3, stored 2x) ; `phone-preview-dark` (Kitchen Renovation preview · Dark · 390×844 · 3, stored 2x) ; `phone-catalog-light` (Catalog · Light · 390×844 · 3, stored 2x) |
| `showcase-phones` | showcase | Three phones on blueprint: the quotes list (Dark), the quote preview with its slate band and amber Send Quote (Light, centre, raised), the catalog (Dark) | `phone-quotes-dark`, `phone-preview-light`, `phone-catalog-dark` (390×844 · 3, stored 2x) |
| `showcase-daynight` | showcase | Two tablets, Kitchen Renovation's preview by day and by night; the ground turns from paper to night | `tab-preview-light`, `tab-preview-dark` (Preview · 820×1180 · 3, stored 2x; seconds apart) |
| `focus-quotes` | focused | The quotes list: three stat cards with their sparklines, the count for every status, the filter chips and the first quotes with their status pills, laid back on warm paper | `list-light` (Quotes, one pane · Light · 1060×1000 · 3, crop 876×980 CSS px) |
| `focus-preview` (+ hero) | focused | The on-screen quote preview: QC-2026-0001 with its slate band, line items and total, lifted off a blueprint table; the preview's action bar (Download PDF, amber Send Quote) in front | `doc-kitchen` (Preview · Light · 1440×1200 · 3, the document card 686×850) ; `preview-bar` (same capture, action bar 346×60) |
| `focus-editor` | focused | The quote editor (Dark): Line Items with From Catalog and Add Item, four items, subtotal, tax and total, Add Photo; a night slab on blueprint | `editor-dark` (Full editor · Dark · 1440×1100 · 3, crop 704×1016, line items to Add Photo shown) |
| `focus-catalog` | focused | The catalog: the seven category tiles in front, Electrical's twelve priced items behind them, softened; cool paper | `cat-tiles`, `cat-list` (Catalog, Electrical open · Light · 1440×900 · 3) |
| `stylized-split` (+ hero) | stylized | One Quotes page with Kitchen Renovation open, Light and Dark, on one turned plane cut by a single diagonal in the app's amber rule | `desk-quotes-light`, `desk-quotes-dark` (1440×900 · 3, stored 2x; taken seconds apart) |
| `stylized-sheets` | stylized | Five of the seeded quotes as the app previews them, dealt across a blueprint table; Kitchen Renovation on top, the others softened as out of focus | `doc-kitchen`, `doc-office`, `doc-bathroom`, `doc-drain`, `doc-water` (Preview of each · Light · 1440×1200 · 3) |
| `stylized-mosaic` | stylized | A tilted wall of whole elements, Light and Dark alternating: brand band and nav, stat cards, status counts and chips, quote cards (Accepted, Declined), the preview's band, line items, totals, catalog tiles, search and New Quote | `m-*` (12 tiles) from `desk-quotes` Light and Dark, `list` Light, `preview` Light and `catalog` Dark (all DPR 3, stored 3x) |
| `focus-preview-titled` | titled (**optional**) | `focus-preview` with the display name in DM Sans Bold (the app's face; the rail's "QC" weight) over a short amber rule | as `focus-preview` |

**Strongest: `focus-preview` and `stylized-split`** (both have heroes).
- `focus-preview` is the product in one look: the quote a contractor sends, with its slate band, priced lines and
  total, and the amber Send Quote that finishes the job. It reads from a distance and stays sharp at 2x.
- `stylized-split` shows the whole working screen (list with status pills and sparklines, the open quote) in both
  themes at once, and the cut is the app's own amber rule, so it is unmistakably QuoteCraft rather than a generic split.

## Palette

Every colour comes from `QuoteCraft/Styles/ColorPaletteOverride.xaml` and the status brushes in `QuoteCraft/App.xaml`;
they are listed in `src/frames.css`. Light: Primary #2B4C7E, PrimaryContainer #D6E4F0, OnPrimaryContainer #0A1929,
Secondary (amber) #F59E0B, SecondaryContainer #FEF3C7, Background #F9FAFB, Surface #FFFFFF, SurfaceVariant #F3F4F6,
OnSurface #1F2937. Dark: Primary #93B5E1, PrimaryContainer #1E3A5F, SecondaryContainer #92400E, Background #111827,
Surface #1F2937, SurfaceVariant #374151. Grounds mix two of these at a time (`color-mix()` in the compositions); grain,
grids and shadows are the same values at reduced alpha. The titled variant sets the name in OnPrimary #FFFFFF.

## How it was made

1. **Build.** The `studio` branch (HEAD 3c6a8ec, Uno.Sdk 7.0.0-dev.132) was exported with `git archive` to a scratch
   folder (the repository was not touched) and published:
   `dotnet publish QuoteCraft/QuoteCraft.csproj -f net10.0-browserwasm -c Release -p:QuoteCraftTargetFrameworks=net10.0-browserwasm -p:CustomBeforeMicrosoftCommonTargets=src/tools/sample-hooks.targets`.
   QuoteCraft's sample data is Debug-only (`Helpers/SampleData.cs`, `App.Verification.cs`, `--qc-seed=sample`), so a
   plain Release build opens on empty onboarding. `sample-hooks.targets` adds the `DEBUG` symbol (and the app's own
   `NO_UNO_STUDIO`, so no Studio toolbar) to this Release build: the app's own seeder runs, nothing else changes.
2. **Capture.** `src/tools/cap.js` (Playwright, Chromium) loads the build, hands `--qc-seed=sample` to the app's Main
   (the bootstrapper passes no arguments, so its script is patched in flight), and plays `src/tools/session.json`:
   one session, DPR 3, the system colour scheme switched for Light and Dark (the app follows it), the viewport
   resized for desktop, one-pane, tablet and phone layouts, pointer parked outside the page, and each shot taken
   9 to 15 s after the last interaction, once two frames 700 ms apart are identical.
3. **Crop.** `src/tools/crop.py` cuts `src/cap/` from those captures: elements at 3x, whole screens for the frames
   downsampled to 2x. Nothing is upsampled.
4. **Compose and render.** One HTML file per banner in `src/` (heroes are their own files), shared frames and palette in
   `src/frames.css`, DM Sans in `src/fonts/`. `src/tools/render-all.sh` renders them with `src/tools/render.js`
   (`NODE_PATH` must reach `playwright`).

## Rules kept

- **Real pixels.** Every screen and element is a capture of the running app; no UI, data or copy is redrawn or retouched.
- **No enlarging.** At the 2x export no capture is shown above its native pixels, including the near edge of every
  perspective (largest: the preview's action bar at 2.5 device px per CSS px, from a 3x capture; the 2x-stored screens
  peak at about 1.5).
- **Clean states.** Seeded data; no spinners, scroll indicators, focus rings, hover states or Studio toolbar. A first
  session's shots were discarded: at DPR 3 the app dropped the click after a list or tab selection, so previews and the
  editor never opened and pressed cards stayed grey. `cap.js` now checks a pixel of each expected state and clicks
  again when it is missing.
- **Generic frames.** Window bars and bezels are plain CSS: no notch, camera, buttons or logo.
- **Safe area.** Key content sits inside the central 90%; the mosaic wall and the sheets run off the frame by design and
  dissolve or are cut by the edge, never their focal element.

## Uncertain

- **The build is Release with the Debug sample-data hooks compiled in** (above). The captured UI and data are the app's
  own, but it is not byte-for-byte the shipped Release build.
- **Clock.** All captures come from one session (2026-11-09, about eleven minutes). The Draft quote's age moves with it:
  "Just now" in the desktop frames, "1m ago" in `focus-quotes`, "8m ago" on the Dark quotes phone in `showcase-phones`.
  No banner shows two different ages side by side.
- **Small text in frames.** The showcase banners show whole screens, so their UI text is below 10 px at 1x, as in the
  house-style banners. The focused and stylized banners keep reading text at about 10 px or more, except where softened
  on purpose (the back sheets in `stylized-sheets`, the item list in `focus-catalog`).
- **Tablet stat cards.** At 820 px the app squeezes its three stat cards, so the sparklines in `showcase-daynight` are
  narrow; that is the app's real layout at that width.
- The titled variant uses DM Sans Bold, the app's only face (the rail's "QC" mark is its bold weight).
