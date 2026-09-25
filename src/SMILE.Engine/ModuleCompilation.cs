namespace SMILE.Engine;

// Resolve source-local imports once, before ordinary binding. The existing
// binder still owns exact types, storage, calls and executable semantics.
internal sealed partial class ModuleCompilation
{
    private readonly List<ModuleSource> _sources = [];
    private readonly Dictionary<string, ModuleSymbol> _modules = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Diagnostic> _diagnostics = [];
    private IReadOnlyDictionary<string, IReadOnlySet<string>>? _references;

    public BindResult Bind(IReadOnlyList<SmileSource> sources, bool library,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? references)
    {
        _references = references;
        foreach (SmileSource source in sources)
        {
            ParseResult parsed = new Parser(source.Text, source.Path).Parse();
            _diagnostics.AddRange(parsed.Diagnostics);
            if (parsed.Program is not null) _sources.Add(new ModuleSource(source, parsed.Program));
        }
        if (_diagnostics.Count > 0) return new(null, _diagnostics);
        foreach (ModuleSource source in _sources) Inventory(source, library);
        foreach (ModuleSource source in _sources) ResolveImports(source);
        IReadOnlyList<ModuleSource> ordered = OrderSources();
        if (_diagnostics.Count > 0) return new(null, _diagnostics);
        var items = new List<SourceItemSyntax>();
        foreach (ModuleSource source in ordered)
            items.AddRange(LowerItems(source.Items, source, null, sourceLevel: true));
        if (_diagnostics.Count > 0) return new(null, _diagnostics);
        var syntax = new SmileProgramSyntax(items, default)
        {
            SourceOptions = _sources.ToDictionary(source => source.Source.Path, source => source.OptionExplicit, StringComparer.OrdinalIgnoreCase),
            Modules = ordered.Where(source => source.Module is not null).Select(source => source.Module!).Distinct().ToArray(),
            ModuleSources = _sources.Where(source => source.Module is not null).Select(source => source.Source.Path).ToHashSet(StringComparer.OrdinalIgnoreCase),
            ModuleMembers = _modules.Values.SelectMany(module => module.AllMembers).ToDictionary(member => member.BoundName, StringComparer.OrdinalIgnoreCase)
        };
        BindResult result = new Binder().Bind(syntax);
        if (result.Success) ValidatePublicApi(result.Program!);
        return new(result.Program, result.Diagnostics.Concat(_diagnostics).Select(ReadableDiagnostic).ToArray());
    }

    private Diagnostic ReadableDiagnostic(Diagnostic diagnostic)
    {
        string message = diagnostic.Message;
        foreach (ModuleMember member in _modules.Values.SelectMany(module => module.AllMembers).OrderByDescending(member => member.BoundName.Length))
            message = message.Replace(member.BoundName, member.Owner.Name + "." + member.Name, StringComparison.OrdinalIgnoreCase);
        return diagnostic with { Message = message };
    }

    private void Inventory(ModuleSource source, bool library)
    {
        ModuleDeclarationSyntax[] declarations = source.Syntax.Statements.OfType<ModuleDeclarationSyntax>().ToArray();
        if (declarations.Length > 0)
        {
            ModuleDeclarationSyntax declaration = declarations[0];
            if (declarations.Length != 1 || source.Syntax.Statements.Count != 1)
                Report("SMILE3500", "A source must contain exactly one Module and no statements outside it.", declaration.Span);
            if (!_modules.TryGetValue(declaration.Name, out ModuleSymbol? module))
                _modules.Add(declaration.Name, module = new(declaration.Name, source.Source.Provider));
            else if (!StringComparer.OrdinalIgnoreCase.Equals(module.Provider, source.Source.Provider))
                Report("SMILE3507", $"Module '{module.Name}' has more than one provider.", declaration.NameSpan);
            source.Module = module;
            module.Sources.Add(source);
        }
        else if (library) Report("SMILE3501", "Every library source must declare a Module.", source.Syntax.Span);
        StatementSyntax[] statements = source.Items.OfType<StatementSyntax>().ToArray();
        foreach (OptionExplicitStatementSyntax option in statements.OfType<OptionExplicitStatementSyntax>())
        {
            source.OptionExplicit = true;
            if (!ReferenceEquals(option, statements[0])) Report("SMILE2117", "Option Explicit must be first in its source or Module.", option.Span);
        }
        foreach (StatementSyntax statement in statements)
        {
            if (statement is ImportStatementSyntax or OptionExplicitStatementSyntax) continue;
            StatementSyntax declaration = statement is VisibilityDeclarationSyntax visibility ? visibility.Declaration : statement;
            if (source.Module is null)
            {
                if (statement is VisibilityDeclarationSyntax) Report("SMILE3501", "Public and Private require a Module declaration.", statement.Span);
                if (!ReferenceEquals(source, _sources[0]) && DeclarationName(declaration) is null)
                    Report("SMILE3512", "Executable statements belong in the selected startup source or a routine.", statement.Span);
                continue;
            }
            string? name = DeclarationName(declaration);
            if (name is null) { Report("SMILE3501", "Modules contain declarations, not executable statements.", statement.Span); continue; }
            // Dots cannot occur in authored identifiers; this internal key is
            // unambiguous and also retains readable module ownership.
            bool isType = declaration is InstanceDeclarationSyntax or EnumDeclarationSyntax;
            var member = new ModuleMember(name, source.Module.Name + (isType ? ".type." : ".value.") + name,
                statement is VisibilityDeclarationSyntax { IsPublic: true }, declaration, source.Module);
            if (!(isType ? source.Module.Types : source.Module.Members).TryAdd(name, member))
                Report("SMILE3504", $"Module '{source.Module.Name}' already declares '{name}'.", statement.Span);
        }
    }

    private void ResolveImports(ModuleSource source)
    {
        bool declarations = false;
        var imported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (StatementSyntax statement in source.Items.OfType<StatementSyntax>())
        {
            if (statement is OptionExplicitStatementSyntax) continue;
            if (statement is not ImportStatementSyntax import) { declarations = true; continue; }
            if (declarations) Report("SMILE3506", "Imports must precede declarations and executable statements.", import.Span);
            if (!_modules.TryGetValue(import.Module, out ModuleSymbol? module))
            { Report("SMILE3502", $"Module '{import.Module}' was not found.", import.Span); continue; }
            if (_references is not null && !StringComparer.OrdinalIgnoreCase.Equals(source.Source.Provider, module.Provider) &&
                (!_references.TryGetValue(source.Source.Provider, out IReadOnlySet<string>? direct) || !direct.Contains(module.Provider)))
            { Report("SMILE3528", $"Import '{import.Module}' requires a direct reference to its provider '{module.Provider}'.", import.Span); continue; }
            bool conflict = source.Module is { } owner ? owner.Members.ContainsKey(import.Alias) || owner.Types.ContainsKey(import.Alias)
                : _sources.Where(item => item.Module is null).Any(item => ProjectDeclarations(item).Contains(import.Alias));
            if (conflict || !imported.Add(import.Module) || !source.Imports.TryAdd(import.Alias, module))
                Report("SMILE3506", $"Import alias '{import.Alias}' or module '{import.Module}' is already in use.", import.AliasSpan);
        }
    }

    private HashSet<string> ProjectDeclarations(ModuleSource source)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectLocals(source.Items, source, names);
        foreach (StatementSyntax declaration in source.Items.OfType<StatementSyntax>())
            if (DeclarationName(declaration) is string name) names.Add(name);
        return names;
    }

    private IReadOnlyList<ModuleSource> OrderSources()
    {
        var output = new List<ModuleSource>();
        var state = new Dictionary<ModuleSymbol, int>();
        foreach (ModuleSource source in _sources) if (source.Module is { } module) Visit(module);
        // Support-source initializers precede startup; startup stays at its
        // authored position. The first input is the selected startup source.
        output.AddRange(_sources.Skip(1).Where(source => source.Module is null));
        if (_sources.FirstOrDefault() is { Module: null } startup) output.Add(startup);
        return output;

        void Visit(ModuleSymbol module)
        {
            if (state.TryGetValue(module, out int value))
            {
                if (value == 1) Report("SMILE3508", $"Circular module import includes '{module.Name}'.", module.Sources[0].Syntax.Span);
                return;
            }
            state[module] = 1;
            foreach (ModuleSymbol dependency in module.Sources.SelectMany(source => source.Imports.Values).Distinct()) Visit(dependency);
            state[module] = 2;
            output.AddRange(module.Sources);
        }
    }

    private static string? DeclarationName(StatementSyntax statement) => statement switch
    {
        DimStatementSyntax item => item.Name, ConstStatementSyntax item => item.Name,
        RoutineDeclarationSyntax item => item.Name, InstanceDeclarationSyntax item => item.Name,
        EnumDeclarationSyntax item => item.Name, _ => null
    };

    private void Report(string code, string message, TextSpan span) =>
        _diagnostics.Add(new(code, DiagnosticSeverity.Error, message, span));
}
