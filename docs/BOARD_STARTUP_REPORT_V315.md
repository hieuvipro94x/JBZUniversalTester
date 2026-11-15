# V2026.09.315 — Board startup connection reports

The V314 portable configuration has `EnableSystemLogs=0`. This intentionally
disables `JBZUniversalTester.log`; production history remains independent.

A failed D2XX connection or startup timeout enters the handled UI-only startup
state. Previously that branch only called the optional logger and never called
the crash reporter, so with system logs disabled neither diagnostic file was
created. Runtime board disconnects already called the crash reporter.

Both startup connection failures and startup timeouts now call the same hardware
report writer as runtime device faults, once per startup-unavailable transition.
The output is `Crash/JBZUniversalTester.RPT` under the running executable folder.
With system logs disabled the existing concise operator report is retained;
with logs enabled the existing technical exception/context report is retained.

No change to offline UI access, production arming, D2XX ownership, relay timing,
Leak/Keysight/printer handling, history or user configuration is made.

Regression coverage injects an in-memory report sink: failed connect and repeated
startup timeout must each report once without becoming a fatal device fault.
The existing canonical persistence test checks actual RPT creation and log-off
report formatting. No physical hardware was disconnected during verification.
