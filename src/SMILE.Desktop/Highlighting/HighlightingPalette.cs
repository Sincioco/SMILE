using System.Windows.Media;
using ICSharpCode.AvalonEdit.Highlighting;

namespace SMILE.Desktop.Highlighting;

/// <summary>
/// Applies SMILE's small teaching palette without replacing AvalonEdit's
/// mature lexical grammars for the destination languages.
/// </summary>
internal static class HighlightingPalette
{
    internal static readonly Color CommentGreen = Color.FromRgb(0x00, 0x80, 0x00);
    internal static readonly Color KeywordBlue = Color.FromRgb(0x00, 0x00, 0xFF);
    internal static readonly Color IdentifierBlack = Color.FromRgb(0x00, 0x00, 0x00);
    internal static readonly Color StringRed = Color.FromRgb(0xA3, 0x15, 0x15);
    internal static readonly Color NumberDarkBlue = Color.FromRgb(0x00, 0x00, 0x8B);
    internal static readonly Color OperatorBlack = Color.FromRgb(0x1F, 0x1F, 0x1F);

    private static readonly HighlightingBrush CommentBrush = Brush(CommentGreen);
    private static readonly HighlightingBrush KeywordBrush = Brush(KeywordBlue);
    private static readonly HighlightingBrush IdentifierBrush = Brush(IdentifierBlack);
    private static readonly HighlightingBrush StringBrush = Brush(StringRed);
    private static readonly HighlightingBrush NumberBrush = Brush(NumberDarkBlue);
    private static readonly HighlightingBrush OperatorBrush = Brush(OperatorBlack);

    internal static IHighlightingDefinition Apply(
        string languageId,
        IHighlightingDefinition definition)
    {
        switch (languageId)
        {
            case "smile":
                ApplyCommonDefinition(definition, "Keyword");
                break;

            case "csharp":
                Set(definition, CommentBrush, "Comment");
                Set(definition, StringBrush, "String", "Char");
                Set(definition, IdentifierBrush, "StringInterpolation", "MethodCall");
                Set(definition, NumberBrush, "NumberLiteral");
                Set(definition, OperatorBrush, "Punctuation");
                Set(
                    definition,
                    KeywordBrush,
                    "Preprocessor",
                    "ValueTypeKeywords",
                    "ReferenceTypeKeywords",
                    "ThisOrBaseReference",
                    "NullOrValueKeywords",
                    "Keywords",
                    "GotoKeywords",
                    "ContextKeywords",
                    "ExceptionKeywords",
                    "CheckedKeyword",
                    "UnsafeKeywords",
                    "OperatorKeywords",
                    "ParameterModifiers",
                    "Modifiers",
                    "Visibility",
                    "NamespaceKeywords",
                    "GetSetAddRemove",
                    "TrueFalse",
                    "TypeKeywords",
                    "SemanticKeywords");
                ApplyXmlDocumentationComments();
                break;

            case "c-family":
                Set(definition, CommentBrush, "Comment");
                Set(definition, StringBrush, "Character", "String");
                Set(definition, IdentifierBrush, "MethodName");
                Set(definition, NumberBrush, "Digits");
                Set(definition, OperatorBrush, "Punctuation", "Operators");
                Set(
                    definition,
                    KeywordBrush,
                    "Preprocessor",
                    "CompoundKeywords",
                    "This",
                    "Namespace",
                    "Friend",
                    "Modifiers",
                    "TypeKeywords",
                    "BooleanConstants",
                    "Keywords",
                    "LoopKeywords",
                    "JumpKeywords",
                    "ExceptionHandling",
                    "ControlFlow");
                EnableCFamilyPreprocessorComments(definition);
                break;

            case "masm-x64":
                Set(definition, CommentBrush, "Comment");
                Set(definition, StringBrush, "String");
                Set(definition, IdentifierBrush, "Register");
                Set(definition, NumberBrush, "Number");
                Set(definition, OperatorBrush, "Operator");
                Set(definition, KeywordBrush, "Instruction", "Directive");
                break;

            case "javascript":
                Set(definition, CommentBrush, "Comment");
                Set(definition, StringBrush, "String", "Character", "Regex");
                Set(definition, NumberBrush, "Digits");
                Set(
                    definition,
                    KeywordBrush,
                    "JavaScriptKeyWords",
                    "JavaScriptIntrinsics",
                    "JavaScriptLiterals",
                    "JavaScriptGlobalFunctions");
                break;

            case "java":
                Set(definition, CommentBrush, "Comment", "CommentTags", "JavaDocTags");
                Set(definition, StringBrush, "String", "Character");
                Set(definition, IdentifierBrush, "MethodName");
                Set(definition, NumberBrush, "Digits");
                Set(definition, OperatorBrush, "Punctuation");
                Set(
                    definition,
                    KeywordBrush,
                    "AccessKeywords",
                    "OperatorKeywords",
                    "SelectionStatements",
                    "IterationStatements",
                    "ExceptionHandlingStatements",
                    "ValueTypes",
                    "ReferenceTypes",
                    "Void",
                    "JumpStatements",
                    "Modifiers",
                    "AccessModifiers",
                    "Package",
                    "Literals");
                break;

            case "cobol":
            case "swift":
                ApplyCommonDefinition(definition, "Keyword");
                break;

            case "python":
                Set(definition, CommentBrush, "Comment");
                Set(definition, StringBrush, "String");
                Set(definition, IdentifierBrush, "MethodCall");
                Set(definition, NumberBrush, "NumberLiteral");
                Set(definition, KeywordBrush, "Keywords");
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(languageId),
                    languageId,
                    "No syntax-highlighting palette is registered for this language.");
        }

        NormalizeNestedCommentColors(definition);
        return definition;
    }

    private static void ApplyCommonDefinition(
        IHighlightingDefinition definition,
        string keywordColorName)
    {
        Set(definition, CommentBrush, "Comment");
        Set(definition, StringBrush, "String");
        Set(definition, NumberBrush, "Number");
        Set(definition, OperatorBrush, "Operator");
        Set(definition, KeywordBrush, keywordColorName);
    }

    private static void ApplyXmlDocumentationComments()
    {
        IHighlightingDefinition xmlDocumentation =
            HighlightingManager.Instance.GetDefinition("XmlDoc") ??
            throw new InvalidOperationException(
                "AvalonEdit's built-in XML documentation highlighting was not found.");

        // C# imports this rule set inside its green comment span. Its nested
        // colors must also be green or XML tags would visually break the rule
        // that comments, and only comments, own the green palette color.
        Set(
            xmlDocumentation,
            CommentBrush,
            "XmlString",
            "DocComment",
            "XmlPunctuation",
            "KnownDocTags");
    }

    private static void NormalizeNestedCommentColors(IHighlightingDefinition definition)
    {
        HighlightingColor comment = definition.GetNamedColor("Comment") ??
            throw new InvalidOperationException(
                $"Highlighting definition '{definition.Name}' omitted required color 'Comment'.");
        var visited = new HashSet<HighlightingRuleSet>();

        Visit(definition.MainRuleSet);

        void Visit(HighlightingRuleSet? ruleSet)
        {
            if (ruleSet is null || !visited.Add(ruleSet))
            {
                return;
            }

            foreach (HighlightingSpan span in ruleSet.Spans)
            {
                if (ReferenceEquals(span.SpanColor, comment))
                {
                    // Built-in C#, Python, and Java definitions contain nested
                    // TODO, HACK, documentation-tag, and numeric marker colors.
                    // Clone those uses so their styles stay intact while their
                    // foreground becomes comment green. Replacing the use also
                    // avoids turning Java's ordinary Digits category green.
                    RecolorRuleSet(span.RuleSet, new HashSet<HighlightingRuleSet>());
                    span.StartColor = CommentVariant(span.StartColor);
                    span.EndColor = CommentVariant(span.EndColor);
                }

                Visit(span.RuleSet);
            }
        }
    }

    private static void EnableCFamilyPreprocessorComments(IHighlightingDefinition definition)
    {
        HighlightingColor preprocessor = definition.GetNamedColor("Preprocessor")!;
        HighlightingColor comment = definition.GetNamedColor("Comment")!;
        HighlightingColor text = definition.GetNamedColor("String")!;
        HighlightingColor character = definition.GetNamedColor("Character")!;
        HighlightingSpan preprocessorSpan = definition.MainRuleSet.Spans.Single(span =>
            ReferenceEquals(span.SpanColor, preprocessor));
        HighlightingSpan[] nestedSpans = definition.MainRuleSet.Spans
            .Where(span =>
                ReferenceEquals(span.SpanColor, comment) ||
                ReferenceEquals(span.SpanColor, text) ||
                ReferenceEquals(span.SpanColor, character))
            .ToArray();
        var nestedRuleSet = new HighlightingRuleSet();

        // AvalonEdit's C++ grammar makes the preprocessor span own the rest of
        // its physical line, so the later top-level comment spans never get a
        // chance to match. Reuse the grammar's own String, Character, //, and
        // /* */ expressions inside that span; only their ownership changes.
        foreach (HighlightingSpan span in nestedSpans)
        {
            nestedRuleSet.Spans.Add(CloneSpan(span));
        }

        preprocessorSpan.RuleSet = nestedRuleSet;
    }

    private static HighlightingSpan CloneSpan(HighlightingSpan original) =>
        new()
        {
            StartExpression = original.StartExpression,
            EndExpression = original.EndExpression,
            RuleSet = original.RuleSet,
            StartColor = original.StartColor,
            SpanColor = original.SpanColor,
            EndColor = original.EndColor,
            SpanColorIncludesStart = original.SpanColorIncludesStart,
            SpanColorIncludesEnd = original.SpanColorIncludesEnd
        };

    private static void RecolorRuleSet(
        HighlightingRuleSet? ruleSet,
        ISet<HighlightingRuleSet> visited)
    {
        if (ruleSet is null || !visited.Add(ruleSet))
        {
            return;
        }

        foreach (HighlightingRule rule in ruleSet.Rules)
        {
            rule.Color = CommentVariant(rule.Color);
        }

        foreach (HighlightingSpan span in ruleSet.Spans)
        {
            span.StartColor = CommentVariant(span.StartColor);
            span.SpanColor = CommentVariant(span.SpanColor);
            span.EndColor = CommentVariant(span.EndColor);
            RecolorRuleSet(span.RuleSet, visited);
        }
    }

    private static HighlightingColor? CommentVariant(HighlightingColor? original)
    {
        if (original is null)
        {
            return null;
        }

        HighlightingColor comment = original.Clone();
        comment.Foreground = CommentBrush;
        return comment;
    }

    private static void Set(
        IHighlightingDefinition definition,
        HighlightingBrush brush,
        params string[] colorNames)
    {
        foreach (string colorName in colorNames)
        {
            HighlightingColor color = definition.GetNamedColor(colorName) ??
                throw new InvalidOperationException(
                    $"Highlighting definition '{definition.Name}' omitted required color '{colorName}'.");
            if (color.IsFrozen)
            {
                throw new InvalidOperationException(
                    $"Highlighting color '{definition.Name}/{colorName}' cannot accept the SMILE palette.");
            }

            color.Foreground = brush;
        }
    }

    private static HighlightingBrush Brush(Color color) => new SimpleHighlightingBrush(color);
}
