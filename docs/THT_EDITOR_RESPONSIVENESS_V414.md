# V2026.09.414: THT editor responsiveness

V413 reproduction on a temporary WH320853 copy (78 connectors, 492 PIN rows)
measured ~351 ms entering Wire: ~68 ms data preparation and ~284 ms layout.
PIN/Connector reloads cleared their collections, invalidating realized cells.
Wire displayed editable ComboBoxes in every visible row. Settings Loaded also
continued preparing covered panels after direct navigation to the editor.

- Reuse unchanged Connector and PIN rows. Rebase PIN native metadata and hidden
  wire fields on reload; rebuild when physical identities change.
- Render color text in idle Wire cells; instantiate the ComboBox only while
  editing. Preserve keyboard editing, normalization and automatic wire colors.
- Do not initialize/reflow covered Settings panels while editing THT.
- Isolate editor DataContext and detach its content during Settings teardown.
- Log THT_PERF OPEN, TAB_DATA and TAB_READY. TAB_READY includes dispatcher/layout
  waiting; it is not a CPU-only duration. Existing UI_STALL remains authoritative
  evidence of delayed UI heartbeat, not an attribution to a particular method.

Regression tests cover stable row identities, updated hidden fields, sparse
PIN/IO identities, lazy color editor creation and commit, skipped hidden panel
initialization, teardown binding ownership, existing native save/topology tests.

Same-machine repeated measurements after changes reduced first Wire entry to
~201 ms and subsequent PIN/Connector entry to ~30-64 ms. Timings vary with CPU,
cache and system load. The 1-1.6 s stalls observed in the production V413 log
were not reproduced locally; a production-machine check remains necessary.
All reproduction edits use copies; original THT bytes remain unchanged.
