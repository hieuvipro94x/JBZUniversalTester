# JBZUniversalTester V10 — logic reconstructed from production trace

Reference trace: `20260807_105731_production_Htdrv3-JBZ8_RT`
Reference model loaded by Htdrv: `C:\ITEM\K32000-22401R-260224.tht`
Reference product result: `K32000-22402 / WIRING ASSY-MAIN`

## 1. Verified board protocol

The board returns one 128-channel state frame:

- `80 nn` = protocol address `nn` NORMAL
- `A0 nn` = protocol address `nn` ACTIVE
- `C0 00` = end of frame
- `nn` is 0-based; model/UI I/O is `nn + 1`

A complete production frame contains all 128 channel states.

## 2. Critical THT continuity rule found from the trace

The old implementation assumed every I/O listed in a THT net must itself be A0 ACTIVE.
The production trace proves that is not how Htdrv evaluates a harness.

For each connected component/network in the THT:

1. The **first I/O in THT order** is the source/stimulus.
2. The remaining I/Os are receiver/target addresses.
3. Continuity is satisfied when all receiver/target addresses are A0 ACTIVE.
4. The source I/O does not need to appear in the A0 set.

Reference model reconstructed networks:

| Net | Source | Receiver(s) expected A0 |
|---|---:|---|
| MC1 | 1 | 18 |
| MC2/P1 | 2 | 25 |
| MC3/MC3A/GND | 3 | 17, 22, 24 |
| MC4 | 4 | 23 |
| RT01 | 9 | 10 |
| PR1 | 11 | 26 |
| SS1 | 11 | 14 |
| T1 | 12 | 13 |
| PR3 | 15 | 19 |
| PR2 | 16 | 27 |
| SS2 | 20 | 21 |

Union of expected receivers:

`10, 13, 14, 17, 18, 19, 21, 22, 23, 24, 25, 26, 27`

At `04:00:28` through `04:00:29.861Z`, the real board repeatedly returned:

`10, 13, 14, 17, 18, 21, 22, 23, 24, 25, 26, 27`

Only I/O 19 was missing, matching one remaining OPEN network (PR3 source 15 -> receiver 19).

At `04:00:30.033Z`, the board returned exactly:

`10, 13, 14, 17, 18, 19, 21, 22, 23, 24, 25, 26, 27`

The original program then sent `STOP_SCAN` at `04:00:30.267Z`.

## 3. Dynamic TestView behavior

V10 does not accumulate PASS forever.
Each complete frame updates the live state:

- receiver active -> corresponding connection disappears from the OPEN list
- receiver released -> connection reappears on the next frame
- simple pair: source + target rows disappear together when target is active
- splice: target rows disappear independently; source remains while any branch is still open

The displayed OPEN count is the number of missing receiver endpoints, not the number of table rows.

## 4. Duplicate I/O rows in THT

The production THT uses physical I/O 11 for both PR1 and SS1.
The previous parser stored pins in `SortedDictionary<int, RawPin>` and therefore deleted one of these rows.
V10 preserves every THT pin row and allows the same physical I/O to be the source of multiple networks.

## 5. `선연결` must be undirected

The production model contains reciprocal/linked wire definitions such as MC2 <-> P1.
Following `LinkedWire` in one direction creates a cycle and can split one real network into two names.
V10 builds connected components from `선연결` as an undirected graph and chooses a deterministic canonical wire name.

## 6. AO/a1..a7 are not ordinary continuity networks

The old code synthesized an `A0-COMMON` network from AO/a1..a7 rows.
The real passing production frame does not require those addresses; trace also shows transient I/O 29 without causing a product fault.
V10 records these as special/ignored I/O metadata and excludes them from ordinary OPEN/PASS requirements.

## 7. Resistance block in the real THT

The THT is an OLE Compound Document. Immediately after `model_text`, the reference model contains:

`FF FF FF 00` + MFC CString

with text:

```
1
8000
11000

2
8000
11000
```

Therefore the real model requires:

- R1: 8000..11000 ohm
- R2: 8000..11000 ohm

V10 reads this embedded block first. Text-table resistance parsing remains only as a compatibility fallback.

The production result file confirms measured values:

- R1 = 9.228 kOhm
- R2 = 9.481 kOhm

## 8. Verified post-continuity command sequence

After continuity PASS:

- `04:00:30.267` `8D 00 00 00` STOP_SCAN
- `04:00:30.548` `80 00 00 00` RESET_CLEAR

R1 route:

- `90 00 00 01`
- ~350 ms
- `91 00 00 01`
- Keysight `:MEASURE:RES?`

R2 route:

- `90 00 00 01` (important: first route byte remains 01)
- ~350 ms
- `91 00 00 02`
- Keysight `:MEASURE:RES?`

After R2 the original program performs three recovery/prepare cycles:

- INIT_1 `91 00 00 00`
- INIT_2 `90 00 00 30`
- repeated 3 times

V10 follows this sequence and does **not** release/reinitialize between R1 and R2.

## 9. Verified PASS relay timing

Trace:

- `04:00:36.073` Relay 1 ON
- `04:00:36.333` all relays OFF (260 ms pulse)
- `04:00:36.768` Relay 2 ON (435 ms after R1 OFF)
- `04:00:37.013` all relays OFF (245 ms pulse)
- `04:00:37.213` START_SCAN (200 ms later)

V10 defaults:

- relay pulse = 250 ms
- relay interlock = 430 ms
- next scan restart = 200 ms

## 10. Re-arm after PASS

A key behavior visible in the trace is that after a completed test, the original program restarts scanning while the previous harness may still be connected.
A full valid A0 set can therefore appear immediately and must not count as a second product.

V10 enters `PASS - CHỜ NHẢ SẢN PHẨM` after relay completion. It ignores full-continuity frames until at least one expected receiver is released. Only then does it re-arm for the next harness.

This matches the trace: an already-complete set appears immediately after an earlier scan restart, then the product is released; the next legitimate full set is at `04:00:30.033Z`.

## 11. Production CFG values used as behavioral reference

Captured `Htdrv3-JBZ8_RT.cfg` includes:

- `[카드 수]2`
- `[IO 확인1]1`
- `[IO 확인n]1`
- `[시작카드번호]1`
- `[테스트포인터사용]1`
- `[합선확인시간(msec)]1000`
- `[저항측정 지연(msec)]0`

V10 uses `IoConfirm1/IoConfirmN` for consecutive continuity confirmation and `ShortConfirmMs` for sustained unexpected A0 detection. The trace's transient unexpected I/O 29 lasted below the 1000 ms threshold, so it is not falsely treated as a short.

## Files changed in V10

- `Models/ProductModel.cs`
- `Services/ThtModelParser.cs`
- `Services/TestEngine.cs`
- `Services/D2xxBoardTransport.cs`
- `Services/AppSettings.cs`
- `ViewModels/MainViewModel.cs`
- `ViewModels/TestViewModel.cs`
- `Views/PinProbeWindow.xaml.cs`
- `appsettings.json`
- `appsettings.example.json`

No `.xaml` interface file is intentionally changed.
