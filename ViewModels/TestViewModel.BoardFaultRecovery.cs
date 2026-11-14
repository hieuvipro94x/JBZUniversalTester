using System.IO;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;

namespace JBZUniversalTester.ViewModels;

public sealed partial class TestViewModel
{
    private Task<bool> _deviceFaultPersistenceTask = Task.FromResult(true);
    private int _boardFaultDataSecured;
    private BoardFaultCheckpoint? _startupBoardRecovery;
    private int _boardRestartRemovalRequired, _boardRestartEmptyFrames;
    private long _boardRestartLastSequence = -1, _boardRestartScanGeneration = -1;
    private string _boardRecoveryNotice = string.Empty;
    public string BoardRecoveryNotice { get => _boardRecoveryNotice; private set => Set(ref _boardRecoveryNotice, value); }
    public bool CanCloseAfterBoardFault => IsDeviceFault && Volatile.Read(ref _boardFaultDataSecured) != 0;
    public event EventHandler? BoardFaultExitRequested;

    private async Task<bool> PersistBoardFaultAsync(Exception error, string source)
    {
        ProductModel? model = _model;
        if (model is null || string.IsNullOrWhiteSpace(model.SourcePath))
        {
            Volatile.Write(ref _boardFaultDataSecured, 1);
            return true; // Startup/no selected product has no production cycle to recover.
        }
        DateTime at = DateTime.Now;
        string cycle = _activeCycleId;
        var checkpoint = new BoardFaultCheckpoint(cycle, model.SourcePath, model.PartNumber,
            Views.PartSelectionWindow.PartKey(model), at);
        bool inProgress = (_cycleTestStartedAt.HasValue || _productDetectedThisCycle) &&
            Volatile.Read(ref _probeSessionActive) == 0;
        var history = !inProgress ? null : new TestHistoryRecord
        {
            CycleId = cycle, Started = _cycleStartedAt, InstallStartedAt = _cycleStartedAt,
            TestStartedAt = _cycleTestStartedAt, ResultAt = at, Finished = at,
            InspectionType = HistoryInspectionType.Interrupted, Passed = false, Result = "INTERRUPTED",
            FaultCode = "BOARD_DISCONNECTED", FaultType = "MẤT KẾT NỐI BO / 보드 연결 끊김",
            FaultSummary = error.Message, PartName = model.ProductName, PartNumber = model.PartNumber,
            VehicleType = model.VehicleType, Eco = model.Eco, Nco = model.Nco, Alc = model.Alc,
            ModelFile = model.SourcePath, ModelName = model.ModelName, LotNo = 0,
            HtdrvName = ProgramIdentityService.BuildHtdrvName(),
            InspectionTrace = $"회로검사:{(_cycleContinuityCompletedAt.HasValue ? "PASS" : "미완료")}" +
                $" | 저항검사:{(_cycleResistanceCompletedAt.HasValue ? "완료" : "미완료")}" +
                $" | 기밀검사:{(_cycleWaterProofCompletedAt.HasValue ? _cycleWaterProofSummary : "미완료")}" +
                $" | 검사 중단: 보드 연결 끊김 [{source}] State={State}",
            PrintStatus = LabelPrintStatus.NotRequested.ToString()
        };
        bool journalSaved = false;
        try
        {
            ProductionResultCommitRequest? request = null;
            await InvokeUiAsync(() =>
            {
                if (history is not null)
                {
                    var faults = CaptureFaultDetails().Append(new FaultDetail
                        { Type = ProductFaultType.SystemDeviceError, Message = error.Message }).ToArray();
                    var measured = Resistance.Where(item => (item.ValueOhm.HasValue || item.IsOpen) &&
                        (item.ValueOhm is null || double.IsFinite(item.ValueOhm.Value))).ToArray();
                    history.OpenCount = faults.Count(item => item.Type == ProductFaultType.OpenCircuit);
                    history.WrongCount = faults.Count(item => item.Type == ProductFaultType.WrongWiring);
                    history.ShortCount = faults.Count(item => item.Type == ProductFaultType.ShortCircuit);
                    history.FaultDetailsJson = System.Text.Json.JsonSerializer.Serialize(faults);
                    history.Resistance = string.Join("; ", measured.Select(item => $"{item.Name}={item.Display}"));
                    request = ProductionResultCommitRequest.Capture(history, model, _productionSettings,
                        faults, measured, _lastWaterProofMeasurements, ProgramIdentityService.VersionText);
                }
            });
            var recovery = new BoardFaultRecovery(checkpoint, request);
            string database = ResolveHistoryDatabasePath(_productionSettings);
            try { await Task.Run(() => BoardFaultRecoveryJournal.Write(database, recovery)); journalSaved = true; }
            catch (Exception ex) { AsyncFileLogService.Current.Error($"BOARD_FAULT journal write failed: {ex}"); }
            // A result already being committed owns this CycleId. It must win over
            // an interruption snapshot, so completed PASS/FAIL can never be rewritten.
            try { await Task.WhenAll(_productionResultCommitTask, _leakFailureCommitTask); }
            catch (Exception ex) { AsyncFileLogService.Current.Error($"BOARD_FAULT pending result failed: {ex}"); }
            await ProductionPersistence.SaveBoardFaultRecoveryAsync(recovery);
            Volatile.Write(ref _boardFaultDataSecured, 1);
            try { await Task.Run(() => BoardFaultRecoveryJournal.Clear(database)); }
            catch (Exception ex) { AsyncFileLogService.Current.Error($"BOARD_FAULT journal retained for idempotent replay: {ex}"); }
            AddLog($"BOARD_FAULT saved cycle={cycle}; product={Path.GetFileName(model.SourcePath)}; interrupted={inProgress}");
            return true;
        }
        catch (Exception ex)
        {
            AsyncFileLogService.Current.Error($"BOARD_FAULT save failed; durable_journal={journalSaved}: {ex}");
            if (journalSaved) Volatile.Write(ref _boardFaultDataSecured, 1);
            return journalSaved;
        }
    }

    private async Task CompleteBoardFaultExitAsync()
    {
        if (!await _deviceFaultPersistenceTask)
        {
            await InvokeUiAsync(() => BoardRecoveryNotice = "CHƯA LƯU ĐƯỢC LƯỢT GIÁN ĐOẠN. Kiểm tra ổ đĩa/database trước khi thoát.");
            return;
        }
        await _deviceFaultHardwareLockTask;
        await InvokeUiAsync(() => BoardFaultExitRequested?.Invoke(this, EventArgs.Empty));
    }

    private bool HandleBoardRestartRemoval(ScanFrame frame)
    {
        if (Volatile.Read(ref _boardRestartRemovalRequired) == 0) return false;
        if (!frame.Complete || frame.UnknownBytes > 0 || !frame.TerminatorKnown || frame.Mode != BoardScanMode.Production) return true;
        if (frame.ScanGeneration > 0 && _boardRestartScanGeneration > frame.ScanGeneration) return true;
        if (_boardRestartScanGeneration != frame.ScanGeneration)
        {
            _boardRestartScanGeneration = frame.ScanGeneration;
            _boardRestartLastSequence = -1;
            _boardRestartEmptyFrames = 0;
        }
        if (frame.Sequence <= _boardRestartLastSequence) return true;
        _boardRestartLastSequence = frame.Sequence;
        bool occupied = frame.ActiveIo.Any(io => _pinsByIoLookup.Contains(io));
        _boardRestartEmptyFrames = occupied ? 0 : _boardRestartEmptyFrames + 1;
        if (_boardRestartEmptyFrames < ProductPresenceStabilityGate.ConfirmationFrames)
        {
            InvokeUi(() => { if (!IsDeviceFault) State = "THÁO SẢN PHẨM"; });
            return true;
        }
        Volatile.Write(ref _boardRestartRemovalRequired, 0);
        InvokeUi(() => State = ReadyStateForCurrentModel());
        return true; // Only a later fresh installation can enter the test engine.
    }
}
