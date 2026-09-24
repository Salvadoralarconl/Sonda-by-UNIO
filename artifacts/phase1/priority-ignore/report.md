# SONDA Profile simulation

Interpretation complete: **True**. Incomplete reports contain partial facts only; do not treat them as validated results.
Engine: phase1.1. Server timezone: America/New_York. As of: 2026-09-23T16:00:00.0000000+00:00.
Profile hash: `9512EEE1FE59EA580C176BE3C301CCF100A3DC00C9127B3FDFE72C046F5C030F`. Sample hash: `C4BFE8FE6BB79631388C3AF1878835E3B5B5A624B95308667F972AB791B2CF3D`.
Request hash: `4AB94612C0D0376B15224BC88C624EB8559F30E8D4EACEE1E01A70C46681B2F9`. Report schema: 1.

## Metrics (sample only)

System Health: **100%** (1/1 evaluated runs). Null means no evaluated runs.
Logs Today / completedOrderRunsProcessedToday: **0**.

| Server date | Completed Order Runs processed |
| --- | ---: |
| 2026-09-19 | 0 |
| 2026-09-20 | 0 |
| 2026-09-21 | 0 |
| 2026-09-22 | 0 |
| 2026-09-23 | 0 |
Sample buckets describe supplied evidence only; zeros do not prove production coverage.

## Application and Order Runs

| Run | Scope | Parent | Identifier | Result | Evidence lines |
| --- | --- | --- | --- | --- | --- |
| priority-ignore:cam:cycle:0001 | Application |  |  | Success | 1, 2, 3 |

## Incidents and exact problem keys

## Per-input explanation

### Line 1 — cam — Applied

Raw: CAM process started
Message: CAM process started; identifier: ; event: 2026-09-23T14:00:00.0000000+00:00; processed: 2026-09-23T14:00:00.0000000+00:00; timestamp quality: ProcessingTimeFallback.
Fields: 
- Rule cycle-begin: CycleBegin/Application, priority 0, , **Matched**, alternatives 0.
- CycleStarted:priority-ignore:cam:cycle:0001

### Line 2 — cam — Applied

Raw: optional service connection failed
Message: optional service connection failed; identifier: ; event: 2026-09-23T14:00:01.0000000+00:00; processed: 2026-09-23T14:00:01.0000000+00:00; timestamp quality: ProcessingTimeFallback.
Fields: 
- Rule broad-error: Detection/Application, priority 5, Error, **Suppressed**, alternatives 0.
- Rule specific-ignore: Detection/Application, priority 20, Ignore, **Winner**, alternatives 0.
- Ignored:specific-ignore

### Line 3 — cam — Applied

Raw: CAM process completed
Message: CAM process completed; identifier: ; event: 2026-09-23T14:00:02.0000000+00:00; processed: 2026-09-23T14:00:02.0000000+00:00; timestamp quality: ProcessingTimeFallback.
Fields: 
- Rule cycle-success: CycleSuccess/Application, priority 0, , **Matched**, alternatives 0.
- CycleFinalized:priority-ignore:cam:cycle:0001:Success

## Diagnostics

- Warning **RuleOverlap**, Profile cam, line : Rules broad-error and specific-ignore can overlap; the higher configured priority wins.
- Warning **RuleOverlap**, Profile cam, line 2: Matching rules &#91;specific-ignore, broad-error&#93; compete on Application.

## Rule coverage

- cam/cycle-begin: 1 matched input(s), 0 classification selection(s).
- cam/cycle-success: 1 matched input(s), 0 classification selection(s).
- cam/cycle-failure: 0 matched input(s), 0 classification selection(s).
- cam/order-begin: 0 matched input(s), 0 classification selection(s).
- cam/order-success: 0 matched input(s), 0 classification selection(s).
- cam/order-failure: 0 matched input(s), 0 classification selection(s).
- cam/order-warning: 0 matched input(s), 0 classification selection(s).
- cam/broad-error: 1 matched input(s), 0 classification selection(s).
- cam/specific-ignore: 1 matched input(s), 1 classification selection(s).

## Current state

- cam: Stable, interpretation complete: True.
