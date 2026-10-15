using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using Microsoft.Data.Sqlite;
using JBZUniversalTester.ViewModels;
using System.Reflection;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestBcwThtAutomaticPassLabel()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        string root = Path.Combine(Path.GetTempPath(), "JBZ-BCW-ThtPass-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "FILENAME-IS-NOT-THE-PART-NUMBER.tht");
            const string text = "파트번호\t파트명\tBCW_VEHICLE_CODE\tBCW_POSITION\n" +
                "OE366-33050\tBCW PRODUCTION\t09\tLH\n\n" +
                "번 호\t커넥터\t핀 수\n1\tCN1\t2\n\n" +
                "커넥터\t선이름\tI/O\t핀번호\nCN1\tW1\t1\t1\nCN1\tW1\t2\t2\n\n" +
                "선이름\t선연결\t굵기\t색깔\nW1\t\t0.5\tR";
            File.WriteAllBytes(path, BuildMinimalThtFile(text));
            var model = new ThtModelParser().Load(path);
            Assert(model.PartNumber == "OE366-33050" && model.LabelVariables["BCW_VEHICLE_CODE"] == "09" &&
                   model.LabelVariables["BCW_POSITION"] == "LH", "BCW data is read through the real THT parser from the selected product");
            var settings = new LabelSettings { TemplateType = "BCW", BcwVehicleCode = "99", BcwPosition = "RH",
                RawDestination = Path.Combine(root, "printer.epl") };
            var history = new TestHistoryRecord { CycleId = "auto-pass-bcw", Passed = true, Result = "PASS",
                Started = new DateTime(2026,10,14), Finished = new DateTime(2026,10,14,10,0,0),
                LotNo = 2001, PartNumber = model.PartNumber, PartName = model.ProductName, ModelFile = model.SourcePath,
                ModelName = model.ModelName, LabelTemplateType = "BCW" };
            var capture = typeof(TestViewModel).GetMethod("TryCapturePassLabel", BindingFlags.NonPublic|BindingFlags.Static)!;
            object?[] arguments = [history, model, settings, null, null, null];
            Assert((bool)capture.Invoke(null, arguments)!, "Production PASS label capture accepts a THT model with BCW selected");
            var request = (LabelPrintRequest)arguments[3]!;
            Assert(request.Data.PartNumber == "OE366-33050" && request.Data.BcwVehicleCode == "09" && request.Data.BcwPosition == "LH",
                "Automatic BCW uses THT values before machine defaults, independent of the manual-print picker");
            var gate = typeof(TestViewModel).GetMethod("ShouldAutoPrintLabel", BindingFlags.NonPublic|BindingFlags.Static)!;
            Assert((bool)gate.Invoke(null,[true,true])! && !(bool)gate.Invoke(null,[false,true])! && !(bool)gate.Invoke(null,[true,false])!,
                "Automatic BCW follows the existing PASS plus enabled-print gate; FAIL/disabled never auto-print");
            history.LabelPayload = request.Payload;
            var store = new TestHistoryStore(Path.Combine(root,"history.db"));
            long id = store.Add(history);
            model.PartNumber = "NEXT-MODEL";
            settings.BcwVehicleCode = "88";
            settings.BcwPosition = "RH";
            var printer = new LabelPrintService(new BcwSerialStore(Path.Combine(root,"bcw.db")), () => history.Finished);
            try
            {
                var result = printer.PrintPassLabelAsync(request, beforeBcwSend: actual =>
                { store.SaveBcwLabelSnapshot(id,history.CycleId,actual); return Task.CompletedTask; }).GetAwaiter().GetResult();
                Assert(result.Printed && result.EffectiveRequest!.Data.Barcode == "2628709L00001" &&
                       File.ReadAllText(Path.Combine(root,"printer.epl")).Contains("\"OE366-33050\"", StringComparison.Ordinal),
                    "Captured THT production snapshot prints the correct BCW even if model/settings change afterwards");
                store.UpdateLabelPrintOutcome(id,history.CycleId,LabelPrintStatus.Printed,history.Finished,result.Message,result.EffectiveRequest!.Data.Barcode);
                using var connection = new SqliteConnection($"Data Source={store.DatabasePath};Pooling=False");connection.Open();
                using var query = connection.CreateCommand();
                query.CommandText = "SELECT Barcode,LabelSerial,Lot FROM Tests WHERE CycleId='auto-pass-bcw';";
                using var reader = query.ExecuteReader();
                Assert(reader.Read() && reader.GetString(0)=="2628709L00001" && reader.GetString(1)=="2628709L00001" && reader.GetInt64(2)==2001,
                    "BCW automatic print retains the same QR in History without changing the production LOT");
            }
            finally { printer.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, recursive:true); }
    }

    private static LabelPrintRequest BcwRequest(string cycle, string output = "") => LabelPrintRequest.Capture(
        new TestHistoryRecord { CycleId = cycle, Finished = new DateTime(2026,10,14), LotNo = 1 },
        new ProductModel { PartNumber = "OE366-33050", ProductName = "BCW SAMPLE", LabelTemplate = new LabelTemplateDefinition(RawTemplate: "OLD MODEL TEMPLATE") },
        new LabelSettings { TemplateType = "BCW", BcwVehicleCode = "09", BcwPosition = "LH", RawDestination = output });

    private static void TestBcwJulianAndPayload()
    {
        Assert(BcwLabelService.Qr(new DateTime(2026,10,14), "09", "LH", 1) == "2628709L00001", "BCW exact Julian QR example");
        Assert(BcwLabelService.Qr(new DateTime(2028,2,29), "09", "RH", 1) == "2806009R00001", "BCW leap day and RH");
        Assert(BcwLabelService.Qr(new DateTime(2026,12,31), "09", "LH", 99999) == "2636509L99999", "BCW final day and five-digit upper bound");
        foreach (long serial in new[] { 0L, 100000L })
        {
            bool rejected = false;
            try { BcwLabelService.Qr(DateTime.Now, "09", "LH", serial); } catch (InvalidDataException) { rejected = true; }
            Assert(rejected, "BCW rejects serial overflow/zero without truncation");
        }
        var request = BcwRequest("BCW-vector");
        Assert(request.Payload.Contains("b245,16,Q,h5,\"2628709L00001\"", StringComparison.Ordinal) &&
               request.Payload.Contains("A80,80,0,2,1,1,N,\"2628709L00001\"", StringComparison.Ordinal), "BCW QR and printed text identical");
        Assert(request.Payload.Contains("A110,5,0,3,2,2,N,\"LH\"", StringComparison.Ordinal) &&
               request.Payload.Contains("A85,56,0,2,1,1,N,\"OE366-33050\"", StringComparison.Ordinal), "BCW preserves original positions and fonts");
        Assert(!request.Payload.Contains("FK", StringComparison.Ordinal) && !request.Payload.Contains("FS", StringComparison.Ordinal) &&
               !request.Payload.Contains("V03", StringComparison.Ordinal) && request.Payload.EndsWith("P1\r\n", StringComparison.Ordinal), "BCW direct EPL never deletes/rewrites flash forms or supplies fake fourth field");
        Assert(request.Profile.Id == "BCW" && !request.Payload.Contains("OLD MODEL", StringComparison.Ordinal), "Explicit BCW selection overrides old THT label payload only for BCW");
        var product = new ProductModel { PartNumber = "OE366-33050" };
        product.LabelVariables["BCW_VEHICLE_CODE"] = "10";
        product.LabelVariables["BCW_POSITION"] = "RH";
        Assert(BcwLabelService.Vehicle(product, new LabelSettings()) == "10" &&
               BcwLabelService.Position(product, new LabelSettings()) == "RH", "Explicit per-product BCW variables override machine defaults");
        string xaml = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "Views", "ProductionSettingsPage.xaml"));
        Assert(xaml.Contains("Tag=\"BCW\"", StringComparison.Ordinal), "BCW appears in existing label selector");
        string root = Path.Combine(Path.GetTempPath(),"JBZ-BCW-Cfg-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root,"station.cfg");
            var settings = new ProductionSettings { Label = new LabelSettings
                { TemplateType="BCW",BcwVehicleCode="09",BcwPosition="RH" } };
            ProductionConfigService.SaveLegacyCfg(settings,path);
            var load = typeof(ProductionConfigService).GetMethod("LoadEnglishCfg",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!;
            var restored = (ProductionSettings)load.Invoke(null,[path])!;
            Assert(restored.Label.TemplateType=="BCW" && restored.Label.BcwVehicleCode=="09" && restored.Label.BcwPosition=="RH",
                "BCW configuration persists through the existing CFG pipeline");
        }
        finally { Directory.Delete(root,recursive:true); }
    }

    private static void TestBcwPersistentSerialAndTransport()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZ-BCW-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new BcwSerialStore(Path.Combine(root, "bcw.db"));
            DateTime now = new(2026,10,14,23,59,59);
            var printer = new LabelPrintService(store, () => now);
            try
            {
                var failed = BcwRequest("fail");
                var failure = printer.PrintPassLabelAsync(failed).GetAwaiter().GetResult();
                Assert(!failure.Printed && store.Peek(now,"09","LH") == 1, "Failed print never advances BCW serial");
                bool blocked = false;
                try { printer.PrintPassLabelAsync(BcwRequest("other")).GetAwaiter().GetResult(); }
                catch (InvalidOperationException) { blocked = true; }
                Assert(blocked, "Uncertain/failed serial cannot be reassigned to another product");
                var restarted = new BcwSerialStore(Path.Combine(root,"bcw.db"));
                var pending = restarted.Find("fail")!;
                Assert(pending.Data.Barcode == "2628709L00001", "Restart retains immutable pending QR");
                bool transportThrew = false;
                try { printer.PrintPassLabelAsync(pending with { RawDestination = root }, explicitReprint:true).GetAwaiter().GetResult(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { transportThrew = true; }
                Assert(transportThrew && store.Peek(now,"09","LH")==1,
                    "Thrown transport error retains the same BCW serial and never advances the counter");
                using (var cancel = new CancellationTokenSource())
                {
                    cancel.Cancel();
                    bool cancelled = false;
                    try { printer.PrintPassLabelAsync(BcwRequest("cancelled"),cancel.Token).GetAwaiter().GetResult(); }
                    catch(OperationCanceledException) { cancelled = true; }
                    Assert(cancelled && store.Find("cancelled") is null,"Cancellation before sending never reserves a BCW serial");
                }
                string output = Path.Combine(root,"printer.epl");
                var retry = printer.PrintPassLabelAsync(pending with { RawDestination = output }, explicitReprint:true).GetAwaiter().GetResult();
                Assert(retry.Printed && retry.EffectiveRequest!.Data.BcwSerial == 1 && store.Peek(now,"09","LH") == 2, "Explicit retry uses same serial and increments only after send");
                string bytes = File.ReadAllText(output);
                var duplicate = printer.PrintPassLabelAsync(pending with { RawDestination = output }).GetAwaiter().GetResult();
                Assert(duplicate.Printed && store.Peek(now,"09","LH") == 2 && File.ReadAllText(output) == bytes, "Automatic repeat of sent cycle does not allocate another serial");
                now = new DateTime(2026,10,15);
                var reprint = printer.PrintPassLabelAsync(pending with { RawDestination = output }, explicitReprint:true).GetAwaiter().GetResult();
                Assert(reprint.EffectiveRequest!.Data.Barcode == "2628709L00001" && File.ReadAllText(output) == bytes, "Next-day reprint retains old day, original QR and exact payload");
                var nextDay = printer.PrintPassLabelAsync(BcwRequest("new-day", output)).GetAwaiter().GetResult();
                Assert(nextDay.EffectiveRequest!.Data.Barcode == "2628809L00001", "Actual local send time, not capture/test time, controls fresh day sequence");
                var requests = Enumerable.Range(0,12).Select(i => printer.PrintPassLabelAsync(BcwRequest("parallel-"+i,output))).ToArray();
                var results = Task.WhenAll(requests).GetAwaiter().GetResult();
                Assert(results.All(r=>r.Printed) && results.Select(r=>r.EffectiveRequest!.Data.Barcode).Distinct().Count()==12,
                    "Concurrent print requests serialize and receive distinct QR serials");
            }
            finally { printer.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            using var connection = new SqliteConnection($"Data Source={Path.Combine(root,"bcw.db")};Pooling=False");
            connection.Open();
            using var check = connection.CreateCommand(); check.CommandText = "PRAGMA integrity_check;";
            Assert((string)check.ExecuteScalar()! == "ok", "BCW journal SQLite integrity remains ok");
            string previous = Path.Combine(root,"V2026.09.236");
            string upgraded = Path.Combine(root,"V2026.09.237");
            Directory.CreateDirectory(Path.Combine(previous,"Data"));
            File.Copy(Path.Combine(root,"bcw.db"),Path.Combine(previous,"Data","BCW-labels.db"));
            ProductionDataUpgradeService.MigrateMissingProductionData(upgraded,[previous]);
            var inherited = new BcwSerialStore(Path.Combine(upgraded,"Data","BCW-labels.db"));
            Assert(inherited.Peek(new DateTime(2026,10,15),"09","LH")==14,
                "Publishing a new version inherits BCW serial history without resetting same-day serials");
        }
        finally { Directory.Delete(root,recursive:true); }
    }

    private static void TestBcwHistorySnapshotPreservesRows()
    {
        string root = Path.Combine(Path.GetTempPath(),"JBZ-BCW-History-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new TestHistoryStore(Path.Combine(root,"history.db"));
            var row = new TestHistoryRecord { CycleId="bcw-history", Started=new DateTime(2026,10,14), Finished=new DateTime(2026,10,14),
                PartNumber="OE366-33050", ModelName="BCW", Passed=true, Result="PASS", LotNo=2001, LabelTemplateType="BCW" };
            long id = store.Add(row);
            store.Add(new TestHistoryRecord { CycleId="old-label", Started=row.Started, Finished=row.Finished,
                PartNumber="OLD-PART", Passed=true, Result="PASS",LotNo=123,LabelTemplateType="TEM_BE_QRCODE",LabelPayload="OLD-PAYLOAD" });
            var request = BcwRequest(row.CycleId);
            store.SaveBcwLabelSnapshot(id,row.CycleId,request);
            using var connection = new SqliteConnection($"Data Source={store.DatabasePath};Pooling=False");connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText="SELECT COUNT(*),MAX(Lot),MAX(LabelSerial),MAX(LabelPayload) FROM Tests WHERE CycleId='bcw-history';";
            using(var reader=cmd.ExecuteReader())
            {
                Assert(reader.Read() && reader.GetInt64(0)==1 && reader.GetInt64(1)==2001 && reader.GetString(2)==request.Data.Barcode &&
                       reader.GetString(3)==request.Payload,"BCW updates only label snapshot, preserving production LOT/history rows");
            }
            cmd.CommandText="SELECT LabelPayload FROM Tests WHERE CycleId='old-label';";
            Assert((string)cmd.ExecuteScalar()! == "OLD-PAYLOAD","BCW snapshot does not rewrite old label data");
            cmd.CommandText="SELECT COUNT(*) FROM Tests;";
            Assert(Convert.ToInt64(cmd.ExecuteScalar())==2,"BCW keeps all existing History rows");
            cmd.CommandText="PRAGMA integrity_check;";
            Assert((string)cmd.ExecuteScalar()! == "ok","History SQLite integrity remains ok after BCW snapshot");
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root,recursive:true); }
    }
}
