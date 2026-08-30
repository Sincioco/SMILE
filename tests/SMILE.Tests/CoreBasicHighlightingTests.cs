using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using SMILE.Desktop.Highlighting;

namespace SMILE.Tests;

[TestClass]
[TestCategory("CoreBasic")]
public sealed class CoreBasicHighlightingTests
{
    [TestMethod]
    public void Canonical_keywords_and_apostrophe_comments_are_highlighted()
    {
        const string source = """
Dim Total As Number
For I = 1 To 3
    Total = Total + I ' update
End For
Print Total
""";
        var document = new TextDocument(source);
        IHighlightingDefinition definition = SyntaxHighlightingCatalog.GetDefinition("smile")!;
        var highlighter = new DocumentHighlighter(document, definition);

        Assert.IsNotNull(definition.GetNamedColor("Keyword"));
        Assert.IsNotNull(definition.GetNamedColor("Comment"));
        HighlightedLine forLine = highlighter.HighlightLine(2);
        HighlightedLine commentLine = highlighter.HighlightLine(3);
        Assert.IsTrue(forLine.Sections.Any(section => section.Color.Name == "Keyword"));
        Assert.IsTrue(commentLine.Sections.Any(section => section.Color.Name == "Comment"));
    }

    [TestMethod]
    public void Obsolete_keywords_are_not_colored_as_canonical_keywords()
    {
        const string source = "LET Name = 1\nSET Name = 2\nINPUT Name\nWHILE Name\nREM old";
        var document = new TextDocument(source);
        IHighlightingDefinition definition = SyntaxHighlightingCatalog.GetDefinition("smile")!;
        var highlighter = new DocumentHighlighter(document, definition);

        for (int line = 1; line <= document.LineCount; line++)
        {
            Assert.IsFalse(highlighter.HighlightLine(line).Sections.Any(section => section.Color.Name == "Keyword"));
        }
    }

    [TestMethod]
    public void Profile_two_routines_select_and_arrays_are_highlighted()
    {
        const string source = "Option Explicit\nFunction Pick(ByVal Values[3] As Number) As Number\nSelect Case Values[0]\nCase 1\nReturn Values[1]\nEnd Select\nEnd Function";
        var document = new TextDocument(source);
        IHighlightingDefinition definition = SyntaxHighlightingCatalog.GetDefinition("smile")!;
        var highlighter = new DocumentHighlighter(document, definition);

        foreach (int line in new[] { 1, 2, 3, 4, 5, 6, 7 })
        {
            Assert.IsTrue(
                highlighter.HighlightLine(line).Sections.Any(section => section.Color.Name == "Keyword"),
                $"Expected a Profile 2 keyword on line {line}.");
        }

        Assert.IsTrue(
            highlighter.HighlightLine(2).Sections.Any(section => section.Color.Name == "Operator"),
            "Array brackets and parameter punctuation should use the operator color.");
    }
}
