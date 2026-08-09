# Requirement 3 implementation

## ALL6.xls mapping observed
The binary XLS sample contains the history/label fields `Eco`, `Nco`, `Alc`, `Lot`, `HtdrvName` and sample values such as:

- PartName: `UART`
- PartNumber: `NI375C1000`
- Eco: `NE N EV`
- Alc: `NI375/C1000`
- Lot: `2001`, `2002`, ...
- label serial: `2407152001WH`
- label barcode: `NI375C10002407152001`
- format: `KS91`

V12 uses those values/order for history and EPL PASS labels.

## Config
On startup `ProductionConfigService.EnsureSavedOnStartup()` rewrites the complete current settings into:

- `production.settings.json`
- `UniversalTester.cfg` (English keys)

Known original Htdrv keys `[카드 수]`, `[USB 지연]`, `[스탬프 지연(msec)]` are accepted for migration when the JSON file does not exist.

## History
Database: `Data/History/test-history.db` by default.

Primary sample-compatible fields:
`DateTime, 파트명, 파트번호, Eco, Nco, Alc, Lot, HtdrvName`.

Additional diagnostic fields are retained for traceability: result, model, open/wrong/short counts, resistance values and production/device metadata.

## HtdrvName
Example:
`JBZUniversalTester V12.0.0 [Card Count]10 [USB Delay]1 [Stamp Delay(ms)]10,20`

## Stamp delay
`StampDelay="10,20"` means Relay 1 (JIG EJECT) ON 10 ms and Relay 2 (MARKING) ON 20 ms. PASS UI/audio starts together with Relay 1 command.

## Label
When production result is PASS and AutoPrintLabelOnPass is enabled:
1. Save EPL preview.
2. Print through configured COM printer, or Windows RAW printer if COM is blank.
3. Data order follows ALL6 sample.

## External settings without a defined protocol
`WaterproofSerialPort` and `TemperatureTolerance` are persisted and available to runtime/history metadata, but this project still has no waterproof/temperature-device protocol from the supplied traces/source. V12 does not invent commands for those external devices.
