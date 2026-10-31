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
window and exposes Basic information, Connector/Leak, Pin/IO, Wire/connections and
Color pages adapted from the user-provided ModelCreator V1.4 source at
`C:/Users/user/Desktop/Nam/ModelCreator 1.4/ModelCreator V1.4`.
Only native HarnProg V1.110 THT is supported. MODEL conversion, packet compilation,
external application startup and hardware ownership are not included.

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
