# Current health, System Health, and activity aggregation

Status: overall direction and completed-Order-Run volume definition approved; Phase 1 is implemented and stopped for review before Phase 2. Confirmed timezone: **the monitoring server's computer timezone**, shared by all team members. Remaining System Health edge cases are tracked separately in [decisions](decisions.md).

## Three different dashboard measures

| Measure | Input | Time scope |
| --- | --- | --- |
| Application current health | Unresolved eligible incidents/conditions | Current state, including unresolved problems from prior days |
| Today's System Health | Per-finalized-run health facts | Today's reporting window |
| Logs Today | Completed logical Order Runs, one completion-processing fact each | Today's first committed completion-processing window |

An incident resolved today may have opened yesterday. It affects current health until resolved, but its status change does not create a new evaluated run or a new activity record. Historical metrics do not turn healthy just because someone changes workflow status.

## Current application state

Proposed current-state projection:

```text
Error   if any unresolved Error incident exists
Warning else if any unresolved Warning incident exists
Stable  otherwise, when observation is current and the application has been evaluated
```

Both Active and Investigating count as unresolved. Resolved does not. A successful cycle only removes eligible application failures; a failed order keeps the application Warning until its own matching recovery or manual resolution. A remaining Error takes precedence over any number of Warnings.

This current-state severity aggregation occurs after classification. Competing detection rules are selected by configured priority, never Error > Warning > Ignore. A suppressed Error cannot influence current health merely because it would have been more severe than the winning Ignore.

Manual resolution is permitted by the specification. Proposal D06: it clears that problem from current health just like automatic resolution, but changes no detection result or historical health contribution. A new occurrence opens a new incident episode and affects current state again.

Observation validity is separate from the business enum. Store `hasEvaluatedData`, `monitoringAvailability`, `lastObservedAt`, and `processingLag`. For a never-observed, disabled, disconnected, or stale source, return the last known business state (or null before any evaluation) with an explicit availability qualifier. Do not invent a fourth incident workflow state or display an unqualified green Stable dot when SONDA has no current evidence. The accessible/detail presentation needs review while preserving the approved layout (D13).

The Active Incidents card counts all unresolved incidents for the selected team/application scope, including Investigating, not only rows literally named Active. It shows the highest unresolved severity. Home's Error Logs table may include Resolved history; it is not restricted to the card's count.

## Today's System Health

Authoritative formula:

```text
System Health = successful evaluated runs / total evaluated runs × 100
```

Both Application Run Cycles and Order Runs are evaluated entities. Count each once, independently. A parent cycle and its child orders both contribute because the master specification explicitly includes both. Do not count raw messages, individual detections, retries' log lines, or incident history entries as denominator units.

Proposed finalized-run contribution policy (D04/D07):

| Run state | Eligible units | Successful units | Reason |
| --- | ---: | ---: | --- |
| Open, no result | 0 | 0 | Not yet evaluated |
| Finalized Success, no Error/Warning detection on that run | 1 | 1 | Successful evaluated run |
| Finalized Failure | 1 | 0 | Unsuccessful regardless of Error vs Warning |
| Finalized Undefined Order Run | 1 | 0 | Confirmed unsuccessful by the additional specification |
| Finalized Undefined Application Run | 1 | 0 | Proposed extension to incomplete cycles; requires approval |
| Finalized Success with its own configured Error/Warning condition | 1 | 0 | Preserve terminal Success but count its detected problem as unsuccessful; requires approval |
| Ignore-only diagnostic on an otherwise successful run | 1 | 1 | Ignore does not penalize health |
| Unmatched raw record / quarantined uncorrelated input | 0 | 0 | No established evaluated run; surface coverage issue separately |

A child order's Warning does not additionally mark its successful parent cycle unsuccessful unless there is a distinct configured application-level condition. Otherwise one business error would implicitly penalize two units beyond the specified parent/child counting rule. A run with several incidents still has only one health contribution.

Resolution never changes these historical facts. A successful retry adds its own contribution; the original failed attempt remains unsuccessful. Warning and Error have equal weight: both contribute one unsuccessful unit, with no weighting multiplier.

### Required arithmetic

Specification example: 980 successful orders + 20 failed orders + 5 successful cycles + 1 failed cycle.

```text
Successful = 980 + 5 = 985
Total      = 980 + 20 + 5 + 1 = 1006
Health     = 985 / 1006 × 100 = 97.9125248509…%
```

Recommended display is **97.91%**, retaining counts at full precision. The original 1,000 successful / 1,025 total example produces **97.56%**. Display precision is configurable without changing the formula or card geometry.

Zero denominator returns null with “No evaluated runs,” not 0% or 100%. The API includes numerator, denominator, percentage, coverage, `asOf`, and reporting window so the UI does not recalculate a different result.

## Reporting timezone and day boundaries

The server reads the designated monitoring host's OS timezone and records its resolved ID/rules as a timezone epoch. Every viewer receives the same reporting window; browser locale may format numbers/text but must not change the metric boundary. A future remote collector's timezone is a source-time parsing concern, not automatically the team's reporting timezone.

For local date D, calculate the UTC instants corresponding to D at local midnight and D+1 at local midnight using timezone rules. Query `[start, end)`; never assume a local day equals 24 hours. Test daylight-saving changes and repeated/missing wall-clock times. Prefer source timestamps containing explicit offsets; when a source provides ambiguous local time, preserve the ambiguity and follow an approved parser policy instead of guessing.

Proposed System Health attribution (remaining D07 detail): health belongs to the terminal event's effective time; Undefined uses its effective completion boundary or configured fallback deadline. An overnight run finishing today contributes today. Store captured, event, and completion-processed timestamps separately. For timestamp-free live logs, use capture time with a quality flag. Backfill without trustworthy event times needs an explicit policy. Logs Today has its own approved processed-activity meaning below; do not force both metrics to share one timestamp field.

If the monitoring server's timezone changes, detect and audit it. Adopt the new computer timezone through a versioned projection rebuild from stored UTC facts; keep serving one complete prior snapshot marked with its old zone until the new projection is ready, then switch atomically. Do not mix bucket counts from two zones. Historical displayed date assignments may change under the new timezone; preserve old epoch metadata for audit. This change-handling policy is proposed, not a request to change the server timezone.

Midnight resets the query window, not database history. Unresolved incidents persist across midnight. The period service accepts explicit windows so longer-range reporting can be added later without changing run interpretation.

## Logs Today and five-day history

Approved definition (D07): **the number of completed logical Order Runs processed today**. Name the domain/API metric `completedOrderRunsProcessedToday`, its time-series measure `completedOrderRunsProcessedCount`, and its stored fact `completed_order_run_processing_fact`. “Logs Today” remains the approved Home label.

Count each finalized Order Run attempt exactly once, whether its result is Success, Failure, or Undefined. A retry is a new Order Run, so its completion counts once too. Exclude raw physical lines, incidents, occurrence counts as such, and Application Run Cycles. Do not use a cycle-as-record fallback for applications without orders. A cycle-only application can show System Health activity with zero Logs Today.

Operational definition of “processed today”: bucket by `completed_processed_at`, the server timestamp of the first successfully committed completion-processing operation. Preserve the separate source `completed_event_at` for history and duration. This replaces the earlier proposal to bucket volume by source completion/deadline time. A prior-day order first completed by SONDA during today's backlog processing counts today; an already-committed order replayed today does not count again. Surface backlog context rather than relabeling ingestion throughput as source-day activity.

Open orders do not count. At a deterministic cycle close, each unfinished order finalized Undefined counts once. A rollback contributes nothing; a retried committed operation retains its original completion-processing fact/time. Rebuilding projections uses those original facts and does not move old counts into the rebuild date. Incident resolution contributes no extra volume beyond the successful retry Order Run itself. Future additional activity measures must have different names and must not silently widen Home's metric.

The mini chart returns exactly five daily buckets: D-4, D-3, D-2, D-1, D in the server timezone. It uses the same grouped-volume definition as the card. Zero-fill dates with verified zero activity; include coverage flags for dates with no monitoring/gaps so unknown history is not presented as proven inactivity. Keep the approved chart footprint and visual language; the master specification's five-day meaning is authoritative over decorative bar count in the illustration.

## Aggregation and consistency

Write a unique run metric fact when a run finalizes. Write one `completed_order_run_processing_fact` per finalized Order Run, unique by session and Order Run, recording its first committed completion-processing timestamp. In the same interpretation transaction, update the relevant daily projection from newly inserted facts only. Input replay cannot increment a projection twice. Store policy/version and source watermark for reproducibility; a policy version is not permission to insert another count for the same order.

Application current state is updated in the same transaction as incident status changes. Dashboard reads use a single short committed snapshot. A rebuild computes fresh projections from immutable facts, verifies totals, then swaps projection revision atomically. Derived tables can be deleted/rebuilt in controlled maintenance; source run facts and incident history cannot.

For team health, sum numerators and denominators before division. Never average application percentages: a one-order application and a million-order application do not carry equal volume. Disabling an application does not erase its earlier daily activity by default; an explicit query filter can exclude it with visible scope. Validate this policy in D07.
