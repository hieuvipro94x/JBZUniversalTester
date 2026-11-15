using System.Globalization;
using System.IO;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestTestViewDiagnostics()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZ-diagnostics-" + Guid.NewGuid().ToString("N"));
        var logger = new AsyncFileLogService();
        var diagnostics = new TestViewDiagnostics(logger);
        CultureInfo previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            FaultRow[] before =
            [
                new() { Connector = "A", Status = "OLD" },
                new() { Connector = "A", Status = "STABLE" },
                new() { Connector = "B", Status = "REMOVED" }
            ];
            Assert(diagnostics.CaptureRenderedRows(before, _ => throw new Exception("Disabled diagnostics enumerated rows")) is null &&
                   !diagnostics.TryBeginScanMetrics(),
                "Disabled diagnostics must skip table inspection and scan metrics");

            logger.Configure(true, root);
            Assert(diagnostics.TryBeginScanMetrics() && !diagnostics.TryBeginScanMetrics(),
                "Scan metrics retain the five-second throttle");
            diagnostics.ResetScanMetrics();
            int winners = 0;
            Parallel.For(0, 32, _ =>
            {
                if (diagnostics.TryBeginScanMetrics()) Interlocked.Increment(ref winners);
            });
            Assert(winners == 1, "Concurrent metric callers must produce one throttle winner after reset");

            diagnostics.LogScanMetrics(new TestViewScanMetrics(
                100, 90, 3, 7, 80, 8, 6, 12, 8, 6, true, "Production", 10, 15));
            var captured = diagnostics.CaptureRenderedRows(before, row => row.Connector)!;
            before[0].Status = "CHANGED AFTER CAPTURE";
            FaultRow[] after =
            [
                new() { Connector = "A", Status = "NEW" },
                new() { Connector = "A", Status = "STABLE" },
                new() { Connector = "C", Status = "ADDED" }
            ];
            var electrical = new ProductionElectricalSnapshot(77, 3, true, true, true, 1, false, false, 1.25);
            var snapshot = new TestEnginePresentationSnapshot(electrical, false, after, 2.5);
            diagnostics.LogEngineUiFrame(captured, after, row => row.Connector, snapshot, 9,
                new TestViewUiDiagnosticContext("TestingRealtime", "Inactive", "Installation", 3), 4.5, -2);
            Assert(captured[0].Status == "OLD" && after[0].Status == "NEW" &&
                   ReferenceEquals(snapshot.Rows, after),
                "Diagnostics must detach the before snapshot without mutating rows or the engine snapshot");

            ScanFrame frame = FrameSeq(77, (1, new[] { 2 })) with { ScanGeneration = 3 };
            var evidence = new ProductEvidenceSnapshot(77, 3, 2, 1, 0, 0, 0, 0,
                false, true, ProductPresenceState.Present, "MODEL");
            diagnostics.WriteProductEvidenceLog(frame, evidence);
            diagnostics.WriteProductEvidenceLog(frame with { Sequence = 78 }, evidence with { FrameSequence = 78 });
            diagnostics.ResetProductEvidence();
            diagnostics.WriteProductEvidenceLog(frame, evidence);
            diagnostics.WriteProductEvidenceLog(frame, evidence with { ProbeEvidence = true, Reason = "PROBE_WITH_EXISTING_PRODUCT" });
            logger.DisposeAsync().AsTask().GetAwaiter().GetResult();

            string[] lines = File.ReadAllLines(Path.Combine(root, "JBZUniversalTester.log"));
            Assert(lines.Any(line => line.EndsWith(
                "CONTINUOUS_SCAN_METRICS rx_production=100 engine_processed_by_vm=90 engine_processed_total=80 dropped=3 probe_routed=7 ui_scheduled=8 ui_rendered=6 scan_running=True mode=Production", StringComparison.Ordinal)) &&
                lines.Any(line => line.EndsWith(
                "UI_PIPELINE requests=12 dispatcher_enqueued=8 rendered=6 coalesced=6 frame_queue_depth=10 frame_queue_high=15", StringComparison.Ordinal)),
                "Extracted metrics must retain the existing log fields, values and order");
            Assert(lines.Any(line => line.EndsWith(
                "UI_SNAPSHOT seq=77 generation=3 product_state=TestingRealtime probe_state=Inactive presentation_mode=Installation source_rows=3 rendered_rows=3 removed=1 added=1 changed=3 source=engine", StringComparison.Ordinal)) &&
                lines.Any(line => line.EndsWith(
                "UI_FRAME seq=77 generation=9/3 product_state=TestingRealtime probe_state=Inactive presentation=Installation rendered_rows=3 added=1 removed=1 changed=3 engine_compute_ms=1.25 row_diff_ms=2.5 queue_delay_ms=0 apply_ms=4.5", StringComparison.Ordinal)),
                "Duplicate-key status changes and additions/removals must preserve existing UI diagnostic semantics");
            Assert(lines.Count(line => line.Contains("PRODUCT_EVIDENCE seq=", StringComparison.Ordinal)) == 3 &&
                   lines.Count(line => line.EndsWith("state=Present reason=MODEL", StringComparison.Ordinal)) == 2 &&
                   lines.Any(line => line.EndsWith("probe_evidence=True valid_product_evidence=True state=Present reason=PROBE_WITH_EXISTING_PRODUCT", StringComparison.Ordinal)),
                "Evidence logging must suppress sequence-only duplicates, reset per cycle and log changed evidence");
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            logger.DisposeAsync().AsTask().GetAwaiter().GetResult();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
