# Phase 6 acceptance and review plan

Proposal only. No tests have been added or run for this documentation change. The **382 tests accepted by the user** are the baseline, not a claimed fresh run.

Use unit/component tests for rendering, forms and client adapters; HTTP fixtures for deterministic UI edge states; real existing ASP.NET Core plus disposable PostgreSQL for security/workflow end-to-end proof. UI mocks cannot establish authorization, persistence, idempotency, or any pending Phase 4 capability. Synthetic profiles/logs only; no real GiroSol data. Preserve all accepted assertions and record source/test integrity before implementation.

| Acceptance scenario | Required evidence |
|---|---|
| Home metrics | Render API percentage/null, unresolved count, order-only volume and exactly five returned buckets; no recomputation. Test differing event/processing dates across server midnight and a browser in another timezone |
| Parent success with failed orders | Existing engine fixture's separate results/metrics appear unchanged in Home/investigation; no result propagation in UI |
| Incident rows | Explicit defined summary/time provenance; never show arbitrary evidence as latest. Approved additive read projection tested for team scope, bounds and unchanged counts |
| Search identities | Evidence linked to multiple matches/Incidents appears once; raw lines, Order Runs and Incidents remain distinct |
| Search paging | Forward cursor pages, filter/page-size reset, expiry, cancellation/racing responses, no unbounded fetch; limits and all supported filters exercised |
| Run investigation | Parent, attempts/previous attempt, identifier, pinned version and timestamp labels correct; approved child filter isolates parent/session/team and paginates |
| Workflow | Render only returned allowedActions; successful revisioned action, stale revision, unsupported LegacyV1, duplicate click, lost acknowledgment and same-ID reconciliation against real backend |
| Roles/team isolation | Member cannot see Admin navigation or use Admin routes; real forbidden/cross-team API requests rejected; role revocation clears stale privileges/data |
| Provisioning | Admin creates Application/Profile/source; valid local and allowed UNC config accepted, outside-root error shown; inactive until full lifecycle; no history changes; source restrictions remain |
| Configuration | Nested alternatives/priorities/Ignore, parsing/identifier/timing/r2 fields round-trip without losing values; legacy version preserved; server warnings/errors surfaced |
| Simulation | Bounded paste/upload, explicit time schedule, full report/provenance and problem keys; complete/incomplete/warning/timeout/unsupported-report states; no client interpreter |
| Publish/activate | Current report accepted, changed Draft/stale report rejected; explicit warning acknowledgment, separate activation, revision conflict, pinned historical runs unchanged |
| Accounts | Login, invalid/locked credentials, expired/redeemed grant, copy-once/replacement secret, password/logout-all, role/enable/last-admin conflicts; real revocation behavior |
| CSRF/session | Missing/stale token rejected, refresh after login, expired session, no protected cache after logout or late response, no secrets persisted; inactive polling cannot keep a tab refreshing forever |
| API outage | Loading vs empty vs unavailable; cached data marked stale, no false zeros/green, retry/unknown-command outcome retained |
| Availability | Stable plus unobserved/stale/disconnected is visibly non-healthy; timestamps/reasons correct, diagnostics role-filtered |
| Security rendering | Script-like logs/rules render as text; tokens/raw logs absent from URLs, client diagnostics and persistent storage; routes cannot bypass backend policy |
| Limits | Truncated evidence/report links, bounded Profile/source/application lists, search timeout and body/rate caps produce truthful UX |
| Accessibility | Keyboard-only full workflows, focus trapping/return, validation focus, named controls, semantic tables, text alternatives for status; automated axe plus manual zoom/contrast review |
| Responsive | Approved desktop first; narrow-screen monitor access, forms and scrollable tables remain usable without altering desktop geometry |
| Visual fidelity | Home AND Search references verified; static deterministic fixtures at approved viewport, compare rail/workspace/panel, cards, table, type, icons, whitespace, shadows and states |
| Regression | All 382 accepted tests pass unchanged, plus all new component, browser, real-PG and approved additive contract cases |

## Visual evidence method

Preserve the local Home PNG hash and Canva link/page provenance. Obtain the missing Search capture before claiming Search fidelity. Verify exact fonts/assets if accessible; unresolved extraction remains a named limitation. Capture implementation at the approved desktop viewport (Home 1366×736), fixed DPR/browser/font environment, deterministic API data and frozen display times. Do not mask structural content; mask only explicitly documented unavoidable nondeterminism.

Compare against approved design with side-by-side images and overlays, then establish Playwright regression screenshots from the reviewed implementation. A pixel-difference threshold must be calibrated for rendering noise and documented, not widened to hide a layout defect. New automatic baselines are not design approval. [Playwright visual comparison documentation](https://playwright.dev/docs/test-snapshots).

Report functional-data differences separately from visual deviation (five returned daily bars, neutral greeting, metric badge decision, availability qualifier). Any contrast/technical conflict requiring a design change needs explicit review. No “modernization” pass. New Configuration/account/detail screens receive consistent-style and accessibility review, without claiming Canva contains them.

## Completion artifacts and stop gate

Deliver source/file inventory; unchanged-regression evidence; counts/results for every test suite; real backend/PostgreSQL workflow and security results; API contract delta if the narrow query extensions were approved; Home/Search baseline provenance, screenshots/overlays and reviewed deviations; keyboard/contrast findings; operation retry/conflict evidence; simulation/publication/version evidence; bounded-list/report limitations; local build/run instructions and dependency lockfiles. Do not promise a test total before implementation.

Phase 6 completes only after this matrix passes or any unresolved item is explicitly presented for user review rather than marked passed. No deployment, production source access or Phase 7 work. Continue to list all five Phase 4 environmental gates as pending; simulated/browser/local-database success cannot close them.

## Approved acceptance amendments
The [interaction amendments](phase-6-interaction-amendments.md) extend this matrix to all three Canva pages, application and Incident modals, fullscreen monitoring, shared/unassigned evidence, and dynamic authenticated greeting. All three require reference comparisons; Search is Page 3. Earlier proposal-only status is superseded by the preserved approval attachment.

## Home badge acceptance supplement

- All monitored applications Stable with Fresh availability: unqualified Stable.
- Any Warning and no Error: Warning; any Error: Error.
- Stable plus Stale/Disconnected/Unknown/NotObserved: Stable business state with mandatory explicit availability qualifier; never unqualified green.
- No monitored applications: unknown/null business state and explicit no-monitoring qualifier.
- Active and Investigating participate in highest unresolved severity; Resolved does not. Query the complete monitored-session set, not the recent-incident page.
- Mixed-orders fixture retains 75% daily health and three completed orders while both current badges are Warning. These values are not percentage thresholds.
- API projections are read-only; no metric facts, run outcomes, persistence schema or historical interpretation changes.
- Canva font substitution requires separate user approval after the recorded side-by-side review. Current review artifact is not a visual-regression pass for the website.

Approved 2026-09-24: Inter is the standalone font substitute. The subsequent foreground-only Warning/Error/muted contrast amendment is recorded with exact hex values in docs/phase-6-visual-review.md. All fills, typography size/weight and geometry remain fixed. Current three-screen audit fixtures have no axe violations; this does not replace the remaining full-state/keyboard/responsive acceptance matrix.
