# Phase 3: approved interpretation and workflow policies

Status: **approved policies implemented and verified; stopped for user review**. Phases 1 and 2 are accepted. All 81 tests remain regression gates, with only the explicitly approved exact two-migration-chain assertion update. The policies below were approved by the user. Phase 4 remains unauthorized.

Read with [acceptance fixtures](phase-3-acceptance.md), [persistence and implementation boundaries](phase-3-persistence.md), and the [decision register](decisions.md). Canva remains the visual authority; this phase has no frontend work.

## Bounded objective and compatibility

Extend the same Domain/Application engine to interpret explicitly selected policies, simulate deterministic time and workflow commands, and persist their effects through the existing PostgreSQL adapter. Do not build a second engine or change historical interpretation.

**P3-D01 — version the semantics explicitly.** Published Profile snapshots without a policy revision retain `LegacyV1` behavior. Add a proposed `InterpretationPolicyRevision = 2` for newly published, validated Profiles selecting these policies. This is a long-lived compatibility contract, not test-specific branching. Existing snapshot bytes/hashes, stored receipts and report formats remain readable; old reports are not regenerated under new semantics. Keep existing snapshot serialization unchanged for absent new options (a versioned contract/serializer is required). New reports identify engine, policy revision and routing revision.

Why this is necessary: `Winning_ignore_plus_independent_failure_is_explicitly_deferred` explicitly asserts `PolicyDecisionRequired`, and `Versions_activate_only_at_temporary_safe_boundary_and_keep_history` asserts open-run activation rejection. Both must still pass on legacy Profiles. New revision-2 fixtures exercise approved extensions. Unknown policy revisions are rejected; no global flag silently upgrades every Profile. A legacy lane may move to revision 2 at a closed boundary after simulation. Thereafter compatible pinned-version activation is available. This proposal does not reopen previously blocked legacy receipts or authorize historical reprocessing.

## Proposed policy decisions

### P3-D02 — incomplete Application Runs and timing

A real configured Failure marker is evidence of **Failure**. Missing completion is **Undefined**, not proof of Failure. At explicit End: prior Failure wins over prior Success across separate inputs; Success alone gives Success; neither gives Undefined. An explicit Failure remains Failure even if an End never arrives and a configured fallback eventually closes the cycle. Success observed without the required End is still incomplete: fallback closes Undefined with the observed Success preserved. A duration limit is not an implicit Failure marker.

Each Profile version defines optional Application timing settings. No global timeout or production default is introduced:

| Setting | Proposed meaning |
| --- | --- |
| ExpectedDuration | Nominal elapsed duration from run start; crossing it exposes Overdue metadata, without closing the run or adding a business Incident |
| GracePeriod | Additional tolerance; with ExpectedDuration, Overdue starts only after expected duration plus grace |
| FallbackTimeout | Optional maximum elapsed duration from start, used when the configured boundary has not arrived; if grace is present the terminal deadline is start + fallback timeout + grace |
| UndefinedApplicationSeverity | Required Error or Warning when revision-2 incomplete-cycle handling is enabled; recommended initial choice Error, but publication must show the choice explicitly |

Durations must be positive, grace nonnegative; grace alone is invalid. If both expected duration and fallback timeout are set, fallback timeout must be at least expected duration. Expected duration alone never finalizes anything. Without a fallback deadline, a run may remain open indefinitely and is reported as such. An actual End/terminal boundary is processed before a due timer at the same effective instant. No timeout silently follows from sample EOF.

On closure, every still-open child Order becomes Undefined as in Phase 1, each with its own contribution and incident. Parent and child results remain independent. Failure severity remains Profile-configurable Error/Warning. Undefined Application severity is separate from the existing Undefined Order severity; Ignore is not a timeout severity.

### P3-D03 — Order fallback timing and deterministic clock inputs

Order fallback is explicitly enabled for Profiles whose application boundary is absent or cannot be relied on to arrive. Orders still require a known parent Application Run; this phase does not invent synthetic parents or standalone orders. A profile that cannot detect a parent Begin remains unsupported for structural orders.

Use the same expected/grace/fallback definitions at order scope. At a configured terminal deadline, an open order becomes **Undefined**, creating an outcome Incident with configured UndefinedOrderSeverity and `NextSuccessfulRun` recovery. Parent remains open. If the parent closes first, its boundary completes the order once and cancels the pending deadline. Later parent closure must not complete an already timed-out order again.

Clock progression is an explicit replayable command, not `DateTime.Now`, sleep, or a background scheduler. Event-based deadlines require a trustworthy normalized start instant. Profiles may explicitly choose processing-time anchoring when source time is unavailable; never silently switch clocks. Missing required event time produces a diagnostic and no guessed deadline. Store anchor kind and instant when opening the run; a later Profile activation cannot move its deadline.

`AdvanceTime(commandId, effectiveAt, processedAt, evidenceFrontier)` is accepted only after all declared earlier/equal evidence in the simulated frontier is consumed. A frontier is an explicit harness assertion, not proof that unread production files contain no success. In simulation, process evidence before timers on ties; due child closures precede parent closure, with stable run-ID tie ordering. Reject decreasing clock/frontier values. Uncertain or incomplete frontier delays timer evaluation rather than inventing a result. Production backlog reconciliation and timer scheduling are deferred with the file monitor.

Timeout closure has a synthetic **effective completion instant** equal to the deadline and a `CompletionTimeKind=Deadline` marker. It has no fabricated raw/source timestamp. Actual command processing/completion-processing timestamps remain separate, as does nullable database commit time. System Health attributes a timeout to its effective-completion reporting date; completed-order volume uses the first committed completion-processing date. Marker-driven legacy metric dates are unchanged. This explicit extension must be visible in reports.

### P3-D04 — same-cycle retries

An explicit Order Begin for an identifier whose previous attempt in that same parent is finalized creates a new attempt, numbered 1, 2, 3… within `(team, Profile, parent run, exact identifier namespace/value)`. Retain `PreviousAttemptId`; never overwrite a completed attempt. Begin while that identifier's attempt is still open is an ambiguous duplicate: preserve evidence and block that routing partition before business mutation. A plain repeated outcome without a new Begin is not a retry.

A later successful attempt may recover earlier failed/Undefined attempts in the same parent when exact Problem Identity and recovery eligibility match. It must begin after the earlier failed attempt's finalization and succeed after it; a success that was already running when the failure occurred cannot recover it. Across parents, apply the same causal eligibility rule, not merely numerical cycle ordering. No fuzzy identifier matching. Identifier reuse without a configured discriminator remains a Profile validation/production-data concern.

Each finalized attempt contributes once to completed-order volume and health. A successful parent with failed attempt 1 and successful attempt 2 has health 2/3, volume 2, and a recovered Incident; retry success does not erase the failed unit.

### P3-D05 — overlapping cycles and serial behavior

Offer two explicit modes:

- `Serial`: only one open parent per stream. Default unexpected Begin policy is `RejectAmbiguousBegin` with diagnostic and no business mutation. An explicitly selected `CloseIncompleteAndStartNew` may treat the new Begin as a boundary: close old parent Failure if Failure was observed, otherwise Undefined; finalize unfinished children Undefined; then start the new parent atomically. Prior Success without its required End does not imply completion. Validation requires a format-level guarantee that Begin means a new serial execution.
- `Correlated`: simultaneous parents require a configured exact cycle correlation key, reliably extracted on every structural input and run-scoped diagnostic. Route children by parent key plus order identifier. Missing/ambiguous key is quarantined with no inferred association. A Begin reusing an open correlation key is rejected. A finalized key cannot be reused in the same configured correlation epoch; repeated external IDs require a reliable configured epoch/discriminator, not a guessed date.

No nearest-time or “most recently opened cycle” heuristic. Correlated mode without complete correlation configuration/sample coverage fails publication. A keyless diagnostic may only use explicitly selected application-wide scope. Unrelated correlated partitions can continue after a partition-local conflict; if no partition is identifiable, block the routing lane. Distinct parents using the same order identifier remain distinct attempts.

For concurrent cycles, recovery requires a successful attempt whose Begin is causally after every unresolved occurrence it proposes to cover. Overlapping independent successes do not clear newer/concurrent failures. This deliberately favors keeping an incident open over falsely reporting recovery.

### P3-D06 — structural conflicts

Detection-rule priority resolves competing diagnostic classifications; it does not make a Success marker win over a Failure marker.

| Input situation | Proposed decision |
| --- | --- |
| Same logical input matches Success and Failure for one target | Diagnostic `ConflictingStructuralOutcomes`; no state/effects from that input; block affected partition |
| Same input matches Application Begin and terminal/End | Reject ambiguous combined lifecycle input in Phase 3; configuration must disambiguate it |
| Order Begin plus one terminal outcome on one input | Preserve existing supported deterministic begin-then-finalize behavior |
| Explicit-End cycle sees Success then Failure, or Failure then Success, on distinct inputs | Preserve both observations; Failure wins when End arrives |
| Explicit-End cycle sees Failure before End | Record Failure observation and create/enrich its outcome Incident immediately; run stays open and adds no metric until closure |
| Terminal-marker run already finalized, then opposite outcome arrives | Late evidence policy; never change original result |
| End with neither outcome | Undefined, configured incomplete severity |

A run's immediate Failure occurrence is a detection fact before finalization, not a prematurely completed run. End enriches that same occurrence; it does not duplicate it. A later success marker on that same failing explicit-End cycle cannot recover its own problem.

### P3-D07 — structural Failure with winning Ignore

Recommend **Failure remains Failure, counts as unsuccessful, and creates the configured structural outcome Incident**. Ignore suppresses only the diagnostic-classification effect selected by that rule; it cannot suppress an independently configured structural Failure, its outcome Incident or its evaluated-run metric. Report both facts and a conspicuous `IgnoreDoesNotSuppressStructuralFailure` explanation. To declare that line harmless, change the structural Failure rule and resimulate before publishing.

This keeps priority meaningful: a specific Ignore can suppress broad diagnostic Error/Warning patterns without making independently proven execution failure disappear. Even if Ignore uses the outcome condition key, it cannot cancel structural Failure. Validation warns about this combination. This is a proposed explicit resolution of D04 and needs approval.

### P3-D08 — Success with an independent diagnostic

Result stays **Success**. A winning Error/Warning diagnostic creates or enriches its own Incident; current application health follows unresolved severity. System Health still counts the completed run as successful. Severity never changes the result or adds another denominator unit. Thus 100% System Health and current Error can truthfully coexist and must be explainable.

A run cannot resolve a diagnostic that it generated itself. `NextSuccessfulRun` requires an eligible later run with no matching diagnostic for that same problem; unrelated diagnostics do not prevent recovery of another problem. `ManualOnly` remains unresolved until manual resolution. Parent Success cannot recover an order problem. Once a run is finalized, subsequently received diagnostics are handled as separate late observations, not inserted into its final metric fact.

### P3-D09 — manual Incident workflow

Allow Active → Investigating, Investigating → Active, Active → Resolved and Investigating → Resolved. Do not allow manual reopening of Resolved in this phase. A later genuinely new problem observation creates a new episode with the same exact Problem Identity; a duplicate receipt or late enrichment of an old occurrence does not.

Commands require stable command ID, expected incident revision, actor identifier, processing time and reason (nonblank for resolution). Store immutable before/after status, actor, reason and command provenance. Same command retry returns its prior result; changed payload under the same ID is rejected. A new command requesting the existing status returns an explicit no-op receipt without fictitious transition history. Stale revisions conflict. Only an accepted non-no-op command increments revision.

Manual resolution removes that episode from unresolved health, but changes no run result, occurrence or historical metric. It records `ResolutionKind=Manual`, not a successful Recovery. This phase provides trusted harness/Application commands; permission roles, actor authentication and UI are deferred. Actor strings supplied by a harness are not claimed to be authenticated identities.

### P3-D10 — recovery after manual resolution and recurrence

Recommend attaching the **first later eligible success as a Recovery confirmation** to a manually resolved episode if it matches the episode's original recovery contract. Preserve the manual resolution timestamp, actor, reason and status transition; append an audit fact “success observed after manual resolution,” not a second Resolved transition. ManualOnly episodes do not gain automatic confirmations.

The confirmation creates no new Incident episode. A new failure after resolution does create a new episode with the same Problem Identity, new incident ID and next episode ordinal. At most one unresolved episode exists per exact key. Recovery is evaluated per episode: one success may confirm an older manually resolved episode and resolve a newer active episode only if its causal ordering and recovery contract cover each independently. An older/in-flight success cannot clear a newer failure. A failure arriving after a success, with insufficient trustworthy ordering, remains unresolved rather than being retroactively cleared.

Keep at most one successful recovery confirmation per episode for this scope (first eligible success); duplicate processing or further successes add none. Recovery describes successful execution, while status history describes workflow. Neither replaces the other.

### P3-D11 — late/out-of-order evidence

Committed finalized results, first completion timestamps, metric facts and original evidence associations remain immutable. Do not reopen a finalized run or revise yesterday's percentage because a late opposite marker appears. Record a separate evidence disposition linking the late input and, when unambiguous, the closed target.

Late evidence may create an independent diagnostic Incident **only** when a configured diagnostic rule explicitly permits late-observation scope. No automatic business Error is inferred from lateness itself. Such observations contribute no run or volume unit. Without reliable correlation, do not attach the evidence to a closed run by identifier/time proximity; label unmatched/ambiguous and retain it for investigation.

Out-of-order inputs addressing an open run can be accepted only if their normalized times do not precede its start and their sequence respects the declared evidence frontier. Pre-start/ambiguous terminal inputs are quarantined. Accepted processing order remains authoritative for causal recovery; source timestamps additionally prevent backward causality, not reorder history. Historical correction/reprocessing requires a separately approved future feature.

### P3-D12 — activating versions while runs are open

For revision-2 Profiles, replace blanket rejection with **pinned interpretation plus validated stable routing**. Each parent keeps the exact Profile Version that opened it; all of its child attempts, parsing, matching, deadlines and diagnostic contracts use that version until it closes. A new parent after activation uses the newly active version. A new order inside an old parent uses the old parent version. Superseded means unavailable for new parents, not unavailable for completion or replay.

This requires routing before version-specific interpretation. Publish an immutable routing contract describing source ownership, serial/correlated mode, cycle key extraction and Begin routing. It must be compatible across simultaneously draining versions. A raw input is captured once and has one durable routing disposition; never blindly apply it to every version. Ambiguous routing blocks that input without double interpretation.

Serial mode continues non-Begin inputs under the open parent version. Only a Begin recognized by the shared routing contract can start a new-version parent; unexpected Begin follows the explicitly selected serial policy. Correlated mode routes an existing key to its pinned version and a new key to the active version. Activation is serialized with input processing and its effective sequence is recorded. Retries return the original routing/version decision.

Changes to cycle-key extraction, source ownership, Begin routing or serial/correlated mode that cannot coexist require a **drain-and-activate** transition. This is a precise incompatibility rejection, not the old blanket business prohibition. No forced closure or version guessing. Legacy-to-revision-2 upgrade also drains first.

Unresolved Incidents retain their occurrence versions and immutable recovery contracts. New-version success may recover an old problem only when a validated recovery-compatibility identity matches: exact identifier namespace/normalization, condition meaning, stream and recovery criteria. Identifier or condition meaning changes must use a new explicit namespace/condition key; old incidents remain distinct. Different extraction syntax may be declared compatible only with explicit validation and sample evidence. Never silently migrate keys. Incompatible recovery semantics with an unchanged key are rejected until rekeyed or drained/resolved; no automatic cross-version mapping feature is included.

### P3-D13 — diagnostics outside cycles

An explicitly configured application-wide diagnostic Error/Warning can create an evidence-backed Occurrence and Incident without any Application/Order Run. Exact problem key includes Profile, stream and condition; recurrence groups by that key, not message similarity. Each distinct diagnostic input contributes one observation occurrence; duplicate input contributes none. Ignore creates no Incident. Order diagnostics still require a reliably associated parent and order attempt.

These incidents affect current application health, but add **zero** evaluated-run or completed-order metric units. Recovery defaults to ManualOnly unless the Profile explicitly selects a later compatible successful Application Run. That run must begin after the diagnostic observation. No synthetic run is created to satisfy existing foreign keys.

### P3-D14 — availability and staleness

Introduce an independent availability read model: `NotObserved`, `Fresh`, `Stale`, `Disconnected`, `Unknown` (including incomplete source coverage), with observation reason and as-of time. Preserve business health as nullable Stable/Warning/Error. No incident plus no completed observation is not Stable. If prior finalized observations exist, retain last-known business health, but do not describe stale/disconnected data as currently healthy.

Simulated source observation facts provide last successful read, last read failure and last evidence time. Runs separately provide last run start/completion. A successful empty read proves source access, not a successful application run. Profile-specific read-freshness thresholds and expected run cadence are optional; no universal silent freshness limit. Missing thresholds mean Unknown, not indefinitely Fresh. For multiple required sources: explicit current failure yields Disconnected; missing evidence yields Unknown/NotObserved; expired read or configured run cadence yields Stale; all required checks passing yields Fresh. Optional sources do not conceal a required-source failure.

Projection may expose last-known business health and unresolved severity alongside availability. An unqualified “healthy now” assertion is allowed only when business health is Stable and availability Fresh. Staleness/read errors create operational facts, not fabricated business failures or System Health penalties. No colors/layouts are implemented or redesigned; how the approved design presents these semantics must be reviewed during the frontend phase.

## Proposed completion gate

Phase 3 would complete only after explicit approval of these decisions, additive domain/application changes, a reviewed forward migration, all **81 existing regression tests unchanged and passing**, new deterministic fixtures, PostgreSQL parity/restart/crash/concurrency tests, and a review report identifying remaining unsupported inputs. Do not silently replace legacy assertions with new expected outcomes.

See the companion documents for executable-fixture specifications and persistence scope. Deliver source, actual test counts, simulation reports, migration/upgrade evidence and known limitations. Stop for review before any following phase.

## Explicit exclusions

No production file watching, continuous ingestion, real timer scheduler, source discovery/rotation/cursors, live GiroSol integration, frontend/Home/Search implementation, accounts/authentication UI, HTTP API, notifications, remote collectors or deployment. No microservices, Redis, queues or other speculative infrastructure. No historical reprocessing, fuzzy identity matching, arbitrary overlap without keys, standalone parentless orders, manual reopening, or automatic recovery-key remapping. Existing simulator remains runnable without PostgreSQL.
