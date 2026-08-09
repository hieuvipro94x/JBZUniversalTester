# V11.7 - TestPin contact isolation

## Required behavior

### Probe / TestPin
- Opening PinProbeWindow immediately switches UI runtime to Probe before Loaded/ShowDialog work continues.
- Production TestEngine is disabled synchronously.
- A0/A1 contact is interpreted only as the physical I/O touched by the GND probe.
- An I/O not present in the THT is still shown as `IO(n)` and is not a fault.
- If the I/O exists in the THT, PinProbeWindow shows connector, pin, wire name, splice, section, color, and related I/O from the same THT wire/net.
- Releasing the probe clears the row on the next empty Probe frame.
- Probe never opens the WrongWiring/Short popup, never runs relay, PASS/FAIL, resistance, or TESTPOINT fault alarm.

### Production
- Wrong wiring/short is evaluated only while RuntimeMode.Production and no Probe session is active.
- The runtime generation is checked before and after asynchronous board-stop operations and again immediately before MessageBox.Show.
- An actual production wrong connection is reported on the first wrong frame; there is no 1000 ms ShortConfirm delay and it does not wait for `ĐANG KIỂM TRA...`.
- WrongWiring/Short rows remain sorted above open-circuit rows.

## Important operational distinction
The electrical stream is interpreted differently in Probe and Production. Therefore a physical GND probe is only treated as a probe while PinProbeWindow/Probe mode is active. Production mode intentionally interprets the stream as harness continuity/wrong-wiring data.
