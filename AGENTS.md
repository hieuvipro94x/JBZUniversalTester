# Project Overview

JBZUniversalTester is a Windows production harness tester for JBZ wiring products. The current .NET application is WPF on .NET 8 for Windows x86, with self-tests in `Tests/`.

The production board backend is FTDI D2XX: scan frames, `.tht` model files, PC-side `TestEngine`, D2XX card capacity and relay behavior. The Leak machine and label printer use their own independent Windows COM connections and must not be mixed with the D2XX board lifecycle.

# Architecture Boundaries

Source of truth:

- `.tht`: D2XX model source, parsed by `ThtModelParser`.
- Product Bundle (`*.jbzproduct.json`): links one part number to its D2XX `.tht` file.
- Leak configuration: per-model runtime settings stored by `ProductionConfigService`; it does not replace or modify `.tht` topology.

Current source code is the final source of truth. If docs and source conflict, investigate with the smallest relevant source reads instead of guessing.

# Important Modules

- `JBZUniversalTester.csproj`: main WPF app project.
- `Tests/JBZUniversalTester.SelfTests.csproj`, `Tests/Program.cs`: self-test harness.
- `Models/ProductModel.cs`, `Models/TestModels.cs`: neutral model/result DTOs.
- `Models/ProductBundle.cs`: D2XX product bundle mapping.
- `Models/BoardMode.cs`, `Models/ProductionSettings.cs`: production configuration surface.
- `Services/ThtModelParser.cs`: D2XX `.tht` parser.
- `Services/D2xxBoardTransport.cs`: FTDI D2XX lifecycle, scan, relay, resistance routing.
- `Services/UnifiedBoardTransport.cs`: D2XX lifecycle wrapper.
- `Services/TestEngine.cs`: PC-side continuity/fault engine for D2XX flow.
- `Services/BoardAddressMapper.cs`, `Services/BoardIoDecoder.cs`, `Services/ProbeContactClassifier.cs`: D2XX pin/frame/probe mapping.
- `Services/ProductionConfigService.cs`: load/save production settings.
- `ViewModels/MainViewModel.cs`, `ViewModels/TestViewModel.cs`, `ViewModels/ProductionSettingsViewModel.cs`: production UI orchestration and independent Leak/label COM ownership.
- `Views/MainWindow.xaml(.cs)`, `Views/TestWindow.xaml(.cs)`, `Views/ProductionSettingsPage.xaml`: WPF wiring and operator UI.

# Golden Rules / Invariants

- Do not infer or convert another topology format into `.tht`.
- D2XX probe uses only the D2XX mapping/classifier.
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
- Treating `:RESISTOR,3961` as ohms instead of raw ADC.

# Coding Safety Rules

- Every source-code change must include a release version increment in `Version.props`; keep `VersionPrefix`, `Version`, `AssemblyVersion`, `FileVersion`, `InformationalVersion`, `VersionFileTag`, and `AssemblyTitle` synchronized.
- Make minimal, task-scoped changes.
- Find root cause before editing.
- Do not refactor outside the requested task.
- Do not change protocol/API/data formats unless explicitly required.
- Do not upgrade dependencies unless requested.
- Do not hide errors with empty `catch`; log or preserve meaningful failures.
- Do not edit generated/build output.
- Do not create `*.bak`, `*_old.cs`, `*_copy.cs`, `*_fixed.cs`, or similar backup source files.
- Do not delete source unless the task explicitly requires it and the diff is understood.
- Do not add broad backend switches inside the production flow.

# Hardware Safety Rules

- One physical FTDI or COM device has one active owner/reader.
- Avoid multiple `SerialPort.DataReceived`/reader loops on one COM.
- On reconnect, fully dispose or cancel the previous lifecycle before opening again.
- Guard generation/stale callbacks so old transport events cannot mutate current state.
- If a Leak/printer COM is occupied, report it as occupied.
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
- Preserve the boundary between D2XX production scanning and independent Leak/printer COM services.
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
