# V2026.09.319 — Product label choice, live LOT base and first selection

## Product label type

The label type was previously global. Settings now persist a compact
`[Label.Product.<escaped product key>]TYPE` entry, using the same PartNumber /
model fallback identity as product LOT settings. Printer COM and template layout
overrides stay shared. On product selection, the saved type and its standard
physical dimensions are restored. The Home label caption uses the selected
product's choice. Explicitly saved choices take precedence over old THT label
commands; unconfigured legacy models retain their previous THT/custom behavior.
Manual preview/printing uses the type explicitly selected in its editor.
The new map belongs to the existing Label permission group. `NONE` in the same
per-product entry means no automatic printing, without adding a global switch.
Settings exposes an editable per-product print checkbox. Existing unconfigured
products retain the previous always-print default.

## Immediate LOT edits

Changing the start value already created a new HistoryBatchId, but background
statistics recovery ignored that revision and could restore the earlier batch's
LOT. The CFG ProductLot record now appends LastStartChangedDate (old records
remain readable). On that date only, LOT recovery reads the latest committed
PASS for the exact new ProductionBatchKey. If no PASS exists yet, the configured
start remains active. Other dates retain existing date-based recovery behavior.
All daily/monthly/lifetime counts still include the original production rows.
No database migration, history rewrite, deletion or re-numbering is performed.

Existing production convention is preserved: start 2000 displays 2000 before
the first PASS; the first PASS is 2001. Changing to 7000 makes the next PASS 7001.
Increases and decreases both take effect after saving without restarting.

## First-time product selection

Explicit user selection checks the saved product choice first. If absent, it
queries existing production history asynchronously. A product with any previous
production test, including FAIL, is not prompted. A truly new product opens a
modal setup requiring a nonnegative numeric starting LOT and an explicit
"Có in tem" / "Không in tem" choice. A printing product must choose among the
five existing templates; a nonprinting product does not need a template.
Cancel leaves the previous product selected.
Storage failure keeps the dialog open and does not publish runtime settings.
Successful setup writes CFG once; its product entry suppresses future prompts
even before the first test or after an application restart. Stale model-load
generations and the physical-removal gate remain protected. A nonprinting PASS
still commits LOT, counts and product history; it neither captures a label job
nor sends printer commands. No measurement,
printer command or relay action is performed by setup.

## Verification

Tests use temporary CFG/SQLite files and fake hardware. Coverage includes:
A -> B -> A, distinct SQDZ formats with old embedded THT commands, 2000 -> 7000
and lowering the base, first new PASS allocation, CFG reload, existing history
counts and PRAGMA integrity_check, blank inputs, cancel, failed save/retry, no
repeat prompt, historical FAIL suppression, no-print CFG persistence and a
production PASS without printer output, and an actual rendered WPF dialog.
No real production data or physical printer was modified during verification.
