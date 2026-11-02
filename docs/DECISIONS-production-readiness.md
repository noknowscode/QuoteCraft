# Decisions: production readiness (phase A, Uno 7 foundation)

QuoteCraft moved from Uno.Sdk 6.5.31 to 7.0.0-dev.115 and gained the iOS and Windows (WinAppSDK) heads next to Android,
WebAssembly and desktop. These are the choices that change behaviour on some heads, with what they cost.

## D1. SQLite on every head: SQLCipher, without a key in the browser (2026-11-01)

Decision: every head keeps `SQLitePCLRaw.bundle_e_sqlcipher` 2.1.10 with `Microsoft.Data.Sqlite.Core`. No head needed a
different package.

| Head | Native library | Encryption | Where the data lives |
|---|---|---|---|
| Desktop (Windows, Linux, macOS) | `e_sqlcipher` per runtime | On, random key in `.dbkey` | LocalApplicationData/QuoteCraft |
| Windows (WinAppSDK) | `e_sqlcipher.dll` (x64/ARM64) | On | LocalApplicationData/QuoteCraft (packaged: redirected to the package's LocalCache) |
| Android | `libe_sqlcipher.so` (arm64-v8a, x86_64 in the APK) | On | the app's private files |
| iOS | `SQLitePCLRaw.lib.e_sqlcipher.ios` (static, `__Internal`) | On | the app's sandbox |
| WebAssembly | `e_sqlcipher.a` linked into `dotnet.native.wasm` | Off | IndexedDB (IDBFS) under ApplicationData's LocalFolder |

WebAssembly needed four things (`QuoteCraft.csproj`, `Data/AppDatabase.cs`, `Helpers/AppStorage.cs`):
- **The matching static library.** SQLitePCLRaw's targets link the `net8.0` `e_sqlcipher.a` (built for .NET 8's emscripten)
  on any .NET. In a .NET 10 build every write failed with "disk I/O error". The package's `net9.0` build matches the
  emscripten that .NET 9 and 10 use (3.1.56); the `QuoteCraftLinkMatchingSqlCipher` target swaps it in before the link.
- **IDBFS** (`WasmShellEnableIDBFS`). Without it the browser file system is in memory and nothing survives a page load.
  Uno writes it to IndexedDB every 10 seconds and when the page unloads, so the last few seconds before a tab is killed
  can be lost.
- **The data folder under `ApplicationData.Current.LocalFolder`**, awaited through a `StorageFolder` call before the first
  `System.IO` access (`AppStorage.EnsureReadyAsync`): only those folders are persisted.
- **No WAL.** It is for concurrent readers, and the browser runtime has one thread.

The key is off in the browser, as the original code intended (its `#if __BROWSERWASM__` never compiled: no SDK defines
that symbol): the key would sit in the same IndexedDB as the database, and SQLCipher's key derivation would run on the
single UI thread at every open.

Tradeoff and open points:
- WASM0001 (SQLitePCLRaw declares varargs `sqlite3_config` and `sqlite3_db_config`, which WebAssembly cannot call) is
  reported as a message. Microsoft.Data.Sqlite does not call them; creating, reading and writing quotes worked in the
  browser.
- On the other heads the key is a file next to the database. That protects a copied database file, not a copied app
  folder. Moving the key to the platform store (Keychain, Android Keystore, DPAPI) is a code-review item.
- When SQLitePCLRaw ships targets for .NET 10 (or the app moves to a later SQLitePCLRaw), delete the swap target.
- Verified at runtime: desktop (Linux, Xvfb) and WebAssembly (headless Chromium: a quote created, still there after
  the browser was closed and reopened). Android is build-verified (the APK carries `libe_sqlcipher.so`); iOS and
  WinAppSDK are built by CI only.

## D2. PDF export: QuestPDF on desktop and Windows; a clear note elsewhere (2026-11-01)

Decision: QuestPDF stays the PDF engine, referenced by the `net10.0-desktop` and WinAppSDK heads only (`QUESTPDF`
symbol). On Android, iOS and WebAssembly, `IPdfGenerator` is `UnsupportedPdfGenerator`, `IShareService.CanExportPdf` is
false, and the pages show "PDF export is available in the desktop and Windows apps." where the Download PDF (dashboard
preview) and Export PDF (editor) buttons would be. The Send dialog drops "Download PDF Only" there and opens the email
without an attachment.

Reason: QuestPDF ships native libraries for Windows, Linux and macOS only. Before this change the mobile and browser
heads compiled QuestPDF and would have failed at runtime. The old browser-download branch never compiled.

What SkiaSharp's own PDF backend would take (evaluated, not implemented):
- **Rendering is possible.** SkiaSharp 4.151.1, which Uno 7 already ships to every head, exports
  `sk_document_create_pdf_from_stream` in its Android `libSkiaSharp.so` and WebAssembly `libSkiaSharp.a` (checked here;
  the iOS native package was not inspected). `SKDocument.CreatePdf` needs no new package.
- **It is not small.** The QuestPDF layout would have to be drawn by hand on `SKCanvas`: the header with logo and
  contact lines, the line-item table with alternating rows and page breaks, totals, wrapped notes, the footer and
  watermark, and the photo appendix (about 200 lines). The browser has no system fonts, so DM Sans must be loaded from
  the package (`ms-appx:///Assets/Fonts/...`) into an `SKTypeface`.
- **Delivery differs per head.** WebAssembly needs a browser download (JS interop). Android and iOS need a
  `FileSavePicker`: Uno's share sheet (`DataTransferManager`) carries text and URIs, not files.
- Android and iOS could not be verified at runtime here.

Options for the owner:
1. Keep this state (smallest; honest about the gap).
2. Add an `SKDocument` generator for Android, iOS and WebAssembly behind `IPdfGenerator`, keeping QuestPDF on desktop and
   Windows. Two layouts to keep in step.
3. Replace QuestPDF with the `SKDocument` generator everywhere: one layout, one fewer package, and no QuestPDF Community
   licence terms to track, at the cost of rewriting a working desktop renderer.

Verified: on desktop (Linux) Export PDF wrote `exports/QC-2026-0001.pdf` with the business name, line item, tax,
total and notes. Opening it needs `xdg-open`, which this machine lacks (logged).

## D3. Local-first; sign-in, sync and billing only with a Supabase project (2026-11-01)

Owner decision (round 2 plan): QuoteCraft runs fully on local data with no account.

- `Data/Remote/SupabaseConfig` no longer holds constants. The service-role key is gone from the app: it bypasses
  row-level security and belongs to the Edge Functions in `supabase/` only.
- The project URL and anon key come from the `Supabase` section of `appsettings.json` (empty in the repo), or from
  `QUOTECRAFT_SUPABASE_URL` and `QUOTECRAFT_SUPABASE_ANON_KEY`. `Supabase:CustomerPortalUrl` replaces the placeholder
  Stripe portal link.
- Without them: no session is restored, the Supabase client, share links and checkout refuse to run, and the
  connectivity poll (which called httpbin.org every 15 seconds) does not start; when it runs, it probes the configured
  project. Settings hides the upgrade banner, the subscription section and Sign Out, and says the data stays on this
  device.
- **No plans without billing.** `FeatureGateService` lifts the Free limits (5 quotes a month, 10 clients) and the PDF
  watermark when billing is not configured, and keeps cloud sync off. An upgrade prompt that cannot be acted on would
  be a dead end. If the owner prefers to demo the Free limits in local builds, `HasPlans` is the one switch, and the
  prompts then need a non-billing message.

Open points for the next phases (behaviour with a Supabase project configured, unchanged here):
- `ISyncService.StartAsync` is never called, so sync never runs.
- The Auth page is reachable only through Sign Out; there is no Sign In entry point.
- The refresh token is stored in a plain file (`.auth_token`), not in platform secure storage as the Architecture Brief
  says.
- The trimmed WebAssembly build serializes the Supabase payloads by reflection (IL2026, suppressed because
  `LinkerConfig.xml` preserves the assembly); cloud mode in the browser has not been run.

## D4. Smaller Uno 7 changes (2026-11-01)

- **Fonts (Uno Themes 9).** The type ramp reads `DefaultFontFamily`, and `FontOverrideSource` no longer applied, so every
  page fell back to Roboto. The override is an inline `FontOverrideDictionary` that sets `DefaultFontFamily` and the
  `Material*FontFamily` keys; weights resolve through `DMSans-Regular.ttf.manifest`.
- **Onboarding.** It never showed on a first run, on 6.5 as on 7: `ShellModel`'s constructor raced the default route.
  `App.OnLaunched` now opens it after the first navigation completes. Opening it earlier (from MainPage) left the
  WebAssembly splash screen up for good. Skip and Create First Quote now go to `Main` and `Main/QuoteEditor`.
- **Navigation from code-behind.** The dashboard's New Quote dialog and the client page's quote rows took `INavigator`
  from the root service provider; under Uno.Extensions 8 the dialog closed without creating a quote. They use the
  page's navigator now.
- **Pickers.** The WinAppSDK owner window is set under `#if WINDOWS` (the old `#if !__BROWSERWASM__` was always true),
  `SuggestedStartLocation` is set (WinUI requires it), and picked files are read through `StorageFile` streams.
- **Trimming.** `MvuxHelper` reflects over MVUX-generated view models (IL2070) and the Supabase payloads are
  serialized by reflection (IL2026); both live in this assembly, which `Platforms/WebAssembly/LinkerConfig.xml`
  preserves, so the warnings are suppressed with that justification.
- **WebAssembly splash.** Toolkit 11's `ExtendedSplashScreen` requests `appmanifest.js` in lower case while the bootstrapper
  serves `AppManifest.js`. On a case-sensitive host it logs "Error while getting native splash screen" and shows its
  loading template instead; the app is unaffected.

# Phase B: UI hardening (2026-11-02)

## D5. Four layouts with the default breakpoints; one pane below 1080

Every page answers the four `utu:Responsive` categories (Narrow below 600, Normal 600 to 800, Wide 800 to 1080, Widest
1080 and up); the table is in the README under Layouts.

- **Quotes, Clients and Catalog are one pane below 1080.** The list fills the page; picking an item opens it over the
  list, with a back button (`BackToList` in each model clears the selection). Before, the detail pane was simply hidden
  below 800, so a tap on a quote or client did nothing on a phone, and the 380 px list column clipped at 360 px.
- **Side by side from 1080.** At 800 the rail leaves 644 px; a 380 px list would leave the detail 264 px, too little
  for the action row or the line-item table.
- **One custom layout, `SplitLayout` (Widest at 1400), for splits inside a pane**: the quote's client/details/totals
  column and the client's summary sit beside the main column, and Settings shows its live preview, only from 1400.
  At 1080 the detail is about 470 px wide, and a 340 px side column left the line-item table below 130 px. Below 1400
  these stack.
- **Navigation:** bottom tabs below 800, the side rail from 800 (unchanged). Notifications and the rail's theme menu
  are rail-only; below 800 the theme is in Settings. Notifications have no entry point on phones (open point).

## D6. Theme: System by default, Light or Dark by choice, through the Uno.Extensions theme service

`UseThemeSwitching()` (already in Uno.Extensions.Hosting, no new package) registers the window's `IThemeService`,
which applies the theme and saves it. Settings has an Appearance row (System default, Light, Dark) and the rail's
Theme button opens the same three choices; the old toggle flipped Light/Dark and was forgotten at the next launch.

- Colours set from code read the element's theme (`Helpers/ThemeResources`) and are set again on
  `ActualThemeChanged` (card hover/selection in `InteractionHelper`). The status pills are a control whose colours are
  `ThemeResource` setters; the old converters cached the first theme's brushes, so pills kept Light colours in Dark.
- Contrast: text on the amber accent is now `OnAccentBrush` #1A365D (5.6:1; the slate band colour gave 4.0:1), and
  hover/pressed ambers are lighter so the label stays above 4.5:1. `OnSurfaceVariant` moved from #6B7280 to #5F6673
  (Light) and from #9CA3AF to #AAB1BD (Dark) to reach 4.5:1 on the search fields and surface-variant panels. Draft
  and Expired pill text moved to #4B5563 (Light) and #D1D5DB (Dark); Expired was 2.3:1.
- Hex left on purpose: the palettes themselves (`ColorPaletteOverride.xaml`, `StatusColors.xaml`, brand colours in
  `App.xaml`) and QuestPDF's colours in `Services/PdfGenerator.cs`, which draw a printed page, not the UI.

## D7. Smaller Phase B fixes

- **Quote editor back arrow** is an explicit action on the navigation bar and falls back to the quotes list when
  there is no page to go back to (the editor opened straight from onboarding had none). Tapping the selected tab now
  navigates to it, so the Quotes tab works after the editor (the editor is not a tab, so Quotes stayed selected).
- **Relative dates** handle the future: a valid-until date four weeks ahead read "Just now"; it reads "In 4 weeks"
  (`Helpers/RelativeDate`, tested). Day words (Today, Tomorrow) use the local day.
- **Dead or broken controls:** Edit client did nothing (it read a selection only an unwired tap handler set); the
  catalog item dialog opened a second dialog for Delete, which a ContentDialog cannot do (Delete is now the dialog's
  own button); "+ Add Catalog" in Settings was text with no action (now "Manage catalog", which opens Catalog); the
  dashboard's Upgrade buttons had no action (they open Settings, where plans live).
- **States:** every list and detail feed has Progress, None and Error templates, and Retry reaches the page model by
  `ElementName` (the error template's DataContext is the exception). The editors show validation messages (dialogs
  no longer just refuse to close), Settings and the quote editor show a saving bar, and Settings says when it saved.
