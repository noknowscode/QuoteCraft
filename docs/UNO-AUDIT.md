# Uno audit: QuoteCraft (2026-11-04)

Branch `feat/production-readiness`, from `0fcdae4`. The uno-audit pass: hygiene, pattern conformance, the semantic
lint gate, performance, the build gate and a runtime re-check. It is not a feature pass; the functional gaps in
`docs/COMPLETENESS.md` stay for the next pass. Settled decisions D1 to D13 stand.

## Skills applied

| Skill | Pattern it governs here |
|---|---|
| uno-platform-agent | Single project on Uno.Sdk, MVUX for app state, platform symbols, DI through the host |
| uno-scaffolding (+ runtime gotchas) | Code-behind budget, semantic-first colours, `{Binding}` on MVUX pages, the gotchas checked below |
| mvux | Commands from model methods, states for two-way fields, `Feed.AsyncEnumerable` over service events, `Refresh` by `ElementName` in error templates, no FeedView over a primitive feed |
| uno-navigation | Dialogs and the drawer as `!` routes, results through `NavigateBackWithResultAsync`, `ResultDataViewMap` |
| uno-toolkit | `CardContentControl`, `ChipGroup`, `CommandExtensions`, `VisualStateManagerExtensions`, `NavigationBar`, `DrawerFlyoutPresenter` |
| uno-material | Role brushes, Material card and chip styles, lightweight keys instead of templates |
| winui-xaml | Keyboard and screen-reader access for list items (buttons, `AutomationProperties`), WinAppSDK XAML compiler limits |
| dotnet-csharp | Async without `async void`, no reflection for the models, disposal of event subscriptions |

## Semantic lint gate

`xaml-semantic-lint.ps1 -Path QuoteCraft`, gating rules:

| Rule | Before | After |
|---|---|---|
| CARD | 71 | 0 |
| HEX | 21 | 0 |
| TOKENTHEME | 12 | 0 |
| BACKBAR | 0 | 0 |
| CODEBEHIND | 74 | 0 |
| OVERLAY | 2 | 0 |
| RESPONSIVE | 1 | 0 |
| BUILTIN | 0 | 0 |
| ICON | 0 | 0 |

Info after: TOKEN 100 refs to 26 app brushes, ADAPTIVE 0 (utu:Responsive 164), TEMPLATE 4, WORKAROUND 0.

Nine hits are kept with a reason in a comment above them:
- `allow backbar` (5): the Cancel button of each form dialog closes the dialog (`Navigation.Request="-"`); a dialog has
  no navigation bar.
- `allow card` (2): the job photo and the logo thumbnail are Borders that clip an image to rounded corners.
- `allow codebehind` (1): `App.OnUnhandledException`, the app-wide exception hook; `Application` has no XAML surface.
- `allow responsive` (1): a Debug-only verification hook picks the tab bar that is laid out (`ActualWidth > 0`); it is
  not a breakpoint.

## Findings and fixes, by category

### Code-behind and dialogs built in C# (the core of this pass)

Page code-behind went from about 1,500 lines to `InitializeComponent` (`*.xaml.cs` overall: 2,000 lines to 717, of
which `App.xaml.cs` is 461 and the two controls 93).

| Page | Before | After |
|---|---|---|
| MainPage | 28 handlers and Visibility toggles; a notifications overlay filled from code | `MainModel` commands and feeds; `utu:VisualStateManagerExtensions.States="{Binding ShellState}"` for the rail and the offline banner; the overlay is the `!Notifications` route in a right-hand drawer (`DrawerFlyout`, registered as the navigation `Flyout`) |
| DashboardPage | 13 handlers, two page dependency properties, four dialogs built in C# | `DashboardModel` commands; filters are a `ChipGroup` bound to `SelectedFilter`; the selected card comes from `QuoteListItem.IsSelected`; stripes from `LineItemRow.IsAlternate`; overdue quotes expire on the first load |
| QuoteEditorPage | 6 handlers, the unsaved-changes dialog, the photo picker | `QuoteEditorModel.Back`, `AddLineItem`, `EditLineItem`, `OpenCatalog`, `AddPhoto`; client suggestions are a feed; each line item is a button |
| ClientsPage | 6 handlers, the client dialog built in C# | `ClientsModel` commands; quote rows are buttons |
| CatalogPage | 5 handlers, the item dialog built in C# | `CatalogModel` commands; `CommandExtensions` on the repeaters |
| SettingsPage | a Loaded handler that waited 300 ms, the logo picker, four Visibility toggles | `SettingsModel.UploadLogo`; thumbnails bind to `LogoPath` |
| LineItemEditorDialog, CatalogBrowserDialog | 8 handlers | models (`LineItemEditorModel`, `CatalogBrowserModel`) |

Dialogs are XAML `ContentDialog`s with MVUX models in `Presentation/Dialogs`, registered as routes in `App.xaml.cs` and
opened from models with the `!` qualifier (`Helpers/DialogNavigation`): `Confirm` (deletes, plan limits, unsaved
changes, failure notices), `NewQuote`, `SendQuote`, `LineItemEditor`, `CatalogBrowser`, `ClientEditor`,
`CatalogItemEditor`. Forms validate in their model and stay open with a message; the result goes back through
`NavigateBackWithResultAsync`. `Controls/DialogHeader` replaces the gradient banner that `DialogHelper` drew in code.
Catalog items are now added by the browser's own model; the dashboard and the editor had duplicate copies.

Defects found and fixed on the way:
- Re-tapping the selected Quotes tab after the editor replaced the whole `MainPage`, rail included, with the dashboard
  (reproduced on `0fcdae4`). `MainModel.OpenTab` navigates `./-/<route>`: the shell stays and the editor closes.
- The rail's unread badge was a `FeedView` over an `IFeed<int>`, which never leaves its progress template (mvux NEW-02),
  so it never showed. It is an `InfoBadge` bound to the count.
- The rail's theme label read a page-scoped `IThemeService`; `ThemeChoiceNotifier` now tells the shell when Settings
  changes the theme, and the icon follows the window through a `ThemeResource` glyph.
- Deleting a catalog item asked through a second dialog that a code-built `ContentDialog` could not open; the question
  is now its own route after the item dialog closes.

### Colours and cards

- Every colour literal moved from `App.xaml` to `Styles/BrandColors.xaml` (a palette file). Brand brushes that are the
  same in both themes say so (D6): `*Fixed` where their ground is fixed (the slate band, the amber, a photo),
  `AppBarAccentInvariantBrush` for the accent that is also drawn on themed surfaces.
- Rounded containers are `utu:CardContentControl` with styles in `Styles/Cards.xaml`: `StaticCardStyle`,
  `PaddedCardStyle`, `TonalCardStyle`, `BannerCardStyle`, `AvatarCardStyle`, `ElevatedSurfaceCardStyle`. The Material
  hover, pressed and focus washes are off app-wide (lightweight keys in `App.xaml`); list cards keep
  `Helpers/InteractionHelper` for hover, selection and Enter/Space, now on `Control`.
- The status counts reuse `StatusPill` with a `Count`, so they carry an icon as well as a colour. The onboarding step
  bars are `Rectangle`s (drawn bars, not containers).

### Hygiene (dead code)

Removed: `NotificationsModel`/`NotificationsPage` as dead code (they are now the live drawer), `DialogHelper`,
`MvuxHelper` (models reached by reflection; the debug hooks use the generated view model's `Model`),
`ErrorReporter.RunAsync`/`ShowAsync`, `UpperCaseConverter`, `ChevronConverter`, `AppBarPillButtonStyle` and its two
brushes, the `BadgeSize` token, `App.Services`, InteractionHelper's `Border` path and legacy `ApplyHover`/`RemoveHover`,
the unused `ICatalogItemRepository` and profile repository in two models, the template's commented-out logging lines
and comments that restated the next line.

### Pattern conformance (runtime gotchas checked)

- `{Binding}` throughout the MVUX pages; `x:Bind` only in `DialogHeader`, a plain `UserControl`.
- Error templates reach `Refresh` or the page model by `ElementName` (the template's DataContext is the exception).
- Bool to Visibility always through `BoolToVisibilityConverter` (WinUI's `{Binding}` does not convert), with
  `FallbackValue=Collapsed` on the new bindings.
- The picker opens on the UI thread from models (`Helpers/ImagePicker` through `IDispatcher`).
- No attached-property setters in styles based on Toolkit templates (microsoft-ui-xaml#6388), except the drawer's
  `DrawerLength`, which carries the Toolkit's definition-only workaround style; no `Translation`/`Shadow` on Toolkit
  controls (WMC0612, found by CI).
- The Material card template passes its alignment to its own fill, so a fixed-size card that is centred or top-aligned
  shrinks its fill to its content. Avatars stay `Stretch` (a fixed size centres them) and the Settings logo tile sits in
  a top-aligned Grid. Worth a gotcha entry.

## Stack currency (reported, not changed)

| Package | In use | Newest stable |
|---|---|---|
| Uno.Sdk | 7.0.0-dev.115 (owner decision) | 6.7.30 (newest dev 7.0.0-dev.132) |
| Microsoft.Data.Sqlite.Core | 10.0.3 | 10.0.14 |
| SQLitePCLRaw.bundle_e_sqlcipher | 2.1.10 | 2.1.11 (check whether it ships .NET 10 WebAssembly assets; D1's swap target could go) |
| xunit | 2.9.3 | 2.9.3 (v3 is the separate `xunit.v3`) |
| xunit.runner.visualstudio | 3.0.2 | 4.0.1 |
| Microsoft.NET.Test.Sdk | 17.13.0 | 18.11.0 |
| NSubstitute | 5.3.0 | 6.2.0 |

The SDK brings Uno.Extensions 8.0.0-dev.150, Toolkit 11.0.0-dev.157, Themes 9.0.0-dev.61 and SkiaSharp 4.151.1. All heads
target .NET 10.

## Performance

- Lists: the quote, client and catalog lists are `ItemsRepeater`s in a `ScrollViewer` (virtualized from 800 px). Below
  800 the dashboard's stats, filters and list scroll as one page, so the repeater sees unbounded height and realizes
  every card; fine for a contractor's quote count, worth a `ListView` if lists grow to hundreds. The drawer's list is a
  `ListView`. The editor's line items and history are `ItemsControl`s (short lists).
- The dashboard reads the quotes once per change (`GetCachedQuotesAsync`) and derives the list, counts and analytics
  from that read; a selection change re-filters the cached list without a database read.
- No per-frame work: the sparkline is a XAML `Polyline` redrawn on size changes (debounced); the PDF is drawn once
  with SkiaSharp. The connectivity poll only runs with a Supabase project (D3).
- Startup: storage readiness and the optional seed run before the first navigation, as before; the expiry check moved
  from the dashboard's `Loaded` into the first quote read.

## Build and tests

- Release builds, 0 warnings, 0 errors: `net10.0-desktop`, `net10.0-browserwasm`, `net10.0-android` (Linux). iOS and
  WinAppSDK are built by CI.
- Unit tests: 137 passed, 0 failed (`QuoteCraft.Tests`).
- CI: the WinAppSDK build check failed on `6093eeb` (WMC0612: `Translation` set on a Toolkit control) and passes from
  `2f37422`, with the unit tests; the other heads' jobs were green on the commits that finished.

## Runtime checks (Debug desktop, Xvfb, `--qc-data/--qc-seed/--qc-theme/--qc-route`)

- Shell: notifications drawer (list, Mark all read, the badge clears), theme menu (System, Light, Dark; label and icon
  follow), rail collapse and expand, tab bar at 390 px, tab re-tap after the editor (rail kept).
- Dashboard (1440 Light and Dark, 390 Dark): cards, status pills, filter chips (Draft filters), selected card, inline
  Edit and Done, a row tap opens the line-item dialog, New Quote (validation, client suggestions, Create opens the
  editor), Send dialog, Delete confirmation, Preview, one-pane back to the list.
- Editor: Add Item (validation, quantity buttons, running total, Save), edit and Delete an item, From Catalog (two items
  added, the list reloads on close), Back.
- Clients: Edit Client (email validation, Save updates the detail), Delete confirmation, a quote row opens the editor.
- Catalog (Dark): item dialog (category preselected, "1,5" refused, price saved), Delete Item then the second question,
  category selection.
- Settings (Light and Dark at 1440), Onboarding welcome and step 3 (390, Dark).
- Not exercised: the image picker (it opened from the model on the UI thread, but this machine has no DBus session for
  the desktop file portal), the Auth page (reachable only through Sign Out with a Supabase project), touch, screen
  readers, the other heads at runtime.

## Remaining (with size estimates)

- `DashboardModel` is 498 lines (list, analytics, detail, preview, send, inline edit). Split into a list model and a
  detail model with their own regions: about a day, with re-verification of the four layouts.
- `InteractionHelper` paints list-card states from code; a Material selectable card (or a `ListView` with lightweight
  item keys) would replace it: half a day.
- Settings' theme row does not hear changes made from the rail (pre-existing; `ThemeChoiceNotifier` could feed it, with
  care not to loop through its `ForEach`): an hour.
- Literal table column widths (60, 90, 120, 140) in Dashboard and Settings; the `x:Double` tokens for them exist but
  `ColumnDefinition` needs `GridLength` resources: an hour, with a WinAppSDK check.
- Three app `ControlTemplate`s (`AccentButtonStyle`, `InlineTextBoxStyle`, `SideNavTabBarItemStyle`) could become
  `BasedOn` Material styles with lightweight keys: half a day.
- New Quote keeps its "Enter a title" message until the next Create after the title is typed: a small model change.

## Proposed decisions (for the owner)

- D14. Dialogs are XAML `ContentDialog` routes with models, opened with the `!` qualifier; forms keep their actions in
  the content so the model can validate before closing; Cancel uses `Navigation.Request="-"`.
- D15. `!` routes to a Page open in `DrawerFlyout` (a right-hand drawer, 420 px) instead of the full-window
  `NavigationFlyout`.
- D16. Cards are `CardContentControl`s with the styles in `Styles/Cards.xaml`; the Material state washes are off
  app-wide, and raised cards use the Material elevated card.
