# Phase 1 proposal: domain foundation and Profile simulator

Status: **implemented and accepted by the user, including all 42 tests**. This document preserves the approved Phase 1 contract and original delivery gate. The subsequent [Phase 2 proposal](phase-2-scope.md) awaits approval; no Phase 2 implementation is authorized.

Approved amendments: equal priorities are invalid only for rules competing for the same input/target; `ProblemIdentity` / `IncidentKey` is an explicit exact domain value shown in reports; and a successful parent cycle containing multiple independently successful/failed orders is a required acceptance fixture.

## Outcome

A developer can provide a draft Profile and sample logs to a local simulation harness and inspect exactly how SONDA parses, groups, classifies, and resolves the configured scenarios. The engine must know no CAM/GiroSol-specific phrases by default. This becomes the same engine/report contract later used by the website's first-class Profile testing workflow.

Phase 1 proves approved domain behavior before file monitoring, a database, accounts, or Home Page integration. This approved scope has now been implemented; see [review results](../phase-1-review.md). Stop for review before Phase 2.

## Deliverables

| Deliverable | Proposed scope and reviewable output |
| --- | --- |
| Minimal .NET foundation | Create only `Sonda.Domain`, `Sonda.Application`, `tools/Sonda.Simulator`, relevant test projects, and needed solution/build settings. Keep future web, host, persistence, and deployment projects in the architecture tree until their phases |
| Typed domain contracts | Profile/version snapshot, parsing options, pattern alternatives/priority, scoped identifier, Application Run, Order Run, Detection Result, Classification, Incident, Occurrence, recovery record, and simulation/report types |
| Configurable parsing/matching | Sample timestamp/message/key-value extraction and bounded regex capture; Contains/Exact/Regex alternatives; explicit case options. Preserve raw sample references and parsed values; reject unsupported formats visibly |
| Priority and validation | Highest-priority matching detection rule wins; intentional Ignore override works. Show matches and overlap warnings. Unrelated rules may share priorities; reject demonstrated equal-priority competition on the same input/target, with no severity/ID tie-break. Report unproven regex overlaps as uncertainty |
| Core cycle/order engine | Serial, explicitly ordered sample stream; Begin, terminal Success/Failure, optional separate End for unambiguous configured outcomes; Profile-scoped ID extraction and per-cycle attempts; deterministic parent-close Undefined |
| Incident/Occurrence model | Repeated failures of the same unresolved problem append occurrences; different identities remain separate; later matching successful order/cycle records recovery and resolves eligible incidents without rewriting prior evidence |
| Problem Identity / Incident Key | Exact typed tuple of team, application, Profile, scope, identity namespace/identifier when applicable, and condition key. Canonical escaped serialization; no fuzzy text grouping. Reports display the calculated key, selected incident, and whether each occurrence created or appended to it |
| Simulation runner | Explicit sample input, Profile snapshot, source date/time context, server reporting timezone, seeded IDs, and fake clock; no filesystem watchers or production sources |
| Explainable reports | Human-readable report plus structured JSON with parsed fields, IDs, rules/priorities/winners/suppressed matches, overlap warnings, runs/results, incidents/occurrences/severities/recovery, open/unmatched evidence, and metric contributions |
| Provenance | Include draft/sample hashes, engine/report schema version, timezone, clock inputs, diagnostics, and run identity. Changing Profile content invalidates applicability of a previous report; sample output has no live side effects |
| Approved metric examples | Pure calculation for standard run outcomes; `completedOrderRunsProcessedToday` and five-day processed-order buckets use the simulated server clock. Keep source event time distinct from completion-processing time |
| Tests and usage guide | Synthetic fixtures, independently specified expected results, executable tests, and documented harness commands/report examples |

Use test/in-memory adapters only for this phase's orchestration. They do not stand in for later PostgreSQL transaction, locking, crash recovery, or authorization tests. Keep adapter boundaries narrow so the same Domain/Application behavior can be connected to real persistence later.

## Sample workflow

1. Supply a draft Profile and sample log text/file with explicit date/encoding/timezone context.
2. Validate the draft; show malformed rules, duplicate priorities, straightforward overlap warnings, and missing interpretation requirements.
3. Run the same engine intended for production in an isolated simulation session; a fake clock makes processing-time facts reproducible.
4. Inspect parsed lines, extracted IDs, cycles/orders, outcomes, selected severities, incidents/occurrences/recovery, and suppressed-rule explanations.
5. Edit the Profile and rerun; reports identify the exact content hash they describe.

The initial runner is a developer-facing console tool and report artifact. The eventual website must support paste/upload, visual draft editing, report review, and publication/activation. That user-facing workflow remains first-class in the architecture; its visual interface is Phase 6, not a second interpretation engine. Raw JSON configuration is not the eventual normal-user experience.

## Boundary-first behavior in this phase

At a valid parent-cycle close, process any order outcome on that input first, then finalize remaining open orders Undefined. A sample ending without a cycle close leaves its open runs Running; file/sample EOF does not invent completion. If no outcome or boundary exists, report the unresolved state.

Include optional Profile-specific expected-duration/grace/fallback fields in contracts and validate their shape. Do not invent durations or implement a global business timeout. Full fallback scheduling, restart/backlog coordination, and incomplete-application policies belong to later approved work. Phase 1 reports requested but not-yet-supported timing behavior explicitly instead of claiming it simulated a timeout it did not execute.

## Acceptance scenarios

| Scenario | Required result |
| --- | --- |
| Failed order inside a successful cycle | One failed order, one successful cycle, one Active Warning for the configured example |
| Multiple orders in one successful cycle | Three completed orders (two Success, one Failure), parent Success, one appropriate incident, Logs Today 3, and System Health 3 successful / 4 evaluated = 75% |
| Two failures then a successful retry in later cycles | One Incident with two failed Occurrences, then a preserved successful recovery event and Resolved status; all three order attempts remain |
| Same textual ID in two Profiles | No cross-Profile association or recovery |
| Different unresolved order/problem identities | Separate Incidents |
| Order unfinished at cycle close | Undefined, configured severity, Active incident, one completed-order volume contribution |
| Sample EOF without a close and no configured fallback | Run remains Open/Running; no synthetic timeout, Undefined result, or completed-order count |
| Broad Error plus higher-priority specific Ignore | Ignore wins; overlap warning and suppressed Error shown; no diagnostic incident/penalty from the losing rule |
| Reversed numeric priorities | Winner changes according to priority, proving severity does not decide |
| Equal competing priorities | Validation diagnostic; no silent winner |
| Equal unrelated priorities | Valid when predicates/targets do not compete; no blanket priority uniqueness requirement |
| Many lines for one completed order | One completed logical Order Run in Logs Today, not the number of lines |
| Application cycles and incidents without additional order completions | No additional Logs Today count |
| Historical source event first finalized by simulation today | Count in today's processed-order bucket; preserve old event timestamp separately |
| Same completed-order fact projected twice | Still one count; this tests pure projection idempotency, not database crash safety |
| Draft or sample changed after a report | Prior report is visibly stale for the new input |
| Standard health fixture | 985 / 1006 × 100 = 97.91%; failed historical attempts stay failed after recovery |

For deterministic fixtures, the same Profile/sample/clock/version inputs produce the same semantic report. Multiple alternatives matching one rule produce one logical match. Invalid parsing or missing identifiers is visible, not silently repaired with guessed identities.

## Explicit exclusions

- PostgreSQL migrations, live durable ingestion, real file/share monitoring, production deadlines, rotation, restart recovery, or real GiroSol access.
- Authentication/account website, shared deployment, notifications, remote collectors, or Home Page implementation/redesign.
- New implicit business policies for same-cycle retries, contradictory structural outcomes, overlapping cycles, terminal Success plus independent diagnostic health effects, or structural Failure plus winning Ignore. Report these as `PolicyDecisionRequired` when encountered until their remaining decision-register items are resolved.
- A production publication/activation API. Phase 1 defines simulation provenance and applicability; the real workflow gate arrives with persistence/configuration phases.

## Exit and next approval

Phase 1 is complete when the agreed synthetic scenarios pass, reports explain every relevant transition, no application phrases are embedded in engine code, and the user can review a sample Profile/report through documented commands. Record actual test results and limitations; do not claim persistence or operational guarantees from an in-memory simulator.

Deliver source, tests, sample Profiles/logs, human-readable and JSON reports, and a short review summary. Phase 2 remains the separate durable-persistence increment. **Phase 1 implementation is authorized; stop for review before Phase 2.**
