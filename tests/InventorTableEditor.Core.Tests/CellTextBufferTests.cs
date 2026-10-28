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
    public void UndoRedo()
    {
        var buffer = Buffer("a");
        buffer.MoveTo(1, extend: false);
        buffer.Insert("b");
        buffer.Insert("c");

        Assert.True(buffer.Undo());
        Assert.Equal("ab", buffer.Text);
        Assert.True(buffer.Redo());
        Assert.Equal("abc", buffer.Text);
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
    public void DeleteAtEndAndBackspaceAtStart_DoNothingAndKeepUndoClean()
    {
        var buffer = Buffer("a");
        buffer.MoveTo(1, extend: false);
        buffer.Delete();
        buffer.MoveTo(0, extend: false);
        buffer.Backspace();

        Assert.Equal("a", buffer.Text);
        Assert.False(buffer.Undo());
    }

    [Fact]
    public void ToggleWithoutSelection_DoesNotChangeTextOrUndo()
    {
        var buffer = Buffer("ab");
        buffer.MoveTo(1, extend: false);

        buffer.ToggleBold();

        Assert.True(buffer.TypingFormat.Bold);
        Assert.False(buffer.ToCell(new TableCell()).Runs.Single().Bold);
        Assert.False(buffer.Undo());

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
    public void Redo_ClearedByNewEdit()
    {
        var buffer = Buffer("a");
        buffer.MoveTo(1, extend: false);
        buffer.Insert("b");
        buffer.Undo();

        buffer.Insert("c");

        Assert.False(buffer.Redo());
        Assert.Equal("ac", buffer.Text);
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
        Assert.True(buffer.Undo());
        Assert.False(buffer.ToCell(new TableCell()).Runs.Single().Underline);
    }

    [Fact]
    public void HasText_IgnoresWhitespace()
    {
        Assert.False(Buffer(" \n ").HasText);
        Assert.True(Buffer(" a").HasText);
        Assert.False(new CellTextBuffer(new TableCell(), Style).HasText);
    }
}
