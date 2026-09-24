# Phase 6 progress — Inter implementation, not final completion

2026-09-24. Phase 6 implementation is authorized. The user approved locally hosted **Inter** as the legal/technical Canva Sans substitute. Canva remains the visual authority: **Page 1 Home, Page 2 Monitoring, Page 3 Search**. No component redesign is authorized. Earlier pending-font and two-page notes are superseded.

## Implemented and verified

The routed React application now includes Home, Monitoring, grouped Search, Incident/Application/Run modals, monitoring fullscreen, authenticated greeting, account controls and structured Admin Configuration. Home badges come from read-only backend current-state projections, independent of the daily percentage. Application detail counts include all unresolved incidents rather than only the recent page. Static assets and explicit client routes can be served by the existing ASP.NET Core host; no separate infrastructure was introduced.

Inter is served from the project with its OFL license. Typography is tuned within the measured Canva card/workspace geometry. Official full-resolution exports of all three current Canva pages are preserved in `docs/reference/canva-phase6-export/`; all are 1366×736. The unchanged images are compared with browser captures in [the three-page comparison](../artifacts/phase6/visual-comparison.html). Reference versus implementation screenshots and an adjustable overlay are available. These are review artifacts, not automatic visual approval.

The real Chrome/HTTPS/disposable-PostgreSQL workflow successfully signed in, created an Application and inactive Draft, configured a disabled source under an allowed root, edited structural rules and revision-2 identities, validated, simulated, published and activated the Profile. It also verified secure HttpOnly cookies, missing-CSRF rejection and sign-out. No file ingestion was started. [Evidence](../artifacts/phase6/real-browser-results.json).

## Test evidence

- **398 backend tests passed, 0 failed, 0 skipped** in `artifacts/phase6/screens-final-regression/`: Phase 1 42; Phase 3 76; Acquisition 11; Access 28; Persistence 180; API 61.
- The regression verifier confirmed **all 382 accepted cases passed once** and **139 protected source/test hashes remain unchanged**. [Summary](../artifacts/phase6/regression-summary.json).
- The frontend foundation has 14 passing unit tests. The latest browser run has six passing interaction/geometry checks plus one completed accessibility-audit capture. The audit capture passing means its report was produced; it does **not** mean accessibility passed.
- Earlier failing new browser fixtures remain preserved. Missing required revision-2 policy identities caused the backend to reject incomplete simulation publication correctly. The fixture was corrected through the structured UI; engine validation and accepted assertions were not weakened.
- Original approved Home image SHA-256 remains `EEE126D340B56EAED07732845885CC6CBE4D7FF92886A6D16E55FA56DD70CB73`.

## Remaining acceptance work

Phase 6 is **not complete**. Remaining work includes full real-backend workflow conflict/lost-acknowledgment/revocation coverage, pending-command navigation safeguards, richer grouped Incident evidence, Profile-version/audit/role-filtered diagnostics views, broader configuration round trips and simulation timing schedules, responsive/keyboard review, and final visual acceptance. Rendering is bounded for structured report arrays, but overall large-report/performance limits still need acceptance evidence.

The user approved a foreground-only contrast amendment for affected small Warning/Error/muted text. The exact original and adjusted hex values are recorded in [the visual review](phase-6-visual-review.md); fills, sizes, weight, spacing and geometry are unchanged. Current Home/Monitoring/Search audit fixtures report zero axe violations after these adjustments and the nonvisual logo/chart ARIA correction. This is bounded fixture evidence, not full accessibility certification; all state, keyboard, zoom and responsive checks remain required.

The five Phase 4 environmental gates remain pending: dedicated-identity SMB/UNC, disconnect/reconnect/share identity, installed Windows Service/SCM lifecycle, boot/recovery and service-account ACL verification. No mock, API or frontend test closes them.

[Local build/test instructions](development/frontend.md). No Phase 7, deployment, real GiroSol access, collectors, notifications or AI work.


Final Inter-build recheck: the real PostgreSQL/HTTPS browser lifecycle passed again in artifacts/phase6/final-inter-browser after bounded report rendering and approved foreground adjustments. All seven browser tasks passed again, with zero recorded axe violations in the three current audit fixtures. Production build and 14 unit tests pass.

## Latest priority increment — three-screen integration

Home Info, application-specific Monitoring popups, fullscreen fallback, direct Search Order Run navigation, unassigned/shared evidence, server-timezone greeting and backend badges now have a combined real Chrome/HTTPS/PostgreSQL acceptance scenario. It covers a committed Incident transition with a deliberately lost acknowledgment, close/reopen and same-ID reconciliation, while eleven durable metric facts remain unchanged. Dedicated investigating records are fetched separately from the latest incident page. Inactive polling is visibly marked paused.

Final results: **398/398 backend tests**, **382 accepted cases**, **139 protected files unchanged**; **14 frontend unit tests**; **8 browser tasks** (six interaction checks, one audit capture, one deterministic visual capture). Production build passes. Real results are in `artifacts/phase6/screen-integration-results.json`; final TRX is `artifacts/phase6/screens-final-regression/`. No accepted assertion was rewritten.

[Three-screen integration review](phase-6-screen-integration-review.md) records the scope, evidence and remaining limits. [New comparison](../artifacts/phase6/screens-review/comparison.html) and individual overlays are explicitly **unreviewed, not frozen baselines**. The original references, Inter license/font and exact approved foreground palette remain unchanged. Fine visual review and the broader Configuration/account/security/responsive/accessibility matrix remain unfinished. Phase 6 is not complete.
