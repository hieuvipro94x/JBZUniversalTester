using System.Diagnostics;
using System.Reflection;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestLargeProbeImmediateTransitions()
    {
        var decoder = new BoardIoDecoder();
        decoder.ConfigureCapacity(BoardCapacity.Create(10));
        decoder.ConfigureMode(BoardScanMode.Probe);
        byte[] Word(int io, bool active)
        {
            int address = io - 1;
            return [(byte)((active ? BoardIoDecoder.TargetBase : BoardIoDecoder.SourceBase) +
                address / BoardAddressMapper.IoPerProtocolBank),
                (byte)(address % BoardAddressMapper.IoPerProtocolBank)];
        }
        ScanFrame touch = decoder.Feed(Word(600, true)).Single();
        Assert(touch.ActiveIo.SetEquals([600]) && !touch.Complete,
            "640-IO Probe reports IO600 touch before the terminator");
        Assert(decoder.Feed(Word(600, true)).Count == 0,
            "An unchanged explicit Probe contact cannot enqueue another UI update");
        Assert(decoder.Feed([0xC0, 0]).Single().ActiveIo.SetEquals([600]),
            "Probe C0 snapshot retains its current contact");
        var emptyPrefix = Enumerable.Range(1, 550).SelectMany(io => Word(io, false)).ToArray();
        Assert(decoder.Feed(emptyPrefix).Count == 0 &&
               decoder.Feed([0xC0, 0]).Single().ActiveIo.SetEquals([600]),
            "Hundreds of unrelated normal IO words in the next scan cannot create a false release");
        ScanFrame release = decoder.Feed(Word(600, false)).Single();
        Assert(release.ActiveIo.Count == 0 && !release.Complete,
            "IO600 releases on its own OFF word without waiting for C0 or another scan");

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var vm = CreateTestViewModel(new ProductionSettings { MasterFaultRequiredCount = 0 }, out FakeBoard board);
        typeof(TestViewModel).GetField("_runtimeMode", flags)!.SetValue(vm, 2);
        typeof(TestViewModel).GetField("_probeSessionActive", flags)!.SetValue(vm, 1);
        int commands = board.Commands.Count;
        board.Publish(touch);
        Assert(vm.HasInlineProbeContacts && vm.ProbeContacts.Any(row => row.Io == 600),
            "The first authoritative Probe ON event immediately updates TestView without a second frame");
        board.Publish(release);
        Assert(!vm.HasInlineProbeContacts && vm.Total == 0 && vm.Fail == 0 &&
               board.Commands.Count == commands && vm.ProductionFramesProcessed == 0,
            "Authoritative Probe OFF immediately clears the UI without production evaluation, counting or relay");
        _ = decoder.Feed(Word(600, true));
        decoder.Reset();
        Assert(decoder.Feed([0xC0, 0]).Single().ActiveIo.Count == 0,
            "Transport reset clears the retained Probe contacts");
    }

    private static void TestWrongWiringDelayedRemovalStatus()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        foreach (bool confirmation in new[] { true, false })
        {
            var vm = CreateTestViewModel(new ProductionSettings
                { MasterFaultRequiredCount = 0, WiringFaultConfirmationEnabled = confirmation }, out FakeBoard board);
            void Set(string name, object value) => typeof(TestViewModel).GetField(name, flags)!.SetValue(vm, value);
            Set("_runtimeMode", 1);
            Set("_masterApproved", true);
            Set("_activeCycleId", "WRONG-STATUS");
            Set("_wrongWiringStatusCycleId", "WRONG-STATUS");
            Set("_productionPhase", 4); // Popup/confirmation stays in its existing phase.
            vm.State = FaultDisplayFormatter.OperatorInstruction(ProductFaultType.WrongWiring);
            int commands = board.Commands.Count;
            long now = Stopwatch.GetTimestamp();
            Set("_wrongWiringStatusStartedTimestamp", now);
            Assert(vm.ResultStatusText == "KIỂM TRA LỖI SAI DÂY" && vm.StateBackground == "#C62828",
                "Wrong-wire status names the fault immediately; the removal instruction alone waits five seconds");
            Set("_wrongWiringStatusStartedTimestamp", now - (long)(4.9 * Stopwatch.Frequency));
            Assert(vm.ResultStatusText != "THÁO SẢN PHẨM LỖI", "Removal status cannot appear before five seconds");
            Set("_wrongWiringStatusStartedTimestamp", now - 6 * Stopwatch.Frequency);
            Assert(vm.ResultStatusText == "THÁO SẢN PHẨM LỖI" &&
                   vm.State == "KIỂM TRA LỖI SAI DÂY" && vm.Total == 0 && board.Commands.Count == commands,
                "After five seconds only the status projection changes; popup configuration, fault state and hardware remain untouched");
            Set("_activeCycleId", "NEXT-CYCLE");
            Assert(vm.ResultStatusText != "THÁO SẢN PHẨM LỖI", "Old delayed status cannot leak into another product cycle");
            Set("_activeCycleId", "WRONG-STATUS");
            Set("_productionPhase", 1);
            vm.State = "CHỜ LẮP SẢN PHẨM";
            Assert(vm.ResultStatusText == "LẮP SẢN PHẨM", "Removal/reset makes the delayed fault prompt ineligible");
        }
    }
}
