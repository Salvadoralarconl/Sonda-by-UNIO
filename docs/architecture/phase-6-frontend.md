# Phase 6 proposed frontend architecture and workflows

Proposal only; read [scope and decisions](phase-6-scope.md) first.

## Stack and component ownership

Use React with TypeScript, Vite, CSS Modules and CSS custom properties. React's [from-scratch guidance](https://react.dev/learn/build-a-react-app-from-scratch) supports a Vite-based client; SONDA already has its server and needs no SSR framework. Add React Router for browser routing, TanStack Query for bounded server-state caching, React Hook Form for nested configuration forms, native fetch behind one client, Vitest/Testing Library for components, and Playwright for browser/visual tests. Verify compatible supported versions and lock dependencies only after approval. No Redux, dashboard template, component framework that dictates appearance, or second backend. [TanStack Query documentation](https://tanstack.com/query/latest/docs/framework/react/overview).

Proposed new files (none created by this proposal):

```text
src/Sonda.Web/
  package.json, package-lock.json, tsconfig.json, vite.config.ts, index.html
  src/
    app/                 router, providers, session boundary, route errors
    api/                 generated OpenAPI types, fetch client, errors,
                         operation reconciliation, runtime report adapters
    components/          Shell, Rail, UtilityBar, MonitoringPanel, Card,
                         StatusLabel, EvidenceTable, Pager, Dialog, Field
    features/
      home/ search/ investigations/ incidents/
      configuration/ simulation/ accounts/ monitoring/
    styles/              verified tokens, typography, base accessibility
    test/                setup, bounded API fixtures, contract samples
  public/                approved licensed/exported visual assets only
tests/web/
  workflows/ security/ visual/ accessibility/
  references/            approved capture provenance and comparison metadata
docs/development/frontend.md
docs/phase-6-review.md
artifacts/phase6/         baseline integrity, tests, screenshots, comparisons
```

Shell owns the three visual regions. Feature components own presentation and forms. Query hooks own fetching, never business interpretation. Shared status components distinguish result, classification, workflow, business health and availability; they must not reduce all states to a generic red/green badge.

Development uses HTTPS/same-origin proxy semantics compatible with Secure cookies. A bounded static SPA build/hosting integration may serve the frontend from the existing host for local acceptance: preserve `/api`, health, OpenAPI, authorization and non-SPA 404 behavior; no deployment or service installation. Deep links must reload correctly. No Node service is introduced into production monitoring.

## API-to-screen mapping

All paths below are under `/api/v1`; the delivered OpenAPI and `src/Sonda.Server/Program.cs` are authoritative.

| UI | Existing API families / contract |
|---|---|
| Home/right monitor | GET `/dashboard`; returned metrics, five buckets, Incidents and application states; narrow display projection proposed in scope |
| Search | GET `/search`; filter-bound cursor, evidence identities, returned timestamps and linked run IDs |
| Applications | GET `/applications`, `/{id}`; paged `/profiles`; `/profiles/{id}/activation` supplies active version/session |
| Application/Order Runs | GET `/application-runs`, `/order-runs`, and `/{id}` with required session; proposed optional parent filter only if approved |
| Incident investigation | GET `/incidents/{id}` and `/occurrences`, `/recoveries`, `/history`, `/evidence`, with session and child pagination |
| Evidence | GET `/evidence/{id}?session=…`; stored evidence, interpretation JSON, truncation indicators |
| Workflow | POST `/incidents/{id}/status`; server `allowedActions`, expected revision, operation ID |
| Create application/Profile | POST `/applications`, `/applications/{id}/profiles`; authenticated team, initial inactive Draft |
| Sources | GET/POST `/profiles/{id}/sources`, PUT `/profiles/{id}/sources/{sourceId}` |
| Draft lifecycle | GET/PUT `/profiles/{id}/draft`; POST `/validate`, `/simulations`, `/publish`, `/activate` |
| Reports/versions | GET `/profiles/{id}/simulations/{reportId}`, `/versions`, `/versions/{version}`, `/activation` |
| Sessions | GET `/session/csrf`, `/session`; POST `/session/login`, `/logout`, `/logout-all`, `/password` |
| Grants/members | POST `/account-grants/redeem`; GET/POST `/team/members`; PATCH member; POST invitation/reset-grants |
| Diagnostics/audit | GET `/monitoring`, `/monitoring/applications/{id}`; Admin `/audit` |
| Uncertain commands | GET `/operations/{id}` and exact retry of original command where supported |

Preserve endpoint-specific paging: versions use `before`, not invented cursor parameters; Profile list has no application filter. Session-scoped identities include the session in routes and cache keys. Do not silently scan every Profile/page to complete a selector; provide explicit bounded paging. Surface server caps/truncation instead of claiming complete history. Flexible JSON report/operation payloads need tested runtime adapters and explicit unsupported-version states; OpenAPI generation alone does not type their contents.

## Session, cache and errors

Use HttpOnly Secure cookie authentication. Fetch CSRF token before unsafe requests (including login/redemption), send `X-CSRF-TOKEN`, refresh after identity changes. No bearer token storage, client-selected team authority, persistent raw-log/sample cache, or secrets in URLs/telemetry. Render logs/rules as escaped text; never `dangerouslySetInnerHTML`. Use browser upload only to read the user's chosen bounded sample, never to browse server paths.

Session state comes from GET session. On logout, expiry, disablement, role change or identity switch, cancel requests and clear all cached protected data; prevent late responses from the previous identity populating the next session. Sensitive text/identifier filters stay in memory rather than URL history. Grant tokens are explicitly pasted, shown once when issued, and not persisted or logged.

Queries are bounded, keyed by actor/session/resource/filters. Home/monitor may refresh every 30 seconds while visible and actively used; pause hidden tabs and inactive sessions, cancel superseded requests, and invalidate relevant queries after successful commands. The backend refreshes idle activity on authenticated requests, so unrestricted polling would keep sessions alive: stop background polling on browser inactivity; server expiry remains authoritative. No infinite automatic history fetching. Show last successful `asOf` with a stale/unavailable banner during outage; do not present cached data as live.

Use limited backoff for safe transient reads. Mutations never get blind generic retries. Generate operation ID once per intent, retain the exact body/revision while pending, reconcile by operation ID after timeout, and retry that same ID/body only when appropriate. An uncertain command remains “outcome unknown” until reconciled, not “failed.” Show a copyable operation ID for recovery after reload; do not reconstruct/resend a lost payload automatically. Conflict means refresh and review differences before a new intent/ID. A validation/authorization rejection is not a transient retry. Grant replay may return SecretUnavailable; explain explicit replacement instead of pretending the original secret is recoverable.

401: clear protected state, sign in again. 403: access denied, including revoked roles/CSRF failure; distinguish returned error codes and reacquire CSRF without replaying unsafe actions blindly. 409: revision conflict/unsupported operation with explicit refresh/review. Validation errors: summary and field focus, preserve entered values. Body/rate/cap limits: actionable bounded-input message. 5xx/network: unavailable with explicit retry. Empty is distinct from loading, unknown, unobserved, stale, disconnected and unsupported. No false zero/green fallback.

## Visual Configuration and simulation

Admin creates Application name/description, then initial Profile Draft, then source configuration. Team/IDs come from server responses. Source form includes manually entered local/UNC root, include pattern, recursion, encoding, required/enabled and accepted acquisition options. Explain that this path belongs to the monitoring server. Server allowed-root/authority validation is authoritative; a syntactically accepted UNC configuration is not evidence that SMB works. No browser, credentials, mapped-drive promise or arbitrary read action.

Editor sections: general/completion mode; parsing expression/timestamp/date/offset/source timezone; identifier kind/name/namespace/capture/case; Application cycle and Order rules; rule alternatives (Contains/Exact/Regex), enabled/target/priority, classification including Ignore, condition key/recovery; expected duration/grace/fallback; revision-2 correlation/routing/recovery compatibility, late/application-wide diagnostics, freshness/cadence. Preserve LegacyV1 revision without implicit upgrade. Explain fields using engine terminology and keep advanced policies collapsible. No global timeout defaults, hardcoded detection phrases, browser regex interpretation engine, or duplicated overlap/publication validator.

Forms provide type/required/size assistance; the backend validates business rules. Draft saving uses expected Draft revision, source edits use source revision, activation uses its own expected version/revision. Keep unsaved-change navigation protection. Live source restrictions remain: adding a source to an active Profile is not exposed as unrestricted CRUD; create a new Profile workflow as supported. Existing generation/framing restrictions display the server's rejection, never suggest a repair bypass. Enabled source configuration alone does not activate interpretation.

Lifecycle: **Draft → validate → simulate → inspect → publish → activate**, each explicit. Show validation diagnostics and warning acknowledgment; saving changes invalidates the displayed report's applicability. Publication submits current revision/report ID and acknowledgment; the server decides eligibility. There is no existing authoritative `canPublish` field: local presence checks may assist, but cannot announce eligibility or override server validation. Activation is separate and displays the intended immutable version, expected active version and concurrency conflict. Historical pinned versions remain unchanged.

Paste/upload sample text with client preflight and authoritative server limits (1 MiB request, 1,000 entries; normal requests 256 KiB). Expose an explicit simulation processing-time schedule, `asOf` and optional sample date: synthetic processing timestamps must be visible and reviewed, not silently equated with log event timestamps or browser business dates. Report uses existing simulator/server timezone. Show immutable provenance, Draft revision, completeness, diagnostics, rules matched/suppressed, Ignore, Application Runs, Order attempts/results, identifiers, calculated Problem Identity, Incidents/Occurrences/Recoveries and returned metrics. Paginate/virtualize bounded report display without changing its meaning. Never calculate alternate outcomes in JavaScript.

The existing preview accepts finite sample entries, not an interactive arbitrary deadline/manual-workflow command console. Do not promise browser controls for unsupported simulation commands. Show timeout/cap/unsupported report states. Publication's existing resimulation behavior and limits remain unchanged; frontend cannot claim to add hard process isolation where the backend does not have it.

## Investigation, accounts and diagnostics

Keep raw evidence, Application Runs, Order attempts and Incidents in distinct views with explicit cross-links. Run detail labels source/normalized event, processing, completion-processing, database commit/acknowledgment and reporting date/timezone independently wherever returned; show unavailable fields honestly. Incident detail consumes `allowedActions`; empty means no supported transition. Display revision, audit history, Problem Identity/episode, all bounded occurrence/recovery pages; never infer automatic resolution from a successful row. Preserve server ordering; UUID order is not “latest.”

Login and redemption use existing password policy/errors. Account supports password change/logout/all sessions. Admin members view supports invitation, role, enabled state, replacement invitation/reset grant, revision conflicts and last-Admin rejection. Grants are delivered manually through an explicit copy-once flow; no email integration or public account creation. Password/reset/disable session revocation comes from the server. Clear secrets from the UI after use.

Diagnostics consume only role-authorized fields: Member sees high-level availability; Admin sees existing source/host detail. Show pending environmental capabilities separately. No path probing, checkpoints, generation controls, force-read, gap clearing, file deletion/rename or collector controls.
