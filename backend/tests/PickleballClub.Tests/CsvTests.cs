using PickleballClub.Api.Features.Admin;

namespace PickleballClub.Tests;

public class CsvTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("Court 1", "Court 1")]
    [InlineData("Smith, John", "\"Smith, John\"")]
    [InlineData("Nok \"Ace\" S.", "\"Nok \"\"Ace\"\" S.\"")]
    [InlineData("two\nlines", "\"two\nlines\"")]
    [InlineData("=SUM(A1)", "'=SUM(A1)")]
    [InlineData("+66812345678", "'+66812345678")]
    [InlineData("-1", "'-1")]
    [InlineData("@home", "'@home")]
    [InlineData("=1,2", "\"'=1,2\"")]
    public void Cells_are_quoted_and_cannot_start_a_formula(string? value, string expected) =>
        Assert.Equal(expected, ReportEndpoints.Cell(value));
}
