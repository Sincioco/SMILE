namespace SMILE.Engine;

// A compilation owns physical sources; imports and Option Explicit belong to
// their source, while a module may collect declarations from several sources.
public sealed record SmileSource(string Path, string Text, string Provider = "local");

public sealed record ModuleDeclarationSyntax(string Name, TextSpan NameSpan,
    IReadOnlyList<SourceItemSyntax> SourceItems, TextSpan Span) : StatementSyntax(Span);

public sealed record ImportStatementSyntax(string Module, string Alias,
    TextSpan AliasSpan, TextSpan Span) : StatementSyntax(Span);

public sealed record VisibilityDeclarationSyntax(bool IsPublic,
    StatementSyntax Declaration, TextSpan Span) : StatementSyntax(Span);

internal sealed class ModuleSymbol(string name, string provider)
{
    public string Name { get; } = name;
    public string Provider { get; } = provider;
    public Dictionary<string, ModuleMember> Members { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, ModuleMember> Types { get; } = new(StringComparer.OrdinalIgnoreCase);
    public IEnumerable<ModuleMember> AllMembers => Members.Values.Concat(Types.Values);
    public List<ModuleSource> Sources { get; } = [];
}

internal sealed record ModuleMember(string Name, string BoundName, bool IsPublic,
    StatementSyntax Declaration, ModuleSymbol Owner);

internal sealed class ModuleSource(SmileSource source, SmileProgramSyntax syntax)
{
    public SmileSource Source { get; } = source;
    public SmileProgramSyntax Syntax { get; } = syntax;
    public ModuleSymbol? Module { get; set; }
    public bool OptionExplicit { get; set; }
    public Dictionary<string, ModuleSymbol> Imports { get; } = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<SourceItemSyntax> Items => Syntax.Statements.FirstOrDefault() is ModuleDeclarationSyntax module
        ? module.SourceItems : Syntax.SourceItems;
}
