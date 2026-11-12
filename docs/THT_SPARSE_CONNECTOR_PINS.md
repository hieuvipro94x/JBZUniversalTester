# Sparse physical PIN labels in the THT editor

`Connector.PinCount` is the populated row count, not the maximum physical PIN
number. A native connector can contain PINs 3 and 4 only, or PINs 1..48 except
34. Editing validation still requires positive IO/PIN values, existing unique
connector names and an exact populated row count for every connector.

Connector regeneration retains existing physical PIN/IO identities and source
row indices. ConnectorPage supplies original row names so renames, including
name swaps, cannot remap sparse groups by position. Explicit growth fills vacant
PIN labels and allocates unused IO; explicit shrink removes the final physical
PIN rows. Other connectors keep their original addresses. Native/sparse wire
row insertion and deletion do not renumber unrelated PINs or IO.

Export preserves unchanged native connector metadata. New/resized connectors
and explicitly edited physical PIN numbers retain sufficient physical PIN span;
the exported span is independent of the populated row count. Sparse support
does not synthesize unused PIN rows on Open or a metadata-only Save.

Regression coverage includes synthetic sparse files and an optional real-file
copy via `--test-sparse-connector-pins PATH`. Tests exercise actual editor Open,
no-op byte-exact Save, metadata Save, connector rename, row growth/insertion/
deletion, preservation of native tables and runtime PIN/IO/CLIP topology.
The WH320853 LAN source was read only; all editing used temporary copies.

The editor displays connectors in natural numeric order (1, 2, 10), followed by
CLIP and interlock connectors. PIN and wire tables sort physical PIN numbers
within each connector. This display ordering preserves source row identities,
IO addresses and missing PIN numbers. Saving a changed PIN keeps the selected
wire row selected at its new sorted position. Ordering alone does not mark the
document as edited. `--test-editor-ordering PATH` verifies this on a temporary
copy, including byte-exact no-op Save and unchanged runtime topology.
