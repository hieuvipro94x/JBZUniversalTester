# Project Overview

JBZUniversalTester is a Windows production harness tester for JBZ wiring products. The current .NET application is WPF on .NET 8 for Windows x86, with self-tests in `Tests/`.

The project has two hardware families that must stay conceptually separate:

- D2XX backend: FTDI D2XX transport, scan frames, `.tht` model files, PC-side `TestEngine`, D2XX card capacity and relay behavior.
- UART TTL backend: original Raspberry Pi Universal Tester firmware protocol over Windows COM/USB-UART, 115200 8N1 CRLF, `.model` topology, `.setup` runtime config, firmware-driven OPEN/OTHER/TESTPIN/CIRCUIT lifecycle.

V15.2 refactor direction: split mixed production logic into independent Windows apps, e.g. `JBZ.PiBoard.PC.exe` for the original Pi board protocol and `JBZ.Windows.Integrated.exe` for the native Windows/D2XX system, with only proven neutral infrastructure shared.

# Architecture Boundaries

D2XX and UART TTL are different backend/protocol systems. Do not mix protocol semantics, state transitions, pin mapping, resistance commands, waterproof flow, model download, or PASS/FAIL behavior between them.

Source of truth:

- `.tht`: D2XX model source, parsed by `ThtModelParser`.
- `.model`: Pi/UART topology source, parsed/compiled by Pi legacy model code.
- `.setup`: Pi/UART runtime configuration, not topology and not sent raw to firmware.
- Product Bundle (`*.jbzproduct.json`): links one part number to backend-specific files. It must not be used to infer or convert between `.tht` and `.model`.

Current source code is the final source of truth. If docs and source conflict, investigate with the smallest relevant source reads instead of guessing.

# Important Modules

- `JBZUniversalTester.csproj`: main WPF app project.
- `Tests/JBZUniversalTester.SelfTests.csproj`, `Tests/Program.cs`: self-test harness.
- `Models/ProductModel.cs`, `Models/TestModels.cs`: neutral model/result DTOs.
- `Models/ProductBundle.cs`: backend-specific product bundle mapping.
- `Models/BoardMode.cs`, `Models/ProductionSettings.cs`: production configuration surface.
- `Models/PiLegacyModel.cs`: Pi `.model`/`.setup` parsing and legacy compiler when present.
- `Services/ThtModelParser.cs`: D2XX `.tht` parser.
- `Services/D2xxBoardTransport.cs`: FTDI D2XX lifecycle, scan, relay, resistance routing.
- `Services/UnifiedBoardTransport.cs`: board transport selector/wrapper; must not become a semantic mixing layer.
- `Services/TestEngine.cs`: PC-side continuity/fault engine for D2XX flow.
- `Services/BoardAddressMapper.cs`, `Services/BoardIoDecoder.cs`, `Services/ProbeContactClassifier.cs`: D2XX pin/frame/probe mapping.
- `Services/ProductionConfigService.cs`: load/save production settings.
- `ViewModels/MainViewModel.cs`, `ViewModels/TestViewModel.cs`, `ViewModels/ProductionSettingsViewModel.cs`: production UI orchestration; historically high-risk for mixed backend state.
- `Views/MainWindow.xaml(.cs)`, `Views/TestWindow.xaml(.cs)`, `Views/ProductionSettingsPage.xaml`: WPF wiring and operator UI.

# Golden Rules / Invariants

- Do not convert `.tht` to `.model` or `.model` to `.tht` by inference.
- UART `*IDN?` must identify a real Universal Tester board; do not accept WP-100, GT800, or random USB serial devices.
- UART parser requires a persistent CRLF frame buffer. A read is not a command.
- UART `:MAXEXT,...` is sent only after firmware sends `:MEASURE`.
- UART `:OPEN` during live monitoring is not automatically final FAIL.
- UART `:OTHER` represents wrong/cross connection and must not be collapsed into OPEN.
- UART TESTPIN comes from firmware physical pins and `.model`; D2XX probe uses D2XX mapping/classifier.
- Probe/TESTPIN must not create FAIL, increment production, or fire relay.
- ProductRemoved/removal confirmation is required before re-arming the next cycle.
- FAIL must not trigger marking relay. D2XX FAIL allows only JIG/Relay 1 after confirmation.
- JIG/relay/output must return to initial state after each cycle.
- Hardware lifecycle must serialize owner/reader/dispose/reconnect and reject stale callbacks.
- Never guess firmware commands, ADC formulas, channel mapping, COM roles, FTDI serials, timing, retry policy, or data formats.

# Historical Regressions

Avoid repeating these known failures:

- `StackOverflowException` from recursive property/setter/event loops.
- WPF read-only bindings accidentally configured as `TwoWay`.
- Backup/copy files compiled into the app.
- Stale callbacks after model/board/mode changes.
- Multiple readers or owners on the same FTDI/COM device.
- Disposing a hardware handle while a worker is still reading.
- Probe events entering the fault engine.
- Treating one IO returning as full ProductRemoved.
- Duplicate physical edges inflating master counts.
- FAIL path accidentally firing Relay 2.
- AUTO activating D2XX and UART at the same time.
- Treating `:RESISTOR,3961` as ohms instead of raw ADC.

# Coding Safety Rules

- Make minimal, task-scoped changes.
- Find root cause before editing.
- Do not refactor outside the requested task.
- Do not change protocol/API/data formats unless explicitly required.
- Do not upgrade dependencies unless requested.
- Do not hide errors with empty `catch`; log or preserve meaningful failures.
- Do not edit generated/build output.
- Do not create `*.bak`, `*_old.cs`, `*_copy.cs`, `*_fixed.cs`, or similar backup source files.
- Do not delete source unless the task explicitly requires it and the diff is understood.
- Do not use broad runtime switches like `IsPi`, `PiMode`, `LegacyPi`, `UsePiBoard`, or `PlatformMode` inside shared production flow.

# Hardware Safety Rules

- One physical FTDI/COM device has one active owner/reader.
- Avoid multiple `SerialPort.DataReceived`/reader loops on one COM.
- On reconnect, fully dispose or cancel the previous lifecycle before opening again.
- Guard generation/stale callbacks so old transport events cannot mutate current state.
- If a COM is occupied, report it as occupied, not as "board not found".
- Do not assume firmware behavior not proven by trace or current source.

# Bug Fix Workflow

1. Read `AGENTS.md`.
2. Understand the task.
3. Identify relevant files.
4. Read only required code.
5. Find root cause.
6. Apply minimal fix.
7. Build/test.
8. Check targeted regression risk.
9. Review diff.
10. Report clear result.

# Feature Workflow

- Identify the integration point first.
- Preserve D2XX/UART architecture boundaries.
- Do not break the other backend.
- Build/test the affected app and tests.
- Update documentation if an invariant changes.

# Build / Verification

Known verification commands:

- `dotnet clean`
- `dotnet restore`
- `dotnet build -c Release`
- `VERIFY_BUILD_V15_0_0.cmd`
- `VERIFY_BUILD_V15_2_0.cmd` when working on V15.2 changes.

# Definition of Done

A task is not done just because compile passes. Verification must match the risk: unit/self-tests for logic, build for project structure, and explicit hardware status for D2XX/COM behavior. If real hardware was not tested, say so clearly.

# Detailed Technical Reference

Primary deep reference:

`docs/BAO_CAO_TONG_HOP_CODEX_REVIEW_V15_0_0.md`

Normal tasks should read only `AGENTS.md` plus relevant source files. Open the technical report only for deeper investigation. Source code remains the final source of truth; documentation/source conflicts require investigation, not guesses.
