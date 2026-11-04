# Completeness audit: QuoteCraft (2026-11-04, Uno.Sdk 7.0.0-dev.115, heads: desktop, WinAppSDK, Android, iOS, WebAssembly)

Audited commit: `928e155` on `feat/production-readiness`. Report mode, no code changed.
Runtime: Debug `net10.0-desktop` on Linux under Xvfb (software rendering), driven with xdotool and the `--qc-*` hooks.
Post-state was read from the encrypted database (SQLCipher through `libe_sqlcipher`, the key from `.dbkey`), from
`exports/`, and by relaunching on the same data folder. `dotnet test`: 137 passed, 0 skipped.

**Updated after the A pass (2026-11-04, code at `157c38d`):** every functional Major below is built and proven, and the
Minors that were defects are fixed (Findings, Fixed in the A pass). 229 tests pass (82 of them page-model tests).

**Updated after A pass 2 (2026-11-04, code at `d09249c`):** the Major the code review found (PRD F1.8, a quote's
validity date, neither built nor cut) is built (D20); the Viewed filter and count are gone (D21); a photo thumbnail
opens the photo enlarged (F14.8, D22, built small); the README no longer promises invoicing or editor markup and footer.
301 tests pass (137 of them page-model tests). Details under Findings, Fixed in A pass 2.

Verdict: **Complete with gaps, 0 Blockers, 0 functional Majors** (at `928e155`: 2 Blockers, 8 functional Majors)

Counts now: Blockers 0; Majors (functional) 0; Majors (head) 3 (WinAppSDK, Android and iOS were built by CI, not
run; WebAssembly was run in headless Chromium); Minors 7 open (see Minors).

**Fixed in A pass 2 (2026-11-04):** 1 functional Major found by the code review (F1.8) and 1 Minor (the Viewed
residue); F14.8 is built in part. 2 Minors are new: F14.8's remainder and dialogs drawn Light in the Dark theme.

**Fixed in the A pass (2026-11-04):** the 8 functional Majors and 11 Minors; details and proof under Findings.
- Messages bound through a `FeedView` to an `IState<string>` now render on every page (`ef34305`, `980f3b2`).
- Reminders reach the user: the badge, the drawer and Mark all read, across relaunches (re-proven at runtime).
- A notifications entry below 800 px: the bottom tab bar's Alerts item (`13fefad`, D18).
- The Send body: the whole message reaches the email (re-proven; the XAML dialog of `6093eeb` fixed it).
- Logo removal: a Remove button beside Upload (`980f3b2`).
- The Settings preview is real: it follows the fields as typed (`980f3b2`).
- Cloud mode: Sign In and the annual plan are built (`980f3b2`, D17); not runnable without a Supabase project.
- Load failures: every page that reads data shows its error state and Retry, and recovers (`75e0022`).

Earlier, after the audit: both Blockers (`ca89676`, `d8a5212`).

Safe to show (desktop, any width from 360 px; WebAssembly at 1280 px for create and PDF): every critical workflow
below, the reminders, validation and failure messages, logo removal and the Settings preview.

Keep off screen: cloud mode (sign-in, plans, billing need a Supabase project); photo and logo upload under Linux
without a file-picker portal.

## Inventory

Screens: 20 intended, 20 present, 0 unreachable, 0 missing (source: derived from PRD, routes and dialogs; at
`928e155` the Notifications overlay was unreachable below 800 px)
- Pages: Shell, Onboarding (3 steps), Auth, Main, Quotes (list, detail, preview), Quote editor, Clients (list, detail),
  Catalog (categories, items), Settings, and the Notifications overlay.
- Dialogs (10): New Quote, Line Item, Catalog Browser, Send, Client, Catalog Item, Delete quote, Delete client,
  Unsaved Changes, and the error dialog.
- The notifications are the `!Notifications` drawer (D15): the rail's bell from 800 px, the Alerts tab below.
- Auth (cloud mode only) opens from Settings' Sign In and Sign Out (D17).
- The open quote is `QuoteDetailView` (`QuoteDetailModel`) in the Quotes page's detail region (D19).

Routes: 9 registered, 18 code triggers plus 8 tab items, 0 dead references.

Interactions: every control is a bound command now (0 Click or Tapped handlers since the uno-audit).
- At `928e155`: 2 model commands had no control (`RemoveLogo`, `UpgradeToProAnnual`), Mark all read was dead at
  runtime and the Settings preview was simulated. All four are fixed.

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

At `928e155` with the A pass's outcome after each row (R: runtime-proven, C: code read, X: not checked).

| # | Area | Status | Evidence | Blockers | Majors | Minors | Note |
|---|---|---|---|---|---|---|---|
| 1 | Screens | Complete | R | 0 | 0 | 0 | All present and reachable; Notifications from the Alerts tab below 800 px (`13fefad`) |
| 2 | Navigation | Complete | R | 0 | 0 | 0 | Tabs, rail, editor back and fallback, Full editor, Duplicate and New Quote opening the editor; the detail region (D19) |
| 3 | Interactions | Complete | R | 0 | 0 | 2 | Send body whole; Mark all read works and persists; logo Remove; a photo opens enlarged (F14.8 in part, D22); Send cannot attach the PDF (mailto) |
| 4 | Logic | Complete | R | 0 | 0 | 1 | Tax, numbering, status rules, the valid-until date (D20) and Reopen verified in the database; the Viewed residue is gone (D21). Catalog seed size |
| 5 | Data | Complete | R | 0 | 0 | 0 | Quote, items, notes, client, catalog price, settings, status history and read notifications survive a relaunch |
| 6 | States | Complete | R | 0 | 0 | 0 | Every message renders (direct bindings); loading, empty and error states with Retry, proven with `--qc-fault=load` (`docs/evidence/fault-retry.jpg`) |
| 7 | Forms | Partial | R | 0 | 0 | 1 | Dialogs, onboarding and the editor (title, tax rate, a valid-until day not in the past) validate and say why; messages follow the fields. Explicit Save, not auto-save (F1.10) |
| 8 | Authentication | N/A | C | 0 | 0 | 0 | Local-first (D3). Cloud mode: Sign In and the annual plan built (D17), not runnable without a project |
| 9 | Responsive/adaptive UI | Complete | R | 0 | 0 | 0 | Four categories at 360 to 1920 px in both themes (`docs/evidence`); Alerts below 800 px |
| 10 | Platform behavior | Partial | R | 0 | 0 | 0 | Desktop and WebAssembly run here (PDF saved or downloaded; an unopened PDF says where it is). Other heads CI-built |
| 11 | Failure handling | Complete | R | 0 | 0 | 0 | Banners render (a failed send says why); load failures show Retry, which recovers (`75e0022`) |
| 12 | Accessibility | Partial | R | 0 | 0 | 0 | 194 names (192 in XAML, 2 in C#) over 6,277 XAML lines (30.9 per 1,000); every page walked with Tab (`--qc-focus=log`), the editor's photos again in A pass 2. Screen readers X |
| 13 | Performance | Partial | R | 0 | 0 | 0 | Idle CPU 0 % on every page (a hidden indeterminate bar kept a core busy; fixed). Startup not measured |
| 14 | Security/privacy | Complete | C | 0 | 0 | 0 | No service-role key in the app. Desktop key file and unencrypted WebAssembly are settled (D1, D13) |
| 15 | Visual quality | Complete | R | 0 | 0 | 2 | Settings preview real; residue fixed except the first-run theme label; dialogs Light in the Dark theme (found in A pass 2) |
| 16 | Testing | Complete | R | 0 | 0 | 0 | 301 tests (137 page-model tests, every page and dialog model), real temporary SQLCipher databases; fault hook added |

## Critical workflows by head

| Workflow | desktop | WinAppSDK | android | ios | wasm |
|---|---|---|---|---|---|
| 1. Onboarding to first quote | R, Complete: invalid email and tax refused with their messages; Create First Quote opens the editor on the new Draft (QC-2026-0001, the onboarding tax rate); Skip opens Quotes | X | X | X | R, Skip to Quotes |
| 2. Create and edit a quote | R, Complete: New Quote ("Enter a title" until typed) opens the editor on the Draft; title, tax, client and notes saved from the top bar; the list shows the new title on return; an emptied title is refused with a message in view (also from Mark Sent, A pass 2); the valid-until day picked and saved | X | X | X | R, Complete: New Quote "Panel upgrade", Valid Until Nov 25 picked, item 2 × $1,250.50 = $2,501.00, tax 8.5 % $212.59, total $2,713.59, saved (A pass 2) |
| 3. Export the quote PDF | R, Complete: `exports/QC-2026-0001.pdf`; with no PDF viewer the editor says where it is saved | X | X | X | R, Complete: Download PDF downloaded `QC-2026-0001.pdf` (1 page, Letter, DM Sans Regular and Bold embedded; `pdftotext`: the item row, subtotal $2,501.00 USD, tax (8.5%) $212.59, total $2,713.59 USD; A pass 2: "Valid Until: Nov 25, 2026") |
| 4. Review and send a quote | R, Complete: detail at 390 to 1920 px; the whole message in the mailto; no email app says so in the banner | X | X | X | R, the detail and the preview |
| 5. Status lifecycle | R, Complete (A pass 2: Mark Sent keeps the picked day; a quote past its date expired once, one history row; Reopen gave a fresh date) | X | X | X | X |
| 6. Clients and catalog | R, Complete: client open, edit, delete; catalog categories and items; empty searches say so | X | X | X | X |
| 7. Settings persist and apply | R, Complete: "Settings saved.", the profile in the database, the preview following the fields | X | X | X | X |
| Expiry reminder reaches user (P2) | R, Complete: the badge (rail and Alerts), the drawer, Mark all read persisted across a relaunch | X | X | X | X |

The WinAppSDK, Android and iOS heads are built by CI and were not run here. WebAssembly: Release publish, served on
127.0.0.1 and driven in headless Chromium with Playwright (a fresh browser profile, 1280 × 800): first run, Skip,
New Quote, a line item, Save, the detail, Preview and Download PDF (`docs/evidence/wasm-create-and-pdf.jpg`).

## Findings

### Blockers
None open.

### Fixed after the audit
- [Interactions/Forms] **The editor's Save drops the edit in the focused field.**
  - Where: `QuoteEditorPage.xaml:50` (`Command="{Binding Save}"` on the navigation bar).
  - Evidence: R. Typed " - furnace" into the title and pressed Save at once. The quote stayed "Service call" in the
    detail (read fresh from the repository), twice. After pressing Tab first, "Service call - furnace" saved.
  - Cause: TextBox two-way bindings commit on LostFocus, and the bar command does not take focus. The same path skips
    validation: an emptied title was saved as the old title with no message.
  - Owner: mvux.
  - **Fixed in `d8a5212`.** The title, client (`AutoSuggestBox`), tax rate and notes bind with
    `UpdateSourceTrigger=PropertyChanged`, as the list searches already did, so the model holds the typed text before
    any command runs. The editor had no markup or valid-until field then (markup is a Settings default; validity was set by
    the status rules; A pass 2 added the date, D20). The totals now follow the tax rate as it is typed.
  - Proof: R. Debug desktop under Xvfb at 1280 × 800, sample data, QC-2026-0001 "Kitchen Renovation"; the database
    read with SQLCipher and `.dbkey`, then a relaunch on the same data folder.
    - Control, same driver on the unfixed build: " - furnace" typed into the title and Save pressed at once; Save ran
      (`updated_at` moved) and the title stayed "Kitchen Renovation".
    - Title: " - furnace" and Save at once gave "Kitchen Renovation - furnace". Tax rate: 13 replaced with 8.25 and
      Save at once gave 8.25. Client: "Mike Chen" and Save at once gave `client_name` Mike Chen, linked to that client.
      Notes: " Gate code 4412." appended, the field still focused, then Save; saved. Each relaunch showed the values.
    - Money rules (D9) while typing: "9." stays as typed and the totals read "Tax (9 %)" $171.90 on $1,910.00; "9.975"
      reads "Tax (9.975 %)" $190.52. Nothing rounds, refuses or rewrites the field.
    - Back straight after typing now asks "Unsaved Changes" (before, by code, it read the old value and left without
      asking). Discard left the database as it was.
    - An emptied title now reaches validation: Save keeps the editor open with the field empty, and the database keeps
      the old title (`updated_at` unchanged). No message shows: "Quote title is required." goes to the dead
      `ValidationError` `FeedView` (the open Major under Majors). Its slot is also below Notes, out of view from the
      top bar at 1280 × 1000.
  - Test: none. The bug lives only in the XAML bindings; the runtime proof stands in for one.
- [Responsive/Screens] **The quote detail renders on top of itself below 1400 px.**
  - Where: `DashboardPage.xaml:601-606` (title and actions grid) and `:732-740` (main and side columns).
  - What happens: both grids set `Grid.Row="{utu:Responsive Narrowest=1, …}"` but have no `RowDefinitions`. Commit
    f2837b8 (the PDF change) removed them. Every row clamps to row 0.
  - Evidence: R at 390, 1100 and 1280 px. The title covers Edit and Full editor, the client card and quote details
    cover the line-item table, and the totals cover Duplicate and Delete. At 1500 px it lays out correctly.
  - This is the only detail layout on phones and on laptops narrower than 1400 px. The Phase B evidence sheets predate
    the regression.
  - Owner: uno-toolkit.
  - **Fixed in `ca89676`.** Both grids have their `RowDefinitions` back. Proof: R at 390, 1100, 1280 and 1500 px; the
    title, actions, line items and side column no longer overlap.

### Fixed in A pass 2 (2026-11-04)

Runtime proof: Debug desktop under Xvfb (software rendering), a fresh data folder for the first run and the sample data
otherwise, the database read with SQLCipher and `.dbkey`; WebAssembly in headless Chromium.

- **[Major, Forms/Logic] PRD F1.8, set a quote's validity date** (`4765ab5`, `d09249c`, D20). Found by the code review
  (not built and not recorded as a cut). The editor has a Valid Until date picker under the tax rate. A new quote starts
  at the Settings default ("Valid For", counted from creation); a picked day is stored as its last second in UTC; a
  changed day in the past is refused; sending and Reopen keep a date that is ahead (D11's send rule moved, D20).
  - R desktop: a first-run quote showed 11/18/2026 (14 days); a day in the past on Mark Sent showed "The valid-until
    date cannot be in the past." and nothing was written; Nov 25 picked, Export PDF wrote "Valid Until: Nov 25, 2026"
    (`pdftotext`), Mark Sent stored `2026-11-25T23:59:59Z` with status Sent; Back after changing the day asked
    "Unsaved Changes"; Settings "Valid For" 21 saved, and the next New Quote opened on 11/25/2026; a Sent quote given Nov
    6 raised "Quote #QC-2026-0007 expires in 3 days." on the next launch, now counted by calendar day ("in 2 days",
    `d09249c`); a Sent quote put past its date expired with one Expired history row, and Reopen gave it a fresh date.
    `docs/evidence/editor-valid-until-and-photo.jpg`.
  - R WebAssembly: the same picker on the Settings default, Nov 25 picked, the detail and preview show it, and the
    downloaded PDF reads "Valid Until: Nov 25, 2026" (`docs/evidence/wasm-valid-until-and-pdf.jpg`).
  - Tests: `QuoteEditorModelTests` (default from Settings, save to the end of the day, an untouched date kept, a past day
    refused, an old answered quote still saves, Back asks, Mark Sent keeps the day and expiry follows it),
    `ValidUntilDateTests`, `QuoteLifecycleTests.Sending_keeps_a_valid_until_date_that_is_ahead…`,
    `QuoteExpiryServiceTests.The_reminder_counts_calendar_days…`, `PdfGeneratorTests.The_header_shows_the_valid_until_day…`.
- **[Minor, Logic/Visual] The Viewed residue** (`2a40f51`, D21): no Viewed filter chip and no Viewed count; the README no
  longer lists the status. A quote stored as Viewed still loads, keeps its pill, and counts and filters as Sent; the
  sample quote is Sent. R: the chips read All, Draft, Sent, Accepted, Declined, Expired and the counts Draft, Sent,
  Accepted, Declined, Expired. Test: `DashboardModelTests.A_viewed_quote_from_an_older_install…`.
- **[Minor, Interactions] F14.8, a photo at a larger size** (`afb0e5f`, D22, built small): a thumbnail is a button (tap,
  Enter or Space) that opens `!PhotoViewer`, a dialog with the photo scaled to fit and Close. R: at 1280 px a 4:3 photo
  showed at 512 × 384 px and a portrait one fitted the window; at 390 px (Dark) Tab reached "View photo enlarged" and
  Enter opened it (230 px wide). Test: `QuoteEditorModelTests.A_thumbnail_opens_its_photo_enlarged`.
- **README** (`2a40f51`, `63d7fde`): "quoting app" (it does not invoice); the editor's line reads what it edits; markup
  and the custom footer are Settings defaults (catalog items take the markup, the PDF prints the footer).
- The other items of the review's list (structure, failures, dead members, tests for the sign-in and dialog models,
  stale comments) are code changes covered in `docs/UNO-AUDIT.md` and `HANDOFF.md`; the critical workflows were run
  again on desktop after them (1 to 7 and the reminder) and workflows 1 to 3 in WebAssembly.

### Fixed in the A pass (2026-11-04)

Each functional Major of the audit, then the Minors that were defects. Runtime proof: Debug desktop under Xvfb
(software rendering), sample data unless said, the database read with SQLCipher and `.dbkey`; tests: the page-model
tests in `QuoteCraft.Tests/Presentation` (real temporary SQLCipher databases).

- **Messages through a `FeedView` over an `IState<string>`** (`ef34305`, `980f3b2`, and `4b1c349` for Quotes). Every
  such message binds directly (text, and visibility through `EmptyToVisibleConverter`); `IFeed<int>` counts too.
  - R: onboarding "Please enter a valid email address." and "Tax rate must be between 0 and 100."; the editor's empty
    title shows "Quote title is required." in a banner under the bar (in view from the top bar's Save); Settings shows
    "Settings saved." and the profile is in the database; a failed send shows "The quote could not be sent. No email
    app opened, so the quote is not marked as sent."; New Quote's "Enter a title for the quote." goes as soon as a
    title is typed; the totals ("7 total"), the catalog count ("65 items") and the editor's quote number show.
  - Tests: `QuoteEditorModelTests`, `OnboardingModelTests`, `SettingsModelTests`, `NewQuoteModelTests`,
    `LineItemEditorModelTests`, `QuoteDetailModelTests`.
- **Reminders reach the user.** The badge is an `InfoBadge` (rail) and the Alerts tab's badge (below 800 px); the
  drawer is the `!Notifications` route (D15).
  - R: a Sent quote set to expire in 2 days; on the next launch the expiry service wrote "Quote #QC-2026-0002 expires
    in 2 days." (unread), the Alerts badge read 1 at 390 px and the rail's badge 1 at 1280 px (Dark); the drawer listed
    it with the unread dot; Mark all read cleared the dot and the badge and wrote `is_read = 1`; after a relaunch the
    badge stayed clear and the drawer showed the reminder as read; the check did not add it again.
  - Tests: `ShellModelTests` (the badge from the expiry check, Mark all read surviving a relaunch on a new service
    over the same database).
- **A notifications entry below 800 px** (`13fefad`, D18): Alerts in the bottom tab bar opens the drawer. R at 390 px.
- **The Send body** is whole: R, the logged mailto carried "Hi Sarah Williams, … Please find attached quote
  QC-2026-0002 … valid until December 1, 2026. … Best regards, Northside Plumbing & Heating". The XAML dialog of the
  uno-audit (`6093eeb`) fixed it; `QuoteDetailModelTests` checks the body goes to the email untouched.
- **Logo removal** (`980f3b2`): Remove beside Upload while there is a logo; the copy is deleted and Save stores no
  logo. C and tests (`SettingsModelTests.Remove_logo_…`); adding a logo still needs a file-picker portal here.
- **The Settings preview** (`980f3b2`): `SettingsPreview` works the sample quote out from the fields as typed. R at
  1500 px: the business name and email as typed, today's date, valid until 14 days on, markup 10 % raising the prices
  ($12.50 to $13.75), tax 8.25 % on $507.10 = $41.84, total $548.94. Tests: `SettingsPreviewTests`.
- **Cloud mode, Sign In and the annual plan** (`980f3b2`, D17): built; C and tests (`SettingsModelTests`), not runnable
  without a Supabase project.
- **Load failures recover** (`75e0022`, found in this pass). With `--qc-fault=load` the database fails for the first
  20 s. R at 1280 × 800 (`docs/evidence/fault-retry.jpg`): Quotes, Clients, Catalog and Settings each showed their
  error state with Retry; after the window Settings' Retry loaded the profile, and Quotes, Clients and Catalog loaded
  when opened again; Catalog (Dark) loaded from its own Retry without leaving the page. Before the fix, Clients and
  Catalog kept "could not be loaded" over the data and Retry did nothing: opening a tab gives its page a new view
  model, and MVUX's FeedView moves a visual state only for the axes a message changed, so the new feed's first message
  never cleared the error state. Every FeedView now sets its data, error and progress states on each message
  (`WholeFeedViewStateSelector`, in the implicit FeedView style). Tests: `WholeFeedViewStateSelectorTests`,
  `ClientsModelTests.Retry_after_a_failed_read_shows_the_clients`. The Quotes status counts hide on a failed load
  (`a6757b8`); the list below says why.
- Minors fixed: Back after Export PDF no longer asks (`e548fd7`); the line-item message follows the fields
  (`ef34305`); the onboarding catalog note and step bars ("Step 1 of 2" over two bars); empty catalog-browser search
  (`e6173d5`, R); an unopened PDF says where it is (`e6173d5`, R: "QC-2026-0001.pdf is saved in …/exports, but no app
  on this computer opened it."); the Settings catalog count; "1 quote" without a stray "·"; delete wording; page-model
  tests and a fault hook (`--qc-fault=load`).

### Majors (functional)
None open. The 8 found at `928e155` (messages, reminders, notifications below 800 px, the Send body, logo removal, the
Settings preview, and cloud mode's Sign In and annual billing) are fixed: see Fixed in the A pass.

### Majors (head)
- WinAppSDK, Android and iOS: every critical workflow is X; CI builds them. PDF delivery differs per head (picker,
  export sheet) and is proven here on desktop and WebAssembly (download). 3 head Majors, one per head. (WebAssembly
  was X at `928e155`; it now runs workflows 1 to 4 in headless Chromium.)

### Minors
Open:
- [Forms] The editor has explicit Save plus an unsaved prompt rather than auto-save (F1.10). Line items do save
  immediately.
- [Screens] Onboarding has no trade picker and no logo step (F18.2).
- [Logic] The catalog seeds 65 items: 4 to 13 per category, against 20 to 40 per catalog in PRD F8.2.
- [Interactions] F14.8 in part (D22): a photo opens enlarged in a dialog, at most Material's 560 px wide (230 px of
  photo on a 390 px phone); there is no full-window view, and no long press or swipe to delete (the thumbnail's delete
  button stays).
- [Visual] Found in A pass 2, outside its closed scope: in the Dark theme the dialogs (`ContentDialog` routes: Line
  Item, Edit Client, Unsaved Changes, the photo) are drawn with the Light surface, while flyouts (the date picker, the
  drawer) follow the theme. R at 390 and 1280 px, with and without `--qc-theme=dark`.
- [Visual] The theme reads "Light" on a desktop first run, where the README says System (the browser's first run
  reads "System theme").
- [Interactions] Send can open email but cannot attach the PDF (mailto); the dialog says to attach it by hand (F11.1).

Fixed in A pass 2: the Viewed residue (D21).

Fixed in the A pass (see Fixed in the A pass): Back's prompt after Export PDF; the stale line-item message; the
onboarding catalog note and step bars; the two empty searches; where an unopened PDF went; the Settings catalog
count; "0 quotes ·"; "permanently removed"; model tests and a fault hook.

## Not checked (X)
- Android, iOS and WinAppSDK at runtime. CI builds them. To check: a device or emulator.
- Photos and logo upload: no file-picker backend under Xvfb. To check: a desktop session with a portal, or a device.
- Cloud mode (sign-in, plans, billing, portal): needs a Supabase project.
- Performance: startup time and worst frame were not measured (idle CPU was checked: 0 % on every page).
- Screen readers and touch input. Dark theme is in the evidence sheets (`docs/evidence`, 2026-11-04).

## Noted for other skills
- uno-audit (done, `docs/UNO-AUDIT.md`): the dead code noted at `928e155` is gone.
- gold-standard-pass: the copy residue left in Minors (the first-run theme label); the Light dialogs in the Dark theme.

## Unresolved Questions
- N/A, confirm the product reasons:
  - Cloud sync (F10): off by D8.
  - Plan limits and watermark: lifted without billing by D3.
  - Share links, SMS, client-view tracking and the Viewed status (F11.2–F11.8, F12.1–F12.3): removed by D11.
  - Authentication: local-first, no account, by D3.
- Viewed: answered by D21 (the chip and count went; stored Viewed quotes read as Sent).
- The 7 critical workflows and the reminder check are derived from the PRD. Confirm them.
- X on supported heads (WinAppSDK, Android, iOS, WebAssembly): the next runtime pass should cover them, PDF delivery
  first.
- Cloud mode: decided under D17 (build the two small controls; D3 stands).
