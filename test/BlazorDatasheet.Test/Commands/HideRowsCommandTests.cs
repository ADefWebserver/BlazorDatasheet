using BlazorDatasheet.Core.Commands;
using BlazorDatasheet.Core.Data;
using FluentAssertions;
using NUnit.Framework;

namespace BlazorDatasheet.Test.Commands;

public class HideRowsCommandTests
{
    [Test]
    public void Hide_Rows_Sets_height_to_zero_and_hides_Rows()
    {
        var sheet = new Sheet(100, 100);
        var cmd = new HideRowsCommand(10, 18);
        cmd.Execute(sheet);
        sheet.Rows.IsRowVisible(10).Should().BeFalse();
        sheet.Rows.IsRowVisible(15).Should().BeFalse();
        sheet.Rows.IsRowVisible(19).Should().BeTrue();
        sheet.Rows.GetHeight(10).Should().Be(0);
        sheet.Rows.GetHeight(15).Should().Be(0);
        sheet.Rows.GetHeight(19).Should().Be(sheet.Rows.DefaultHeight);
        cmd.Undo(sheet);
        sheet.Rows.IsRowVisible(10).Should().BeTrue();
        sheet.Rows.IsRowVisible(15).Should().BeTrue();
        sheet.Rows.IsRowVisible(19).Should().BeTrue();
        sheet.Rows.GetHeight(10).Should().Be(sheet.Rows.DefaultHeight);
    }
}