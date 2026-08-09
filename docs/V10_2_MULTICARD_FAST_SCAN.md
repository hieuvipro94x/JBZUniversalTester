# V10.2 — Multi-card + same-wire network + fast scan

Reference trace: `20260807_130520_production_Htdrv3-JBZ27000_RT`
Reference THT captured in trace: `C:\ITEM\WH322244.tht`

## 1. Root cause of missing continuity pairs

The original Htdrv configuration captured in this trace contains:

- `[카드 수]4` = 4 I/O cards
- `[USB 지연]1` = USB delay 1 ms
- `[IO 확인1]1`
- `[IO 확인n]1`

Htdrv sends:

`8C 00 04 00`

V10.1 incorrectly hard-coded:

`8C 00 02 00`

The trace and model prove the board uses **64 I/O per configured card**:

- 2 cards -> 128 I/O -> `8C 00 02 00`
- 4 cards -> 256 I/O -> `8C 00 04 00`

`WH322244.tht` contains mapped I/O above 128 (for example BM06 6↔176,
BF09 9↔132, LT21 135↔187 and many more), up to at least I/O 224. Therefore
an application that only decodes 128 channels can never validate those pairs.

V10.2 automatically raises the scan range from the selected THT:

`required cards = ceil(MaxIo / 64)`

and keeps the configured ProductionSettings.CardCount if it is larger.

## 2. Multi-bank RX protocol

Verified words:

- `80 nn` = bank 0 I/O 1..128 NORMAL
- `A0 nn` = bank 0 I/O 1..128 ACTIVE
- `81 nn` = bank 1 I/O 129..256 NORMAL
- `A1 nn` = bank 1 I/O 129..256 ACTIVE
- `C0 00` = end frame

The decoder accepts repeated/interleaved A0/A1 marker words. This matters in
TestPin mode, where the original program repeatedly inserts the detected probe
I/O marker while the normal bank scan continues.

Example from the real trace:

- `A0 47` -> UI/model I/O 72
- `A0 49` -> UI/model I/O 74

These marker words appear repeatedly until the probe is released.

## 3. Same wire name = same continuity network

THT pin rows are normalized using Unicode FormKC + trimmed/collapsed spaces.
Pins with the same normalized `WireName` always belong to the same continuity
network. Explicit THT `선연결` links may additionally join two different wire
names into the same network.

Examples found in WH322244.tht:

- BG01: I/O 1 ↔ 86
- BG02: I/O 2 ↔ 87
- BF03: I/O 3 ↔ 122
- BM06: I/O 6 ↔ 176
- BF09: I/O 9 ↔ 132
- NUT01: I/O 34 ↔ 35
- LT21: I/O 135 ↔ 187
- ER01: I/O 149 ↔ 193
- OK: I/O 223 ↔ 224

Within a network the first I/O in THT order remains the stimulus/source and the
remaining endpoint(s) are receiver(s), matching the earlier passing production
trace.

## 4. Scan speed changes

V10.1 performed a new `Task.Run` for every FT_GetQueueStatus/FT_Read operation
and waited 3 ms whenever the queue was empty. V10.2 uses one dedicated long-
running D2XX reader and honors the captured USB delay (1 ms default).

Connection setup also applies:

- baud 115200
- 8N1
- no flow control
- FTDI latency 2 ms
- USB transfer buffers 65536 bytes

The TestEngine still processes every complete frame, but it no longer rebuilds
the entire WPF fault table when the active/pass/fault state is unchanged.

## 5. TestPin changes

The probe screen no longer displays the smallest member of the whole ActiveIo
set. It tracks **new ACTIVE transitions** relative to the previous complete
frame. Large startup bursts are treated as board baseline changes and ignored;
a normal probe touch produces one or a few new marker I/Os and is displayed.

No XAML layout is changed in this patch.
