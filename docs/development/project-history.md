# SONDA

SONDA interprets application logs using user-configured Profiles. It reconstructs Application Run Cycles and Order Runs, detects incidents, tracks recovery, and presents a calm monitoring dashboard.

**Status: Phase 5 is accepted; all 382 tests are regression gates. Phase 6 implementation is authorized with exact live Canva fidelity; work is in progress. Five Phase 4 environmental gates remain pending.**

Review the [approved Phase 2 scope](../architecture/phase-2-scope.md), [schema and EF mappings](../architecture/phase-2-schema.md), and [transaction/adapter contract](../architecture/phase-2-transactions.md). Open-run activation rejection remains for LegacyV1; revision-2 Profiles support compatible pinned activation. The approved [Phase 3 policies](../architecture/phase-3-scope.md) replace it for revision-2 Profiles. See the approved Phase 4 implementation scope and review below.

Start with the [Phase 1 review and run instructions](../phase-1-review.md) for source links, test results, sample reports, and limitations.

## Sources of truth

- [Master specification](../reference/master-specification.txt): preserved verbatim from the user attachment.
- [Profile parameters specification](../reference/profile-parameters-specification.txt): subsequent user clarification; preserved verbatim and incorporated into the proposal.
- [Canva approved design record](../reference/canva-approved-design.md): **@Canva → Sonda Home Page Design** is the primary visual authority; all three pages reviewed: Page 1 Home, Page 2 Monitoring, Page 3 Search.
- [Permanent visual design guide](../visual-design-guide.md): preserved raster observations and visual guidance; do not redesign Home.
- [Approved Home Page image](../reference/sonda-approved-home.png): unchanged project-local copy of the approved image.
- [Architecture proposal and reading map](../architecture/README.md): start here for technical design.
- [Decisions requiring approval](../architecture/decisions.md): proposed policies are not approved business rules.
- [Approved Phase 1 scope](../architecture/phase-1-scope.md): bounded domain-engine and simulation deliverables, including the three amendments.

The visual guide's statement about architecture being outside its scope describes that document's original task. Its authority section now records the user's Canva designation; the original raster observations remain. Canva governs appearance, while approved architecture and the accepted Phase 1 engine/tests govern behavior.

Future sessions should read both specifications, the visual guide, architecture proposal, and decision register before implementing features. Record explicit approvals in the decision register; do not infer them from the existence of these documents. Confirmed deployment: shared website, team admin/member accounts, configuration once per team, and the monitoring server's timezone for daily metrics.

Phase 3 delivery: [review, source, tests and limitations](../phase-3-review.md). All 237 tests pass, including the 81 accepted legacy cases; all 119 PostgreSQL tests also pass with commit tracking disabled. Phase 4 implementation is approved; its local results and environmental limits are recorded in the Phase 4 review.

Approved Phase 4 design: [Phase 4 acquisition scope](../architecture/phase-4-scope.md), [checkpoint/schema/transaction design](../architecture/phase-4-persistence.md), and [acceptance matrix](../architecture/phase-4-acceptance.md). Implementation includes the reader, additive migration 003 and worker host; no production service was installed.

Phase 4 delivery: [review and acceptance evidence](../phase-4-review.md), [operation and pilot preparation](../development/file-acquisition.md). Full capability sign-off remains pending controlled SMB and installed Windows-service/boot verification. No real GiroSol access was performed. Phase 5 implementation is approved; see the Phase 5 review.


Approved Phase 5 design: [Phase 5 scope](../architecture/phase-5-scope.md), [security/accounts](../architecture/phase-5-security.md), [API contracts](../architecture/phase-5-api.md), and [acceptance](../architecture/phase-5-acceptance.md). Shared server, direct local/UNC worker, existing PostgreSQL model and engine remain authoritative. No remote collectors. Canva is the Phase 6 visual authority. Phase 5 implementation is delivered for review; stop before Phase 6.



Phase 5 delivery: [review and evidence](../phase-5-review.md), [backend operation runbook](../development/shared-backend.md), and [Admin provisioning amendment](../architecture/phase-5-provisioning.md). Stop for review before Phase 6.

## Historical Phase 6 proposal gate — superseded by implementation approval below

The user accepted Phase 5 and all **382 passing tests**. Preserve all 382 as regression gates, including the 309 prior cases and original 237 subset. Preserve existing Domain/Application, persistence, acquisition, account, API and security behavior. Earlier delivery-time stop gates remain historical; this paragraph supersedes older current-status statements.

Phase 6 implementation is **not authorized**. Review the bounded frontend proposal before writing application code. Canva Page 1 Home and Page 2 Search remain the visual authority. Five Phase 4 environmental verification gates remain explicitly pending; no frontend test can close them. No deployment, real GiroSol access, collectors or notifications.
`Phase 6 review:` [scope and decisions](../architecture/phase-6-scope.md), [frontend architecture and workflows](../architecture/phase-6-frontend.md), [acceptance plan](../architecture/phase-6-acceptance.md).

## Phase 6 authorized implementation — latest authority (2026-09-24)

The user approved Phase 6 implementation with exact Canva fidelity and the two narrow read-only query additions. See docs/reference/phase-6-approval.txt. Preserve all 382 accepted tests and backend behavior. Frontend work is authorized; earlier proposal-only statements are historical and superseded. No Phase 7, deployment, real GiroSol, notifications, collectors or AI.

The user subsequently confirmed the CURRENT live Canva mapping: **Page 1 Home; Page 2 Monitoring; Page 3 Search**. These are three separate approved screens, not alternate Search designs. Earlier two-page mappings are obsolete. Match all three; preserve the original Home image as a historical unchanged reference. Current Canva API page dimensions are 1366×768; visible design content includes a 1366×736 region. Exact API element geometry is recorded in docs/reference/canva-phase6-elements.json. Do not stretch the earlier raster to resolve this difference.

Greeting: use authenticated account display/login name; no hardcoded Salvador. Morning/Afternoon/Evening follows SONDA reporting/server timezone. Preserve greeting typography, location and structure. The smallest read-only session name field is approved.

Home Error Logs Info opens a centered Incident modal over Home: visible background with slight blur/subtle darkening, white rounded shadowed panel, top-right X, narrow left sections Overview / Order-Run / Evidence / History and larger right detail. Preserve page scroll and return focus on close. Use backend allowedActions. Overview includes application, severity/status, summary, first-detected/last-updated, identifiers, linked runs/result/version; Order-Run includes parent, attempt/previous attempt/recovery; Evidence is chronological and grouped with explicit provenance; History preserves transitions/recovery/manual actors/times. Do not invent absent fields or group distinct facts implicitly.

Five Phase 4 environmental gates remain pending separately. Phase 6 completion requires actual reference-versus-implementation captures/overlays, tests and review; no generated baseline is automatic approval.

Phase 6 implementation is in progress: [verified increment and remaining work](../phase-6-progress.md). Latest tests are not a Phase 6 completion claim.



