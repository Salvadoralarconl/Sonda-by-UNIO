# Interpretation engine and incident lifecycle

Status: overall direction and approved boundary/priority/occurrence/simulation decisions incorporated. Remaining edge cases refer to [decisions](decisions.md). The bounded Phase 1 state machine is implemented; see ../phase-1-review.md for limitations and results.

## Deterministic processing pipeline

Input is a durable Normalized Log Entry, an immutable Profile version, persisted runtime state, and an explicit clock/event time. Output is a proposed batch of state transitions and facts. Applying that batch is transactional and idempotent.

```text
Durable input → normalize/extract fields → select Profile version
  → evaluate structural and diagnostic matches
  → correlate application cycle
  → extract application/Profile-scoped order identity
  → associate/create order attempt within that cycle
  → attach original evidence links
  → finalize outcomes when a terminal condition or approved deadline occurs
  → classify detections and create/update incident occurrences
  → apply eligible later-success recovery
  → record metric contributions and current application state
  → commit processing receipt and all effects together
```

No regex, file notification, UI interaction, or incident resolution directly increments a global counter. Counters are projections of committed facts. Replaying an already-committed input receipt produces no additional domain effects.

## Pattern matching

Contains and Exact use explicit ordinal case-sensitive or ordinal case-insensitive comparisons. Case mode is Profile data, never dependent on server culture. Regex operates on the selected bounded field/message with an explicit timeout and supported-option policy. Cache compiled/interpreted matchers by immutable version hash. Prefer the nonbacktracking regex engine for compatible expressions; show unsupported constructs rather than silently changing semantics. User-defined regex is data, not executable code. [Microsoft regex guidance](https://learn.microsoft.com/en-us/dotnet/standard/base-types/best-practices-regex).

Set maximum record length, pattern length/count, capture sizes, per-match timeout, and total evaluation budget. Exact budgets are operational settings measured in simulation. A timed-out mandatory matcher does not become “no match”: quarantine the input, preserve evidence, and mark processing incomplete. Unrelated applications may continue. Repeated rule failures surface as monitoring/configuration issues.

Pause dependent interpretation in the affected correlation lane after a structural parse/matcher failure; acquisition can continue durably within storage limits. Do not skip an unknown Begin/End and then present subsequent grouping as reliable. A quarantined receipt is not an Applied receipt. An explicit audited retry can transition it to Applied once under the same interpretation version if the transient failure is resolved. Changing interpretation rules for quarantined evidence requires an explicit replay/cutover plan; it is not an automatic historical rewrite.

Run structural rules and diagnostic rules in separate passes. A matched Ignore diagnostic does not drop the evidence or bypass begin/end markers. No global search for words like “error,” “failed,” or “success” exists outside explicitly configured rules.

Approved conflict policy (D04): for the same input and classification target, evaluate enabled matching detection rules by explicit priority and use the highest-priority match. The winning rule determines the condition/classification. Ignore can deliberately beat a broader Error or Warning; no severity ranking breaks conflicts. A different condition key does not let a suppressed rule create a second incident. Separate correlated targets receive their own decision. Remove the previous exclusive-match-group loophole.

Approved amendment: reject equal priorities only for enabled rules that compete on the same input/target; unrelated rules may reuse priorities. Reject statically proven equal-priority overlap and actual sample collisions; flag unproven regex intersections as warnings. Show every overlap and suppressed match. Do not implement a blanket numeric uniqueness constraint or silently break ties. Static regex-overlap analysis is not assumed complete.

Current-health aggregation still uses Error > Warning > Stable across already-established unresolved incidents; that separate aggregation must never be reused to select a detection rule. A run contributes at most one unsuccessful health unit regardless of its detection count.

## Application Run Cycles

Each application has a serialized interpreter lane. A cycle has a monotonically assigned sequence, optional extracted external correlation key, Profile version, start evidence, lifecycle, outcome, and terminal reason. The initial supported mode is one active cycle per configured stream. Overlapping cycles require a configured cycle key present or reliably extractable on relevant messages; do not infer correlation from timing alone.

| Existing state | Input | Proposed transition |
| --- | --- | --- |
| No open cycle | Begin | Create Open cycle; retain begin evidence |
| Open cycle | Ordinary line | Link to this cycle when correlation is unambiguous |
| Open cycle, TerminalMarker mode | Success | Finalize Success; close incomplete orders; eligible application recovery |
| Open cycle, TerminalMarker mode | Failure | Finalize Failure; close incomplete orders; normally create/update application Error incident |
| Open cycle, ExplicitEnd mode | Success/Failure | Record outcome evidence; keep lifecycle Open until End; a configured Error can create an incident immediately |
| Open cycle, ExplicitEnd mode | End | Evaluate accumulated outcome evidence, finalize result, and close incomplete orders |
| Open cycle | End with no outcome | Finalize Undefined under configured incomplete-cycle policy |
| Open cycle | Deadline reached | Finalize Undefined under configured deadline/severity policy |
| Open cycle | Another Begin without distinct correlation | Flag overlap; proposed serial-mode policy closes old cycle Undefined then opens new one, subject to D09 |
| No open cycle | Success/Failure/End | Preserve unmatched evidence and diagnostic; do not invent a cycle silently |

The master explicitly requires Undefined for incomplete orders; applying Undefined to incomplete application cycles is a proposal (D02). Never let the absence of a line at the current file end imply completion: the application may simply be idle or still writing.

Evaluate all matches before mutating state. A line matching both Success and Failure for the same run is a conflict; recommended behavior is quarantine/diagnostic rather than silently choosing success. A line matching Begin and a terminal marker can represent a zero-duration run only if the Profile explicitly allows it; otherwise flag it.

The additional specification requires an independent End mode. In ExplicitEnd mode, collect success/failure evidence until End, then select the finalized result. Recommended D03 policy: any configured application Failure evidence dominates Success, Success with no Failure yields Success, and neither yields Undefined. This conflict policy needs approval. In TerminalMarker mode, the first valid terminal event closes the run. Mode is explicit per Profile, never inferred from phrases. A later End remains supporting evidence only if it can be reliably associated with a finalized terminal-mode cycle.

## Order identity and attempts

Confirmed identity scope: team + application + Profile + configured identifier meaning. Every Order Run also records its parent Application Run Cycle. `CAM/93822` and `UNITELLER/93822` are unrelated. `CAM/93822` in cycles 40 and 41 is the same scoped identity with two distinct attempts.

Recommended keys:

```text
Business identity = team + application + profile + identity namespace + identifier
Attempt identity  = application run ID + business identity ID + attempt number
```

The namespace prevents a changed extractor from inadvertently joining unrelated orders. Reused identifiers for unrelated orders within the same Profile remain a data ambiguity; a cycle link alone cannot both prevent all such collisions and enable cross-cycle retry resolution. Verify reuse with realistic sample logs. If needed, add a user-configured business discriminator/retry horizon in a future approved Profile policy (D08), rather than assuming global uniqueness.

An Order Begin with an extracted identity creates an Open attempt in the correlated cycle. Additional matching entries attach by identity and cycle, not physical adjacency. A Success or Failure closes that attempt. A subsequent explicit Begin for the same identity in the same cycle creates another attempt number; it never overwrites the earlier attempt (D03).

| Evidence problem | Proposed handling |
| --- | --- |
| Missing identifier on Order Begin | Keep cycle/evidence and association diagnostic; do not create a guessed identity |
| Identifier missing on a continuation | Attach only through an explicitly configured reliable correlation mechanism; otherwise keep cycle-only evidence |
| Multiple conflicting extracted IDs | Quarantine association; never pick the last capture arbitrarily |
| Interleaved IDs | Maintain separate open attempts keyed by cycle and identity |
| Order Success without a known Begin | Unmatched evidence/diagnostic; no invented successful attempt |
| Order evidence without an identifiable cycle | Keep unassigned; no unrelated cycle guessed |
| Terminal message for a finalized attempt | Deduplicate only if same physical input; otherwise retain unmatched/late evidence and diagnose, unless an approved correlation rule explains it |

Do not carry an open Order Run into another cycle. Cross-cycle retry creates a new attempt linked through the scoped Order Identity. A new cycle does not make an older failed order successful.

## Success, Failure, and Undefined

- **Success:** valid Begin and a matching configured Success terminal condition.
- **Failure:** valid Begin and a matching configured Failure terminal condition.
- **Undefined:** a started run has reached an explicit completion boundary or configured deadline without either outcome.

Open runs have no finalized result. EOF on a currently monitored file is not a completion boundary. File rotation is not automatically a business boundary. Closing a parent cycle can be an approved order-finalization boundary, but its policy must be explicit.

Approved boundary-first policy (D02): when the parent cycle closes, finalize its remaining open orders as Undefined if no configured Success/Failure was seen. Undefined is inferred, not a user-entered pattern. Apply relevant order outcome matches on the closing input before the incomplete-order sweep. A configured grace period must not arbitrarily postpone a reliable cycle close.

Where no reliable cycle boundary is available, the Profile may explicitly configure fallback timeout/grace behavior using observed cadence. With neither a reliable boundary nor a configured fallback, keep the run Open and report that completion cannot yet be determined; never inject a global timeout. Application Runs must eventually support their own Profile-specific expected-duration/grace rules. Define the policy contract now; production duration values and complete deadline execution are later work. The resulting incomplete-application classification remains separately reviewed.

Persist deadlines and target revisions. A fake clock drives them in tests. Before firing a live deadline, process already-captured earlier evidence for the relevant stream and inspect acquisition lag. If the monitor cannot establish a current observation boundary, report delayed evaluation instead of blaming the application for SONDA's backlog. Recheck under the transaction lock so a simultaneous Success cancels the deadline safely.

On restart, restore deadlines and process retained backlog before applying overdue timeouts. Use logged event time where reliable and separate ingestion/processing times for audit. Late data after a finalized deadline is preserved and flagged; initial behavior does not rewrite finalized history. A later correctly started successful attempt may recover its incident (D10).

## Classification: Error, Warning, Ignore

Structural cycle Failure defaults to Error; structural order Failure defaults to Warning. Undefined produces Error or Warning according to Profile policy; it cannot be silently ignored because the specification requires incomplete orders in incident views. Explicitly configured detection rules can alter the classification of an order failure within the approved policy.

Diagnostic Error/Warning creates a problem for a defined target and condition. A diagnostic cannot become an order incident without a reliable order association. Keep an operational diagnostic for that association failure; do not silently promote it to an application failure.

An explicitly application-scoped rule may match outside any cycle (D14). Proposed handling: create an evidence-linked application diagnostic incident without inventing an Application Run; it affects current health but contributes no evaluated-run health unit. Its configured recovery policy controls whether a later cycle can resolve it; default to ManualOnly unless the admin explicitly selects compatible next-cycle recovery. Preserve the detection sequence to prevent older success from clearing it.

When one line matches both a structural Order Failure and its configured Warning filter, coalesce them into one occurrence if they represent the same target and `conditionKey` (normally `order-outcome`). Keep both rule matches as evidence. Distinct explicitly named conditions may create separate incidents. The configuration preview explains this mapping so the supplied full CAM example produces one Warning, not two.

Use `(session, target run, condition key)` as the normal occurrence deduplication key. Repeated evidence within that run adds evidence/matches and may escalate its occurrence severity; a separate attempt creates a separate occurrence. An explicit-end failure detected before End and finalized at End therefore remains one occurrence. For out-of-cycle diagnostics, use the normalized input and rule condition as the occurrence key. Preserve severity changes in audit/match evidence rather than discarding the earlier classification.

When Ignore wins detection priority, record the selection and suppressed matches without creating a diagnostic incident or penalty from those suppressed rules. Never recreate a losing Error through a severity fallback. Interaction with a separately configured structural Failure remains a D04 follow-up; simulation must surface that unresolved policy combination rather than silently favoring Error or rewriting the terminal result. Likewise, terminal Success with its own winning diagnostic Warning/Error retains separate facts, with its health effect still awaiting the remaining D04 policy.

## Incident creation and workflow

Problem identity includes team, application/Profile, scope, recovery namespace, order identity when applicable, and stable condition key. Default structural condition keys are stable across Profile versions (`cycle-outcome`, `order-outcome`); explicit diagnostic rules own their condition keys.

Approved repeated-failure policy (D05): the first failure opens an Incident. Each later failed attempt of that same unresolved scoped problem appends an Occurrence to it, preserving all attempts/evidence. Do not reset Investigating just because another occurrence arrives. A later eligible successful retry records a distinct successful recovery event and resolves the Incident; it never deletes or overwrites a failed occurrence. Different problem identities stay separate. A new failure after resolution is still proposed as a new episode; late/out-of-order cases remain D10.

Current severity is derived from winning classifications on outstanding occurrences; history preserves original classification evidence. Incident identity is not simply an application name or raw identifier shared by unrelated conditions.

Allowed workflow states are exactly Active, Investigating, Resolved. Proposed initial manual transitions allow Active ↔ Investigating and either → Resolved. Manual reopening is deferred pending a policy for recovered occurrences and competing new episodes (D06); recurrence opens a new incident. The initial creation itself writes a history entry from null to Active. Manual resolution requires actor/time/reason; authentication supplies actor identity in the shared website.

Commands use idempotency IDs and expected revisions. Automatic recovery and manual commands serialize on the same application/incident state. If auto-resolution wins first, a stale user command receives a conflict instead of accidentally reopening. A status history row, resolution record, and current-state projection commit atomically.

## Automatic recovery

On finalizing a successful run, find unresolved compatible problems. Do not resolve based on a substring, display name, timestamp alone, or any successful order with a coincidentally equal identifier.

| Successful event | Eligible prior incidents | Must remain unaffected |
| --- | --- | --- |
| Application cycle Success | Earlier application-level cycle-failure incidents in the same application/Profile and recovery stream, where policy is NextSuccessfulCycle | Order failures; different application/stream; conditions requiring explicit recovery/manual action |
| Order attempt Success | Earlier order-outcome incidents for the same application/Profile/namespace/identifier and eligible retry ordering | Different identifier/Profile; unrelated diagnostic condition; newer failure |

“Later” means a later cycle sequence, or a higher attempt number within the same cycle only if that retry policy is approved. Timestamps support ordering but do not override authoritative source/cycle order. A delayed success from an older cycle must not resolve a newer failure. If another failure already exists after the successful attempt, only older eligible occurrences/episodes can recover; do not hide the newer problem.

For an unresolved episode with occurrences straddling the success's ordering point, record recovered older occurrences in `occurrence_recovery` but keep the episode unresolved while newer occurrences remain. Severity/current-state projection considers outstanding occurrences, so a recovered Error need not mask a newer Warning indefinitely. Never resolve the entire episode merely because one occurrence is older. Late attachment/reevaluation policy remains D10.

Resolution writes the successful run link, resolution time, Automatic method, policy/version, and history transition. Original failed/undefined runs and original detection time remain unchanged. Success does not automatically resolve every diagnostic Error: each condition needs a compatible recovery policy. Generic recovery patterns beyond NextSuccessfulCycle/NextSuccessfulOrder/ManualOnly are deferred.

## Required example traces

### CAM order failure then retry

Cycle 40: Begin → Order Begin 93822 → connection evidence → Order Failure 93822 → Cycle Success. Persist one successful cycle, one failed order attempt, one Active Warning, and application health Warning. A successful parent cycle does not resolve the child order problem.

Cycle 41: Begin → Order Begin 93822 → connection evidence → Order Success 93822 → Cycle Success. Persist a second cycle and successful attempt for the same identity; resolve the earlier eligible Warning automatically and record the retry link. Current health becomes Stable if nothing else remains unresolved. Historical order attempt 40 remains Failure.

Under the proposed metric policy, after both complete there are four evaluated runs, three successful health contributions, one unsuccessful contribution: **75%**, not 100%. Recovery improves current health; it does not erase past failures.

### Application failure then recovery

Cycle 50: Begin → “Database unavailable” evidence → configured Cycle Failure. Persist Failure, Active Error, current health Error. The database text is evidence; it only has additional meaning if a rule configures it.

Cycle 51: Begin → Cycle Success. Persist Success; resolve eligible earlier cycle failure; current health Stable unless another problem remains. Two evaluated cycles produce **50%** health for that period.

### Incomplete order

Begin cycle → Begin order 77482 → connection evidence → no order terminal line. Initially the order remains Open. When the cycle ends it becomes Undefined, creates a configured Warning/Error incident in Active status, and contributes one unsuccessful evaluated run as confirmed in the additional specification. If the cycle never ends, D02 defines its deadline. Undefined never appears as an incident workflow status.
