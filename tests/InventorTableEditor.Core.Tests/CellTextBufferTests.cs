using InventorTableEditor.Core.Editing;
using InventorTableEditor.Core.Model;

namespace InventorTableEditor.Core.Tests;

public class CellTextBufferTests
{
    private static readonly TableStyle Style = new();

    private static CellTextBuffer Buffer(string text) => new(TableCell.FromText(text, italic: true), Style);

    [Fact]
    public void Insert_TypesAtCaretWithFormatOfPreviousChar()
    {
        var buffer = Buffer("ac");
        buffer.MoveTo(1, extend: false);

        buffer.Insert("b");

        Assert.Equal("abc", buffer.Text);
        Assert.Equal(2, buffer.Caret);
        Assert.True(buffer.ToCell(new TableCell()).Runs.Single().Italic);
    }

    [Fact]
    public void EmptyCell_TypesWithTableItalic()
    {
        var buffer = new CellTextBuffer(new TableCell(), Style);

        buffer.Insert("x");

        Assert.True(buffer.ToCell(new TableCell()).Runs.Single().Italic);
    }

    [Fact]
    public void Insert_ReplacesSelection()
    {
        var buffer = Buffer("hello");
        buffer.MoveTo(1, extend: false);
        buffer.MoveTo(4, extend: true);

        buffer.Insert("EL");

        Assert.Equal("hELo", buffer.Text);
        Assert.False(buffer.HasSelection);
    }

    [Fact]
    public void BackspaceAndDelete()
    {
        var buffer = Buffer("abc");
        buffer.MoveTo(2, extend: false);

        buffer.Backspace();
        Assert.Equal("ac", buffer.Text);
        Assert.Equal(1, buffer.Caret);

        buffer.Delete();
        Assert.Equal("a", buffer.Text);

        buffer.MoveTo(0, extend: false);
        buffer.Backspace();
        Assert.Equal("a", buffer.Text);
    }

    [Fact]
    public void ToggleScript_WithoutSelection_ChangesTypedTextOnly_AndResetReturnsToNormal()
    {
        var buffer = Buffer("D");
        buffer.MoveTo(1, extend: false);

        buffer.ToggleScript(ScriptPosition.Superscript);
        buffer.Insert("2");
        buffer.ResetScript();
        buffer.Insert("x");

        var runs = buffer.ToCell(new TableCell()).Runs;
        Assert.Equal(new[] { "D", "2", "x" }, runs.Select(r => r.Text));
        Assert.Equal(new[] { ScriptPosition.Normal, ScriptPosition.Superscript, ScriptPosition.Normal }, runs.Select(r => r.Script));
    }

    [Fact]
    public void ToggleScript_OnSelectionTogglesBack()
    {
        var buffer = Buffer("D2");
        buffer.MoveTo(1, extend: false);
        buffer.MoveTo(2, extend: true);

        buffer.ToggleScript(ScriptPosition.Subscript);
        Assert.Equal(ScriptPosition.Subscript, buffer.ToCell(new TableCell()).Runs[1].Script);

        buffer.ToggleScript(ScriptPosition.Subscript);
        Assert.Single(buffer.ToCell(new TableCell()).Runs);
    }

    [Fact]
    public void ToggleBold_MixedSelectionBecomesBold()
    {
        var buffer = Buffer("ab");
        buffer.MoveTo(0, extend: false);
        buffer.MoveTo(1, extend: true);
        buffer.ToggleBold();

        buffer.SelectAll();
        buffer.ToggleBold();

        Assert.True(buffer.ToCell(new TableCell()).Runs.Single().Bold);
    }

    [Fact]
    public void SetFontSize_TableSizeStoredAsNull()
    {
        var buffer = Buffer("ab");
        buffer.SelectAll();

        buffer.SetFontSize(7);
        Assert.Equal(7, buffer.ToCell(new TableCell()).Runs.Single().FontSizeMm);

        buffer.SetFontSize(Style.FontSizeMm);
        Assert.Null(buffer.ToCell(new TableCell()).Runs.Single().FontSizeMm);
    }

    [Fact]
    public void ToCell_KeepsAlignmentAndLineBreaks()
    {
        var buffer = Buffer("a");
        buffer.MoveTo(1, extend: false);
        buffer.Insert("\nb");

        var cell = buffer.ToCell(new TableCell { HorizontalAlignment = HorizontalTextAlignment.Right });

        Assert.Equal("a\nb", cell.PlainText);
        Assert.Equal(HorizontalTextAlignment.Right, cell.HorizontalAlignment);
    }

    [Fact]
    public void WordNavigation()
    {
        var buffer = Buffer("ab  cd ef");

        Assert.Equal(4, buffer.WordRight(0));
        Assert.Equal(4, buffer.WordLeft(6));
        Assert.Equal(0, buffer.WordLeft(3));
    }

    [Fact]
    public void MoveTo_ClampsToText()
    {
        var buffer = Buffer("abc");

        buffer.MoveTo(100, extend: false);
        Assert.Equal(3, buffer.Caret);
        buffer.MoveTo(-5, extend: true);
        Assert.Equal(0, buffer.Caret);
        Assert.Equal("abc", buffer.SelectedText);
    }

    [Fact]
    public void Insert_NormalizesWindowsLineBreaks()
    {
        var buffer = new CellTextBuffer(new TableCell(), Style);

        buffer.Insert("a\r\nb\rc");

        Assert.Equal("a\nb\nc", buffer.Text);
    }

    [Fact]
    public void DeleteAtEndAndBackspaceAtStart_DoNothing()
    {
        var buffer = Buffer("a");
        buffer.MoveTo(1, extend: false);
        buffer.Delete();
        buffer.MoveTo(0, extend: false);
        buffer.Backspace();

        Assert.Equal("a", buffer.Text);
    }

    [Fact]
    public void ToggleWithoutSelection_DoesNotChangeText()
    {
        var buffer = Buffer("ab");
        buffer.MoveTo(1, extend: false);

        buffer.ToggleBold();

        Assert.True(buffer.TypingFormat.Bold);
        Assert.False(buffer.ToCell(new TableCell()).Runs.Single().Bold);

        // Сдвиг курсора сбрасывает формат набора на формат текста.
        buffer.MoveTo(2, extend: false);
        Assert.False(buffer.TypingFormat.Bold);
    }

    [Fact]
    public void TypingAtLineStart_TakesFormatOfFollowingChar()
    {
        var cell = new TableCell { Runs = { new TextRun("a"), new TextRun("\n"), new TextRun("b") { Bold = true } } };
        var buffer = new CellTextBuffer(cell, Style);

        buffer.MoveTo(2, extend: false);

        Assert.True(buffer.TypingFormat.Bold);
    }

    [Fact]
    public void SetFontName_TableFontStoredAsNull()
    {
        var buffer = Buffer("ab");
        buffer.SelectAll();

        buffer.SetFontName("Arial");
        Assert.Equal("Arial", buffer.ToCell(new TableCell()).Runs.Single().FontName);

        buffer.SetFontName(Style.FontName.ToUpperInvariant());
        Assert.Null(buffer.ToCell(new TableCell()).Runs.Single().FontName);
    }

    [Fact]
    public void FormatChangeOnSelection_DoesNotLeakToOtherChars()
    {
        var buffer = Buffer("abc");
        buffer.MoveTo(1, extend: false);
        buffer.MoveTo(2, extend: true);

        buffer.ToggleUnderline();

        var runs = buffer.ToCell(new TableCell()).Runs;
        Assert.Equal(new[] { "a", "b", "c" }, runs.Select(r => r.Text));
        Assert.Equal(new[] { false, true, false }, runs.Select(r => r.Underline));
    }

    [Fact]
    public void All_ChecksSelectionOrTypingFormat()
    {
        var buffer = Buffer("ab");
        buffer.MoveTo(0, extend: false);
        buffer.MoveTo(1, extend: true);
        buffer.ToggleBold();

        buffer.SelectAll();
        Assert.False(buffer.All(f => f.Bold));

        buffer.MoveTo(1, extend: false);
        Assert.True(buffer.All(f => f.Bold));
    }

    [Fact]
    public void ApplyFormat_SetsValueOnWholeSelection()
    {
        var buffer = Buffer("abc");
        buffer.SelectAll();

        buffer.ApplyFormat(f => f.Underline = true);

        Assert.True(buffer.ToCell(new TableCell()).Runs.Single().Underline);
    }

    [Fact]
    public void HasText_IgnoresWhitespace()
    {
        Assert.False(Buffer(" \n ").HasText);
        Assert.True(Buffer(" a").HasText);
        Assert.False(new CellTextBuffer(new TableCell(), Style).HasText);
    }

    [Fact]
    public void InsertEmptyWithoutSelection_IsNoOp()
    {
        var buffer = Buffer("a");

        buffer.Insert(string.Empty);

        Assert.Equal("a", buffer.Text);
    }

    [Fact]
    public void SelectedText_SpansLines()
    {
        var buffer = Buffer("ab\ncd");
        buffer.MoveTo(1, extend: false);
        buffer.MoveTo(4, extend: true);

        Assert.Equal("b\nc", buffer.SelectedText);
    }

    [Fact]
    public void WordNavigation_AtEdges()
    {
        var buffer = Buffer("ab");

        Assert.Equal(0, buffer.WordLeft(0));
        Assert.Equal(2, buffer.WordRight(2));
        Assert.Equal(2, buffer.WordRight(-5 + 5));
    }

    [Fact]
    public void ToCell_EmptyBuffer()
    {
        Assert.Empty(new CellTextBuffer(new TableCell(), Style).ToCell(new TableCell()).Runs);
    }

    [Fact]
    public void CurrentFormat_IsSelectionStartOrTypingFormat()
    {
        var cell = new TableCell { Runs = { new TextRun("a") { Bold = true }, new TextRun("b") } };
        var buffer = new CellTextBuffer(cell, Style);

        buffer.MoveTo(0, extend: false);
        buffer.MoveTo(2, extend: true);
        Assert.True(buffer.CurrentFormat.Bold);

        buffer.MoveTo(2, extend: false);
        Assert.False(buffer.CurrentFormat.Bold);
    }

    // ---------- Перемещение и выделение ----------

    [Fact]
    public void WordLeftAndRight_JumpOverWordsAndSpaces()
    {
        var buffer = Buffer("ab  cd ef");

        Assert.Equal(4, buffer.WordRight(0));
        Assert.Equal(7, buffer.WordRight(4));
        Assert.Equal(9, buffer.WordRight(7));
        Assert.Equal(7, buffer.WordLeft(9));
        Assert.Equal(4, buffer.WordLeft(6));
        Assert.Equal(0, buffer.WordLeft(3));
        Assert.Equal(0, buffer.WordLeft(-5));
        Assert.Equal(9, buffer.WordRight(99));
    }

    [Fact]
    public void MoveTo_ClampsAndExtendsSelection()
    {
        var buffer = Buffer("abc");

        buffer.MoveTo(10, extend: false);
        Assert.Equal(3, buffer.Caret);
        buffer.MoveTo(1, extend: true);

        Assert.Equal((1, 3), (buffer.SelectionStart, buffer.SelectionEnd));
        Assert.Equal("bc", buffer.SelectedText);
        buffer.MoveTo(-1, extend: false);
        Assert.False(buffer.HasSelection);
        Assert.Equal(0, buffer.Caret);
    }

    [Fact]
    public void BackspaceAndDelete_RemoveSelectionFirst()
    {
        var buffer = Buffer("abcd");
        buffer.MoveTo(1, extend: false);
        buffer.MoveTo(3, extend: true);

        buffer.Backspace();
        Assert.Equal("ad", buffer.Text);
        Assert.Equal(1, buffer.Caret);

        buffer.SelectAll();
        buffer.Delete();
        Assert.Equal("", buffer.Text);
    }

    [Fact]
    public void Insert_NormalizesLineBreaksAndReplacesSelection()
    {
        var buffer = Buffer("xyz");
        buffer.MoveTo(0, extend: false);
        buffer.MoveTo(2, extend: true);

        buffer.Insert("1\r\n2\r3");

        Assert.Equal("1\n2\n3z", buffer.Text);
        Assert.Equal(5, buffer.Caret);
    }

    [Fact]
    public void ToCell_KeepsTemplateAlignment()
    {
        var buffer = Buffer("a");

        var cell = buffer.ToCell(new TableCell { HorizontalAlignment = HorizontalTextAlignment.Right, VerticalAlignment = VerticalTextAlignment.Top });

        Assert.Equal(HorizontalTextAlignment.Right, cell.HorizontalAlignment);
        Assert.Equal(VerticalTextAlignment.Top, cell.VerticalAlignment);
    }

    [Fact]
    public void ToggleScript_SecondToggleTurnsOffAndOtherScriptSwitches()
    {
        var buffer = Buffer("ab");
        buffer.SelectAll();

        buffer.ToggleScript(ScriptPosition.Superscript);
        Assert.True(buffer.All(f => f.Script == ScriptPosition.Superscript));
        buffer.ToggleScript(ScriptPosition.Subscript);
        Assert.True(buffer.All(f => f.Script == ScriptPosition.Subscript));
        buffer.ToggleScript(ScriptPosition.Subscript);
        Assert.True(buffer.All(f => f.Script == ScriptPosition.Normal));
    }

    [Fact]
    public void ResetScript_OnlyChangesScript()
    {
        var buffer = new CellTextBuffer(new TableCell { Runs = { new TextRun("2") { Bold = true, Script = ScriptPosition.Superscript } } }, Style);
        buffer.SelectAll();

        buffer.ResetScript();

        var run = buffer.ToCell(new TableCell()).Runs.Single();
        Assert.Equal(ScriptPosition.Normal, run.Script);
        Assert.True(run.Bold);
    }

    [Fact]
    public void SelectedRuns_EmptyWithoutSelection()
    {
        Assert.Empty(Buffer("abc").SelectedRuns());
    }

    [Fact]
    public void InsertRuns_EmptyRunsDeleteSelection()
    {
        var buffer = Buffer("abc");
        buffer.SelectAll();

        buffer.InsertRuns(new[] { new TextRun("") });

        Assert.Equal("", buffer.Text);
    }

    [Fact]
    public void TypingFormat_AfterSuperscriptStaysSuperscriptLikeWord()
    {
        var buffer = new CellTextBuffer(new TableCell { Runs = { new TextRun("m"), new TextRun("2") { Script = ScriptPosition.Superscript } } }, Style);
        buffer.MoveTo(2, extend: false);

        buffer.Insert("3");

        Assert.Equal(ScriptPosition.Superscript, buffer.ToCell(new TableCell()).Runs[^1].Script);
        Assert.Equal("23", buffer.ToCell(new TableCell()).Runs[^1].Text);
    }
}
