using System.Diagnostics;
using JBZUniversalTester.Models;

namespace JBZUniversalTester.Services;

// Observes snapshots only. No transport, engine, UI dispatcher or persistence ownership.
public sealed class TestViewDiagnostics
{
    private readonly AsyncFileLogService _log;
    private long _lastContinuousScanMetricsTick;
    private string _lastProductEvidenceSignature = string.Empty;

    public TestViewDiagnostics(AsyncFileLogService? log = null) =>
        _log = log ?? AsyncFileLogService.Current;

    public bool Enabled => _log.FileLoggingEnabled;

    public void ResetScanMetrics() => Interlocked.Exchange(ref _lastContinuousScanMetricsTick, 0);
    public void ResetProductEvidence() => _lastProductEvidenceSignature = string.Empty;

    public (string Key, string Status)[]? CaptureRenderedRows(
        IReadOnlyList<FaultRow> rows, Func<FaultRow, string> rowKey) =>
        Enabled ? rows.Select(row => (rowKey(row), row.Status)).ToArray() : null;

    public bool TryBeginScanMetrics()
    {
        if (!Enabled) return false;
        long now = Environment.TickCount64;
        long previous = Interlocked.Read(ref _lastContinuousScanMetricsTick);
        if (previous != 0 && now - previous < 5000) return false;
        return Interlocked.CompareExchange(ref _lastContinuousScanMetricsTick, now, previous) == previous;
    }

    public void LogScanMetrics(TestViewScanMetrics m)
    {
        long coalesced = Math.Max(0, m.UiRequests - m.PipelineRendered);
        _log.Performance(
            "CONTINUOUS_SCAN_METRICS " +
            $"rx_production={m.Received} engine_processed_by_vm={m.Processed} " +
            $"engine_processed_total={m.EngineProcessed} dropped={m.Dropped} probe_routed={m.ProbeRouted} " +
            $"ui_scheduled={m.UiScheduled} ui_rendered={m.UiRendered} " +
            $"scan_running={m.ScanRunning} mode={m.RuntimeMode}");
        _log.Performance(
            $"UI_PIPELINE requests={m.UiRequests} " +
            $"dispatcher_enqueued={m.DispatcherEnqueued} " +
            $"rendered={m.PipelineRendered} coalesced={coalesced} " +
            $"frame_queue_depth={m.QueueDepth} frame_queue_high={m.QueueHighWatermark}");
    }

    public void LogEngineUiFrame(
        IReadOnlyList<(string Key, string Status)> before,
        IReadOnlyList<FaultRow> after, Func<FaultRow, string> rowKey,
        TestEnginePresentationSnapshot snapshot, long runtimeGeneration,
        TestViewUiDiagnosticContext ui, double applyMs, double queueDelayMs)
    {
        (int removed, int added, int changed) = CalculateRenderedRowDelta(before, after, rowKey);
        LogUiSnapshot(snapshot.Electrical.FrameSequence, snapshot.Electrical.ScanGeneration,
            snapshot.Rows.Count, "engine", ui, removed, added, changed);
        _log.Performance(
            $"UI_FRAME seq={snapshot.Electrical.FrameSequence} " +
            $"generation={runtimeGeneration}/{snapshot.Electrical.ScanGeneration} " +
            $"product_state={ui.ProductState} probe_state={ui.ProbeState} " +
            $"presentation={ui.PresentationMode} " +
            $"rendered_rows={ui.RenderedRows} added={added} removed={removed} changed={changed} " +
            $"engine_compute_ms={snapshot.Electrical.EngineComputeMilliseconds:0.###} " +
            $"row_diff_ms={snapshot.RowBuildMilliseconds:0.###} " +
            $"queue_delay_ms={Math.Max(0, queueDelayMs):0.###} apply_ms={applyMs:0.###}");
    }

    public void LogUiSnapshot(long frameSequence, long scanGeneration, int sourceRows,
        string source, TestViewUiDiagnosticContext ui, int removed = 0, int added = 0, int changed = 0)
    {
        _log.Performance(
            $"UI_SNAPSHOT seq={frameSequence} generation={scanGeneration} " +
            $"product_state={ui.ProductState} probe_state={ui.ProbeState} " +
            $"presentation_mode={ui.PresentationMode} " +
            $"source_rows={sourceRows} rendered_rows={ui.RenderedRows} " +
            $"removed={removed} added={added} changed={changed} source={source}");
    }

    public static void LogProbeLatency(ScanFrame frame, DateTime uiRequestedAt, IReadOnlyList<int> ios)
    {
        DateTime renderedAt = DateTime.Now;
        double rxToVmMs = Math.Max(0, (uiRequestedAt - frame.Timestamp).TotalMilliseconds);
        double vmToUiMs = Math.Max(0, (renderedAt - uiRequestedAt).TotalMilliseconds);
        string state = ios.Count == 0
            ? "RELEASE"
            : $"TOUCH {string.Join(", ", ios.Select(io => $"IO{io}"))}";

        AsyncFileLogService.Current.Performance(
            $"PROBE_LATENCY {state}; RX->VM={rxToVmMs:0.0} ms; VM->UI={vmToUiMs:0.0} ms; seq={frame.Sequence}",
            AppLogLevel.Normal);
    }

    public void WriteProductEvidenceLog(
        ScanFrame frame,
        ProductEvidenceSnapshot evidence)
    {
        string signature =
            $"{evidence.RawActiveIoCount}|{evidence.ExpectedConnectedCount}|" +
            $"{evidence.WrongCandidateCount}|{evidence.WrongConfirmedCount}|" +
            $"{evidence.ShortCandidateCount}|{evidence.ShortConfirmedCount}|" +
            $"{evidence.ProbeEvidence}|{evidence.ValidProductEvidence}|" +
            $"{evidence.State}|{evidence.Reason}";
        if (string.Equals(signature, _lastProductEvidenceSignature, StringComparison.Ordinal))
            return;

        _lastProductEvidenceSignature = signature;
        _log.Performance(
            $"PRODUCT_EVIDENCE seq={frame.Sequence} generation={frame.ScanGeneration} " +
            $"raw_active_count={evidence.RawActiveIoCount} " +
            $"expected_connected={evidence.ExpectedConnectedCount} " +
            $"wrong_candidates={evidence.WrongCandidateCount} " +
            $"wrong_confirmed={evidence.WrongConfirmedCount} " +
            $"short_candidates={evidence.ShortCandidateCount} " +
            $"short_confirmed={evidence.ShortConfirmedCount} " +
            $"probe_evidence={evidence.ProbeEvidence} " +
            $"valid_product_evidence={evidence.ValidProductEvidence} " +
            $"state={evidence.State} reason={evidence.Reason}");
    }

    private static (int Removed, int Added, int Changed) CalculateRenderedRowDelta(
        IReadOnlyList<(string Key, string Status)> before,
        IReadOnlyList<FaultRow> after, Func<FaultRow, string> rowKey)
    {
        Dictionary<string, List<string>> beforeByKey = before
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Status).OrderBy(value => value, StringComparer.Ordinal).ToList(),
                StringComparer.Ordinal);
        Dictionary<string, List<string>> afterByKey = after
            .GroupBy(rowKey, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Status).OrderBy(value => value, StringComparer.Ordinal).ToList(),
                StringComparer.Ordinal);

        int removed = 0;
        int added = 0;
        int contentChanged = 0;
        foreach (string key in beforeByKey.Keys.Union(afterByKey.Keys, StringComparer.Ordinal))
        {
            List<string> oldValues = beforeByKey.GetValueOrDefault(key) ?? [];
            List<string> newValues = afterByKey.GetValueOrDefault(key) ?? [];
            int shared = Math.Min(oldValues.Count, newValues.Count);
            removed += oldValues.Count - shared;
            added += newValues.Count - shared;
            for (int index = 0; index < shared; index++)
            {
                if (!string.Equals(oldValues[index], newValues[index], StringComparison.Ordinal))
                    contentChanged++;
            }
        }

        return (removed, added, removed + added + contentChanged);
    }

}

public readonly record struct TestViewUiDiagnosticContext(
    string ProductState, string ProbeState, string PresentationMode, int RenderedRows);

public readonly record struct TestViewScanMetrics(
    long Received, long Processed, long Dropped, long ProbeRouted, long EngineProcessed,
    long UiScheduled, long UiRendered, long UiRequests, long DispatcherEnqueued, long PipelineRendered,
    bool ScanRunning, string RuntimeMode, long QueueDepth, long QueueHighWatermark);
