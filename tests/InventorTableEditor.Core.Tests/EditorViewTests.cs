using InventorTableEditor.Core.Editing;
using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Core.Tests;

public class EditorViewTests
{
    [Fact]
    public void Json_KeepsEditorView_NewTableHasNone()
    {
        var model = TableModel.CreateEmpty(1, 1);
        Assert.Null(model.Editor);
        Assert.DoesNotContain("Editor", model.ToJson());

        model.Editor = new EditorView { Width = 1300, Height = 800, Maximized = true, Zoom = 1.2 };
        var restored = TableModel.FromJson(model.ToJson()).Editor!;

        Assert.Equal((1300.0, 800.0, true, 1.2), (restored.Width, restored.Height, restored.Maximized, restored.Zoom));
    }

    [Theory]
    [InlineData(100, 50, 99, EditorView.MinWidth, EditorView.MinHeight, Zoom.Max)]
    [InlineData(1e9, 1e9, 0.01, EditorView.MaxSize, EditorView.MaxSize, Zoom.Min)]
    public void Normalize_ClampsValues(double width, double height, double zoom, double expectedWidth, double expectedHeight, double expectedZoom)
    {
        var view = new EditorView { Width = width, Height = height, Zoom = zoom };

        view.Normalize();

        Assert.Equal((expectedWidth, expectedHeight, expectedZoom), (view.Width, view.Height, view.Zoom));
    }

    [Fact]
    public void FromJson_RepairsBrokenEditorView()
    {
        var json = """{"Editor":{"Width":"NaN","Height":10,"Zoom":50},"ColumnWidths":[20],"RowHeights":[10],"Cells":[[{"Runs":[]}]]}""";

        var model = TableModel.FromJson(json.Replace("\"NaN\"", "0"));

        Assert.Equal(EditorView.MinWidth, model.Editor!.Width);
        Assert.Equal(EditorView.MinHeight, model.Editor.Height);
        Assert.Equal(Zoom.Max, model.Editor.Zoom);
    }

    [Fact]
    public void UndoDoesNotLoseEditorView()
    {
        var session = new TableSession(TableModel.CreateEmpty(1, 1));
        session.Model.Editor = new EditorView { Zoom = 2 };
        session.ReplaceCell(0, 0, TableCell.FromText("a", italic: false), EditKind.Typing);

        session.Undo();

        Assert.Equal(2, session.Model.Editor!.Zoom);
    }

    [Fact]
    public void Clone_IsIndependent()
    {
        var view = new EditorView { Zoom = 1 };
        var clone = view.Clone();
        clone.Zoom = 3;

        Assert.Equal(1, view.Zoom);
    }
}
