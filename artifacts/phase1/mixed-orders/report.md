# SONDA Profile simulation

Interpretation complete: **True**. Incomplete reports contain partial facts only; do not treat them as validated results.
Engine: phase1.1. Server timezone: America/New_York. As of: 2026-09-23T16:00:00.0000000+00:00.
Profile hash: `2F5B9736D8F55D6B36D2BE789CF27ABB64B665B8D82DCB74082145AC760E6B3D`. Sample hash: `35A1A3435CE7B697B46287D86311DC042F212FA784F174B963414ECEADDDD567`.
Request hash: `93F01A47D06ED8F91AE5D789F91B43B599B4EE83133B352204F3787F750E18E6`. Report schema: 1.

## Metrics (sample only)

System Health: **75%** (3/4 evaluated runs). Null means no evaluated runs.
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
| mixed-orders:cam:cycle:0001 | Application |  |  | Success | 1, 2, 3, 4, 5, 6, 7, 8, 9 |
| mixed-orders:cam:order:0002 | Order | mixed-orders:cam:cycle:0001 | 001 | Success | 2, 4, 5 |
| mixed-orders:cam:order:0003 | Order | mixed-orders:cam:cycle:0001 | 002 | Failure | 3, 6 |
| mixed-orders:cam:order:0006 | Order | mixed-orders:cam:cycle:0001 | 003 | Success | 7, 8 |

## Incidents and exact problem keys

### mixed-orders:cam:incident:0004 — Warning / Active

Problem key: problem:v1:&#91;"team-demo","app-cam","cam","Order","default","cam-order-v1","002","order-outcome"&#93;
- Occurrence mixed-orders:cam:occurrence:0005: run mixed-orders:cam:order:0003, cycle 1, Failure / Warning; evidence 3, 6.

## Per-input explanation

### Line 1 — cam — Applied

Raw: 10:00:00 CAM process started
Message: CAM process started; identifier: ; event: 2026-09-23T14:00:00.0000000+00:00; processed: 2026-09-23T14:00:00.0000000+00:00; timestamp quality: Parsed.
Fields: timestamp=10:00:00, message=CAM process started
- Rule cycle-begin: CycleBegin/Application, priority 0, , **Matched**, alternatives 0.
- CycleStarted:mixed-orders:cam:cycle:0001

### Line 2 — cam — Applied

Raw: 10:00:01 Finding order OrderID=001
Message: Finding order OrderID=001; identifier: 001; event: 2026-09-23T14:00:01.0000000+00:00; processed: 2026-09-23T14:00:01.0000000+00:00; timestamp quality: Parsed.
Fields: timestamp=10:00:01, message=Finding order OrderID=001
- Rule order-begin: OrderBegin/Order, priority 0, , **Matched**, alternatives 0.
- OrderStarted:mixed-orders:cam:order:0002

### Line 3 — cam — Applied

Raw: 10:00:02 Finding order OrderID=002
Message: Finding order OrderID=002; identifier: 002; event: 2026-09-23T14:00:02.0000000+00:00; processed: 2026-09-23T14:00:02.0000000+00:00; timestamp quality: Parsed.
Fields: timestamp=10:00:02, message=Finding order OrderID=002
- Rule order-begin: OrderBegin/Order, priority 0, , **Matched**, alternatives 0.
- OrderStarted:mixed-orders:cam:order:0003

### Line 4 — cam — EvidenceOnly

Raw: 10:00:03 Connecting to payer OrderID=001
Message: Connecting to payer OrderID=001; identifier: 001; event: 2026-09-23T14:00:03.0000000+00:00; processed: 2026-09-23T14:00:03.0000000+00:00; timestamp quality: Parsed.
Fields: timestamp=10:00:03, message=Connecting to payer OrderID=001

### Line 5 — cam — Applied

Raw: 10:00:04 Order sent OrderID=001
Message: Order sent OrderID=001; identifier: 001; event: 2026-09-23T14:00:04.0000000+00:00; processed: 2026-09-23T14:00:04.0000000+00:00; timestamp quality: Parsed.
Fields: timestamp=10:00:04, message=Order sent OrderID=001
- Rule order-success: OrderSuccess/Order, priority 0, , **Matched**, alternatives 0.
- OrderFinalized:mixed-orders:cam:order:0002:Success

### Line 6 — cam — Applied

Raw: 10:00:05 Unable to send order OrderID=002
Message: Unable to send order OrderID=002; identifier: 002; event: 2026-09-23T14:00:05.0000000+00:00; processed: 2026-09-23T14:00:05.0000000+00:00; timestamp quality: Parsed.
Fields: timestamp=10:00:05, message=Unable to send order OrderID=002
- Rule order-failure: OrderFailure/Order, priority 0, , **Matched**, alternatives 0.
- Rule order-warning: Detection/Order, priority 10, Warning, **Winner**, alternatives 0.
- OrderFinalized:mixed-orders:cam:order:0003:Failure
- **CreatedIncident** → mixed-orders:cam:incident:0004; problem key: problem:v1:&#91;"team-demo","app-cam","cam","Order","default","cam-order-v1","002","order-outcome"&#93;; run mixed-orders:cam:order:0003.

### Line 7 — cam — Applied

Raw: 10:00:06 Finding order OrderID=003
Message: Finding order OrderID=003; identifier: 003; event: 2026-09-23T14:00:06.0000000+00:00; processed: 2026-09-23T14:00:06.0000000+00:00; timestamp quality: Parsed.
Fields: timestamp=10:00:06, message=Finding order OrderID=003
- Rule order-begin: OrderBegin/Order, priority 0, , **Matched**, alternatives 0.
- OrderStarted:mixed-orders:cam:order:0006

### Line 8 — cam — Applied

Raw: 10:00:07 Order sent OrderID=003
Message: Order sent OrderID=003; identifier: 003; event: 2026-09-23T14:00:07.0000000+00:00; processed: 2026-09-23T14:00:07.0000000+00:00; timestamp quality: Parsed.
Fields: timestamp=10:00:07, message=Order sent OrderID=003
- Rule order-success: OrderSuccess/Order, priority 0, , **Matched**, alternatives 0.
- OrderFinalized:mixed-orders:cam:order:0006:Success

### Line 9 — cam — Applied

Raw: 10:00:08 CAM process completed
Message: CAM process completed; identifier: ; event: 2026-09-23T14:00:08.0000000+00:00; processed: 2026-09-23T14:00:08.0000000+00:00; timestamp quality: Parsed.
Fields: timestamp=10:00:08, message=CAM process completed
- Rule cycle-success: CycleSuccess/Application, priority 0, , **Matched**, alternatives 0.
- CycleFinalized:mixed-orders:cam:cycle:0001:Success

## Diagnostics


## Rule coverage

- cam/cycle-begin: 1 matched input(s), 0 classification selection(s).
- cam/cycle-success: 1 matched input(s), 0 classification selection(s).
- cam/cycle-failure: 0 matched input(s), 0 classification selection(s).
- cam/order-begin: 3 matched input(s), 0 classification selection(s).
- cam/order-success: 2 matched input(s), 0 classification selection(s).
- cam/order-failure: 1 matched input(s), 0 classification selection(s).
- cam/order-warning: 1 matched input(s), 1 classification selection(s).

## Current state

- cam: Warning, interpretation complete: True.
