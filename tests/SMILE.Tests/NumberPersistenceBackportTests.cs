using System.IO;
using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Tests;

[TestClass]
[TestCategory("NumberPersistenceBackport")]
public sealed class NumberPersistenceBackportTests
{
    private const string Source = """
Const Maximum = 9223372036854775807
Dim Saved As Number
Load Saved From "missing" Default Fallback()
Print Saved
Saved = -9223372036854775807 - 1
Save Saved To "minimum"
Saved = 0
Load Saved From "minimum" Default Fallback()
Print Saved
Save Maximum To "maximum"
Load Saved From "maximum" Default 0
Print Saved
Saved = 147
Save Saved To "Café!😀"
Load Saved From "Café!😀" Default 0
Print Saved
Call InRoutine(Saved)
Print Saved
Load Saved From "valid" Default 99
Print Saved
Load Saved From "trailing" Default 99
Print Saved
Load Saved From "overflow" Default 99
Print Saved
Load Saved From "vertical" Default 99
Print Saved
Load Saved From "nul" Default 99
Print Saved
Load Saved From "long" Default 99
Print Saved
Load Saved From "folder" Default 99
Print Saved
Save Saved To "folder"
Function Fallback() As Number
    Print "fallback evaluated"
    Return 7
End Function
Sub InRoutine(ByRef Value As Number)
    Load Value From "maximum" Default 0
    Save Value To "routine"
End Sub
""";
    private const string Expected = "fallback evaluated\n7\nfallback evaluated\n-9223372036854775808\n9223372036854775807\n147\n9223372036854775807\n123\n99\n99\n99\n99\n7\n99\n";

    [TestMethod]
    [TestCategory("MissionGuardrail")]
    public void Integer_storage_preserves_defaults_exact_values_and_file_format()
    {
        using var fixture = new StorageFixture();
        var storage = new SmilePersistentStorage(fixture.ProgramName);
        EvaluationResult result = new SmileEvaluator().Evaluate(Source, new SmileEvaluationOptions(Storage: storage));
        Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
        Assert.AreEqual(Expected, result.Output);
        fixture.AssertSavedFiles();
        Assert.AreEqual(long.MinValue, new SmilePersistentStorage(fixture.ProgramName).LoadNumber("minimum", 0));
        SmileFormatResult formatted = SmileSourceFormatter.Format(Source);
        Assert.IsTrue(formatted.Success, string.Join("\n", formatted.Diagnostics));
        Assert.AreEqual(Expected, new SmileEvaluator().Evaluate(formatted.FormattedSource, new SmileEvaluationOptions(Storage: storage)).Output);
    }

    [TestMethod]
    [DataRow("Load Score From \"x\" Default True")]
    [DataRow("Load Score From \"x\" Default 1.0")]
    [DataRow("Const Score = 1\nLoad Score From \"x\" Default 0")]
    [DataRow("Dim Score As Text\nSave Score To \"x\"")]
    [DataRow("Save Missing To \"x\"")]
    [DataRow("Load Score From \" \" Default 0")]
    [DataRow("Dim KeyName As Text\nLoad Score From KeyName Default 0")]
    [DataRow("Dim Score[2] As Number\nSave Score To \"x\"")]
    public void Integer_storage_requires_Number_values_and_literal_keys(string source) =>
        Assert.IsFalse(new SmileTranspiler().Bind(source).Success);

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
    public async Task Integer_storage_runs_on_all_ten_native_file_apis(TargetLanguage target)
    {
        using var fixture = new StorageFixture();
        TranspileResult transpile = new SmileTranspiler().Transpile(Source, target, fixture.ProgramName);
        Assert.IsTrue(transpile.Success, string.Join("\n", transpile.Diagnostics));
        BuildRunResult result = await ToolchainRegistry.CreateDefault().Get(target).BuildAndRunAsync(transpile.GeneratedProgram!, CancellationToken.None);
        Assert.IsTrue(result.Success, $"{target}: {result.BuildOutput}\n{result.StandardError}");
        Assert.AreEqual(Expected, result.StandardOutput.Replace("\r\n", "\n"), target.ToString());
        fixture.AssertSavedFiles();
        Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(result.BuildOutput, @"(?im)\bwarning(?:\s+[A-Z]+\d+)?\s*:"), result.BuildOutput);
    }

    private sealed class StorageFixture : IDisposable
    {
        public string ProgramName { get; } = "NumberBackport_" + Guid.NewGuid().ToString("N");
        private readonly string _folder;
        public StorageFixture()
        {
            _folder = Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA")!, "SMILE", "Games", ProgramName);
            Directory.CreateDirectory(_folder);
            foreach ((string key, string value) in new[]
            {
                ("valid", " \t+123\r\n"), ("trailing", "12x"), ("overflow", "9223372036854775808"),
                ("vertical", "\v12"), ("nul", "12\0"), ("long", "7" + new string(' ', 70) + "x")
            }) File.WriteAllText(Path.Combine(_folder, key + ".txt"), value);
            Directory.CreateDirectory(Path.Combine(_folder, "folder.txt"));
        }

        public void AssertSavedFiles()
        {
            Assert.AreEqual("-9223372036854775808", File.ReadAllText(Path.Combine(_folder, "minimum.txt")));
            Assert.AreEqual("9223372036854775807", File.ReadAllText(Path.Combine(_folder, "maximum.txt")));
            Assert.AreEqual("9223372036854775807", File.ReadAllText(Path.Combine(_folder, "routine.txt")));
            Assert.AreEqual("147", File.ReadAllText(Path.Combine(_folder, "Caf____.txt")));
        }

        public void Dispose()
        {
            foreach (string key in new[] { "valid", "trailing", "overflow", "vertical", "nul", "long", "minimum", "maximum", "routine", "Caf____" })
                File.Delete(Path.Combine(_folder, key + ".txt"));
            Directory.Delete(Path.Combine(_folder, "folder.txt"));
            Directory.Delete(_folder);
        }
    }
}
