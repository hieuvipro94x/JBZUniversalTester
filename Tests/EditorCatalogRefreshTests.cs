using System.Reflection;
using System.Windows.Controls;
using JBZUniversalTester.ModelEditor;
using JBZUniversalTester.ModelEditor.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;
using JBZUniversalTester.Views;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestEditorCatalogRefresh()
    {
        OnSettingsDispatcher(async () =>
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            string root = Path.Combine(Path.GetTempPath(), "JBZ-EditorCatalog-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var catalog = new MainProductCatalog();
            try
            {
                string active = Path.Combine(root, "ACTIVE.tht");
                string created = Path.Combine(root, "NEW_MODEL.tht");
                string fixture = Path.Combine(Environment.CurrentDirectory, "Tests", "Fixtures", "MultiPartOptions.tht");
                File.Copy(fixture, active);
                int opens = 0; catalog.ProductRequested += _ => opens++;
                await catalog.LoadAsync(root, active, Path.Combine(root, "none.db"));
                var list = (ListBox)catalog.FindName("ProductList");
                Assert(list.Items.Count == 1, "Catalog starts without the not-yet-saved model");
                var editor = new ThtEditorPage();
                await editor.OpenPathAsync(active);
                Assert(!editor.HasCatalogChanges, "Opening THT alone does not request a catalog refresh");
                var doc = (ModelDocument)typeof(ThtEditorPage).GetField("_document", flags)!.GetValue(editor)!;
                doc.SourcePath = created;
                typeof(ThtEditorPage).GetField("_hash", flags)!.SetValue(editor, null);
                object basePage = typeof(ThtEditorPage).GetField("_base", flags)!.GetValue(editor)!;
                ((TextBox)((System.Windows.FrameworkElement)basePage).FindName("NameBox")).Text = "NEWLY CREATED MODEL";
                Assert(await editor.SaveAsync() && File.Exists(created) && editor.HasCatalogChanges && editor.SavedPaths.Contains(created),
                    "A native THT saved at a new path marks the editor catalog dirty only after success");
                var page = new ProductionSettingsPage(null, new ProductionSettingsViewModel(initialSettings: new(), saveSettings: _ => throw new Exception("No settings save")), deferInitialLayout: true);
                typeof(ProductionSettingsPage).GetField("_modelEditor", flags)!.SetValue(page, editor);
                Assert(page.HasThtCatalogChanges, "The host sees a successful save while the editor is open");
                bool closeRequested = false;
                page.RequestClose += (_, _) => closeRequested = true;
                await (Task)typeof(ProductionSettingsPage).GetMethod("CloseModelEditorAsync", flags)!.Invoke(page, null)!;
                Assert(closeRequested && !page.IsThtEditorOpen && page.HasThtCatalogChanges,
                    "Back retains the catalog-change signal after detaching the editor");
                // This is the same reload used by MainWindow after the host closes.
                await catalog.LoadAsync(root, active, Path.Combine(root, "none.db"));
                Assert(list.Items.Count == 2 && list.Items.Cast<ProductFileEntry>().Any(file => file.FullPath == created) &&
                       ((ProductFileEntry)list.SelectedItem).FullPath == active && opens == 0,
                    "Reload shows the saved model immediately without selecting/loading it into the test runtime");
                ((TextBox)catalog.FindName("SearchBox")).Text = "NEW_MODEL";
                await Task.Delay(260);
                Assert(list.Items.Count == 1 && ((ProductFileEntry)list.Items[0]).FullPath == created,
                    "The new filename is searchable without pressing Refresh");
                page.ReleasePageResources();
                string catalogXaml = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "Views", "MainProductCatalog.xaml"));
                Assert(!catalogXaml.Contains("Text=\"{Binding Folder}\"", StringComparison.Ordinal) &&
                       catalogXaml.Contains("ToolTip=\"{Binding FullPath}\"", StringComparison.Ordinal),
                    "Compact catalog rows show only names; full paths remain available as tooltips");
                string mainSource = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "Views", "MainWindow.xaml.cs"));
                Assert(mainSource.Contains("refreshCatalog = settingsPage.HasThtCatalogChanges;", StringComparison.Ordinal) &&
                       mainSource.Contains("if (refreshCatalog && !_shutdownStarted) await RefreshCatalogAsync();", StringComparison.Ordinal),
                    "The real MainWindow close path consumes the retained editor signal and awaits the background reload");
            }
            finally { catalog.Dispose(); Directory.Delete(root, recursive: true); }
        });
    }

    private static void TestCatalogRefreshWhileLoading()
    {
        OnSettingsDispatcher(async () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "JBZ-CatalogPending-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int reads = 0, opens = 0;
            using var catalog = new MainProductCatalog((path, ct) =>
            {
                var snapshot = ProductFileCatalog.Read(path, ct);
                if (Interlocked.Increment(ref reads) == 1)
                {
                    entered.TrySetResult();
                    release.Wait(ct);
                }
                return snapshot;
            });
            try
            {
                string first = Path.Combine(root, "FIRST.tht"), second = Path.Combine(root, "SECOND.tht");
                File.WriteAllText(first, "");
                catalog.ProductRequested += _ => opens++;
                Task initial = catalog.LoadAsync(root, first, Path.Combine(root, "none.db"));
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
                File.WriteAllText(second, "");
                Task refresh = catalog.LoadAsync(root, first, Path.Combine(root, "none.db"));
                Assert(!refresh.IsCompleted, "A refresh during a scan waits for its fresh result instead of returning immediately");
                release.Set();
                await Task.WhenAll(initial, refresh);
                var list = (ListBox)catalog.FindName("ProductList");
                Assert(reads == 2 && list.Items.Count == 2 && list.Items.Cast<ProductFileEntry>().Any(file => file.FullPath == second) && opens == 0,
                    "A save while enumeration is pending triggers another scan and cannot lose the new file");
            }
            finally { release.Set(); Directory.Delete(root, recursive: true); }
        });
    }
}
