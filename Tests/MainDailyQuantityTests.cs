using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;
using JBZUniversalTester.Views;
using Microsoft.Data.Sqlite;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestMainDailyQuantityByProduct()
    {
        OnSettingsDispatcher(async () =>
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            string directory = Path.Combine(Path.GetTempPath(), "JBZ-MainDailyQuantity-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var settings = new ProductionSettings { MasterFaultRequiredCount = 0, LotNo = 0 };
            var store = new TestHistoryStore(Path.Combine(directory, "history.db"));
            await using var persistence = new ProductionPersistenceService(store, settings, "SELFTEST");
            await persistence.Initialization;
            try
            {
                DateTime today = DateTime.Now.Date;
                ProductModel Product(string name)
                {
                    var model = Model(("PAIR", new[] { 1, 2 }));
                    model.PartNumber = name;
                    model.ModelName = name;
                    model.SourcePath = Path.Combine(directory, name + ".tht");
                    return model;
                }
                var a = Product("DAILY-A");
                var b = Product("DAILY-B");
                var unused = Product("DAILY-NEW");
                int sequence = 0;
                void Record(ProductModel model, DateTime at, bool passed, string type = HistoryInspectionType.Product)
                {
                    store.CommitResult(ProductionResultCommitRequest.Capture(new TestHistoryRecord
                    {
                        CycleId = "daily-" + sequence++, PartNumber = model.PartNumber, ModelName = model.ModelName,
                        ModelFile = model.SourcePath, Started = at, TestStartedAt = at, ResultAt = at, Finished = at,
                        Passed = passed, Result = passed ? "PASS" : "FAIL", InspectionType = type, LotNo = 0
                    }, model, settings, [], [], null, "SELFTEST"), null);
                }
                for (int i = 0; i < 5; i++) Record(a, today.AddDays(-1).AddHours(10), true);
                Record(a, today.AddHours(8), true);
                Record(a, today.AddHours(9), true);
                Record(a, today.AddHours(10), false);
                Record(a, today.AddDays(1).AddHours(8), true);
                Record(a, today.AddHours(11), true, HistoryInspectionType.MasterGood);
                Record(a, today.AddHours(12), false, HistoryInspectionType.MasterBad);
                Record(b, today.AddHours(8), true);
                Record(b, today.AddHours(9), false);

                var main = new MainViewModel(new AppSettings(), settings);
                var vm = main.Test;
                void Set(string name, object value) => typeof(TestViewModel).GetField(name, flags)!.SetValue(vm, value);
                Set("_historyStore", store);
                Set("_productionPersistence", persistence);
                Set("_lotSequence", new LotSequenceService(settings,
                    saved => ProductionConfigService.SaveLegacyCfg(saved, Path.Combine(directory, "settings.cfg"))));
                Set("_partCounterStore", new Lazy<PartCounterStore>(() => new PartCounterStore(Path.Combine(directory, "PartCnt.txt"))));
                var window = new MainWindow(main);
                var surface = (FrameworkElement)window.Content;
                window.Content = null; // No Loaded/ContentRendered hardware startup.
                surface.DataContext = main;
                surface.Resources.MergedDictionaries.Add(window.Resources);
                var quantity = (TextBlock)window.FindName("ProductionQuantityText");

                async Task Selected(ProductModel model, long expected)
                {
                    vm.SetModel(model);
                    main.Model = model;
                    main.Home.Refresh();
                    await ((Task)typeof(TestViewModel).GetField("_statisticsLoadTask", flags)!.GetValue(vm)!)
                        .WaitAsync(TimeSpan.FromSeconds(5));
                    await Dispatcher.Yield(DispatcherPriority.Background);
                    surface.Measure(new Size(1440, 900));
                    surface.Arrange(new Rect(0, 0, 1440, 900));
                    surface.UpdateLayout();
                    Assert(vm.DailyTestCount == expected && vm.Total == expected &&
                           quantity.Text == expected.ToString("N0", CultureInfo.CurrentCulture),
                        $"Actual MainWindow shows only today's PASS+FAIL for {model.PartNumber}: expected={expected}, actual={quantity.Text}");
                }
                await Selected(a, 3);
                Assert(vm.LifetimeTestCount == 9 && vm.Pass == 2 && vm.Fail == 1,
                    "Prior/future dates and Master checks cannot inflate the current daily quantity");
                await Selected(b, 2);
                await Selected(unused, 0);
                await Selected(a, 3);
                Record(a, today.AddHours(13), true);
                ProductionStatisticsSnapshot updated = await persistence.GetStatisticsAsync(PartIdentitySnapshot.Capture(a), today);
                typeof(TestViewModel).GetMethod("ApplyProductionStatistics", flags)!.Invoke(vm, [updated]);
                await Dispatcher.Yield(DispatcherPriority.Background);
                Assert(quantity.Text == "4" && vm.Pass == 3 && vm.Fail == 1,
                    "Committed results update the live daily quantity without reopening MainWindow");
                var nextDay = await persistence.GetStatisticsAsync(PartIdentitySnapshot.Capture(a), today.AddDays(1));
                typeof(TestViewModel).GetMethod("ApplyProductionStatistics", flags)!.Invoke(vm, [nextDay]);
                await Dispatcher.Yield(DispatcherPriority.Background);
                Assert(quantity.Text == "1", "Changing the statistics date displays that date's saved quantity");
                var emptyDay = await persistence.GetStatisticsAsync(PartIdentitySnapshot.Capture(a), today.AddDays(2));
                typeof(TestViewModel).GetMethod("ApplyProductionStatistics", flags)!.Invoke(vm, [emptyDay]);
                await Dispatcher.Yield(DispatcherPriority.Background);
                Assert(quantity.Text == "0", "A new date has zero quantity until its first product");
                Assert(ScalarInt(store.DatabasePath, "SELECT COUNT(*) FROM Tests;") == 14,
                    "Switching product/date and rendering cannot rewrite or remove history");
                using var connection = new SqliteConnection("Data Source=" + store.DatabasePath);
                connection.Open();
                using var check = connection.CreateCommand();
                check.CommandText = "PRAGMA integrity_check;";
                Assert((string?)check.ExecuteScalar() == "ok", "Daily quantity leaves temporary history intact");
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                // All fixtures are isolated in the temporary directory; no production DB is opened.
            }
        });
    }
}
