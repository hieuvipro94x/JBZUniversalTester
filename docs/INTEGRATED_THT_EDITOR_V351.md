# Native THT editor and per-product discard configuration

The Settings editor now reads discard contacts from the selected THT (falling back
to the most recently loaded THT path). Saving an edited pair updates that exact
file. Clearing both fields explicitly removes its discard contacts. Existing
station-wide CFG keys are read for compatibility but are neither applied to a
selected product nor written on future saves. Selecting a product is read-only.
Native writes retain backups under ThtBackups, verify topology before replacing
the original, and rebind only a previously valid Leak sidecar. Failure to replace
the sidecar rolls the THT back. The current runtime model is updated after Save.

Administrators can open SỬA CẤU HÌNH THT directly from MainWindow, using the existing
Settings login, or CHỈNH SỬA THT from Settings. The editor occupies the same
window and exposes Basic information, Connector, Pin/IO, Wire/connections and
Color pages adapted from the user-provided ModelCreator V1.4 source at
`C:/Users/user/Desktop/Nam/ModelCreator 1.4/ModelCreator V1.4`.
Only native HarnProg V1.110 THT is supported. MODEL conversion, packet compilation,
external application startup and hardware ownership are not included.

Since V2026.09.371, Connector shows only order, name, pin count and start IO.
The waterproof checkbox and Leak channel selector are removed. Automatic Leak
uses only the per-product profile saved in Production Settings, including its
enable flag, CH1/CH2/CH3 connector assignments, pressure limits and timing. A
product without a saved profile defaults to Leak OFF. Legacy THT flags and
`.tht.leak.json` files cannot auto-enable, override or block the runtime profile.
Existing saved CFG profiles remain unchanged. Legacy metadata is retained by
the native editor's compatibility writer; no source THT, sidecar or history is
bulk rewritten or deleted by this change.

Opening and saving run disk/parsing work off the Dispatcher. Save snapshots the
document, stages and validates THT and Leak output, checks for stale external
changes, retains original backups and rolls back a failed paired-file commit.
CP949 text is strict: unsupported characters fail without silently changing wire
or product identifiers. Hidden THT fields, label data, resistance fields, CLIP
representation and OLE metadata use the source ModelCreator round-trip writer.
The editor color catalog is isolated in LocalAppData/JBZUniversalTester/ModelEditor.

V2026.09.354 restores ModelCreator's 1224-by-900 layout: path and New/Open/Save/Delete
at the top, five navigation buttons at the left, editing panel at the right and
Return at the bottom left. The canvas only scales down on smaller displays.
The editor's Open action shares ProductPickerWindow with production selection,
including recursive ITEM search, debounced filtering and recent tested products.
Its browse action can open a THT outside ITEM. Selecting a file for editing does
not select it for production or increment test history/counts.
Delete archives inactive THT files and their Leak sidecars under ThtBackups;
the product currently loaded on the tester is protected from deletion.

Settings contains ShowDiscardConfigurationNotice (default on), persisted in CFG.
It controls only the product-load/startup configuration popup. Queued notices
recheck the preference before displaying. Main status and the physical discard
interlock remain active regardless of the popup preference.

The editor asks about unsaved changes on navigation. When the user saves, an
opened file is written back to its exact original path, including when its displayed
model metadata is edited. Opening, switching unchanged tabs, returning after Save
or reverting to saved values does not prompt for Save. Only actual unsaved edits
require confirmation. On returning to Main, an
edited active product is reloaded through the existing model loader. Production
settings reload on the next Settings visit. File editing does not count products,
write production history, or issue relay commands. Shutdown drains disk work.

Verification uses temporary THT files and fake transports. Tests cover independent
A/B discard pairs, replacement and clearing, global CFG exclusion, exact THT
round trips, UI saves, all five pages, embedded-template new files, stale edits,
CP949 rejection and sidecar rollback. Production databases and original ITEM files
are not edited by tests or previews.

V2026.09.373 updates the PIN page presentation. PIN TYPE replaces the misleading
test-count header. Connection cells show IO, Connector and Pin labels with one
target per line; -1 is displayed as a dash and roles distinguish an unlinked pin.
The editing templates still bind to the original numeric MainIo and slash-delimited
CouplingText, retaining the existing reciprocal-edit and IO-renumbering handlers.
Unknown or duplicate IO does not receive a guessed connector mapping.

For loaded CLIP rows the displayed PIN TYPE comes from the native PIN table,
including empty legacy fields. An unedited display value returns the original
editor-model value on ReadData, so merely visiting the PIN page cannot change
CLIP metadata, make the document dirty or cause a rewrite.

V2026.09.374 aligns the connection cells with TestView's canonical IO-CN-PN:
IO175 displays as 175-6-15 (32 IO per physical connector), independently of
the product endpoint's Connector/Pin labels. Both main and target references use
this display only; stored IO values and edit syntax remain unchanged. Regression
checks compare editor output against TestEngine's formatter at connector boundaries.

The Wire page adds search/next and clear controls, with Enter/Escape shortcuts.
A number or IO prefix locates an exact IO; CN and PN prefixes locate connector
and pin matches; other text searches connector, wire name and added-wire fields.
Search cycles through matches in the complete existing grid without filtering,
reordering, inserting or removing rows. This preserves structural-edit indexes,
all model rows and the unsaved-change state even for large THTs.

V2026.09.375 searches automatically after 180 ms of typing inactivity. Search
selects the first matching row without replacing query text or taking keyboard
focus/caret from the search box; Enter or Find next still cycles through matches.
The query is retained when the Wire page reloads. Clear and page unload cancel
pending search work. The box has an explicit centered 36-pixel height with zero
vertical padding so text is not clipped by inherited control sizing.
