# Startup preparation V2026.09.397

Baseline: V396 local system log, 2026-11-07 (UTC+07).

| Observed step | First logged launch | Second logged launch |
| --- | ---: | ---: |
| STARTUP log to MainWindow ContentRendered | 1305 ms | 1324 ms |
| configuration-ready log to database-ready log | 48 ms | 52 ms |
| D2XX open/handshake | 1060 ms | 1067 ms |
| THT parsing/identity capture | 41.405 ms | 49.569 ms |
| immutable engine model preparation | 7.009 ms | 9.735 ms |

These launches do not measure the entire cold process start: V396 initializes
its performance clock after configuration/database preparation. No V397 bench
speedup is claimed from these baseline measurements.

V397 overlaps database/resource preparation, the existing board handshake and
MainWindow construction. Once database preparation completes, THT parsing and
immutable topology construction run during handshake. Model installation and
statistics access still wait for database readiness and board initialization.
The prepared model retains its selection generation; a newer selection or
shutdown prevents an old startup model from being installed. Preparation is
cached once per TestViewModel and does not change output, test counts or IO.

The existing eight-second hardware timeout applies to the hardware task alone,
so a slower CPU/database does not become a false board-startup timeout. Driver
commands, handshake delays and board ownership are unchanged. Database failures
still reach the splash error path before MainWindow is shown. Legacy data
selection/overwrite rules are retained; existing complete local state bypasses
old-version directory probing.

STARTUP_PERF now records configuration, database/resource preparation and final
splash handoff. elapsed_ms starts at App.OnStartup entry; process_elapsed_ms
includes the process age before that entry, using a monotonic clock thereafter.

Verification: controlled pending-board tests prove no early model installation,
no preparation before database readiness, rejection of stale generations and
shared preparation. Temp SQLite fixtures verify integrity and preserve Tests,
TestFaults, ResistanceMeasurements and WaterProofMeasurements. The full harness
passes 174/174 tests. Production hardware timing must be compared with a V397
startup log on the target station.
