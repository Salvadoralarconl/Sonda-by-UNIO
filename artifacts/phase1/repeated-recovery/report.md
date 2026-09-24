# SONDA Profile simulation

Interpretation complete: **True**. Incomplete reports contain partial facts only; do not treat them as validated results.
Engine: phase1.1. Server timezone: America/New_York. As of: 2026-09-23T16:00:00.0000000+00:00.
Profile hash: `0D530CF5FDA6E4C0F7EDF5C24A6EDA4FFA4E9B96A5152412C396C5627C866EC4`. Sample hash: `3C2153E8DC4C4BC816F95FAAB14BE869D0ABB046DFE2609D1D3C673B96AA730F`.
Request hash: `BCD31BD90867DC2C52E3A4FF53E69D427360959D6F9BB129D80897E73EC4E7EB`. Report schema: 1.

## Metrics (sample only)

System Health: **66.67%** (4/6 evaluated runs). Null means no evaluated runs.
Logs Today / completedOrderRunsProcessedToday: **3**.

| Server date | Completed Order Runs processed |
| --- | ---: |
| 2026-09-19 | 0 |
| 2026-09-20 | 0 |
| 2026-09-21 | 0 |
| 2026-09-22 | 0 |
| 2026-09-23 | 3 |
Sample buckets describe supplied evidence only; zeros do not prove production coverage.

## Application and Order Runs

| Run | Scope | Parent | Identifier | Result | Evidence lines |
| --- | --- | --- | --- | --- | --- |
| repeated-recovery:cam:cycle:0001 | Application |  |  | Success | 1, 2, 3, 4 |
| repeated-recovery:cam:cycle:0005 | Application |  |  | Success | 5, 6, 7, 8 |
| repeated-recovery:cam:cycle:0008 | Application |  |  | Success | 9, 10, 11, 12 |
| repeated-recovery:cam:order:0002 | Order | repeated-recovery:cam:cycle:0001 | 93822 | Failure | 2, 3 |
| repeated-recovery:cam:order:0006 | Order | repeated-recovery:cam:cycle:0005 | 93822 | Failure | 6, 7 |
| repeated-recovery:cam:order:0009 | Order | repeated-recovery:cam:cycle:0008 | 93822 | Success | 10, 11 |

## Incidents and exact problem keys

### repeated-recovery:cam:incident:0003 — Warning / Resolved

Problem key: problem:v1:&#91;"team-demo","app-cam","cam","Order","default","cam-order-v1","93822","order-outcome"&#93;
- Occurrence repeated-recovery:cam:occurrence:0004: run repeated-recovery:cam:order:0002, cycle 1, Failure / Warning; evidence 2, 3.
- Occurrence repeated-recovery:cam:occurrence:0007: run repeated-recovery:cam:order:0006, cycle 2, Failure / Warning; evidence 6, 7.
- Recovery: Automatic, successful run repeated-recovery:cam:order:0009, cycle 3; evidence 10, 11.

## Per-input explanation

### Line 1 — cam — Applied

Raw: CAM process started
Message: CAM process started; identifier: ; event: 2026-09-23T14:00:00.0000000+00:00; processed: 2026-09-23T14:00:00.0000000+00:00; timestamp quality: ProcessingTimeFallback.
Fields: 
- Rule cycle-begin: CycleBegin/Application, priority 0, , **Matched**, alternatives 0.
- CycleStarted:repeated-recovery:cam:cycle:0001

### Line 2 — cam — Applied

Raw: Finding order OrderID=93822
Message: Finding order OrderID=93822; identifier: 93822; event: 2026-09-23T14:00:01.0000000+00:00; processed: 2026-09-23T14:00:01.0000000+00:00; timestamp quality: ProcessingTimeFallback.
Fields: 
- Rule order-begin: OrderBegin/Order, priority 0, , **Matched**, alternatives 0.
- OrderStarted:repeated-recovery:cam:order:0002

### Line 3 — cam — Applied

Raw: Unable to send order OrderID=93822
Message: Unable to send order OrderID=93822; identifier: 93822; event: 2026-09-23T14:00:02.0000000+00:00; processed: 2026-09-23T14:00:02.0000000+00:00; timestamp quality: ProcessingTimeFallback.
Fields: 
- Rule order-failure: OrderFailure/Order, priority 0, , **Matched**, alternatives 0.
- Rule order-warning: Detection/Order, priority 10, Warning, **Winner**, alternatives 0.
- OrderFinalized:repeated-recovery:cam:order:0002:Failure
- **CreatedIncident** → repeated-recovery:cam:incident:0003; problem key: problem:v1:&#91;"team-demo","app-cam","cam","Order","default","cam-order-v1","93822","order-outcome"&#93;; run repeated-recovery:cam:order:0002.

### Line 4 — cam — Applied

Raw: CAM process completed
Message: CAM process completed; identifier: ; event: 2026-09-23T14:00:03.0000000+00:00; processed: 2026-09-23T14:00:03.0000000+00:00; timestamp quality: ProcessingTimeFallback.
Fields: 
- Rule cycle-success: CycleSuccess/Application, priority 0, , **Matched**, alternatives 0.
- CycleFinalized:repeated-recovery:cam:cycle:0001:Success

### Line 5 — cam — Applied

Raw: CAM process started
Message: CAM process started; identifier: ; event: 2026-09-23T14:00:04.0000000+00:00; processed: 2026-09-23T14:00:04.0000000+00:00; timestamp quality: ProcessingTimeFallback.
Fields: 
- Rule cycle-begin: CycleBegin/Application, priority 0, , **Matched**, alternatives 0.
- CycleStarted:repeated-recovery:cam:cycle:0005

### Line 6 — cam — Applied

Raw: Finding order OrderID=93822
Message: Finding order OrderID=93822; identifier: 93822; event: 2026-09-23T14:00:05.0000000+00:00; processed: 2026-09-23T14:00:05.0000000+00:00; timestamp quality: ProcessingTimeFallback.
Fields: 
- Rule order-begin: OrderBegin/Order, priority 0, , **Matched**, alternatives 0.
- OrderStarted:repeated-recovery:cam:order:0006

### Line 7 — cam — Applied

Raw: Unable to send order OrderID=93822
Message: Unable to send order OrderID=93822; identifier: 93822; event: 2026-09-23T14:00:06.0000000+00:00; processed: 2026-09-23T14:00:06.0000000+00:00; timestamp quality: ProcessingTimeFallback.
Fields: 
- Rule order-failure: OrderFailure/Order, priority 0, , **Matched**, alternatives 0.
- Rule order-warning: Detection/Order, priority 10, Warning, **Winner**, alternatives 0.
- OrderFinalized:repeated-recovery:cam:order:0006:Failure
- **AppendedOccurrence** → repeated-recovery:cam:incident:0003; problem key: problem:v1:&#91;"team-demo","app-cam","cam","Order","default","cam-order-v1","93822","order-outcome"&#93;; run repeated-recovery:cam:order:0006.

### Line 8 — cam — Applied

Raw: CAM process completed
Message: CAM process completed; identifier: ; event: 2026-09-23T14:00:07.0000000+00:00; processed: 2026-09-23T14:00:07.0000000+00:00; timestamp quality: ProcessingTimeFallback.
Fields: 
- Rule cycle-success: CycleSuccess/Application, priority 0, , **Matched**, alternatives 0.
- CycleFinalized:repeated-recovery:cam:cycle:0005:Success

### Line 9 — cam — Applied

Raw: CAM process started
Message: CAM process started; identifier: ; event: 2026-09-23T14:00:08.0000000+00:00; processed: 2026-09-23T14:00:08.0000000+00:00; timestamp quality: ProcessingTimeFallback.
Fields: 
- Rule cycle-begin: CycleBegin/Application, priority 0, , **Matched**, alternatives 0.
- CycleStarted:repeated-recovery:cam:cycle:0008

### Line 10 — cam — Applied

Raw: Finding order OrderID=93822
Message: Finding order OrderID=93822; identifier: 93822; event: 2026-09-23T14:00:09.0000000+00:00; processed: 2026-09-23T14:00:09.0000000+00:00; timestamp quality: ProcessingTimeFallback.
Fields: 
- Rule order-begin: OrderBegin/Order, priority 0, , **Matched**, alternatives 0.
- OrderStarted:repeated-recovery:cam:order:0009

### Line 11 — cam — Applied

Raw: Order sent OrderID=93822
Message: Order sent OrderID=93822; identifier: 93822; event: 2026-09-23T14:00:10.0000000+00:00; processed: 2026-09-23T14:00:10.0000000+00:00; timestamp quality: ProcessingTimeFallback.
Fields: 
- Rule order-success: OrderSuccess/Order, priority 0, , **Matched**, alternatives 0.
- OrderFinalized:repeated-recovery:cam:order:0009:Success
- **ResolvedBySuccessfulRun** → repeated-recovery:cam:incident:0003; problem key: problem:v1:&#91;"team-demo","app-cam","cam","Order","default","cam-order-v1","93822","order-outcome"&#93;; run repeated-recovery:cam:order:0009.

### Line 12 — cam — Applied

Raw: CAM process completed
Message: CAM process completed; identifier: ; event: 2026-09-23T14:00:11.0000000+00:00; processed: 2026-09-23T14:00:11.0000000+00:00; timestamp quality: ProcessingTimeFallback.
Fields: 
- Rule cycle-success: CycleSuccess/Application, priority 0, , **Matched**, alternatives 0.
- CycleFinalized:repeated-recovery:cam:cycle:0008:Success

## Diagnostics


## Rule coverage

- cam/cycle-begin: 3 matched input(s), 0 classification selection(s).
- cam/cycle-success: 3 matched input(s), 0 classification selection(s).
- cam/cycle-failure: 0 matched input(s), 0 classification selection(s).
- cam/order-begin: 3 matched input(s), 0 classification selection(s).
- cam/order-success: 1 matched input(s), 0 classification selection(s).
- cam/order-failure: 2 matched input(s), 0 classification selection(s).
- cam/order-warning: 2 matched input(s), 2 classification selection(s).

## Current state

- cam: Stable, interpretation complete: True.
