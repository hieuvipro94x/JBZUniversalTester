# Vietnamese / Korean TestView — V2026.09.309

Settings exposes **Ngôn ngữ TestView** with `Tiếng Việt` (`vi`) and `한국어` (`ko`). Save Settings, then open TestView. No application restart is required. The existing atomic CFG writer persists `[TestViewLanguage]`; old/missing/unknown selections default to Vietnamese. Editing requires the IO settings permission.

## Scope and boundaries

- Localizes TestView captions, navigation and print action labels, column headings, large/center statuses, continuity fault cells, resistance/Leak result labels, maintenance captions and TestView confirmation dialogs.
- Korean dialogs use Korean confirmation/cancel buttons instead of depending on the Windows display language. Native Vietnamese dialog behavior remains unchanged.
- Part/model/product names, wire identifiers, connector/pin values, color codes, IO/COM identifiers and numeric measurements remain opaque data. Their original bindings are preserved. Fault summary formatting translates authored labels separately from these values.
- Printed label payloads, THT topology, history/export data and test protocols are not localized or modified.
- Raw VM states and row result values remain unchanged. State-machine comparisons, PASS/FAIL color triggers, flashing testing dots, removal interlocks and relay/scan logic therefore keep their existing semantics.
- The view installs display converters once when constructed. Frequent known statuses use an in-memory dictionary; no translation network, disk reads or new background polling enters the test path. Unknown external technical diagnostic details remain intact rather than being hidden behind a generic error.

## Verification

New regression coverage checks default/fallback language, CFG round-trip alongside unchanged printer settings, authored XAML/fixed-state catalog coverage, Master/Leak combinations, opaque identifiers that resemble UI words, real WPF bindings/virtualized-column converters, live fault updates and unchanged hardware command count. It also checks settings permissions and renders a Korean TestView image to the temporary directory for layout inspection.

Real D2XX, Leak and Keysight hardware is not used for these localization tests.
