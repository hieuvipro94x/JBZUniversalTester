using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Xml.Linq;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;
using Microsoft.Data.Sqlite;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void RenderHistoryBarcodePreview(string outputPath)
    {
        OnSettingsDispatcher(async () =>
        {
            XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            XElement template = XDocument.Load(Path.Combine(Environment.CurrentDirectory, "Views", "HistoryPage.xaml"))
                .Descendants(ns + "DataGridTemplateColumn").Single(column => (string?)column.Attribute("Header") == "Mã vạch đầu ra")
                .Descendants(ns + "DataTemplate").Single();
            var rows = new List<TestHistoryRecord>();
            var at = new DateTime(2026, 10, 28, 14, 0, 0);
            var model = new ProductModel { PartNumber = "1200020430", ModelName = "ITEM-DEMO", ProductName = "WIRING ASSY" };
            void Add(string type, long lot, string condition, bool passed = true, bool sent = true, bool print = true)
            {
                var row = new TestHistoryRecord { Started = at.AddSeconds(rows.Count), Finished = at,
                    PartNumber = model.PartNumber, LotNo = lot, Passed = passed, LabelTemplateType = print ? type : "KHÔNG IN TEM",
                    LabelProfile = condition, PrintStatus = sent ? LabelPrintStatus.Printed.ToString() : LabelPrintStatus.Failed.ToString() };
                if (print && passed)
                {
                    LabelPrintRequest request = LabelPrintRequest.Capture(row, model, new LabelSettings
                    { TemplateType = type, PreferSelectedTemplate = true, BcwVehicleCode = "09", BcwPosition = "LH" });
                    row.BarcodeValue = request.Data.Barcode;
                    row.LabelPayload = request.Payload;
                }
                rows.Add(row);
            }
            Add(LabelSettings.LargeTemplate, 2001, "Gửi tem thành công");
            Add(LabelSettings.LargeTemplate, 2002, "Gửi tem thành công");
            Add(LabelSettings.LargeTemplate, 2003, "Gửi tem thành công");
            Add(LabelSettings.LargeSqdzTemplate, 2004, "Gửi tem thành công");
            Add(LabelSettings.SmallTemplate, 2005, "Gửi tem thành công");
            Add(LabelSettings.SmallQrTemplate, 2006, "Gửi tem thành công");
            Add(LabelSettings.BcwTemplate, 2007, "Gửi tem thành công");
            Add(LabelSettings.LargeTemplate, 2008, "Không kết nối máy in", sent: false);
            Add(LabelSettings.LargeTemplate, 2008, "Lỗi thiết bị / sản phẩm FAIL", passed: false, sent: false);
            Add(LabelSettings.LargeTemplate, 2009, "Mã hàng không bật in tem", sent: false, print: false);
            var grid = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false, IsReadOnly = true,
                ItemsSource = rows, RowHeight = 40, ColumnHeaderHeight = 40, HeadersVisibility = DataGridHeadersVisibility.Column,
                FontSize = 14, FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                RowBackground = System.Windows.Media.Brushes.White, AlternatingRowBackground = System.Windows.Media.Brushes.White,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, Background = System.Windows.Media.Brushes.White,
                HorizontalGridLinesBrush = System.Windows.Media.Brushes.LightGray };
            var header = new System.Windows.Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
            header.Setters.Add(new System.Windows.Setter(Control.BackgroundProperty, System.Windows.Media.Brushes.AliceBlue));
            header.Setters.Add(new System.Windows.Setter(Control.FontWeightProperty, System.Windows.FontWeights.SemiBold));
            header.Setters.Add(new System.Windows.Setter(Control.HorizontalContentAlignmentProperty, System.Windows.HorizontalAlignment.Center));
            grid.ColumnHeaderStyle = header;
            foreach (var (caption, path, width) in new[] { ("Ngày", "DateText", 100d), ("Mã hàng", "PartNumber", 160d),
                         ("Loại tem", "LabelTemplateType", 160d), ("Số đếm PASS", "ExportPassCountText", 120d),
                         ("Tình huống minh họa", "LabelProfile", 260d) })
                grid.Columns.Add(new DataGridTextColumn { Header = caption, Binding = new System.Windows.Data.Binding(path), Width = width });
            grid.Columns.Add(new DataGridTemplateColumn { Header = "Mã vạch đầu ra", Width = 390,
                CellTemplate = (DataTemplate)XamlReader.Parse(template.ToString()) });
            var panel = new DockPanel { Background = System.Windows.Media.Brushes.White, Margin = new System.Windows.Thickness(12) };
            var title = new TextBlock { Text = "ĐẦU RA MÃ VẠCH · V2026.09.334", FontSize = 23,
                FontWeight = System.Windows.FontWeights.SemiBold, Margin = new System.Windows.Thickness(0, 0, 0, 14) };
            DockPanel.SetDock(title, Dock.Top); panel.Children.Add(title);
            var note = new TextBlock { Text = "PASS tăng LOTNO một lần. Không kết nối / lỗi máy: cột mã vạch để trống. Dữ liệu tem gốc vẫn được lưu.",
                FontSize = 14, Margin = new System.Windows.Thickness(0, 12, 0, 0), TextWrapping = System.Windows.TextWrapping.Wrap };
            DockPanel.SetDock(note, Dock.Bottom); panel.Children.Add(note); panel.Children.Add(grid);
            var canvas = new Border { Background = System.Windows.Media.Brushes.White, Child = panel };
            canvas.Measure(new System.Windows.Size(1230, 550)); canvas.Arrange(new System.Windows.Rect(0, 0, 1230, 550));
            await Dispatcher.Yield(DispatcherPriority.Background); canvas.UpdateLayout();
            Assert(rows.Take(3).Select(row => row.BarcodeOutputText).SequenceEqual(new[]
                { "12000204302610282001", "12000204302610282002", "12000204302610282003" }) &&
                rows.Skip(7).All(row => row.BarcodeOutputText.Length == 0), "Preview renders the requested numeric progression and blank error/offline output");
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1230, 550, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(canvas);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
            using var output = File.Create(outputPath); encoder.Save(output);
        });
    }

    private static void TestHistoryBarcodeOnlyAndPassLot()
    {
        OnSettingsDispatcher(async () =>
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            string root = Path.Combine(Path.GetTempPath(), "JBZ-HistoryBarcode-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var settings = new ProductionSettings { MasterFaultRequiredCount = 0, LotNo = 2000 };
            var store = new TestHistoryStore(Path.Combine(root, "history.db"));
            await using var persistence = new ProductionPersistenceService(store, settings, "SELFTEST");
            await persistence.Initialization;
            try
            {
                XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
                var document = XDocument.Load(Path.Combine(Environment.CurrentDirectory, "Views", "HistoryPage.xaml"));
                XElement textXml = document.Descendants(ns + "DataGridTemplateColumn")
                    .Single(column => (string?)column.Attribute("Header") == "Mã vạch đầu ra")
                    .Descendants(ns + "TextBlock").Single();
                var cell = (TextBlock)XamlReader.Parse(textXml.ToString());
                var model = Model(("PAIR", new[] { 1, 2 }));
                model.PartNumber = "1200020430";
                model.ModelName = "BARCODE-ITEM";
                model.SourcePath = Path.Combine(root, "BARCODE-ITEM.tht");
                var vm = CreateTestViewModel(settings, out FakeBoard board);
                settings.Label.PrinterCom = string.Empty;
                void Set(string name, object value) => typeof(TestViewModel).GetField(name, flags)!.SetValue(vm, value);
                Set("_historyStore", store);
                Set("_productionPersistence", persistence);
                Set("_lotSequence", new LotSequenceService(settings,
                    saved => ProductionConfigService.SaveLegacyCfg(saved, Path.Combine(root, "settings.cfg"))));
                Set("_partCounterStore", new Lazy<PartCounterStore>(() => new PartCounterStore(Path.Combine(root, "PartCnt.txt"))));
                settings.LabelTemplateTypesByProduct[model.PartNumber] = LabelSettings.LargeTemplate;
                vm.SetModel(model);
                await (Task)typeof(TestViewModel).GetField("_statisticsLoadTask", flags)!.GetValue(vm)!;
                long generation = (long)typeof(TestViewModel).GetField("_runtimeGeneration", flags)!.GetValue(vm)!;
                DateTime at = DateTime.Now;
                int sequence = 0, passCount = 0;
                foreach (bool passed in new[] { true, false, true, true })
                {
                    string cycle = "barcode-" + sequence++;
                    Set("_runtimeMode", 1); Set("_masterApproved", true);
                    Set("_activeCycleId", cycle); Set("_cycleStartedAt", at); Set("_resultRecordedThisCycle", 0);
                    bool committed = await (Task<bool>)typeof(TestViewModel).GetMethod("RecordCompletedProductAsync", flags)!
                        .Invoke(vm, [passed, passed ? "PASS" : "FAIL", model, generation, CancellationToken.None,
                            passed ? null : new[] { new FaultDetail { Type = ProductFaultType.WrongWiring, ActualSourceIo = 1, ActualTargetIo = 3 } }, at])!;
                    Assert(committed, "Barcode fixture uses the actual production commit pipeline");
                    if (passed)
                    {
                        // The production first-print worker runs independently of
                        // the commit. Let its real offline outcome finish before
                        // this fixture simulates explicit retry outcomes below.
                        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                        while (store.Search(new HistorySearchCriteria(null, null, null, model.PartNumber, "ALL"))
                                   .Single(item => item.CycleId == cycle).PrintStatus != LabelPrintStatus.Failed.ToString())
                            await Task.Delay(5, deadline.Token);
                    }
                    if (passed) passCount++;
                    long expectedLot = 2000 + passCount;
                    var row = store.Search(new HistorySearchCriteria(null, null, null, model.PartNumber, "ALL"))
                        .Single(item => item.CycleId == cycle);
                    string expectedBarcode = passed ? model.PartNumber + at.ToString("yyMMdd") + expectedLot : string.Empty;
                    Assert(row.LotNo == expectedLot && row.BarcodeValue == expectedBarcode && vm.Lot == expectedLot.ToString(),
                        "PASS advances LOT once; FAIL keeps the last LOT and has no output barcode");
                    if (passed)
                    {
                        store.UpdateLabelPrintOutcome(row.Id, cycle, LabelPrintStatus.Failed, null, "");
                        Assert(store.TryBeginFirstPrint(row.Id, cycle), "An explicit offline retry retains the original product");
                        store.UpdateLabelPrintOutcome(row.Id, cycle, LabelPrintStatus.Unknown, null, "");
                        row = store.Search(new HistorySearchCriteria(null, null, null, model.PartNumber, "ALL"))
                            .Single(item => item.CycleId == cycle);
                        Assert(row.BarcodeValue == expectedBarcode && row.LabelPayload.Length > 0 &&
                               row.PrintStatus == LabelPrintStatus.Unknown.ToString(),
                            "Failed/pending/unknown transport outcomes preserve the barcode without fabricating a printed result");
                    }
                    cell.DataContext = row;
                    await Dispatcher.Yield(DispatcherPriority.Background);
                    Assert(cell.Text == string.Empty && (string?)cell.ToolTip == string.Empty,
                        "Actual History column and tooltip stay empty for failed products or unconfirmed/offline sends");
                    if (passed)
                    {
                        store.UpdateLabelPrintOutcome(row.Id, cycle, LabelPrintStatus.Printed, at, "", expectedBarcode);
                        cell.DataContext = store.Search(new HistorySearchCriteria(null, null, null, model.PartNumber, "ALL"))
                            .Single(item => item.CycleId == cycle);
                        await Dispatcher.Yield(DispatcherPriority.Background);
                        Assert(cell.Text == expectedBarcode && (string?)cell.ToolTip == expectedBarcode && !cell.Text.Contains('\n'),
                            "Successful sends display only the immutable barcode, never printer commands");
                    }
                }
                Assert(vm.Pass == 3 && vm.Fail == 1 && vm.Lot == "2003" &&
                       !board.Commands.Any(command => command.StartsWith("SET:", StringComparison.Ordinal)),
                    "Barcode presentation cannot fire relays or alter PASS/FAIL quantities");

                foreach (string template in new[] { LabelSettings.LargeTemplate, LabelSettings.LargeSqdzTemplate,
                             LabelSettings.SmallTemplate, LabelSettings.SmallQrTemplate, LabelSettings.BcwTemplate })
                {
                    var history = new TestHistoryRecord { Passed = true, LotNo = 2001, Finished = new DateTime(2026, 10, 28),
                        CycleId = "format-" + template, LabelTemplateType = template, PrintStatus = LabelPrintStatus.Printed.ToString() };
                    LabelPrintRequest request = LabelPrintRequest.Capture(history, model,
                        new LabelSettings { TemplateType = template, PreferSelectedTemplate = true,
                            BcwVehicleCode = "09", BcwPosition = "LH" });
                    history.BarcodeValue = request.Data.Barcode;
                    history.LabelPayload = request.Payload;
                    cell.DataContext = history;
                    await Dispatcher.Yield(DispatcherPriority.Background);
                    Assert(cell.Text == request.Data.Barcode && history.ExportBarcodeText == request.Payload,
                        $"{template} displays its exact encoded data while retaining the original command for audit/export");
                    if (template == LabelSettings.LargeTemplate)
                        Assert(cell.Text == "12000204302610282001", "Numeric example is part/date/LOT 2001 without an appended sequence suffix");
                    history.BarcodeValue = string.Empty;
                    cell.DataContext = history.ClonePersistenceSnapshot();
                    await Dispatcher.Yield(DispatcherPriority.Background);
                    Assert(cell.Text == request.Data.Barcode,
                        $"Legacy {template} reads its barcode from the original saved EPL without updating SQLite or today's model");
                    history.InspectionType = HistoryInspectionType.MasterGood;
                    cell.DataContext = history.ClonePersistenceSnapshot();
                    await Dispatcher.Yield(DispatcherPriority.Background);
                    Assert(cell.Text == string.Empty, "Master records never display production barcode output");
                }
                string before = HistoryDataSnapshot(store.DatabasePath);
                store.Search(new HistorySearchCriteria(null, null, null, "", "ALL"));
                Assert(HistoryDataSnapshot(store.DatabasePath) == before && ScalarInt(store.DatabasePath, "SELECT COUNT(*) FROM Tests;") == 4,
                    "Reading/rendering History preserves every existing row and child relationship");
                using var connection = new SqliteConnection("Data Source=" + store.DatabasePath);
                connection.Open();
                using var check = connection.CreateCommand();
                check.CommandText = "PRAGMA integrity_check;";
                Assert((string?)check.ExecuteScalar() == "ok", "Barcode persistence leaves temporary history consistent");
            }
            finally { SqliteConnection.ClearAllPools(); }
        });
    }
}
