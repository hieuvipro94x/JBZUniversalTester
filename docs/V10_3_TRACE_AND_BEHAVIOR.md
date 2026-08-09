# V10.3 — behavior rebuilt from production + TestPin traces

## 1. Card capacity is 64 I/O per physical card

The captured Htdrv configuration for `WH322244.tht` contains:

- `[카드 수]4`
- `[USB 지연]1`
- `[시작카드번호]1`
- `[카드 수(확장박스)]0`

The model reaches I/O **224**, therefore the required main card count is:

`ceil(224 / 64) = 4 cards`.

The original software sends `8C 00 04 00`. The earlier universal tester used
`8C 00 02 00`, so it could not test any pair whose endpoint was above I/O 128.

V10.3 never silently expands the configured card count. Model load may remain
visible, but production test and TestPin are blocked until the operator changes
**Số card IO** to at least the required value.

## 2. RX is a SOURCE -> TARGET graph, not a flat active bitmap

Production raw data proves that words are contextual:

- `80 nn` = source in bank 0, I/O 1..128
- `81 nn` = source in bank 1, I/O 129..256
- `A0 nn` = target in bank 0 connected to the current source
- `A1 nn` = target in bank 1 connected to the current source
- `C0 00` = end of scan round

Example from the real passing production trace:

```
80 00 A0 11
```

means `IO1 -> IO18`.

```
80 02 A0 10 A0 15 A0 17
```

means `IO3 -> IO17, IO22, IO24`.

This distinction is essential. A target appearing under the wrong source is a
wrong-wire/short condition; it must not accidentally PASS just because the same
A0 address exists somewhere in the frame.

## 3. C0 00, not source count, defines a complete production frame

The original app does not emit every possible source in production. The actual
PASS frame reconstructed from the trace has only **115 source I/Os**, while the
command is a 2-card/128-I/O scan.

Therefore V10.3:

1. discards only the first C0-terminated fragment after StartScan to establish a
   frame boundary;
2. accepts following C0-terminated rounds when at least one source was present;
3. does **not** require 128 or 256 source words.

The old completeness test was a major reason the clone appeared slow or did not
react: valid real frames were being ignored.

## 4. Same THT wire name is one continuity network

`WH322244.tht` directly contains many pairs with the same wire name, including:

- `BG01`: IO1 <-> IO86
- `BG02`: IO2 <-> IO87
- `BF03`: IO3 <-> IO122
- `BM06`: IO6 <-> IO176
- `BF09`: IO9 <-> IO132
- `LT21`: IO135 <-> IO187
- `ER01`: IO149 <-> IO193
- `OK`: IO223 <-> IO224

V10.3 normalizes THT wire names with Unicode FormKC + whitespace normalization
and groups equal names case-insensitively. Explicit `선연결` relations are then
used only to join wire names that the THT itself declares electrically common.

For each resulting network, the first I/O in THT order is the expected scan
source and the remaining endpoint(s) are expected targets.

## 5. Dynamic TestView

For a normal two-pin network:

- not connected: both mapped pin rows stay visible as `Chưa thông mạch`;
- correct SOURCE -> TARGET appears: after configured confirmation, both rows
  disappear;
- connection is released: rows appear again on the next valid frame;
- target appears under a source from a different expected electrical component:
  sustain for `ShortConfirmMs`, mark affected rows red, stop production, loop
  `TESTPOINT.wav`, and show the error popup.

For a splice/multi-branch network, receiver rows can disappear independently;
the source row disappears only after the entire network is confirmed.

## 6. TestPin trace

The TestPin trace sends:

`8C 00 04 00`

and contains 256 sources per diagnostic round. A touched probe target is inserted
as A0/A1 after a large number of sources. This makes target hit-count a reliable
probe discriminator.

Confirmed examples:

- `A0 47` -> `IO(72)`
- `A0 49` -> `IO(74)`

In complete probe frames both targets reached 256 hits, while ordinary harness
relations appear only once/few times. V10.3 uses this repeated-hit pattern and
falls back to a new-target transition only for simpler firmware variants.

After detecting an I/O, the Probe window looks up all matching THT Pin rows and
shows:

- IO number
- wire name
- connector
- pin number
- section
- wire color code and a color preview

## 7. Scan performance changes

The original cfg uses USB delay 1 ms. V10.3 aligns the hot path to that behavior:

- one dedicated long-running FTDI read worker;
- 65536-byte USB buffers;
- FTDI latency from production `UsbDelay` (1 ms in the trace);
- reader thread set to `AboveNormal` when permitted;
- no Task.Run for every queue/read operation;
- stateful parser retains split words without O(n^2) shifting;
- TestEngine's expected electrical components are computed once per model;
- production TestEngine is disabled during TestPin mode;
- WPF updates use asynchronous dispatcher scheduling and only happen when the
  connection graph, PASS set or fault set actually changes.

## 8. Address validation

Each A0/A1 or 80/81 bank uses address byte `00..7F`. Values `80..FF` are not
accepted as a valid address inside that bank. This prevents malformed/noise bytes
from being mapped to duplicate I/O ranges.

## 9. Remaining hardware boundary

The traces prove main card counts up to 4 / 256 I/O. `ExpansionCardCount` is a
separate Htdrv setting and was `0` in this capture. V10.3 does not invent a
protocol for expansion boxes beyond 256 I/O. A future trace with a real expansion
box is required before implementing that range safely.
