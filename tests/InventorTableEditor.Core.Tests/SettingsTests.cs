using InventorTableEditor.Core.Editing;
using InventorTableEditor.Core.Layout;
using InventorTableEditor.Core.Model;
using InventorTableEditor.Core.Settings;

namespace InventorTableEditor.Core.Tests;

public class SettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ite-tests-" + Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_directory, "sub", "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    // ---------- Проверка оформления ----------

    [Fact]
    public void FactoryStyle_IsValid()
    {
        Assert.Empty(new TableStyle().Check());
        Assert.Empty(new UserSettings().Check());
    }

    [Fact]
    public void Check_ReportsEveryBadValue()
    {
        var style = new TableStyle
        {
            FontName = " ",
            FontSizeMm = 0,
            OuterLineMm = 5,
            MinScaleBeforeWrap = 0.1,
            ScriptScale = double.NaN,
            DefaultRowHeightMm = 1,
            HeaderAlignment = (HorizontalTextAlignment)42,
        };

        var errors = style.Check();

        Assert.Equal(7, errors.Count);
        Assert.Contains(errors, e => e.StartsWith("Предел сжатия"));
        Assert.Contains(errors, e => e.StartsWith("Высота строки"));
    }

    [Fact]
    public void Repair_ReplacesOnlyBadValuesWithFactory()
    {
        var style = new TableStyle { FontSizeMm = -1, InnerLineMm = 0.35, MinScaleBeforeWrap = 2, DataAlignment = (HorizontalTextAlignment)9 };

        style.Repair();

        Assert.Equal(5, style.FontSizeMm);
        Assert.Equal(0.35, style.InnerLineMm);
        Assert.Equal(0.75, style.MinScaleBeforeWrap);
        Assert.Equal(HorizontalTextAlignment.Left, style.DataAlignment);
        Assert.Empty(style.Check());
    }

    // ---------- Файл настроек ----------

    [Fact]
    public void Load_MissingFile_IsFactory()
    {
        var settings = UserSettings.Load(SettingsPath, out var error);

        Assert.Null(error);
        Assert.Equal(new UserSettings().ToJson(), settings.ToJson());
    }

    [Fact]
    public void SaveAndLoad_RoundTrip()
    {
        var settings = new UserSettings { NewTableRows = 5, NewTableColumns = 2, NewTableHeader = true };
        settings.Style.FontSizeMm = 3.5;
        settings.Style.Italic = false;
        settings.Style.MinScaleBeforeWrap = 0.6;
        settings.Style.HeaderAlignment = HorizontalTextAlignment.Left;

        settings.Save(SettingsPath);
        var loaded = UserSettings.Load(SettingsPath, out var error);

        Assert.Null(error);
        Assert.Equal(settings.ToJson(), loaded.ToJson());
        Assert.False(File.Exists(SettingsPath + ".tmp"));
    }

    [Fact]
    public void Load_CorruptFile_IsFactoryWithError()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, "{ not json");

        var settings = UserSettings.Load(SettingsPath, out var error);

        Assert.NotNull(error);
        Assert.Equal(3, settings.NewTableRows);
        Assert.True(File.Exists(SettingsPath));
    }

    [Fact]
    public void FromJson_RepairsValuesAndKeepsMissingAsFactory()
    {
        var settings = UserSettings.FromJson("""{ "Style": { "FontSizeMm": 500, "PaddingMm": 2 }, "NewTableRows": 0 }""");

        Assert.Equal(5, settings.Style.FontSizeMm);
        Assert.Equal(2, settings.Style.PaddingMm);
        Assert.Equal("Gost merged", settings.Style.FontName);
        Assert.Equal(3, settings.NewTableRows);
    }

    [Fact]
    public void CreateTable_UsesSettings()
    {
        var settings = new UserSettings { NewTableRows = 2, NewTableColumns = 4, NewTableHeader = true };
        settings.Style.DefaultColumnWidthMm = 15;
        settings.Style.DefaultRowHeightMm = 8;

        var model = settings.CreateTable();

        Assert.Equal(3, model.RowCount);
        Assert.Equal(4, model.ColumnCount);
        Assert.True(model.HasHeader);
        Assert.All(model.ColumnWidths, w => Assert.Equal(15, w));
        Assert.All(model.RowHeights, h => Assert.Equal(8, h));
        Assert.NotSame(settings.Style, model.Style);
    }

    // ---------- Оформление в таблице ----------

    [Fact]
    public void DefaultAlignments_ComeFromStyle()
    {
        var model = TableModel.CreateEmpty(2, 1);
        model.SetHeader(true);
        model.Style.HeaderAlignment = HorizontalTextAlignment.Right;
        model.Style.DataAlignment = HorizontalTextAlignment.Center;
        model.Style.VerticalAlignment = VerticalTextAlignment.Top;
        model.GetCell(2, 0).HorizontalAlignment = HorizontalTextAlignment.Left;

        Assert.Equal(HorizontalTextAlignment.Right, model.GetHorizontalAlignment(0, 0));
        Assert.Equal(HorizontalTextAlignment.Center, model.GetHorizontalAlignment(1, 0));
        Assert.Equal(HorizontalTextAlignment.Left, model.GetHorizontalAlignment(2, 0));
        Assert.Equal(VerticalTextAlignment.Top, model.GetVerticalAlignment(1, 0));
    }

    [Fact]
    public void HeaderLine_HasOwnThickness()
    {
        var model = TableModel.CreateEmpty(2, 1);
        model.SetHeader(true);
        model.Style.HeaderLineMm = 0.35;

        var layout = TableLayout.Build(model);

        Assert.Contains(layout.Lines, l => l.WeightMm == 0.35 && l.Y1 == l.Y2 && l.Y1 == model.RowHeights[0]);
    }

    [Fact]
    public void OldTableJson_GetsFactoryAlignmentsAndHeaderLine()
    {
        var model = TableModel.CreateEmpty(1, 1);
        var json = model.ToJson().Replace("\"HeaderLineMm\":0.5,", "");

        var loaded = TableModel.FromJson(json);

        Assert.Equal(0.5, loaded.Style.HeaderLineMm);
        Assert.Equal(HorizontalTextAlignment.Left, loaded.Style.DataAlignment);
    }

    [Fact]
    public void ApplyStyle_FlipsDefaultItalicButKeepsExplicit()
    {
        var model = TableModel.CreateEmpty(1, 1);
        var cell = new TableCell();
        cell.Runs.Add(new TextRun("a", italic: true));
        cell.Runs.Add(new TextRun("b", italic: false));
        model.SetCell(0, 0, cell);
        var style = model.Style.Clone();
        style.Italic = false;
        style.FontSizeMm = 3.5;

        model.ApplyStyle(style);

        // «a» был наклонным по умолчанию — стал прямым, «b» прямой и был; оба склеились.
        Assert.Equal("ab", model.GetCell(0, 0).Runs.Single().Text);
        Assert.False(model.GetCell(0, 0).Runs.Single().Italic);
        Assert.Equal(3.5, model.Style.FontSizeMm);
        Assert.NotSame(style, model.Style);
    }

    [Fact]
    public void Session_ApplyStyle_IsOneUndoStep()
    {
        var session = new TableSession(TableModel.CreateEmpty(2, 2), () => 0);
        var style = session.Model.Style.Clone();
        style.OuterLineMm = 0.7;

        Assert.False(session.ApplyStyle(session.Model.Style.Clone()));
        Assert.True(session.ApplyStyle(style));
        Assert.Equal(0.7, session.Model.Style.OuterLineMm);

        session.Undo();
        Assert.Equal(0.5, session.Model.Style.OuterLineMm);
    }

    [Fact]
    public void Clone_IsIndependent()
    {
        var settings = new UserSettings();
        var copy = settings.Clone();

        copy.Style.FontSizeMm = 7;
        copy.NewTableRows = 9;

        Assert.Equal(5, settings.Style.FontSizeMm);
        Assert.Equal(3, settings.NewTableRows);
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(3, 0)]
    [InlineData(UserSettings.MaxNewTableSize + 1, 3)]
    public void Check_NewTableSize(int rows, int columns)
    {
        var errors = new UserSettings { NewTableRows = rows, NewTableColumns = columns }.Check();

        Assert.Single(errors);
    }

    [Fact]
    public void FromJson_Null_IsFactory()
    {
        Assert.Equal(new UserSettings().ToJson(), UserSettings.FromJson("null").ToJson());
    }

    [Fact]
    public void TableFromJson_RepairsInvalidStyleInsteadOfFailing()
    {
        var model = TableModel.FromJson("""{"Style":{"FontSizeMm":0,"InnerLineMm":-1,"HeaderAlignment":7},"ColumnWidths":[20],"RowHeights":[8],"Cells":[[{"Runs":[]}]]}""");

        Assert.Equal(5, model.Style.FontSizeMm);
        Assert.Equal(0.25, model.Style.InnerLineMm);
        Assert.Equal(HorizontalTextAlignment.Center, model.Style.HeaderAlignment);
    }

    [Fact]
    public void ApplyStyle_SameItalicKeepsRunsUntouched()
    {
        var model = TableModel.CreateEmpty(1, 1);
        model.SetCell(0, 0, new TableCell { Runs = { new TextRun("a", italic: false) } });
        var style = model.Style.Clone();
        style.FontSizeMm = 7;

        model.ApplyStyle(style);

        Assert.False(model.GetCell(0, 0).Runs.Single().Italic);
    }
}
