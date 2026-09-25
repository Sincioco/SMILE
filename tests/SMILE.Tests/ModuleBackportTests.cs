using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Tests;

[TestClass]
[TestCategory("ModuleBackport")]
public sealed class ModuleBackportTests
{
    private const string Startup = """
Option Explicit
Import Samples.Counting As Counts
Import Samples.Tools As Tools
Dim Current As Counts.Box
Dim Position As Counts.Point
Dim State As Counts.State
Print Counts.Total
Current = New Counts.Box(7)
Call Counts.Store(Current.Value)
Call Tools.Increment(Current.Value)
Position.X = Current.Value
State = Counts.State.Ready
Print Counts.Stored(); ":"; Position.X; ":"; State = Counts.State.Ready
Print Counts.Caption()
With Counts.Initial
    .Value = .Value + 1
End With
Print Counts.Initial.Value; ":"; Current Is Not Nothing
""";

    private const string Counting = """
Module Samples.Counting
    Option Explicit
    Private Const Prefix = "count:"
    Public Dim Total As Number
    Public Dim Values[2] As Number
    Public Dim Initial As New Box(5)
    Public Enum State
        Ready = 1
    End Enum
    Public Type Point
        X As Number
    End Type
    Public Class Box
        Public Value As Number
        Sub New(Value As Number)
            Me.Value = Value
            Total = Total + 1
        End Sub
    End Class
End Module
""";

    private const string Extension = """
Module Samples.Counting
    Public Function Caption() As Text
        Return Prefix
    End Function
    Public Sub Store(Value As Number)
        Values[1] = Value
    End Sub
    Public Function Stored() As Number
        Return Values[1]
    End Function
End Module
""";

    private const string Helpers = """
Module Samples.Tools
    Option Explicit
    Import Samples.Counting As Counts
    Public Sub Increment(ByRef Value As Number)
        Value = Value + 1
    End Sub
End Module
""";

    private static SmileSource[] Sources =>
    [
        new("startup.smile", Startup),
        new("tools.smile", Helpers), new("counting.smile", Counting), new("extension.smile", Extension)
    ];

    [TestMethod]
    public void Module_type_and_value_names_have_separate_namespaces()
    {
        SmileSource[] sources = [new("startup.smile", "Import Sample As Lib\nLib.Mode = 3\nPrint Lib.Mode; \":\"; Lib.Mode = Lib.Read(); \":\"; Lib.ModeState = Lib.Mode.Ready"),
            new("library.smile", "Module Sample\nPublic Enum Mode\nReady = 0\nEnd Enum\nPublic Dim Mode As Number\nPublic Dim ModeState As Mode\nPublic Function Read() As Number\nReturn Mode\nEnd Function\nEnd Module")];
        EvaluationResult result = new SmileEvaluator().EvaluateSources(sources);
        Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
        Assert.AreEqual("3:True:True\n", result.Output);
    }

    [TestMethod]
    public void Import_aliases_cannot_collide_with_implicit_application_globals()
    {
        BindResult result = new SmileTranspiler().BindSources([new("startup.smile", "Import Sample As Alias\nAlias = 4"), new("library.smile", "Module Sample\nEnd Module")]);
        Assert.IsTrue(result.Diagnostics.Any(item => item.Code == "SMILE3506"));
    }

    [TestMethod]
    public async Task Cobol_module_routine_identity_truncation_does_not_end_in_a_hyphen()
    {
        SmileSource[] sources = [new("main.smile", "Import ABCDEFGHIJKLMNOPQRSTUV As Lib\nPrint Lib.Read()"),
            new("module.smile", "Module ABCDEFGHIJKLMNOPQRSTUV\nPublic Function Read() As Number\nReturn 5\nEnd Function\nEnd Module")];
        TranspileResult transpile = new SmileTranspiler().TranspileSources(sources, [TargetLanguage.Cobol]).Single();
        Assert.IsTrue(transpile.Success, string.Join("\n", transpile.Diagnostics));
        BuildRunResult run = await ToolchainRegistry.CreateDefault().Get(TargetLanguage.Cobol).BuildAndRunAsync(transpile.GeneratedProgram!, CancellationToken.None);
        Assert.IsTrue(run.Success, run.BuildOutput + run.StandardError);
        Assert.AreEqual("5\n", run.StandardOutput.Replace("\r\n", "\n"));
    }

    [TestMethod]
    public void Source_local_imports_split_modules_and_initializers_evaluate()
    {
        EvaluationResult result = new SmileEvaluator().EvaluateSources(Sources);
        Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics) + result.RuntimeError);
        Assert.AreEqual("1\n7:8:True\ncount:\n6:True\n", result.Output);
    }

    [TestMethod]
    [DataRow(TargetLanguage.CSharp)]
    [DataRow(TargetLanguage.C)]
    [DataRow(TargetLanguage.MasmX64)]
    [DataRow(TargetLanguage.JavaScript)]
    [DataRow(TargetLanguage.Java)]
    [DataRow(TargetLanguage.Cobol)]
    [DataRow(TargetLanguage.ObjectiveC)]
    [DataRow(TargetLanguage.Swift)]
    [DataRow(TargetLanguage.Python)]
    [DataRow(TargetLanguage.Cpp)]
    public async Task Modules_execute_on_every_target(TargetLanguage target)
    {
        TranspileResult transpile = new SmileTranspiler().TranspileSources(Sources, [target]).Single();
        Assert.IsTrue(transpile.Success, string.Join("\n", transpile.Diagnostics));
        BuildRunResult run = await ToolchainRegistry.CreateDefault().Get(target).BuildAndRunAsync(transpile.GeneratedProgram!, CancellationToken.None);
        Assert.IsTrue(run.Success, $"{target}: {run.BuildOutput}\n{run.StandardError}");
        Assert.AreEqual("1\n7:8:True\ncount:\n6:True\n", run.StandardOutput.Replace("\r\n", "\n"));
    }

    [TestMethod]
    [DataRow("Import Samples.Counting As C\nPrint C.Prefix", "SMILE3505")]
    [DataRow("Import Samples.Counting As C\nPrint C.Missing", "SMILE3503")]
    [DataRow("Import Missing As C", "SMILE3502")]
    [DataRow("Print 1\nImport Samples.Counting As C", "SMILE3506")]
    [DataRow("Import Samples.Counting As C\nImport Samples.Counting As Other", "SMILE3506")]
    public void Invalid_imports_keep_the_physical_source_location(string source, string code)
    {
        BindResult result = new SmileTranspiler().BindSources([new("bad.smile", source), new("counting.smile", Counting)]);
        Assert.IsFalse(result.Success);
        Diagnostic diagnostic = result.Diagnostics.First(item => item.Code == code);
        Assert.AreEqual("bad.smile", diagnostic.Span.SourcePath);
    }

    [TestMethod]
    public void Modules_cannot_access_consumer_globals_and_imports_do_not_leak_between_sources()
    {
        BindResult result = new SmileTranspiler().BindSources([new("main.smile", "Dim Consumer As Number"),
            new("module.smile", "Module Example\nPublic Function Read() As Number\nReturn Consumer\nEnd Function\nEnd Module")]);
        Assert.IsTrue(result.Diagnostics.Any(item => item.Code == "SMILE3510"));
        result = new SmileTranspiler().BindSources([new("main.smile", "Import Samples.Counting As C"),
            new("other.smile", "Dim Value As C.Box"), new("counting.smile", Counting)]);
        Assert.IsTrue(result.Diagnostics.Any(item => item.Code == "SMILE3502" && item.Span.SourcePath == "other.smile"));
    }

    [TestMethod]
    public void Module_implicit_locals_do_not_capture_consumer_globals()
    {
        EvaluationResult result = new SmileEvaluator().EvaluateSources([
            new("main.smile", "Import Sample As SampleApi\nDim Local As Text\nLocal = \"consumer\"\nPrint SampleApi.Read(); \":\"; Local"),
            new("module.smile", "Module Sample\nPublic Function Read() As Number\nLocal = 7\nReturn Local\nEnd Function\nEnd Module")]);
        Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
        Assert.AreEqual("7:consumer\n", result.Output);
    }

    [TestMethod]
    public void Option_Explicit_is_local_to_the_physical_source()
    {
        EvaluationResult result = new SmileEvaluator().EvaluateSources([
            new("main.smile", "Option Explicit\nPrint Read()"),
            new("support.smile", "Function Read() As Number\nLocal = 4\nReturn Local\nEnd Function")]);
        Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
        Assert.AreEqual("4\n", result.Output);
    }

    [TestMethod]
    public void Public_signatures_cannot_expose_private_types()
    {
        BindResult result = new SmileTranspiler().BindSources([new("module.smile",
            "Module Sample\nType Hidden\nX As Number\nEnd Type\nPublic Function Read() As Hidden\nDim Value As Hidden\nReturn Value\nEnd Function\nEnd Module")]);
        Assert.IsTrue(result.Diagnostics.Any(item => item.Code == "SMILE3513"));
    }

    [TestMethod]
    public void Import_cycles_and_executable_support_sources_are_rejected()
    {
        BindResult result = new SmileTranspiler().BindSources([new("main.smile", "Print 1"),
            new("first.smile", "Module First\nImport Second As NextModule\nEnd Module"),
            new("second.smile", "Module Second\nImport First As NextModule\nEnd Module")]);
        Assert.IsTrue(result.Diagnostics.Any(item => item.Code == "SMILE3508"));
        result = new SmileTranspiler().BindSources([new("main.smile", "Print 1"), new("support.smile", "Print 2")]);
        Assert.IsTrue(result.Diagnostics.Any(item => item.Code == "SMILE3512"));
    }

    [TestMethod]
    public void Module_formatting_preserves_import_context_and_initialization()
    {
        SmileSource[] sources = Sources;
        foreach (SmileSource source in sources)
        {
            SmileFormatResult result = SmileSourceFormatter.Format(source, sources);
            Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
            Assert.AreEqual(result.FormattedSource, SmileSourceFormatter.Format(source with { Text = result.FormattedSource }, sources).FormattedSource);
        }
    }
}
