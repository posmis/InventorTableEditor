using InventorTableEditor.Core.Editing;
using InventorTableEditor.Core.Layout;
using InventorTableEditor.Core.Model;
using InventorTableEditor.Core.Templates;

namespace InventorTableEditor.Core.Tests;

public class TemplateTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ite-templates-" + Guid.NewGuid().ToString("N"));

    private TemplateLibrary Library => new(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private static TableModel Sample()
    {
        var model = TableModel.CreateEmpty(2, 3);
        model.SetHeader(true);
        model.SetText(0, 0, "Параметр");
        model.Merge(new CellRange(1, 1, 2, 2));
        model.ColumnWidths[0] = 42;
        model.Style.OuterLineMm = 0.7;
        model.Editor = new EditorView { Zoom = 2 };
        return model;
    }

    // ---------- Встроенные ----------

    [Fact]
    public void BuiltIns_AreListedFirstAndBuild()
    {
        var templates = Library.List();

        Assert.Equal(BuiltInTemplates.Names, templates.Take(BuiltInTemplates.Names.Count).Select(t => t.Name));
        Assert.All(templates, t => Assert.True(t.BuiltIn));
        foreach (var template in templates)
        {
            var model = Library.Load(template);
            Assert.Equal(3, model.ColumnCount);
            Assert.Equal(110, model.ColumnWidths.Sum());
            Assert.NotEmpty(TableLayout.Build(model).Texts);
        }
    }

    [Fact]
    public void Sprocket_HasSubscriptDesignations()
    {
        var model = BuiltInTemplates.Create(BuiltInTemplates.Sprocket);

        var row = Enumerable.Range(0, model.RowCount).Single(r => model.GetText(r, 0) == "Диаметр окружности впадин");
        var runs = model.GetCell(row, 1).Runs;
        Assert.Equal(new[] { "D", "i" }, runs.Select(r => r.Text));
        Assert.Equal(ScriptPosition.Subscript, runs[1].Script);
        Assert.Equal(HorizontalTextAlignment.Center, model.GetHorizontalAlignment(row, 1));
    }

    [Fact]
    public void BuiltIn_LoadReturnsFreshCopy()
    {
        var template = Library.List()[0];
        var first = Library.Load(template);
        first.SetText(0, 2, "changed");

        Assert.Equal("", Library.Load(template).GetText(0, 2));
    }

    // ---------- Пользовательские ----------

    [Fact]
    public void SaveAndLoad_KeepsTableWithoutEditorView()
    {
        var saved = Library.Save("  Моя таблица ", Sample());

        Assert.Equal("Моя таблица", saved.Name);
        var loaded = Library.Load(Library.List().Single(t => !t.BuiltIn));
        Assert.Equal("Параметр", loaded.GetText(0, 0));
        Assert.True(loaded.HasHeader);
        Assert.Equal(new CellRange(1, 1, 2, 2), loaded.GetMerge(1, 1));
        Assert.Equal(42, loaded.ColumnWidths[0]);
        Assert.Equal(0.7, loaded.Style.OuterLineMm);
        Assert.Null(loaded.Editor);
    }

    [Fact]
    public void Save_DoesNotChangeSourceModel()
    {
        var model = Sample();

        Library.Save("a", model);

        Assert.NotNull(model.Editor);
    }

    [Fact]
    public void Save_ExistingNameNeedsOverwrite()
    {
        Library.Save("a", Sample());

        Assert.Throws<InvalidOperationException>(() => Library.Save("A", Sample()));
        var model = Sample();
        model.SetText(0, 0, "new");
        Library.Save("a", model, overwrite: true);
        Assert.Equal("new", Library.Load(Library.List().Single(t => !t.BuiltIn)).GetText(0, 0));
    }

    [Fact]
    public void Save_BuiltInNameIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => Library.Save(BuiltInTemplates.Sprocket.ToUpperInvariant(), Sample()));
    }

    [Fact]
    public void UserTemplates_SortedByName()
    {
        Library.Save("б", Sample());
        Library.Save("А", Sample());
        Library.Save("в", Sample());

        Assert.Equal(new[] { "А", "б", "в" }, Library.List().Where(t => !t.BuiltIn).Select(t => t.Name));
    }

    [Fact]
    public void RenameAndDelete()
    {
        var template = Library.Save("old", Sample());

        var renamed = Library.Rename(template, "new");
        Assert.Equal(new[] { "new" }, Library.List().Where(t => !t.BuiltIn).Select(t => t.Name));

        var recased = Library.Rename(renamed, "NEW");
        Assert.Equal(new[] { "NEW" }, Library.List().Where(t => !t.BuiltIn).Select(t => t.Name));

        Library.Delete(recased);
        Assert.DoesNotContain(Library.List(), t => !t.BuiltIn);
    }

    [Fact]
    public void Rename_ToTakenNameFails()
    {
        var a = Library.Save("a", Sample());
        Library.Save("b", Sample());

        Assert.Throws<InvalidOperationException>(() => Library.Rename(a, "b"));
    }

    [Fact]
    public void BuiltIn_CannotBeDeletedOrRenamed()
    {
        var builtIn = Library.List()[0];

        Assert.Throws<InvalidOperationException>(() => Library.Delete(builtIn));
        Assert.Throws<InvalidOperationException>(() => Library.Rename(builtIn, "x"));
    }

    [Fact]
    public void Load_CorruptFileThrowsInvalidData()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "broken.json"), "{ nope");

        var template = Library.List().Single(t => !t.BuiltIn);

        Assert.Throws<InvalidDataException>(() => Library.Load(template));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("a/b", false)]
    [InlineData("a?", false)]
    [InlineData("name.", false)]
    [InlineData("con", false)]
    [InlineData("COM1.txt", false)]
    [InlineData("Таблица звёздочки z=17", true)]
    [InlineData("Шаблон (копия)", true)]
    public void CheckName(string name, bool valid)
    {
        Assert.Equal(valid, TemplateLibrary.CheckName(name) == null);
    }

    [Fact]
    public void CheckName_TooLong()
    {
        Assert.NotNull(TemplateLibrary.CheckName(new string('x', TemplateLibrary.MaxNameLength + 1)));
    }

    // ---------- Сеанс ----------

    [Fact]
    public void ReplaceTable_IsOneUndoStepAndResetsCursor()
    {
        var session = new TableSession(TableModel.CreateEmpty(2, 2), () => 0);
        session.Model.SetText(0, 0, "mine");
        session.MoveTo(1, 1);

        session.ReplaceTable(BuiltInTemplates.Create(BuiltInTemplates.Gear));

        Assert.Equal((0, 0), (session.Row, session.Column));
        Assert.Equal("Модуль", session.Model.GetText(0, 0));
        session.Undo();
        Assert.Equal("mine", session.Model.GetText(0, 0));
        Assert.Equal(2, session.Model.RowCount);
    }

    [Fact]
    public void BuiltIn_UnknownNameThrows()
    {
        Assert.Throws<ArgumentException>(() => BuiltInTemplates.Create("нет такого"));
    }

    [Fact]
    public void Exists_IgnoresCaseAndSpaces()
    {
        Library.Save("Моя", Sample());

        Assert.True(Library.Exists(" моя "));
        Assert.True(Library.Exists(BuiltInTemplates.Gear.ToLowerInvariant()));
        Assert.False(Library.Exists("другая"));
    }

    [Fact]
    public void Save_InvalidNameThrows()
    {
        Assert.Throws<ArgumentException>(() => Library.Save("a/b", Sample()));
        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public void List_WithoutFolder_OnlyBuiltIns()
    {
        Assert.Equal(BuiltInTemplates.Names.Count, Library.List().Count);
    }

    [Fact]
    public void Overwrite_WithDifferentCase_KeepsOneTemplate()
    {
        Library.Save("abc", Sample());

        Library.Save("ABC", Sample(), overwrite: true);

        Assert.Equal(new[] { "ABC" }, Library.List().Where(t => !t.BuiltIn).Select(t => t.Name));
    }

    [Fact]
    public void BuiltIns_FitDefaultPaddingAndRowHeight()
    {
        foreach (var name in BuiltInTemplates.Names)
        {
            var model = BuiltInTemplates.Create(name);
            var layout = TableLayout.Build(model, FixedMetrics.Instance);
            // С переносом длинных наименований строки могут вырасти, но не уменьшиться.
            for (var r = 0; r < model.RowCount; r++)
                Assert.True(layout.RowEdges[r + 1] - layout.RowEdges[r] >= 8 - 1e-9);
            Assert.Empty(model.Style.Check());
        }
    }
}
