using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using JBZUniversalTester.ModelEditor;
using JBZUniversalTester.ModelEditor.Services;
using JBZUniversalTester.Services;

namespace JBZUniversalTester.SelfTests;
internal static partial class Program
{
    private static void TestMultiPartThtEditor()
    {
        TestMultiPartThtEditorFile(Path.Combine(Environment.CurrentDirectory,"Tests","Fixtures","MultiPartOptions.tht"));
    }
    private static void TestMultiPartThtEditorFile(string source)
    {
        string root=Path.Combine(Path.GetTempPath(),"JBZ-MultiPartEditor-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        byte[] original=File.ReadAllBytes(source);
        try
        {
            string path=Path.Combine(root,"MULTIPART.tht"); File.WriteAllBytes(path,original);
            var doc=ThtFileService.Load(path);
            var originalRows=doc.ThtState!.ProductRows.Select(r=>r.ToArray()).ToArray();
            Assert(originalRows.Length==3 && originalRows.Select(r=>r[5]).SequenceEqual(new[]{"C","A","B"}),"Real native fixture has three option-scoped parts");
            string pinRows=System.Text.Json.JsonSerializer.Serialize(doc.ThtState.PinRows);
            string connectorRows=System.Text.Json.JsonSerializer.Serialize(doc.ThtState.ConnectorRows);
            var tracker=new DocumentChangeTracker(); tracker.MarkSaved(doc);
            for(int i=0;i<3;i++) { ThtFileService.SelectPart(doc,i); Assert(doc.Base.Number==originalRows[i][1] && !tracker.HasChanges(doc),"Selecting a part neither mutates metadata nor marks the document dirty"); }
            var invalid=doc.Clone(); invalid.Base.Name="INVALID\tNAME";
            ThtFileService.SelectPart(invalid,0);
            bool refused=false;
            try { IntegratedThtStore.Save(invalid,path,IntegratedThtStore.Fingerprint(path)); }
            catch (JBZUniversalTester.ModelEditor.Models.ValidationException) { refused=true; }
            Assert(refused && File.ReadAllBytes(path).SequenceEqual(original),"Invalid hidden part cells are rejected before replacing the source");
            IntegratedThtStore.Save(doc.Clone(),path,IntegratedThtStore.Fingerprint(path));
            Assert(File.ReadAllBytes(path).SequenceEqual(original),"Untouched multi-part save preserves exact original bytes");
            OnSettingsDispatcher(async ()=>
            {
                var page=new ThtEditorPage(); await page.OpenPathAsync(path);
                var combo=(ComboBox)page.FindName("PartCombo");
                Assert(combo.Items.Count==3 && !page.HasUnsavedChanges,"Editor exposes every native part");
                for(int i=0;i<3;i++)
                {
                    combo.SelectedIndex=i;
                    var basic=(FrameworkElement)((ContentControl)page.FindName("ContentHost")).Content;
                    Assert(((TextBox)basic.FindName("NumberBox")).Text==originalRows[i][1],"Part selection loads the correct metadata");
                    ((TextBox)basic.FindName("NameBox")).Text="EDITED PART "+i;
                }
                Assert(await page.SaveAsync() && combo.SelectedIndex==2 && !page.HasUnsavedChanges,"UI saves all pending part edits and retains selected part");
                byte[] saved=File.ReadAllBytes(path);
                combo.SelectedIndex=0;combo.SelectedIndex=1;
                Assert(!page.HasUnsavedChanges && await page.SaveAsync() && File.ReadAllBytes(path).SequenceEqual(saved),"Switching parts after saving does not trigger another write");
            });
            var savedDoc=ThtFileService.Load(path);
            for(int i=0;i<3;i++)
            {
                var expected=originalRows[i].ToArray(); expected[4]="EDITED PART "+i;
                Assert(savedDoc.ThtState!.ProductRows[i].SequenceEqual(expected),"All hidden part cells and option markers survive editing another part");
            }
            Assert(System.Text.Json.JsonSerializer.Serialize(savedDoc.ThtState!.PinRows)==pinRows &&
                System.Text.Json.JsonSerializer.Serialize(savedDoc.ThtState.ConnectorRows)==connectorRows,"Native connector/pin option masks remain unchanged");
            ThtFileService.SelectPart(savedDoc,1); savedDoc.Pins.First(p=>p.Color.Length>0).Color="G";
            IntegratedThtStore.Save(savedDoc,path,IntegratedThtStore.Fingerprint(path));
            var models=new ThtModelParser().LoadAll(path);
            Assert(models.Count==3 && models.Select(m=>m.PartNumber).SequenceEqual(originalRows.Select(r=>r[1])) &&
                models.Select(m=>m.ProductName).SequenceEqual(new[]{"EDITED PART 0","EDITED PART 1","EDITED PART 2"}),"Runtime parser still reads all parts after shared wiring edits");
            Assert(File.ReadAllBytes(source).SequenceEqual(original),"User source is never modified during regression tests");
        }
        finally { Directory.Delete(root,true); }
    }
}
