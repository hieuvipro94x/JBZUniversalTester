# Multi-part native THT editing (V2026.09.451)

The native editor now retains every Product row in its original order, including
inspection flags, options, resistance expressions, variables and label metadata.
Previously only the first Product row was loaded and emitted on an edited save.

For files with multiple parts, CHỌN PART / 파트 선택 in the left navigation selects
the basic information to edit. Switching parts commits pending form input without
writing the file. Save writes all pending part edits to the original opened path.
Switching parts alone is not a content change and does not prompt to save. The
selected part remains selected after Save. A single-part file keeps its prior UI.

Connector/PIN/wire tables belong to the whole native file. Their existing option
cells (such as C/A/B in QL PE 66300-66400-NAM.tht) remain intact; selecting a part
does not filter, flatten or duplicate these shared tables. This release does not
add option editing controls or change runtime option interpretation.

Regression coverage uses the supplied native three-part file on temporary copies:
byte-exact no-op save, UI editing and saving each part, hidden metadata and option
preservation, shared wire edit, runtime LoadAll compatibility, clone isolation and
rejection of tab/newline input in previously selected parts. The supplied source
is never overwritten by tests. No production database migration is involved.
