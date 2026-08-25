# 0x81 TstPnt research note

Source target: `Htdrv3-JBZ27000_RT.exe`.

Known from static command table:

- `0x81` = `TstPnt` / Test Point / Probe
- `0x8C` = `TstWhl` / Whole Test
- `0x8D` = `TstStp` / Stop Test
- `0x80` = `ClrSys`
- `0x8E` = `RlySet`

Implementation status:

- `0x81` is not implemented in production D2XX transport yet.
- The current WPF app keeps Probe as an inline observer over the verified `0x8C` production stream.
- Probe observation must not reset production state, clear SHORT/WRONG/OPEN, suppress frames, trigger PASS/FAIL, or operate relays.

Required evidence before implementing `0x81`:

- exact 4-byte TX command format;
- response frame format;
- ON/OFF encoding;
- zero-based or one-based I/O address mapping;
- whether `0x81` can interleave with `0x8C` or requires `0x8D` first;
- timing between mode switch, read, and restore;
- whether original software restores `0x8C` with a first-frame verification.

Until those points are confirmed by disassembly or a D2XX trace from the original software, do not invent packet bytes.
