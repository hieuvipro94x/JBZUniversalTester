# V2026.09.290 — UI/state audit and persistent TESTPOINT

## Review scope

Reviewed startup and offline entry, remembered-model loading and selection locks,
single-window Settings/History navigation, Master sample transitions, production
fault confirmation, full removal, Leak retries, resistance results, PASS/relay
presentation, label-port selection and diagnostic fault details. Existing tests also
exercise history queries/export, legacy data compatibility, daily counters, COM
ownership and stale callbacks. No real production database was altered for the new
UI/audio tests. Hardware tests use fake transports; COM enumeration is read-only.

## Corrected behavior

- A saved PASS with a blocked relay retains its explicit warning and red status;
  neither the center PASS result nor the held green PASS lamp may mask that warning.
  Existing durable result/counter/relay behavior is unchanged.
- Persistence faults suppress held PASS lamps and center installation/PASS overlays.
  Queued scan-activity callbacks cannot relight the lamps while persistence is blocked.
- System errors, including insufficient card capacity, take priority over Master
  sample prompts. The center installation overlay is restricted to a real waiting
  prompt, preventing contradictory installation text during error/manual states.
- The fault popup falls back to the known physical IO-pair summary when THT wire or
  housing metadata is missing, rather than replacing useful IO detail with an
  unspecified-wire/location message. Every fault remains its own item.
- A saved printer COM remains selected and visible when absent from detected ports,
  including enumeration-error fallback. Opening/refreshing settings does not erase
  it, connect it automatically, or write the configuration.
- BCW reprint reports a missing saved label directly. Unexpected failures keep their
  detailed log and show an action-specific operator message.
- Persistent TESTPOINT and navigation-removal behavior are documented in
  PERSISTENT_TESTPOINT_V289.md and included in this version.

## Verification approach

Updated obsolete test expectations for the explicitly requested Master labels,
current LED timing/relay hold policy, shared settings layout, asynchronous printer
selection, TEM_BE_QRCODE, removal confirmation and embedded STAGEOK. The previous
four failing test groups stopped early on outdated UI/source-string assumptions;
they are now exercised through their later assertions as well.

Added real WPF template checks for multiple popup faults with missing metadata,
unplugged printer COM selection, PASS/error lamp behavior, and all ten manual
resistance result lines. These complement existing layout/responsiveness, data,
hardware-lifecycle and production safety regressions. Builds and self-tests do not
establish real hardware timing or audible output quality; those require the station.

Final verification: Release build succeeds with 0 warnings and 0 errors. The full
self-test suite passes 116/116 groups, including the four formerly failing groups.
Targeted UI status audit, persistent TESTPOINT, PASS sound/relay independence,
settings lifecycle and removal-status tests also pass. No real D2XX/Leak/Keysight
device or printer was driven by the new audit fixtures.
