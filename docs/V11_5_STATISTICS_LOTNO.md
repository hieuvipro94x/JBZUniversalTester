# V11.5 - Statistics per model and automatic LOTNO

## Production statistics

`production.statistics.json` is created next to the application executable.
Each model has an independent record keyed primarily by PartNumber + ModelName.
Stored fields include Total, Pass, Fail, LastResult, LastLotNo and LastTestedAt.

When a model is loaded, TestView immediately restores its counters. Switching to
another THT does not reset the previous model. Switching back restores the old
values.

## LOTNO

`ProductionSettings.LotNo` is editable only in Production Settings. TestView binds
LOTNO OneWay and its TextBox is read-only.

For example, if LOTNO is configured as 2000:

- product 2000 finishes PASS/FAIL -> persisted next LOTNO becomes 2001
- next completed product -> 2002
- closing and reopening the program continues from the persisted value

Hardware/communication failures do not increment production statistics or LOTNO.

## Result recording points

A product is recorded once at one of these final outcomes:

- PASS
- FAIL resistance
- final CHƯA ĐẠT
- confirmed wiring/short fault

`_resultRecordedThisCycle` prevents duplicate counting from repeated callbacks.
Changing model cancels the previous cycle before assigning the new model, so a
late async callback cannot credit the result to the wrong part number.
