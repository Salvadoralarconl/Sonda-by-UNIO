# Phase 6 proposal — SONDA web frontend

Status: **implementation approved with visual-fidelity amendments**, 2026-09-24. The user accepted Phase 5 and all **382 tests**. The subsequent approval and interaction amendments authorize bounded implementation and supersede conflicting proposal details. The [original request](../reference/phase-6-request.txt), [frontend architecture/workflows](phase-6-frontend.md), and [acceptance plan](phase-6-acceptance.md) together bound this proposal.

Canva controls appearance; accepted Phase 1–5 code controls behavior. Preserve all 382 tests and existing Domain/Application, persistence, acquisition, accounts, API and security behavior. No database migration or engine change is proposed. The five Phase 4 environmental gates remain pending independently: dedicated-identity SMB/UNC, disconnect/reconnect/share identity, installed SCM lifecycle, boot/recovery, and service-account ACL verification.

## Product and navigation map

| Screen / proposed browser route | Role and navigation | Content |
|---|---|---|
| Home `/` | Both roles; existing Home rail icon | Canva Page 1, three cards, recent Incidents table, separate monitoring panel |
| Search `/search` | Both; existing Search icon | Canva Page 2 evidence table and bounded search; compact filter disclosure using the search affordance |
| Applications `/applications` | Both; activity/chart navigation | Paged applications, Profile/session selection, current health and availability; run lists |
| Run `/runs/:scope/:session/:id` | Both; contextual links | Result, parent, attempts, identifier, times, pinned Profile Version; child orders subject to contract gap below |
| Incident `/incidents/:session/:id` | Both; Home/investigation links | Problem Identity, Occurrences, Recoveries, history, evidence and allowed workflow actions |
| Evidence `/evidence/:session/:id` | Both; Search/Incident links | Escaped stored evidence, interpretation and linked run identities, provenance and truncation notices |
| Configuration `/configuration` | Admin; lower settings navigation | Application/Profile/source provisioning and visual Draft lifecycle |
| Profile `/configuration/profiles/:id` | Admin | Structured editor, validation, simulation reports, immutable versions, publish and activate |
| Diagnostics `/monitoring` | Both; lower tools navigation | Existing role-filtered read-only diagnostics; expanded monitor |
| Account `/account` | Both; avatar | Password change, logout and logout-all |
| Members `/configuration/members` | Admin | Invitations, role/enablement, replacement grants, reset grants and audit |
| Login `/login`, grant redemption `/redeem` | Anonymous | Existing cookie login and explicit token/password entry; no registration |

The two Canva pages retain their composition. Other screens extend the same shell, density and typography; they are new workflow layouts, not claims of additional approved Canva pages. Contextual detail links avoid adding a crowded navigation rail. Role visibility improves usability but authorization always remains server-side.

## Home and Search semantics

Home reads the existing Dashboard snapshot. Display server `asOf`, reporting date/timezone in contextual detail. System Health uses the returned percentage (null means no evaluated runs); never derive it from Incidents. The accepted backend uses completion-event dates for evaluated-run health and completion-processing dates for order volume. Logs Today is exactly `completedOrderRunsProcessedToday`; the miniature chart uses the five returned daily completed-order buckets, including zeroes. Active Incidents displays the API's unresolved count, including Investigating. No new metrics or percentage-to-health thresholds.

The right panel presents returned business health **and** availability. Stable with stale/disconnected/unobserved availability must not appear healthy green. Use the returned `healthyNow` and availability semantics, with accessible text and a visible qualifier for non-current states. A missing response is unavailable, not zero incidents or Stable. No polling runs the monitor; monitoring continues independently of browsers.

Home Error Logs rows represent Incidents, not raw lines. Search rows represent evidence, keyed by session/evidence identity and rendered once. Incident filters do not turn matching evidence into Incident rows. Search provides Application/Profile, time basis/range, literal message text/case option, identifier/namespace, result, classification and Incident status. Preserve existing 31-day range, 3–256 character text limits, default 50/max 200 page size, protected cursor and expiry. Reset cursor on filter/size change. Never download all history or invent regex search. Do not infer grouping rules from Canva's example repeated CAM rows.

## Canva implementation and fidelity gate

Permanent authority: [Sonda Home Page Design](https://www.canva.com/design/DAHV7wwVO9g/ey_I9os2basGua69TXtdFA/edit), Page 1 Home and Page 2 Search. Preserve the [visual guide](../visual-design-guide.md), [review provenance](../reference/canva-approved-design.md), and unchanged local Home image. This proposal uses that recorded review; it does not claim a fresh Canva extraction.

First implementation milestone after approval: reopen both pages, record date/design identity, obtain usable reference captures and available asset/font properties, and establish desktop baselines. The existing Home baseline is 1366×736; confirm Search's corresponding full-page dimensions. Search has no local exported baseline yet. If access/extraction is blocked, report the fidelity gate blocked; do not create a generic substitute or approve a screenshot generated from the implementation itself.

Use CSS variables for canvas/surface/text/separators, status foreground/background pairs, radii, shadows, spacing and type scale. Current raster estimates are provisional: 64 px rail; central shell x74–984; right shell x995–1352; 11–12 px gutters; three approximately 283×109 cards; 35 px utility bar; roughly 28/20/14/11–12 px type hierarchy; 10–12 px radii. Preserve the tall sparse Home table and shorter three-column Search table, line icons and lower avatar position. Exact font, colors and shadows require verification; do not label estimates as Canva exports.

At narrower widths propose a collapsible monitoring panel with an explicit opener, a compact rail, stacked cards only when necessary, and horizontal table scrolling. Desktop remains unchanged. Test keyboard reachability, 200% zoom, semantic tables, labels and visible focus; never encode status solely by color. If approved small/pastel text fails contrast, show the exact conflict and proposed smallest adjustment for approval before changing it.

## Explicit decisions and contract gaps for approval

These are proposals, not implemented changes:

1. **Home display fields:** Dashboard recent Incidents lack a defined message/time and direct application display projection. Do not label an arbitrarily fetched evidence row “latest.” Recommend a narrow additive read projection supplying application label, deterministic summary text and explicitly defined first-occurrence processing time (with evidence provenance where present). It must preserve existing DTO fields, ordering/counts and all behavior. If this API extension is not approved, those cells must explicitly say unavailable; full Home data fidelity remains blocked.
2. **Parent-to-child navigation:** existing Order Run list is session-paged, without parent filtering. Recommend an optional bounded parent-Application-Run filter using existing team/session authorization and keyset rules. It exposes existing facts only. Without approval, show known direct links and ordinary paged lists, never claim a complete child list obtained by client-side scanning.
3. **Illustrative labels:** session has no human display name; propose a neutral greeting without “Salvador.” The Dashboard does not expose the overall Stable/Warning badges illustrated on the metric cards. Keep the card footprint but use a neutral explanatory caption pending visual approval; do not synthesize overall severity or derive Stable from the daily percentage. Five API chart buckets replace illustrative marks within the existing footprint.
4. **New interaction surfaces:** propose compact Search filter disclosure, detail screens, responsive panel collapse and non-green availability qualifiers as functional extensions. They require review against Canva, not a redesigned default Home/Search. Preserve the bell's position as an unavailable notification affordance with an accessible explanation and no badge; no notification system.

Approval may include the two narrowly scoped additive query extensions above; they are the only proposed backend changes. They require separate new contract/security tests and cannot alter accepted assertions. No mutation, policy, schema or acquisition change is included. A discovered material conflict outside these bounds is reported before implementation.

## Sequence and completion

1. Freeze accepted source/test baseline; verify Canva references and settle the explicit design/contract choices.
2. Build frontend foundation, typed API/session boundary, tokens and shell; implement only approved read extensions with tests.
3. Home/Search and distinct evidence/run/Incident investigations.
4. Visual Configuration, simulation lifecycle, account management and diagnostics.
5. Run all 382 regression gates, new browser/component/contract tests, real PostgreSQL-backed end-to-end cases, accessibility and visual comparison; produce source, test and review artifacts.

Complete only when every required workflow and acceptance row is evidenced, approved Home/Search fidelity is reviewed, no business logic has moved into the client, security behavior remains intact, and limitations are reported. Stop for user review before any later phase. Phase 6 cannot close the five Phase 4 environmental gates through UI tests.

Excluded: deployment, real GiroSol access, collectors, new ingestion/control operations, notifications, AI, billing, public organizations/registration, Redis, Elasticsearch, brokers, new distributed infrastructure, arbitrary files/credentials, checkpoint edits, force-read, gap clearing and source repair.

## Phase 6 authorized implementation — latest authority (2026-09-24)

The user approved Phase 6 implementation with exact Canva fidelity and the two narrow read-only query additions. See docs/reference/phase-6-approval.txt. Preserve all 382 accepted tests and backend behavior. Frontend work is authorized; earlier proposal-only statements are historical and superseded. No Phase 7, deployment, real GiroSol, notifications, collectors or AI.

The user subsequently confirmed the CURRENT live Canva mapping: **Page 1 Home; Page 2 Monitoring; Page 3 Search**. These are three separate approved screens, not alternate Search designs. Earlier two-page mappings are obsolete. Match all three; preserve the original Home image as a historical unchanged reference. Current Canva API page dimensions are 1366×768; visible design content includes a 1366×736 region. Exact API element geometry is recorded in docs/reference/canva-phase6-elements.json. Do not stretch the earlier raster to resolve this difference.

Greeting: use authenticated account display/login name; no hardcoded Salvador. Morning/Afternoon/Evening follows SONDA reporting/server timezone. Preserve greeting typography, location and structure. The smallest read-only session name field is approved.

Home Error Logs Info opens a centered Incident modal over Home: visible background with slight blur/subtle darkening, white rounded shadowed panel, top-right X, narrow left sections Overview / Order-Run / Evidence / History and larger right detail. Preserve page scroll and return focus on close. Use backend allowedActions. Overview includes application, severity/status, summary, first-detected/last-updated, identifiers, linked runs/result/version; Order-Run includes parent, attempt/previous attempt/recovery; Evidence is chronological and grouped with explicit provenance; History preserves transitions/recovery/manual actors/times. Do not invent absent fields or group distinct facts implicitly.

Five Phase 4 environmental gates remain pending separately. Phase 6 completion requires actual reference-versus-implementation captures/overlays, tests and review; no generated baseline is automatic approval.
