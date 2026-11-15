using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows.Controls;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;
using JBZUniversalTester.Views;
using Microsoft.Data.Sqlite;

namespace JBZUniversalTester.SelfTests;
internal static partial class Program
{
    private static string CreateMultiPartBcwTht(string root)
    {
        string path = Path.Combine(root, "QL PE 66300-66400-VIET.tht");
        string text = "파트번호\t파트명\tECO\tALC\n" +
            "D9958-66300\tQL PE\tD9PL\tLH\nD9958-66200\tQL\tD9P\tRH\nD9958-66400\tQL PE\tD9PR\tRH\n\n" +
            "번 호\t커넥터\t핀 수\n1\tCN1\t2\n\n" +
            "커넥터\t선이름\tI/O\t핀번호\nCN1\tW1\t1\t1\nCN1\tW1\t2\t2\n\n" +
            "선이름\t선연결\t굵기\t색깔\nW1\t\t0.5\tR";
        File.WriteAllBytes(path, BuildMinimalThtFile(text));
        return path;
    }

    private static void TestBcwThtEcoIdentityAndSerials()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZ-bcw-eco-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var models = new ThtModelParser().LoadAll(CreateMultiPartBcwTht(root));
            var date = new DateTime(2026, 11, 15, 10, 0, 0);
            var settings = new LabelSettings { TemplateType="BCW", BcwVehicleCode="99", BcwPosition="RH" };
            LabelPrintRequest Capture(ProductModel model, string cycle) => LabelPrintRequest.Capture(
                new TestHistoryRecord { CycleId=cycle, Finished=date, Passed=true, LotNo=7000 }, model, settings);
            string[] codes = ["26319D9PL00001", "26319D9P00001", "26319D9PR00001"];
            for (int i=0;i<3;i++)
            {
                var request=Capture(models[i],"tht-"+i);
                Assert(request.Data.Barcode==codes[i] && request.Data.BcwUsesThtEco &&
                    request.Payload.Contains("b210,16,Q,h4,\""+codes[i]+"\"",StringComparison.Ordinal) &&
                    request.Payload.Contains("\""+models[i].Alc+"\"",StringComparison.Ordinal),
                    "Actual multi-part THT fields create full-ECO QR, ALC position and h4 without appending L/R");
                var history=new TestHistoryRecord { CycleId=request.CycleId, PartNumber=request.Data.PartNumber,
                    PartName=request.Data.PartName, Eco=request.Data.Eco, Alc=request.Data.Alc, Finished=date,
                    LabelPayload=BcwLabelService.Bind(request,date,47).Payload };
                var restored=BcwLabelService.RestoreUnsent(history,settings);
                Assert(restored.Payload==history.LabelPayload && restored.Data.Barcode==codes[i][..^5]+"00047" &&
                    restored.Data.BcwSerial==47 && restored.Data.BcwUsesThtEco,
                    "Unsent full-ECO commands restore their exact QR, serial and immutable payload");
            }
            var store=new BcwSerialStore(Path.Combine(root,"journal.db"));
            var first=store.Prepare(Capture(models[0],"eco-first"),date,false).Request;
            store.Outcome(first.CycleId,"Sent","");
            var sameEco=Capture(new ProductModel { PartNumber="OTHER", Eco="D9PL", Alc="RH" },"eco-same");
            Assert(store.Prepare(sameEco,date,false).Request.Data.Barcode=="26319D9PL00002",
                "Same ECO shares serials across parts/positions, because ALC is absent from the QR");
            store.Outcome("eco-same","Sent","");
            var old=BcwRequest("old-13-char");
            var oldSent=store.Prepare(old,date,false).Request; store.Outcome(oldSent.CycleId,"Sent","");
            var rawSamePrefix=Capture(new ProductModel {PartNumber="RAW",Eco="09L",Alc="RH"},"same-old-prefix");
            Assert(store.Prepare(rawSamePrefix,date,false).Request.Data.BcwSerial==2,
                "A full ECO identical to a legacy QR prefix cannot reuse the existing serial 00001");
            var oldHistory=new TestHistoryRecord {CycleId=oldSent.CycleId,PartNumber=oldSent.Data.PartNumber,
                Finished=date,LabelPayload=oldSent.Payload};
            var oldRestored=BcwLabelService.RestoreUnsent(oldHistory,settings);
            Assert(!oldRestored.Data.BcwUsesThtEco && oldRestored.Payload==oldSent.Payload && oldRestored.Data.Barcode==oldSent.Data.Barcode,
                "Existing 13-character legacy jobs still restore without a format change");
            string json=JsonSerializer.Serialize(oldSent).Replace(",\"BcwUsesThtEco\":false","");
            Assert(!JsonSerializer.Deserialize<LabelPrintRequest>(json)!.Data.BcwUsesThtEco,
                "Old journal JSON without the new optional flag retains legacy identity semantics");
            bool rejected=false;
            try { Capture(new ProductModel {PartNumber="BAD",Eco="D9 PL",Alc="LH"},"bad"); } catch(InvalidDataException){rejected=true;}
            Assert(rejected,"Invalid ECO is reported instead of silently using the unrelated settings code");
            using var connection=new SqliteConnection("Data Source="+Path.Combine(root,"journal.db"));
            connection.Open();using var command=connection.CreateCommand();command.CommandText="PRAGMA integrity_check;";
            Assert((string?)command.ExecuteScalar()=="ok","Mixed legacy/ECO serial reservations keep SQLite integrity ok");
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root,true); }
    }

    private static void TestManualLabelPartSelection()
    {
        OnSettingsDispatcher(async () =>
        {
            string root=Path.Combine(Path.GetTempPath(),"JBZ-label-parts-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            object? previous=AppContext.GetData("JBZUniversalTester.SelfTest.DatabaseFile");
            AppContext.SetData("JBZUniversalTester.SelfTest.DatabaseFile",Path.Combine(root,"history.db"));
            ProductionSettingsPage? page=null;
            MainViewModel? main=null;
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            try
            {
                _=new TestHistoryStore(RuntimePaths.DatabaseFile);
                string path=CreateMultiPartBcwTht(root); byte[] original=File.ReadAllBytes(path);
                var settings=new ProductionSettings {LastThtPath=path};settings.Label.TemplateType="BCW";
                settings.Label.RawDestination=Path.Combine(root,"batch.epl");
                main=new MainViewModel(new AppSettings(),settings);
                var vm=new ProductionSettingsViewModel(main.Test,initialSettings:settings,saveSettings:_=>{});
                page=new ProductionSettingsPage(main,vm);
                var messages=new List<string>();
                typeof(ProductionSettingsPage).GetProperty("MessagePresenter",flags)!.SetValue(page,
                    (Action<string,string,System.Windows.MessageBoxImage>)((message,_,_)=>messages.Add(message)));
                var files=(ComboBox)page.FindName("LabelPrintModelComboBox");
                var parts=(ComboBox)page.FindName("LabelPrintPartComboBox");
                var entry=new ProductFileEntry("BCW",root,path);files.ItemsSource=new[]{entry};files.SelectedItem=entry;
                await (Task)typeof(ProductionSettingsPage).GetField("_labelPartsLoadTask",flags)!.GetValue(page)!;
                Assert(parts.Items.Count==3,"Manual label chooser exposes every product row in the selected THT");
                var build=typeof(ProductionSettingsPage).GetMethod("BuildSettingsLabelRequestAsync",flags)!;
                for(int index=0;index<3;index++)
                {
                    parts.SelectedIndex=index;
                    var request=await (Task<LabelPrintRequest>)build.Invoke(page,["PREVIEW",1L,null,null,null])!;
                    Assert(request.Data.PartNumber==new[]{"D9958-66300","D9958-66200","D9958-66400"}[index] &&
                        request.Data.Eco==new[]{"D9PL","D9P","D9PR"}[index],
                        "Preview/test/batch request builder uses the chosen row, never the first row by default");
                }
                parts.SelectedIndex=0;
                await (Task)typeof(ProductionSettingsPage).GetField("_bcwSummaryTask",flags)!.GetValue(page)!;
                ((TextBox)page.FindName("BcwQuantityTextBox")).Text="2";
                ((ComboBox)page.FindName("BatchPrintDelayMsComboBox")).SelectedValue="500";
                typeof(ProductionSettingsPage).GetMethod("BatchPrintLabel_Click",flags)!.Invoke(page,[page,new System.Windows.RoutedEventArgs()]);
                string ReadRawJob()
                {
                    if(!File.Exists(settings.Label.RawDestination)) return "";
                    using var stream=new FileStream(settings.Label.RawDestination,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);
                    using var reader=new StreamReader(stream);return reader.ReadToEnd();
                }
                for(int tick=0;tick<400 && (!File.Exists(settings.Label.RawDestination) ||
                    !ReadRawJob().Contains("D9PL00001",StringComparison.Ordinal));tick++) await Task.Delay(5);
                string firstSent=ReadRawJob();
                Assert(firstSent.Contains("D9PL00001",StringComparison.Ordinal) && !parts.IsEnabled,
                    "The actual batch command sends its first RAW job and locks the selected THT row");
                settings.Label.BcwVehicleCode="88";settings.Label.BcwPosition="RH";
                BuiltInLabelTemplateStore.SaveOverride(settings.Label,"BCW",
                    BuiltInLabelTemplateStore.Load(BuiltInLabelTemplateStore.ReferenceFor("BCW")).Replace("b210,16", "b999,99",StringComparison.Ordinal));
                for(int tick=0;tick<600 && (int)typeof(ProductionSettingsPage).GetField("_batchPrintInProgress",flags)!.GetValue(page)! != 0;tick++) await Task.Delay(5);
                var sent=ReadRawJob();
                Assert(firstSent.Contains("b210,16,Q,h4,",StringComparison.Ordinal) &&
                    sent.Contains("b210,16,Q,h4,",StringComparison.Ordinal) && !sent.Contains("b999,99",StringComparison.Ordinal) &&
                    sent.Contains("D9PL00002",StringComparison.Ordinal) &&
                    ScalarInt(RuntimePaths.DatabaseFile,"SELECT COUNT(*) FROM BcwPrints WHERE State='Sent';")==2 &&
                    messages.Any(m=>m.Contains("Đã gửi 2 tem BCW",StringComparison.Ordinal)),
                    "Two actual batch sends retain the selected THT identity and captured template despite live settings edits");
                Assert(File.ReadAllBytes(path).SequenceEqual(original),"Selecting and previewing label rows never edits the THT");
                Assert(ScalarInt(RuntimePaths.DatabaseFile,"SELECT COUNT(*) FROM Tests;")==0,
                    "Manual BCW previews do not create test History, PASS/FAIL or LOT records");
            }
            finally
            {
                page?.ReleasePageResources();
                if(main is not null) await main.ShutdownAsync();
                AppContext.SetData("JBZUniversalTester.SelfTest.DatabaseFile",previous);
                SqliteConnection.ClearAllPools(); Directory.Delete(root,true);
            }
        });
    }
}
