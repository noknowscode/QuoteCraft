# Completeness audit: QuoteCraft (2026-11-04, Uno.Sdk 7.0.0-dev.115, heads: desktop, WinAppSDK, Android, iOS, WebAssembly)

Audited commit: `928e155` on `feat/production-readiness`. Report mode, no code changed.
Runtime: Debug `net10.0-desktop` on Linux under Xvfb (software rendering), driven with xdotool and the `--qc-*` hooks.
Post-state was read from the encrypted database (SQLCipher through `libe_sqlcipher`, the key from `.dbkey`), from
`exports/`, and by relaunching on the same data folder. `dotnet test`: 137 passed, 0 skipped.

Verdict: **Incomplete**

Safe to show (desktop, window 1400 px or wider): onboarding to the first quote; adding items by hand and from the
catalog; totals; Export PDF; Mark Sent, Accepted and Reopen; clients add, search, edit and delete; catalog price edit;
saving Settings. In the editor, press Tab before the top-bar Save.

Keep off screen: the quote detail below 1400 px; the Send email body; notifications and reminders; any validation
or failure message; logo removal; cloud mode.

## Inventory

Screens: 20 intended, 20 present, 1 unreachable below 800 px (Notifications), 0 missing (source: derived from PRD, routes and dialogs)
- Pages: Shell, Onboarding (3 steps), Auth, Main, Quotes (list, detail, preview), Quote editor, Clients (list, detail),
  Catalog (categories, items), Settings, and the Notifications overlay.
- Dialogs (10): New Quote, Line Item, Catalog Browser, Send, Client, Catalog Item, Delete quote, Delete client,
  Unsaved Changes, and the error dialog.
- `NotificationsPage.xaml` and `NotificationsModel` are not routed. They are dead code.
- Auth (cloud mode only) opens only from Sign Out.

Routes: 9 registered, 18 code triggers plus 8 tab items, 0 dead references.

Interactions: about 102 found: 56 bound commands, 40 Click handlers, 6 Tapped handlers.
- 2 model commands have no control: `RemoveLogo` and `UpgradeToProAnnual`.
- 1 control is dead at runtime: Mark all read.
- 1 surface is simulated: the Settings live preview.

Critical workflows (derived; confirm):
1. First run: onboarding to the first quote
2. Create and edit a quote: items, catalog, tax, save
3. Export the quote PDF
4. Review and send a quote: list, detail, send
5. Status lifecycle: Mark Sent, Accepted, expiry, Reopen
6. Clients and catalog upkeep
7. Business settings persist and apply

Also checked: an expiry reminder reaches the user (P2, F17.2).

## Completeness by area

| # | Area | Status | Evidence | Blockers | Majors | Minors | Note |
|---|---|---|---|---|---|---|---|
| 1 | Screens | Partial | R | 0 | 0 | 1 | All present. Notifications overlay unreachable below 800 px (counted in 9). NotificationsPage is dead code |
| 2 | Navigation | Complete | R | 0 | 0 | 0 | Tabs, rail, editor back and fallback, Full editor, and Duplicate opening the editor all drove correctly |
| 3 | Interactions | Broken | R | 1 | 3 | 1 | Editor Save drops the focused field. Send body is truncated. Mark all read is dead. No logo removal |
| 4 | Logic | Complete | R | 0 | 0 | 1 | Tax 8.25 % on $2,621.00 is $216.23. Numbering, status rules and Reopen verified in the database. Viewed is residue (D11) |
| 5 | Data | Complete | R | 0 | 0 | 0 | Quote, items, notes, client, catalog price, settings and status history survive a relaunch |
| 6 | States | Broken | R | 0 | 1 | 2 | Every message bound through `FeedView` to an `IState<string>` never renders. The Quotes error state renders |
| 7 | Forms | Partial | R | 0 | 0 | 3 | Item and client dialogs validate. Discard prompt after Export. Stale item error. Explicit Save, not auto-save |
| 8 | Authentication | N/A | C | 0 | 2 | 0 | Local-first, no account (D3). Cloud mode not runnable: no Sign In entry, annual billing unbound |
| 9 | Responsive/adaptive UI | Broken | R | 1 | 1 | 0 | Quote detail overlaps itself at 390, 1100 and 1280 px. No notifications entry below 800 px |
| 10 | Platform behavior | Partial | R | 0 | 0 | 1 | Desktop only. PDF writes `exports/`, but nothing opens it without `xdg-open` and nothing says so |
| 11 | Failure handling | Broken | R | 0 | 0 | 0 | Wrong key shows "Quotes could not be loaded" with Retry. Failed email shows a dialog. Banners dead (counted in 6) |
| 12 | Accessibility | Partial | C | 0 | 0 | 1 | 91 `AutomationProperties.Name`. Step indicator announces "Step 1 of 2" over 3 bars. Screen readers X |
| 13 | Performance | X | X | 0 | 0 | 0 | Not measured |
| 14 | Security/privacy | Complete | C | 0 | 0 | 0 | No service-role key in the app. Desktop key file and unencrypted WebAssembly are settled (D1, D13) |
| 15 | Visual quality | Simulated | R | 0 | 1 | 2 | Settings "live preview" is static sample data. Residue: Viewed, "0 quotes ·", Delete copy, theme label |
| 16 | Testing | Partial | R | 0 | 0 | 1 | 137 tests pass. No model, binding or layout tests, no fault hook, 0 `AutomationId` |

## Critical workflows by head

| Workflow | desktop | WinAppSDK | android | ios | wasm |
|---|---|---|---|---|---|
| 1. Onboarding to first quote | R, Partial: flag file written; the editor opens with the onboarding tax 8.25; validation messages invisible | X | X | X | X |
| 2. Create and edit a quote | R, **Broken**: top-bar Save kept "Service call" after typing " - furnace" (detail and database, twice); with Tab first it saved | X | X | X | X |
| 3. Export the quote PDF | R, Complete: `exports/QC-2026-0001.pdf`, 1 page, DM Sans embedded. `pdftotext` shows items in reordered order, subtotal $2,621.00, tax $216.23, total $2,837.23 and the notes | X | X | X | X |
| 4. Review and send a quote | R, **Broken**: detail overlapped below 1400 px; mailto body only "Hi Dana Reyes,"; no email app reported honestly | X | X | X | X |
| 5. Status lifecycle | R, Complete: Mark Sent (Sent, valid 2 days), Accepted (history row), Reopen (Draft, valid Dec 04, history row) | X | X | X | X |
| 6. Clients and catalog | R, Complete: client add, search, edit and soft delete in the database; Sink Installation 350 to 375 in the database | X | X | X | X |
| 7. Settings persist and apply | R, Complete: validity 2 days and footer saved; a new quote got "In 2 days". No "saved" confirmation (see Majors) | X | X | X | X |
| Expiry reminder reaches user (P2) | R, **Broken**: reminder row in the database, unread; no badge; overlay blank; Mark all read wrote nothing | X | X | X | X |

The WinAppSDK, Android and iOS heads are built by CI and were not run here. WebAssembly was not run in this audit; D12
records an earlier headless-Chromium PDF download.

## Findings

### Blockers
- [Interactions/Forms] **The editor's Save drops the edit in the focused field.**
  - Where: `QuoteEditorPage.xaml:50` (`Command="{Binding Save}"` on the navigation bar).
  - Evidence: R. Typed " - furnace" into the title and pressed Save at once. The quote stayed "Service call" in the
    detail (read fresh from the repository), twice. After pressing Tab first, "Service call - furnace" saved.
  - Cause: TextBox two-way bindings commit on LostFocus, and the bar command does not take focus. The same path skips
    validation: an emptied title was saved as the old title with no message.
  - Owner: mvux.
- [Responsive/Screens] **The quote detail renders on top of itself below 1400 px.**
  - Where: `DashboardPage.xaml:601-606` (title and actions grid) and `:732-740` (main and side columns).
  - What happens: both grids set `Grid.Row="{utu:Responsive Narrowest=1, …}"` but have no `RowDefinitions`. Commit
    f2837b8 (the PDF change) removed them. Every row clamps to row 0.
  - Evidence: R at 390, 1100 and 1280 px. The title covers Edit and Full editor, the client card and quote details
    cover the line-item table, and the totals cover Duplicate and Delete. At 1500 px it lays out correctly.
  - This is the only detail layout on phones and on laptops narrower than 1400 px. The Phase B evidence sheets predate
    the regression.
  - Owner: uno-toolkit.

### Majors (functional)
- [States/Forms/Failure] **Messages bound through `FeedView` to an `IState<string>` never render.**
  - Cause: MVUX exposes a string state as a plain `string` on the bindable view model, so a `FeedView` gets no feed.
  - Evidence: R.
    - Onboarding: an invalid email and a tax of "abc" both blocked the step with no message (`OnboardingPage.xaml:105,211`).
    - Editor: an empty title kept the page open with no message (`QuoteEditorPage.xaml:386`).
    - Settings: saving showed no confirmation (`SettingsPage.xaml:596,607`).
  - Same pattern, not driven (C): the Quotes `ActionError` and `UpgradeMessage` banners (`DashboardPage.xaml:116,157`)
    and the Settings `LoadError` (`:53`). A failed Download PDF on Quotes is therefore silent.
  - Owner: mvux.
- [Interactions] **Notifications never reach the user.**
  - Evidence: R. The expiry service wrote "Quote #QC-2026-0002 expires in 2 days.", unread, to the database.
  - The badge never shows. `MainPage.xaml:330` runs `EmptyToVisibleConverter` on an `int`, which always collapses.
    `NotificationVersion` is also never bumped (`MainModel.cs:14`).
  - On every launch after the first, the overlay stays blank and Mark all read does nothing (`is_read` stayed 0).
    `MainPage_Loaded` resolves services through `App.Services` before `Host` is assigned, and an empty `catch` hides
    the failure (`MainPage.xaml.cs:50-62`). On the first run, entered from onboarding, the overlay works ("No
    notifications yet").
  - Owner: mvux.
- [Responsive] **No notifications entry below 800 px.** The bell lives only on the side rail
  (`MainPage.xaml:223`). At 390 and 700 px the bottom tabs carry Quotes, Clients, Catalog and Settings only, so
  expiry reminders (F17.2) are unreachable on phones (R). D5 lists this as an open point, not a decision.
  Owner: uno-navigation.
- [Interactions] **The Send email body is cut to its first line.**
  - Where: `DashboardPage.xaml.cs:281-288` sets `Text` (with newlines) before `AcceptsReturn = true`, so the TextBox
    keeps only "Hi Dana Reyes,".
  - Evidence: R. The dialog showed one line, and the logged mailto read `body=Hi%20Dana%20Reyes%2C`. The quote
    number, validity and signature are lost.
  - Owner: winui-xaml.
- [Interactions] **A logo cannot be removed once set.** `SettingsModel.RemoveLogo` (`SettingsModel.cs:223`) has no
  control. Settings shows only Upload (R screenshot). Adding a logo could not be driven here: there is no file-picker
  backend under Xvfb. Evidence: C. Owner: mvux.
- [Visual/Simulated] **The Settings "live quote preview" is static.**
  - Totals are hardcoded: Subtotal $461.00, Markup $69.15, Tax $59.93 (`SettingsPage.xaml:864,892`), whatever the
    settings say (8.25 % and 0 % were set). The date is hardcoded to "Mar 02, 2026" (`:750`).
  - The business name, phone and email header is blank (dead `FeedView`s, as above).
  - Evidence: R at 1500 px. Owner: gold-standard-pass (content), mvux (bindings).
- [Authentication, cloud mode] **No Sign In entry point.** With a Supabase project configured, Auth is reachable only
  through Sign Out (D3 open point). Evidence: C. Not runnable without a project. Owner: uno-extensions-services.
- [Authentication/billing, cloud mode] **Annual billing has no control.** `UpgradeToProAnnual` (`SettingsModel.cs:247`) is
  unbound, so PRD F13.6 is missing whenever plans exist. Evidence: C. Owner: mvux.

### Majors (head)
- WinAppSDK, Android, iOS and WebAssembly: every critical workflow is X; CI builds them. This includes PDF delivery,
  which differs per head (picker, export sheet, download) and is proven here only on desktop. 4 head Majors, one per
  head.

### Minors
- [Forms] After Export PDF or a status change has saved the edits, Back still asks "Unsaved Changes". Discard then
  discards nothing (R).
- [Forms] The line-item dialog keeps "Enter a description." after the field is filled, until the next Save (R).
- [Forms] The editor has explicit Save plus an unsaved prompt rather than auto-save (F1.10). Line items do save
  immediately.
- [Screens] Onboarding has no trade picker and no logo step (F18.2). Its catalog note says "a Plumbing catalog" while
  all 7 categories are seeded.
- [Logic] The catalog seeds 65 items: 4 to 13 per category, against 20 to 40 per catalog in PRD F8.2.
- [States] Two searches with no match render a blank area instead of an empty state (R): the catalog browser, and
  Clients after a search.
- [Platform] Export PDF gives no confirmation. On Linux without `xdg-open` the file is written and nothing opens or
  says where it went (R; the warning is logged).
- [States] The Settings "Catalog items" row shows no count (R).
- [Logic/Visual] The Viewed status, its filter chip and the "Viewed: n" count remain. Nothing in the app can set it;
  the mechanism is N/A under D11. This is residue.
- [Visual] Small residue:
  - A client with no address reads "0 quotes ·".
  - "Delete Quote?" says "permanently removed" for a soft delete.
  - The theme reads "Light" on a first run, where the README says System.
- [Interactions] Send can open email but cannot attach the PDF (mailto); the dialog says to attach it by hand (F11.1).
- [Accessibility] The onboarding step indicator is named "Step 1 of 2" over three bars.
- [Testing] Testing gaps:
  - No presentation-model, binding or layout tests. Both Blockers and the dead-message Major would have shown in one.
  - No fault-injection hook.
  - 0 `AutomationId`s.

## Not checked (X)
- Android, iOS, WinAppSDK and WebAssembly at runtime. CI builds them. To check: a device or emulator, and the
  published browser head under Playwright.
- Photos and logo upload: no file-picker backend under Xvfb. To check: a desktop session with a portal, or a device.
- Cloud mode (sign-in, plans, billing, portal): needs a Supabase project.
- Retry recovery: the only failure reachable here was a wrong database key, which needs a restart. To prove Retry
  re-reads after a recovered fault, add a fault hook.
- Performance: startup time and worst frame were not measured.
- Screen readers, touch input, and Dark theme after the 2026-11-03 commits.

## Noted for other skills
- uno-audit: these are dead code:
  - `NotificationsPage`/`NotificationsModel`;
  - the `App.Services` service locator with empty catches in `MainPage.xaml.cs`;
  - `Task.Delay(300)` in `SettingsPage.xaml.cs:20`.
- gold-standard-pass: the Settings preview content, and the copy residue listed in Minors.

## Unresolved Questions
- N/A, confirm the product reasons:
  - Cloud sync (F10): off by D8.
  - Plan limits and watermark: lifted without billing by D3.
  - Share links, SMS, client-view tracking and the Viewed status (F11.2–F11.8, F12.1–F12.3): removed by D11.
  - Authentication: local-first, no account, by D3.
- Should the Viewed chip, count and pill go, now that nothing can set Viewed?
- The 7 critical workflows and the reminder check are derived from the PRD. Confirm them.
- X on supported heads (WinAppSDK, Android, iOS, WebAssembly): the next runtime pass should cover them, PDF delivery
  first.
- Cloud mode: in scope for this grade? Two Majors (Sign In entry, annual billing) apply only there.
