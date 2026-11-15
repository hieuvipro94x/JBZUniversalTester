# V2026.09.282

The unterminated Leak RESULT fallback previously counted comma-separated entries,
including a trailing empty entry. A chunk containing only five numbers and a final
comma therefore met the six-value readiness condition, cleared the receive buffer
and failed strict parsing. The fallback now waits for six actual numeric fields
without collapsing empty positions or inventing values. Normal newline processing,
strict result validation, channel evaluation and serial lifecycle remain unchanged.
The regression fixture reproduces the screenshot payload and then appends its
missing sixth value; the resulting pressure/leak evaluation is preserved.

Settings now uses the same internal MainWindow page host as History. It no longer
constructs a separate native Settings window, changes taskbar visibility or hides
MainWindow. An active page must be closed before another can open. Back/Save release
manual operations before removing the page; Alt+F4 follows the same unsaved-edit
and close path. Deferred Settings creation is invalidated when production opens.

Verification includes a standalone WPF navigation test with production resources:
one Window object, Settings/History mutual exclusion, released bindings and restored
main actions. The window is never shown, so startup hardware and database discovery
are not run. Physical serial fragmentation and Windows Alt+Tab behavior still need
confirmation on the production machine. No production history or CFG is rewritten.
