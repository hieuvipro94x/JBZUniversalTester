# Master splice classification — V2026.09.335

The old Master-only projection treated every unexpected physical group with at
least three IOs as a short. Replacing a branch of a factory splice therefore
prevented confirmation of a wrong-wire sample.

Master evidence now compares actual connected groups with the expected connected
groups built from THT networks and CLIP AO/aN branches:

- A split expected common group (at least three IOs), with foreign IOs in its
  actual group, is classified as wrong-wire replacement evidence. The expected
  metadata identifies a member present in that group and a missing expected IO.
- An intact expected common group with an additional connection to another group
  remains short evidence.
- Correct common groups and pure missing connections do not create wiring faults.
- The existing three-IO short rule remains for groups without a split THT common
  group. Separate actual groups are classified independently.

This is an electrical-topology classification, not identification of the physical
manufacturing cause: missing and extra connections in the same group cannot
uniquely distinguish every combination of simultaneous defects.

Only `GetMasterWiringFaults` changes. Production classification, debounce, Probe
isolation, per-frame unique edge counts, full-removal gates, Leak/resistance,
relay ownership, LOT and history storage rules remain unchanged.

Verification:

- Regression reproduced the previous splice replacement being classified Short.
- Synthetic tests cover branch replacement, branch swaps between two common
  groups, intact-group bridging, mixed independent faults, valid common groups,
  and AO/aN CLIP replacements/bridges.
- ViewModel tests verify wrong/short Master steps reject the other fault category
  and accept their own category without production counts or Relay 2 marking.
  Persistence uses isolated temporary databases.
- Explicit ITEM replay parsed 27 local THT files and exercised the 9 files with
  both a common group and another eligible group. Each passed correct topology,
  branch swap and intact-group bridge checks; production faults were unchanged.
  Files include K32000-22401R-260224, WH322123, WH322244, WH322465, WH322527,
  WH322546, dddd and two copies of 00366555.

The ITEM replay uses simulated complete scan frames derived from actual THT
topology. No physical board, harness, printer or instrument was operated. No
production database, CFG or ITEM file was changed by these tests.

Commands:

```powershell
dotnet build -c Release --no-restore
.\Tests\bin\Release\net8.0-windows\win-x86\JBZUniversalTester.SelfTests.exe --test-master-splice
.\Tests\bin\Release\net8.0-windows\win-x86\JBZUniversalTester.SelfTests.exe --test-master-splice-items C:\ITEM
.\Tests\bin\Release\net8.0-windows\win-x86\JBZUniversalTester.SelfTests.exe
```
