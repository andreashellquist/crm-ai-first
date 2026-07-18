using CrmApi.Services;

namespace CrmApi.Tests;

// Pure logic, no DB — deliberately not part of CrmApiCollection.
public class CsvParserTests
{
    [Fact]
    public void Parse_SimpleRows_SplitsOnCommas()
    {
        var rows = CsvParser.Parse("email,firstName,lastName\njane@example.com,Jane,Doe\njohn@example.com,John,Smith");

        Assert.Equal(3, rows.Count);
        Assert.Equal(["email", "firstName", "lastName"], rows[0]);
        Assert.Equal(["jane@example.com", "Jane", "Doe"], rows[1]);
        Assert.Equal(["john@example.com", "John", "Smith"], rows[2]);
    }

    [Fact]
    public void Parse_QuotedFieldWithComma_KeepsCommaInsideField()
    {
        var rows = CsvParser.Parse("name,title\n\"Doe, Jane\",\"VP, Sales\"");

        Assert.Equal(["name", "title"], rows[0]);
        Assert.Equal(["Doe, Jane", "VP, Sales"], rows[1]);
    }

    [Fact]
    public void Parse_EscapedQuoteInsideQuotedField_Unescapes()
    {
        var rows = CsvParser.Parse("note\n\"She said \"\"hi\"\" today\"");

        Assert.Equal(["She said \"hi\" today"], rows[1]);
    }

    [Fact]
    public void Parse_QuotedFieldWithEmbeddedNewline_KeepsNewlineInField()
    {
        var rows = CsvParser.Parse("note\n\"line one\nline two\"\nplain");

        Assert.Equal(3, rows.Count);
        Assert.Equal("note", rows[0][0]);
        Assert.Equal("line one\nline two", rows[1][0]);
        Assert.Equal("plain", rows[2][0]);
    }

    [Fact]
    public void Parse_CrlfLineEndings_TreatedAsSingleRowBreak()
    {
        var rows = CsvParser.Parse("a,b\r\nc,d\r\n");

        Assert.Equal(2, rows.Count);
        Assert.Equal(["a", "b"], rows[0]);
        Assert.Equal(["c", "d"], rows[1]);
    }

    [Fact]
    public void Parse_NoTrailingNewline_StillIncludesLastRow()
    {
        var rows = CsvParser.Parse("a,b\nc,d");

        Assert.Equal(2, rows.Count);
        Assert.Equal(["c", "d"], rows[1]);
    }

    [Fact]
    public void Parse_EmptyInput_ReturnsNoRows()
    {
        Assert.Empty(CsvParser.Parse(""));
    }

    [Fact]
    public void Parse_EmptyFields_PreservedAsEmptyStrings()
    {
        var rows = CsvParser.Parse("a,b,c\n1,,3");

        Assert.Equal(["1", "", "3"], rows[1]);
    }
}
