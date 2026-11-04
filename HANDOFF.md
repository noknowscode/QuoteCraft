# Handoff: QuoteCraft (2026-11-04, A pass 2)

Branch `feat/production-readiness`. Read first: `docs/COMPLETENESS.md` (verdict and what is proven), then
`docs/DECISIONS-production-readiness.md` (D1 to D22; D17 to D20 and D22 are proposed), then `docs/UNO-AUDIT.md`.

## Where it stands

- Completeness: Complete with gaps, 0 Blockers, 0 functional Majors. Head Majors: WinAppSDK, Android and iOS are
  CI-built, not run here. 7 Minors open (see COMPLETENESS, Minors).
- A pass 2 (the code review's closed list) is done: the Debug hooks in `App.Verification.cs`, `SettingsModel` split in
  three, currency as `Services/CurrencyService`, one notifications entry, `QuoteLineItemDialogs` and `QuoteDelivery`,
  failures reported, dead members and `null!` gone, one failure helper, editor Export and Mark validated, expiry once,
  model tests for sign-in and the dialogs, stale comments. Then F1.8 (the valid-until date in the editor, D20), the
  Viewed filter and count removed (D21), a photo opens enlarged (F14.8 in part, D22), the README's claims corrected.
- Structure: the Quotes page is `DashboardModel` (the list) and `QuoteDetailModel` (the open quote, route
  `./QuoteDetail`), sharing `Services/QuoteBoard`. Every new quote is made by `Services/QuoteCreationService`; quotes go
  out through `Services/QuoteDelivery`; status rules are `Services/QuoteStatusService`'s. Dialogs are `!` routes with
  models (D14), the photo viewer included (`!PhotoViewer`).
- Tests: 301 (`dotnet test QuoteCraft.Tests/QuoteCraft.Tests.csproj -p:QuoteCraftTargetFrameworks=net10.0-desktop`),
  137 of them page and dialog model tests over real temporary SQLCipher databases (`ModelHarness`, `FakeNavigator`).
  The harness drops xUnit's synchronization context: on it, a state update racing a state's first value can be lost.
- States: `Helpers/WholeFeedViewStateSelector` (implicit FeedView style in `App.xaml`) sets the data, error and progress
  states on every message; keep it unless MVUX changes that.
- Accessibility: 194 automation names over 6,277 XAML lines (30.9 per 1,000).
- Lint gate (`xaml-semantic-lint.ps1 -Path QuoteCraft`): 0 on every gating rule.
- Evidence: `docs/evidence` (page sheets, `fault-retry.jpg`, `wasm-create-and-pdf.jpg`, and from A pass 2
  `editor-valid-until-and-photo.jpg`, `wasm-valid-until-and-pdf.jpg`).

## Debug hooks (Debug builds only, `App.Verification.cs`)

`--qc-data=<folder>`, `--qc-seed=sample`, `--qc-theme=light|dark`, `--qc-route=<route>[:detail]` (`Dashboard`,
`QuoteEditor`, `Clients`, `Catalog`, `Settings`, `Onboarding[:step3|:complete]`), `--qc-focus=log` (each focused
element), `--qc-fault=load` (the database fails for the first 20 s). Under Xvfb set `QC_X11_RENDERER=software`.
`xdotool` wheel events do not scroll the app here: use a taller window or Tab.

## Open

- Owner: D17 to D20 and D22 are proposed.
- Dialogs are drawn Light in the Dark theme (found in A pass 2, outside its closed scope); flyouts follow the theme.
- A photo opens at most 560 px wide (Material's dialog); a full-window viewer would need a page outside the dialogs
  (D22).
- `ISyncService` is registered and never resolved: D8 keeps `SyncService` for the sync pass.
- A tab reopened from the rail or the tab bar builds a new view model (the navigator's ClearBackStack), so its search
  text and selection reset.
