# Handoff: QuoteCraft (2026-11-04, A pass)

Branch `feat/production-readiness`. Read first: `docs/COMPLETENESS.md` (verdict and what is proven), then
`docs/DECISIONS-production-readiness.md` (D1 to D19; D17 to D19 are proposed), then `docs/UNO-AUDIT.md`.

## Where it stands

- Completeness: Complete with gaps, 0 Blockers, 0 functional Majors. Head Majors: WinAppSDK, Android and iOS are
  CI-built, not run here. 6 Minors open (see COMPLETENESS, Minors).
- Structure: the Quotes page is two models (`DashboardModel` the list, `QuoteDetailModel` the open quote in a
  `ContentControl` region, route `./QuoteDetail`), sharing `Services/QuoteBoard`. Every new quote is made by
  `Services/QuoteCreationService`. List cards are Material buttons (`ListCardButtonStyle`); `InteractionHelper` is gone.
- Tests: 229 (`dotnet test QuoteCraft.Tests/QuoteCraft.Tests.csproj -p:QuoteCraftTargetFrameworks=net10.0-desktop`).
  `QuoteCraft.Tests/Presentation` (82) drives each page model over a real temporary SQLCipher database
  (`ModelHarness`, `FakeNavigator` answering dialog routes). The harness drops xUnit's synchronization context: on it,
  a state update racing a state's first value can be lost (models run on background threads in the app).
- States: opening a tab gives its page a new view model (`MainModel.OpenTab` clears the tab's back stack). MVUX's
  FeedView only moves the visual states a message changed, so a list that had failed kept its error template over the
  new model's data. `Helpers/WholeFeedViewStateSelector` (set in the implicit FeedView style in `App.xaml`) sets the
  data, error and progress states on every message. Keep it unless MVUX changes that.
- Accessibility: 190 automation names over 6,195 XAML lines (30.7 per 1,000). Every page was walked with Tab
  (`--qc-focus=log` prints each focused element and its name).
- Lint gate (`xaml-semantic-lint.ps1 -Path QuoteCraft`): 0 on every gating rule.
- Evidence: `docs/evidence` (matrix sheets re-captured 2026-11-04, `fault-retry.jpg`, `wasm-create-and-pdf.jpg`).

## Debug hooks (Debug builds only)

`--qc-data=<folder>`, `--qc-seed=sample`, `--qc-theme=light|dark`, `--qc-route=<route>[:detail]`,
`--qc-focus=log` (each focused element), `--qc-fault=load` (the database fails for the first 20 s, for error states and
Retry). Under Xvfb set `QC_X11_RENDERER=software` (Mesa's OpenGL leaves the window unpainted after a live resize, and
an unpainted area shows the previous app's pixels).

## Open

- Owner: D17 (cloud mode keeps D3 with Sign In and the annual plan built), D18 (the Alerts tab), D19 (the Quotes split
  and the list-card buttons) are proposed.
- Owner question from the audit: should the Viewed status, its chip and count go (nothing can set Viewed, D11)?
- A tab reopened from the rail or the tab bar builds a new view model (Clients built two on its first opening), so
  its search text and selection reset and its data is read again. That is the navigator's ClearBackStack behaviour,
  kept as is.
- The Android emulator CI job is the coordinator's (A-GRADE-PLAN).
