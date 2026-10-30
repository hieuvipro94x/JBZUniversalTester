using System.Reflection;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;
using Microsoft.Data.Sqlite;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static List<TestHistoryRecord> CombinedLeakHistoryExamples()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var vm = CreateTestViewModel(new ProductionSettings { MasterFaultRequiredCount = 0 });
        void Set(string name, object? value) => typeof(TestViewModel).GetField(name, flags)!.SetValue(vm, value);
        Set("_waterProofProfile", new WaterProofModelSettings { Enabled = true, Channel1Enabled = true,
            Channel2Enabled = true, Channel1Connector = "1", Channel2Connector = "2", PressMin = 35, LeakLimit = 2 });
        var rows = new List<TestHistoryRecord>();
        for (int index = 0; index < 3; index++)
        {
            DateTime start = new(2026, 10, 30, 10, index, 0);
            Set("_cycleWaterProofStartedAt", null);
            Set("_cycleWaterProofCompletedAt", null);
            ((List<string>)typeof(TestViewModel).GetField("_cycleWaterProofAttempts", flags)!.GetValue(vm)!).Clear();
            Set("_cycleWaterProofSummary", "");
            Set("_cycleContinuityCompletedAt", start.AddSeconds(12));
            if (index > 0)
            {
                var attempts = new[]
                {
                    new WaterProofRunResult([new(1, true, 80, 74, 6, false)], false, "DEMO_FAIL"),
                    new WaterProofRunResult([new(1, true, 80, 79, 1, true)], true, "DEMO_PASS"),
                    new WaterProofRunResult([new(2, true, 81, index == 2 ? 75 : 80, index == 2 ? 6 : 1, index != 2)], index != 2, "DEMO_CH2")
                };
                for (int attempt = 0; attempt < attempts.Length; attempt++)
                    typeof(TestViewModel).GetMethod("CaptureWaterProofAttemptHistory", flags)!.Invoke(vm,
                        [attempts[attempt], attempt + 1, start.AddSeconds(attempt * 4), start.AddSeconds(attempt * 4 + 3)]);
                var final = new WaterProofRunResult([attempts[1].Channels[0], attempts[2].Channels[0]], index != 2, "DEMO_FINAL");
                Set("_cycleWaterProofSummary", typeof(TestViewModel).GetMethod("BuildWaterProofHistorySummary", flags)!.Invoke(vm, [final]));
            }
            string trace = (string)typeof(TestViewModel).GetMethod("BuildProductInspectionTrace", flags)!.Invoke(vm, [start.AddSeconds(13)])!;
            rows.Add(new TestHistoryRecord { CycleId = "demo-combined-" + index, InspectionType = HistoryInspectionType.Product,
                Started = start, InstallStartedAt = start, TestStartedAt = start, ResultAt = start.AddSeconds(13),
                Finished = start.AddSeconds(13), RemovalStartedAt = start.AddSeconds(14), RemovedAt = start.AddSeconds(16),
                ModelName = "WH322444", ModelFile = "WH322444.tht", PartNumber = "WH322444", PartName = "WIRING ASSY",
                Passed = index != 2, Result = index == 2 ? "FAIL" : "PASS", LotNo = index == 0 ? 2001 : 2002,
                InspectionTrace = trace, PrintStatus = LabelPrintStatus.NotRequested.ToString() });
        }
        return rows;
    }

    private static void TestCombinedLeakHistory()
    {
        var legacy = new TestHistoryRecord { InspectionType = HistoryInspectionType.LeakRetest,
            InspectionTrace = "10:00:00 LEAK RETEST #1 [CH1:PASS]", Passed = true };
        Assert(legacy.InspectionTypeText == "기밀 검사 시작" &&
               legacy.HistoryTestLogText.Contains("기밀 검사 시작 #1", StringComparison.Ordinal) &&
               legacy.ExportTestLogSingleLineText.Contains("기밀 검사 시작 #1", StringComparison.Ordinal) &&
               !legacy.HistoryTestLogText.Contains("LEAK RETEST", StringComparison.Ordinal) &&
               legacy.InspectionTrace.Contains("LEAK RETEST", StringComparison.Ordinal),
            "Legacy Leak labels, tooltips and Excel text are Korean without changing stored history");
        var rows = CombinedLeakHistoryExamples();
        Assert(rows[0].InspectionTypeText == "제품" && rows.Skip(1).All(row => row.InspectionTypeText == "회로·기밀 검사"),
            "Ordinary products retain their type; combined products display the Korean continuity/airtightness type");
        Assert(rows.Skip(1).All(row => row.HistoryTestLogText.Contains("회로검사:PASS", StringComparison.Ordinal) &&
            row.HistoryTestLogText.Contains("기밀 검사 시작 #3", StringComparison.Ordinal) &&
            row.HistoryTestLogSingleLineText.IndexOfAny(['\r', '\n']) < 0),
            "Every channel and failed/passed retry stays in the same single-line product log");
        Assert(!rows[0].InspectionTrace.Contains("기밀", StringComparison.Ordinal) &&
            HistoryInspectionType.KoreanName(HistoryInspectionType.LeakRetest) == "기밀 검사 시작",
            "Cycle reset prevents stale attempts and legacy Leak rows are translated without rewriting their identity");
        string root = Path.Combine(Path.GetTempPath(), "JBZ-CombinedHistory-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string db = Path.Combine(root, "history.db");
            var store = new TestHistoryStore(db);
            var model = new ProductModel { PartNumber = "WH322444", ModelName = "WH322444", SourcePath = "WH322444.tht" };
            foreach (var row in rows)
            {
                var request = ProductionResultCommitRequest.Capture(row, model, new ProductionSettings(), [], [], [], "SELFTEST");
                store.CommitResult(request, null);
                Assert(store.CommitResult(request, null).AlreadyCommitted, "Combined history CycleId commits exactly once");
            }
            var criteria = new HistorySearchCriteria(null, null, null, "", "ALL", MaxRows: 1);
            Assert(store.Search(criteria).Count == 1 && store.GetHistorySummary(criteria).ProductTotal == 3,
                "Pagination does not truncate the combined product summary");
            string path = Path.Combine(root, "history.xlsx");
            Assert(HistoryExportService.ExportXlsx(path, store.EnumerateForExport(criteria)) == 3,
                "Excel exports every product, not separate attempts or only the loaded page");
            using (var zip = ZipFile.OpenRead(path))
            using (var reader = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open()))
            {
                string xml = reader.ReadToEnd();
                Assert(xml.Contains("회로검사:PASS", StringComparison.Ordinal) && xml.Contains("기밀 검사 시작 #3", StringComparison.Ordinal) &&
                       !xml.Contains("LEAK RETEST", StringComparison.Ordinal), "The real exporter includes the merged Korean log");
            }
            using var connection = new SqliteConnection($"Data Source={db};Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check";
            Assert((string?)command.ExecuteScalar() == "ok", "Combined history integrity_check is ok");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    private static void ExportCombinedLeakHistoryPreview(string path)
    {
        var rows = CombinedLeakHistoryExamples();
        HistoryExportService.ExportXlsx(path, rows);
        OnSettingsDispatcher(async () =>
        {
            var panel = new DockPanel { Background = Brushes.White, Margin = new Thickness(20) };
            var title = new TextBlock { Text = "LỊCH SỬ KIỂM TRA · V2026.09.338 · DỮ LIỆU MINH HỌA", FontSize = 24,
                FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 16) };
            DockPanel.SetDock(title, Dock.Top); panel.Children.Add(title);
            var grid = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false, IsReadOnly = true,
                ItemsSource = rows, RowHeight = 46, ColumnHeaderHeight = 40, FontSize = 15,
                RowBackground = Brushes.White, AlternatingRowBackground = Brushes.White, Height = 182 };
            foreach (var (caption, binding, width) in new[] { ("Thời gian", "TimeText", 115d), ("Mã hàng", "PartNumber", 160d),
                ("Loại kiểm tra", "InspectionTypeText", 210d), ("Kết quả", "HistoryResultText", 100d),
                ("Số đếm PASS", "ExportPassCountText", 120d), ("Hồ sơ kiểm tra", "HistoryTestLogSingleLineText", 800d) })
                grid.Columns.Add(new DataGridTextColumn { Header = caption, Binding = new Binding(binding), Width = width });
            DockPanel.SetDock(grid, Dock.Top); panel.Children.Add(grid);
            var detail = new TextBlock { Text = "CHI TIẾT DÒNG FAIL LEAK (cùng một sản phẩm)\n\n" + rows[2].HistoryTestLogText +
                "\n\nExcel: 3 dòng sản phẩm. Các lượt đo lại nằm trong ô 검사기록 của chính sản phẩm đó.",
                FontSize = 16, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 22, 8, 0), Foreground = Brushes.MidnightBlue };
            panel.Children.Add(detail);
            var canvas = new Border { Background = Brushes.White, Child = panel };
            canvas.Measure(new Size(1600, 540)); canvas.Arrange(new Rect(0, 0, 1600, 540));
            await System.Windows.Threading.Dispatcher.Yield(); canvas.UpdateLayout();
            var bitmap = new RenderTargetBitmap(1600, 540, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(canvas);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.ChangeExtension(path, ".png")); encoder.Save(stream);
        });
    }
}

