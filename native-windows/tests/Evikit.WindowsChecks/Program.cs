using System.Diagnostics;
using System.Reflection;
using Evikit.Core;

// Runs the production WinForms dialog and Windows clipboard on the Windows CI
// desktop. This is not an RDP, IME, DPI or real-Excel acceptance test.
internal static class Program
{
    private static readonly Assembly App = Assembly.Load("evikit");
    private static readonly Type Dialog = App.GetType("Evikit.Windows.EvidenceDialog", true)!;
    private static void Assert(bool value) { if (!value) throw new Exception("Windows paste regression check failed"); }
    private static T Control<T>(Form form, string name) where T : Control => (T)form.Controls.Find(name, true).Single();
    private static Form Open(string? text = null, string? file = null)
    {
        var form = (Form)Activator.CreateInstance(Dialog, new object?[] { new TestCase { Id = "TC-DB" }, null, file, null, text })!;
        form.Show(); Application.DoEvents(); return form;
    }
    private static void Ready(Form form)
    {
        var watch = Stopwatch.StartNew();
        while (Control<Label>(form, "EvidenceFormatStatus").Text == "データを確認しています…")
        { Application.DoEvents(); Thread.Sleep(10); if (watch.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("Paste preview did not update"); }
    }
    private static NewEvidence Save(Form form)
    {
        Ready(form); var button = Control<Button>(form, "SaveEvidence"); Assert(button.Enabled); button.PerformClick();
        return (NewEvidence)Dialog.GetProperty("Result")!.GetValue(form)!;
    }
    [STAThread]
    private static int Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); Application.EnableVisualStyles();
        int passed = 0;
        void Check(string name, Action test) { test(); passed++; Console.WriteLine("PASS " + name); }
        try
        {
            const string csv = "id,name\r\n00012,テスト顧客\r\n";
            const string tsv = "id\tname\r\n00012\tテスト顧客\r\n";
            Check("pasting directly into a new dialog updates table preview and saves table", () =>
            {
                foreach (string data in new[] { csv, tsv })
                {
                    using var form = Open(); Clipboard.SetText(data);
                    var content = Control<TextBox>(form, "EvidenceContent"); content.Focus(); content.Paste(); Ready(form);
                    var grid = Control<DataGridView>(form, "EvidencePreview");
                    Assert(grid.Columns.Count == 2 && grid.Rows.Count == 1 && (string?)grid.Rows[0].Cells[0].Value == "00012");
                    var result = Save(form); Assert(result.Kind == "table" && result.Category == "DB");
                    Assert(Tables.Parse(Inputs.Utf8(result.Data))[1][0] == "00012");
                }
            });
            Check("toolbar clipboard text constructor previews CSV immediately", () =>
            {
                using var form = Open(csv); Assert(Control<DataGridView>(form, "EvidencePreview").Rows.Count == 1);
                Assert(Save(form).Kind == "table");
            });
            Check("explicit text stays text after another paste", () =>
            {
                using var form = Open(csv); Control<ComboBox>(form, "EvidenceKind").SelectedItem = "text";
                Clipboard.SetText(tsv); var content = Control<TextBox>(form, "EvidenceContent"); content.SelectAll(); content.Paste(); Ready(form);
                Assert(Control<DataGridView>(form, "EvidencePreview").Columns.Count == 0 && Save(form).Kind == "text");
            });
            Check("editing table into ordinary text removes stale rows and saves latest content", () =>
            {
                using var form = Open(csv); Control<TextBox>(form, "EvidenceContent").Text = "ordinary log"; Ready(form);
                Assert(Control<DataGridView>(form, "EvidencePreview").Rows.Count == 0);
                var result = Save(form); Assert(result.Kind == "text" && Inputs.Utf8(result.Data) == "ordinary log");
            });
            Check("manual malformed table disables Add then recovers after repair", () =>
            {
                using var form = Open(csv); Control<ComboBox>(form, "EvidenceKind").SelectedItem = "table";
                Control<TextBox>(form, "EvidenceContent").Text = "id,name\n00012,A,extra"; Ready(form);
                Assert(!Control<Button>(form, "SaveEvidence").Enabled && Control<DataGridView>(form, "EvidencePreview").Rows.Count == 0);
                Control<TextBox>(form, "EvidenceContent").Text = csv; Ready(form); Assert(Save(form).Kind == "table");
            });
            Check("clipboard text takes priority over bitmap only when it is tabular", () =>
            {
                var method = App.GetType("Evikit.Windows.EvidenceClipboard", true)!.GetMethod("TableText", BindingFlags.Static | BindingFlags.NonPublic)!;
                using var bitmap = new Bitmap(2, 2);
                var data = new DataObject(); data.SetData(DataFormats.Bitmap, bitmap); data.SetText(tsv); Clipboard.SetDataObject(data, true);
                Assert(Clipboard.ContainsImage() && (string?)method.Invoke(null, null) == tsv);
                Clipboard.SetImage(bitmap); Assert(method.Invoke(null, null) == null);
                Clipboard.SetText("normal log"); Assert(method.Invoke(null, null) == null);
            });
            Check("large pasted table previews 500 rows without truncating saved cells", () =>
            {
                string data = "id,name\n" + string.Concat(Enumerable.Range(0, 1000).Select(i => $"{i:D5},{new string('a', 60)}\n"));
                using var form = Open(); Clipboard.SetText(data); Control<TextBox>(form, "EvidenceContent").Paste(); Ready(form);
                Assert(Control<DataGridView>(form, "EvidencePreview").Rows.Count == 500);
                Assert(Tables.Parse(Inputs.Utf8(Save(form).Data)).Count == 1001);
            });
            Check("CSV file selection uses the same visible preview and preserves original source", () =>
            {
                string file = Path.Combine(Path.GetTempPath(), "evikit-paste-" + Guid.NewGuid().ToString("N") + ".csv");
                try { File.WriteAllText(file, csv); using var form = Open(file: file); Assert(Control<DataGridView>(form, "EvidencePreview").Columns.Count == 2); Assert(Save(form).Kind == "table" && File.ReadAllText(file) == csv); }
                finally { File.Delete(file); }
            });
            Console.WriteLine($"{passed} Windows clipboard/dialog checks passed."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Clipboard.Clear(); }
    }
}
