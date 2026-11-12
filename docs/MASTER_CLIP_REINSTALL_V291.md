# V2026.09.291 — Fresh CLIP installation for Master samples

## Cause

Master removal used the scan reset, which intentionally preserves latched CLIP
branches during one physical installation. AO/a1/a2/... could therefore remain
qualified after removing an incomplete sample and fitting another one.

## Change

- Confirmed full removal now resets the entire product cycle, including CLIP,
  for incomplete good/bad Masters and transitions to another Master/production.
- Product-cycle reset clears CLIP and ordinary qualification atomically.
- Two fresh complete valid empty scan frames confirm removal; partial removal
  does not end the current installation. Removal is also observed while early
  Master Leak is pending, without relying solely on coalesced UI events.
- Previous-sample Leak measurements/qualification and history identity are
  cleared. A pending COM operation retains ownership until it finishes; its
  late result/error cannot update the next installation. Pending connector
  work is revisited when that owner releases the port.
- Scan/routing reset within the same installation retains existing behavior.

## Verification

`TestMasterClipReinstallation` covers repeated incomplete good/bad sample
removal, fresh AO/a1/a2/a3 rows, partial-removal protection, next-sample and
production transitions, and full removal during pending Leak PASS/timeout.
It checks no production counter changes or relay actuation from these resets.
Tests use temporary SQLite databases and fake hardware; real production data
is not modified. Physical D2XX/Leak hardware verification remains required.
