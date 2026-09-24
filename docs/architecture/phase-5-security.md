# Phase 5 proposed security, accounts and additive access storage

Status: **proposal, not implementation approval**. Parent: [scope](phase-5-scope.md). These are proposed access policies, not changes to log interpretation.

## Accounts and roles

Use ASP.NET Core Identity for password hashing, credential checks, reset tokens and security stamps. Add explicit SONDA team membership and session validation rather than treating a global Identity role claim as team authorization. Identity exposes configurable lockout/cookie/token behavior; wire only the required JSON endpoints rather than enabling its public registration UI. [Microsoft Identity configuration](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-configuration?view=aspnetcore-10.0).

Proposed first release: one team per account, individual credentials, two roles. Schema membership keys retain team scope; cross-team switching and public organization creation are out of scope. Test multiple independent teams even though the initial installation can use one. Admin is a team administrator, not a machine/DB administrator or cross-team superuser.

| Capability | Member | Admin |
|---|---|---|
| Read own team dashboard/applications/runs/incidents/Search/evidence | Yes | Yes |
| Read published Profile interpretation metadata | Yes | Yes |
| Supported incident workflow with reason and expected revision | Yes | Yes |
| View sanitized acquisition availability/deferred reasons | Yes | Yes |
| View exact configured paths, detailed acquisition diagnostics and audit | No | Yes |
| Draft edit, validation, sample simulation, publication, supported activation | No | Yes |
| Create/invite/disable members; change roles; issue reset grants | No | Yes |
| Change own password / end own sessions | Yes | Yes |
| Arbitrary source-file access, SQL, checkpoint/fence writes, forced outcome | No | No |
| Alter another team, suppress history, delete evidence, deploy service | No | No |

An admin may not disable/demote the last enabled Admin or remove their own last recovery path. A PostgreSQL transaction-scoped advisory lock keyed by TeamId serializes membership changes; disabling/demoting two admins concurrently must not leave zero. Role changes and account disablement increment an access revision and invalidate sessions. Do not hard-delete identities that appear in audit/history.

## Bootstrap and account lifecycle

Bootstrap is an explicit local operator command, not an HTTP route. It maps a first Admin to an already provisioned core team, uses a team lock, refuses to replace an existing enabled admin, and never ships a default password. A one-time credential/activation grant is displayed once through the operator's protected output; do not place a secret in process arguments or logs. Recovery of a completely inaccessible team also requires an audited local operator procedure, not a hidden web bypass.

An authenticated Admin creates an inactive member and single-use expiring invitation (proposed 24 hours). Admin conveys it out of band; no email connector/notification integration. Invitation redemption binds to the pre-created account/team, sets the password and consumes the grant transactionally. A supplied role/team in redemption is rejected. Issuing a replacement revokes the previous grant; token material is never returned from later queries.

If the original grant response is lost, an idempotent retry returns creation metadata with `secretUnavailable`, not a newly generated secret for the old operation. The Admin must issue an explicit replacement operation, which revokes the old grant. This preserves hash-only storage and single-use semantics without falsely promising replay of a secret retained nowhere.

Password change requires current password. Admin-issued reset grant has a proposed 30-minute lifetime and cannot reveal/set a user's permanent password directly; redemption revokes all previous sessions. Do not label an email address verified merely because an Admin typed it. Login identifiers are normalized uniquely; no self-registration, social login, federation or passwordless flow in Phase 5. MFA is explicitly deferred; this proposal is not internet production approval.

Proposed tunable access defaults: minimum 15-character passwords, support spaces/passphrases, maximum 128 characters, no silent truncation; Identity's versioned password hasher, no custom cryptography. Lock out after five failed attempts for five minutes, with separate IP/account rate limits. Proposed login rate cap: ten attempts/minute/IP with a small bounded burst. Return the same credential failure shape for unknown, disabled, locked or incorrect accounts. Do not echo whether an account exists through reset/redemption responses.

## Session and HTTP protection

Same-origin browser session cookie, Secure/HttpOnly, `SameSite=Lax`, host-only `__Host-` name, Path=/, no Domain attribute. Proposed idle expiry 30 minutes and absolute lifetime eight hours, enforced in the durable session row as well as ticket expiry. Rotate session identity on sign-in/privilege change; revoke on logout, reset, disable or role change. No bearer token in browser localStorage and no refresh-token subsystem.

Every authenticated request checks the current account, session and membership/access revision in PostgreSQL. Do not wait for Identity's periodic security-stamp refresh to enforce revocation. Fail closed if authorization state cannot be checked. Reauthorize resource access on retries, operation polling and pagination; an old receipt is not an authorization bypass.

Use ASP.NET Core antiforgery tokens on all cookie-authenticated unsafe methods, including login/logout/password and token redemption flows; a pre-login token endpoint may be anonymous. Reject missing/mismatched tokens before mutation. GET/HEAD never change business state. Validate Origin where supplied; do not rely on SameSite or CORS alone for CSRF protection. [Microsoft antiforgery guidance](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0).

Explicitly return JSON 401/403 rather than HTML login redirects. Disable cross-origin credentials by default. Require HTTPS in any non-test hosting configuration; use only configured trusted proxies for forwarded headers. Minimal public liveness response may expose only a boolean. Readiness, OpenAPI outside development, diagnostics, business resources and account details require authorization. No anonymous connection strings, filesystem paths, detailed exception messages or stack traces.

Persist Data Protection keys across restarts and protect them at rest under the intended service identity (restricted local key directory plus an explicitly configured certificate/Windows protection suitable for backup/restore). Stable application name, key rotation and tested restore of both database and required key material are part of setup. Explicitly setting a key repository does not by itself ensure at-rest encryption. [Microsoft Data Protection configuration](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0).

Proposed server limits: page 50 default/200 maximum; ordinary JSON bodies 256 KiB; simulation upload 1 MiB/1,000 lines; bounded search spans and timeout; one simulation concurrently and no unbounded request queue. Return 413/429 with safe codes/Retry-After. The values are scope defaults to verify against fixtures, not new log-format or business rules. Routine logs contain correlation ID, endpoint, duration and safe error code, not raw logs, search strings, passwords, tokens or sample content.

## Tenant isolation and admission

`ActorContext` is server-created from a validated session: stable AccountId, TeamId, Role and membership revision. Never deserialize it from an API body. Resource IDs are resolved with TeamId in the same query and through their actual parent relationships. A route/body/cursor cannot broaden scope. Unknown and foreign-team resource IDs return the same 404 shape; an authenticated known-team role violation returns 403.

Queries, evidence joins, DTO enrichments, counts, simulations, operation results and audit views all require team predicates. No endpoint takes arbitrary SQL, column names, file paths to read, arbitrary regex search, server timezone override, engine actor, sequence or fence token. Core composite keys remain unchanged. A dedicated read projection context/parameterized queries may be added outside core models; use a least-privilege read-only connection for pure projections where practical.

Authorization linearization for mutations: lock the relevant account/membership and operation identity in the short access transaction that admits the request and audit intent. A disable/demotion committed before admission prevents it. An already admitted command may finish afterward, retaining the original actor/time; it must not be replayed as a newly authorized action. A retry that has not yet been admitted must reauthorize. Recovery can finish an existing core pending command as system recovery with the recorded original actor, never attribute it to a new user. Polling still requires current permission.

## Additive storage, without changing accepted persistence

Propose **one separate `AccessDbContext` in the same PostgreSQL database**, schema `sonda_access`, with its own `__AccessMigrationsHistory`. Existing `sonda` business/acquisition tables, mappings, migrations and three-migration assertions stay unchanged. No second database/service or distributed transaction is introduced.

| New access table/group | Key and constraints / purpose |
|---|---|
| Identity users and required Identity store tables | UUID account key, unique normalized login; framework password hash/stamp/concurrency support; disabled accounts retained |
| `team_access_guards` | TeamId PK/FK to existing team; revision metadata; the shared team advisory lock serializes bootstrap and last-admin mutations |
| `team_memberships` | (TeamId, AccountId) PK; unique AccountId for one-team-per-account release; role CHECK Admin/Member; enabled/revision; membership is role authority |
| `web_sessions` | SessionId PK, account/team composite membership FK, issued/last activity/idle/absolute expiry, revoked timestamp, access revision; no clear bearer secret persisted |
| `account_grants` | UUID grant, account/team, purpose CHECK, unique token digest, expiry/consumed/revoked; transactional single use and replacement |
| `api_operations` | (TeamId, operationId) PK; submitting account, target, semantic payload hash, expected revision, internal command envelope/reference, state, safe outcome; conflicting reuse forbidden |
| `access_audit` | Immutable event UUID; TeamId/account/operation, action, target IDs, before/after revisions, UTC timestamp, disposition and correlation; no secrets/raw payload dump; append-only runtime grants |
| `profile_previews` | TeamId/reportId PK; existing Profile identity/draft revision; exact Profile/sample hashes, engine build, report, warnings, requester; immutable completed report |

Access account mutations and their audit entries share one access transaction. Cross-schema foreign keys point **from new access tables to existing identities** and do not add mutable fields to core rows. Query optimization starts with existing indexes. If measured access plans need a new core index or model change, report it for separate approval; do not silently alter migration 003 or add migration 004 to the accepted chain.

`api_operations` is an audit/idempotency ledger, **not a new work queue or engine receipt authority**. There is no automatic scheduler executing unactioned requests and no message broker. Workflow retries recover from existing core receipts/pending slots. Access metadata marks Submitted/Committed/Rejected/OutcomeUnknown and links the authoritative result; never announce business success from its own row alone.

Access migrations are explicit/offline, never automatic on host startup. Produce fresh access-schema SQL, idempotent reapplication evidence, core-table before/after hashes and backup/restore with accounts/sessions/key recovery. No change to existing core runtime grants is justified merely by the web host: access DML is granted only to the host, and migration DDL only to the operator. Secrets remain out of source control and DTOs.

