using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using SMILE.Engine;

namespace SMILE.Tests;

[TestClass]
[TestCategory("HtmlValidation")]
public sealed class SmileLanguageReferenceTests
{
    private static readonly RegexOptions HtmlOptions = RegexOptions.IgnoreCase | RegexOptions.Singleline;

    [TestMethod]
    public void Reference_is_self_contained_linked_accessible_and_has_one_nav_number()
    {
        string html = ReadReference();

        Assert.IsFalse(Regex.IsMatch(html, @"<script\b[^>]*\bsrc\s*=", HtmlOptions));
        Assert.IsFalse(Regex.IsMatch(html, @"<link\b[^>]*\brel\s*=\s*[\""']stylesheet", HtmlOptions));
        Assert.IsFalse(Regex.IsMatch(html, @"<(?:img|audio|video|source)\b[^>]*\bsrc\s*=", HtmlOptions));
        Assert.IsFalse(Regex.IsMatch(html, @"(?:url|@import)\s*\(\s*[\""']?https?://", HtmlOptions));
        Assert.IsFalse(Regex.IsMatch(html, @"\bfetch\s*\(", HtmlOptions));

        string[] ids = Regex.Matches(html, @"(?<![-\w])id\s*=\s*[\""']([^\""']+)[\""']", HtmlOptions)
            .Select(match => match.Groups[1].Value)
            .ToArray();
        Assert.AreEqual(ids.Length, ids.Distinct(StringComparer.Ordinal).Count(), "Every HTML id must be unique.");

        string[] anchors = Regex.Matches(html, @"\bhref\s*=\s*[\""']#([^\""']+)[\""']", HtmlOptions)
            .Select(match => match.Groups[1].Value)
            .ToArray();
        foreach (string anchor in anchors)
        {
            Assert.Contains(anchor, ids, $"Missing internal anchor target #{anchor}.");
        }

        Match navigation = Regex.Match(html, @"<ul\s+id=[\""']nav-links[\""'][^>]*>(.*?)</ul>", HtmlOptions);
        Assert.IsTrue(navigation.Success);
        string[] labels = Regex.Matches(navigation.Groups[1].Value, @"<a\b[^>]*>(.*?)</a>", HtmlOptions)
            .Select(match => StripTags(match.Groups[1].Value).Trim())
            .ToArray();
        Assert.HasCount(32, labels);
        foreach (string label in labels)
        {
            Assert.IsTrue(Regex.IsMatch(label, @"^\d{2}\s·\s\D+$"), $"Navigation must contain exactly one authored number: {label}");
        }

        Assert.IsFalse(Regex.IsMatch(navigation.Groups[1].Value, @"<ol\b", HtmlOptions));
        Assert.IsFalse(Regex.IsMatch(html, @"counter-(?:reset|increment)", HtmlOptions));
        Assert.IsFalse(Regex.IsMatch(html, @"\.toc[^{}]*::(?:before|marker)[^{]*\{[^}]*content\s*:\s*[\""']?\d", HtmlOptions));
        StringAssert.Contains(html, "Skip to the language reference");
        StringAssert.Contains(html, "prefers-reduced-motion");
        StringAssert.Contains(html, "@media print");
    }

    [TestMethod]
    public void Reference_diagrams_targets_and_terminology_match_the_milestone()
    {
        string html = ReadReference();
        MatchCollection diagrams = Regex.Matches(html, @"<svg\b.*?</svg>", HtmlOptions);
        Assert.IsGreaterThanOrEqualTo(5, diagrams.Count);
        foreach (Match diagram in diagrams)
        {
            StringAssert.Contains(diagram.Value, "<title>");
            StringAssert.Contains(diagram.Value, "<desc>");
            Assert.IsFalse(Regex.IsMatch(diagram.Value, @"<path\b", HtmlOptions), "Diagram connectors must use line/polyline, never path curves.");
        }

        Assert.HasCount(10, Regex.Matches(html, @"\bdata-target\s*=", HtmlOptions));
        StringAssert.Contains(html, "Simple Modern and Intuitive Language for Everyone");
        Assert.IsGreaterThanOrEqualTo(4, Regex.Matches(html, Regex.Escape("JavaScript (Node.js)"), HtmlOptions).Count);
        Assert.IsFalse(Regex.IsMatch(html, @"JavaScript\s+target", HtmlOptions), "Target-facing prose must say JavaScript (Node.js).");
        StringAssert.Contains(html, "no npm package");
        StringAssert.Contains(html, "There is no eleventh target");
        StringAssert.Contains(html, "does <strong>not</strong> implement console Input");
    }

    [TestMethod]
    public void Every_labeled_valid_example_binds_generates_all_targets_and_documented_outputs_match()
    {
        string html = ReadReference();
        MatchCollection validBlocks = Regex.Matches(
            html,
            @"<code\b(?=[^>]*\bdata-smile-valid=[\""']true[\""'])[^>]*>(.*?)</code>",
            HtmlOptions);
        Assert.IsGreaterThanOrEqualTo(20, validBlocks.Count);

        var transpiler = new SmileTranspiler();
        foreach (Match block in validBlocks)
        {
            string source = WebUtility.HtmlDecode(StripTags(block.Groups[1].Value));
            IReadOnlyList<TranspileResult> results = transpiler.TranspileMany(source, ActiveTargetLanguages.All);
            Assert.HasCount(10, results);
            Assert.IsTrue(
                results.All(result => result.Success),
                source + Environment.NewLine + Join(results.SelectMany(result => result.Diagnostics)));
        }

        Dictionary<string, string> outputs = Regex.Matches(
                html,
                @"<code\b[^>]*\bdata-smile-output=[\""']([^\""']+)[\""'][^>]*>(.*?)</code>",
                HtmlOptions)
            .ToDictionary(
                match => match.Groups[1].Value,
                match => ExpectedOutput(WebUtility.HtmlDecode(StripTags(match.Groups[2].Value))),
                StringComparer.Ordinal);

        MatchCollection runnable = Regex.Matches(
            html,
            @"<code\b(?=[^>]*\bdata-smile-valid=[\""']true[\""'])(?=[^>]*\bdata-example-id=[\""']([^\""']+)[\""'])[^>]*>(.*?)</code>",
            HtmlOptions);
        Assert.IsGreaterThanOrEqualTo(12, runnable.Count);
        foreach (Match block in runnable)
        {
            string id = block.Groups[1].Value;
            Assert.IsTrue(outputs.TryGetValue(id, out string? expected), $"Missing output for {id}.");
            string source = WebUtility.HtmlDecode(StripTags(block.Groups[2].Value));
            EvaluationResult result = new SmileEvaluator().Evaluate(source);
            Assert.IsTrue(result.Success, id + Environment.NewLine + Join(result.Diagnostics));
            Assert.AreEqual(expected, Normalize(result.Output), id);
        }
    }

    private static string ExpectedOutput(string text)
    {
        string normalized = Normalize(text);
        return normalized.EndsWith('\n') ? normalized : normalized + "\n";
    }

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static string StripTags(string text) => Regex.Replace(text, "<[^>]+>", string.Empty);

    private static string Join(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.ToString()));

    private static string ReadReference() =>
        File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "smile-1-language-reference.html"));

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(Environment.CurrentDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SMILE.sln"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the SMILE repository root.");
    }
}
