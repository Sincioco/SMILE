using System.IO;
using System.Security.Cryptography;
using System.Text;
using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Tests;

[TestClass]
[TestCategory("DataPersistenceBackport")]
public sealed class DataPersistenceBackportTests
{
    private const string Source = """
Dim Bytes[4] As Number
Dim Destination[4] As Number
Dim Small[1] As Number
Dim Results[2, 2] As Number
Dim CountValue As Number
Dim Status As Number
Bytes[0] = 0
Bytes[1] = 255
Bytes[2] = 65
Destination[3] = 99
Save Data Bytes Count SaveCount() To SaveKey() Status Results[StatusIndex(), 0]
Print Results[1, 0]
Load Data SaveKey() Into Destination Count Results[CountIndex(), 0] Status Results[StatusIndex(), 1]
Print Results[0, 0]; ":"; Results[1, 1]; ":"; Destination[0]; ":"; Destination[1]; ":"; Destination[2]; ":"; Destination[3]
Load Data "missing" Into Destination Count CountValue Status Status
Print CountValue; ":"; Status; ":"; Destination[3]
Load Data "corrupt" Into Destination Count CountValue Status Status
Print CountValue; ":"; Status; ":"; Destination[3]
Load Data "recover" Into Destination Count CountValue Status Status
Print CountValue; ":"; Status; ":"; Destination[0]; ":"; Destination[3]
Load Data "missing-primary" Into Destination Count CountValue Status Status
Print CountValue; ":"; Status; ":"; Destination[0]
Load Data "large" Into Small Count CountValue Status Status
Print CountValue; ":"; Status; ":"; Small[0]
Load Data "checksum" Into Destination Count CountValue Status Status
Print CountValue; ":"; Status
Load Data "signature" Into Destination Count CountValue Status Status
Print CountValue; ":"; Status
Load Data "folder" Into Destination Count CountValue Status Status
Print CountValue; ":"; Status
Bytes[0] = 256
Save Data Bytes Count 1 To "invalid" Status Status
Print Status
Save Data Bytes Count -1 To "invalid" Status Status
Print Status
Bytes[0] = 7
Save Data Bytes Count 1 To "recover" Status Status
Print Status
Load Data "recover" Into Destination Count CountValue
Print CountValue; ":"; Destination[0]; ":"; Destination[3]
Load Data "missing" Into Destination Count CountValue
Print CountValue; ":"; Destination[0]
Save Data Bytes Count 0 To ""
Load Data "" Into Destination Count CountValue Status Status
Print CountValue; ":"; Status
Call LocalData(CountValue)
Print CountValue
Print DATA_BLOCK_MAX_BYTES; ":"; DATA_STATUS_OK; ":"; DATA_STATUS_MISSING; ":"; DATA_STATUS_RECOVERED; ":"; DATA_STATUS_INVALID; ":"; DATA_STATUS_UNAVAILABLE; ":"; DATA_STATUS_CORRUPT; ":"; DATA_STATUS_TOO_LARGE
Function SaveCount() As Number
    Print "count"
    Return 3
End Function
Function SaveKey() As Text
    Print "key"
    Return "Café😀"
End Function
Function CountIndex() As Number
    Print "count location:"; Destination[1]
    Return 0
End Function
Function StatusIndex() As Number
    Print "status location"
    Return 1
End Function
Sub LocalData(ByRef Value As Number)
    Dim LocalBytes[2] As Number
    Load Data "recover" Into LocalBytes Count Value
    Save Data LocalBytes Count Value To "routine"
End Sub
""";
    private const string Expected = "count\nkey\nstatus location\n0\nkey\ncount location:255\nstatus location\n3:0:0:255:65:99\n0:1:99\n0:5:99\n2:2:41:99\n1:2:88\n0:6:0\n0:5\n0:5\n0:4\n3\n3\n0\n1:7:0\n0:0\n0:0\n1\n1048576:0:1:2:3:4:5:6\n";

    [TestMethod]
    [TestCategory("MissionGuardrail")]
    public void Data_storage_preserves_envelopes_recovery_and_destination_order()
    {
        using var fixture = new DataFixture();
        EvaluationResult result = new SmileEvaluator().Evaluate(Source, new SmileEvaluationOptions(Storage: fixture.Storage));
        Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
        Assert.AreEqual(Expected, result.Output);
        fixture.AssertSavedFiles();
        Assert.IsTrue(SmileSourceFormatter.Format(Source).Success);
    }

    [TestMethod]
    [DataRow("Dim Bytes[2] As Text\nLoad Data \"key\" Into Bytes Count Length")]
    [DataRow("Dim Bytes[2, 2] As Number\nLoad Data \"key\" Into Bytes Count Length")]
    [DataRow("Dim Bytes[2] As Number\nLoad Data 1 Into Bytes Count Length")]
    [DataRow("Dim Bytes[2] As Number\nDim Flag As Boolean\nLoad Data \"key\" Into Bytes Count Flag")]
    [DataRow("Dim Bytes[2] As Number\nSave Data Bytes Count 1.0 To \"key\"")]
    [DataRow("Dim Bytes[2] As Number\nConst State = 0\nSave Data Bytes Count 1 To \"key\" Status State")]
    [DataRow("Option Explicit\nDim Bytes[2] As Number\nLoad Data \"key\" Into Bytes Count Missing")]
    public void Data_operands_require_exact_types_and_writable_outputs(string source) =>
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
    public async Task Strict_Data_failures_stop_after_prior_output(TargetLanguage target)
    {
        using var fixture = new DataFixture();
        foreach (string operation in new[] { "Load Data \"corrupt\" Into Bytes Count CountValue", "Save Data Bytes Count 1 To \"invalid\"" })
        {
            string source = "Dim Bytes[2] As Number\nDim CountValue As Number\nBytes[0] = 256\nPrint \"before\"\n" + operation + "\nPrint \"after\"";
            TranspileResult transpile = new SmileTranspiler().Transpile(source, target, fixture.ProgramName);
            Assert.IsTrue(transpile.Success, string.Join("\n", transpile.Diagnostics));
            var result = await ToolchainRegistry.CreateDefault().Get(target).BuildAndRunAsync(transpile.GeneratedProgram!, CancellationToken.None);
            Assert.AreEqual("Running", result.Stage, result.BuildOutput);
            Assert.AreEqual(2, result.ExitCode, result.StandardError);
            Assert.IsFalse(result.Success);
            Assert.AreEqual("before\n", result.StandardOutput.Replace("\r\n", "\n"));
            StringAssert.Contains(result.StandardError, operation.StartsWith("Load") ? "Load Data" : "Save Data");
            EvaluationResult evaluated = new SmileEvaluator().Evaluate(source, new SmileEvaluationOptions(Storage: fixture.Storage));
            Assert.IsFalse(evaluated.Success);
            Assert.AreEqual("before\n", evaluated.Output);
        }
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
    public async Task Data_storage_runs_on_all_ten_targets(TargetLanguage target)
    {
        using var fixture = new DataFixture();
        TranspileResult transpile = new SmileTranspiler().Transpile(Source, target, fixture.ProgramName);
        Assert.IsTrue(transpile.Success, string.Join("\n", transpile.Diagnostics));
        var result = await ToolchainRegistry.CreateDefault().Get(target).BuildAndRunAsync(transpile.GeneratedProgram!, CancellationToken.None);
        Assert.IsTrue(result.Success, $"{target}: {result.BuildOutput}\n{result.StandardError}");
        Assert.AreEqual(Expected, result.StandardOutput.Replace("\r\n", "\n"), target.ToString());
        fixture.AssertSavedFiles();
        Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(result.BuildOutput, @"(?im)\bwarning(?:\s+[A-Z]+\d+)?\s*:"), result.BuildOutput);
    }

    private sealed class DataFixture : IDisposable
    {
        public string ProgramName { get; } = "DataBackport_" + Guid.NewGuid().ToString("N");
        public SmilePersistentStorage Storage { get; }
        private readonly string _folder;
        public DataFixture()
        {
            Storage = new SmilePersistentStorage(ProgramName);
            _folder = Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA")!, "SMILE", "Games", Hash(ProgramName), "Data");
            Assert.AreEqual(SmileDataStatus.Ok, Storage.SaveData(new long[] { 41, 42 }, 2, "recover"));
            Assert.AreEqual(SmileDataStatus.Ok, Storage.SaveData(new long[] { 99 }, 1, "recover"));
            File.WriteAllText(PathFor("recover"), "corrupt");
            File.WriteAllText(PathFor("corrupt"), "corrupt");
            Assert.AreEqual(SmileDataStatus.Ok, Storage.SaveData(new long[] { 88 }, 1, "missing-primary"));
            File.Move(PathFor("missing-primary"), PathFor("missing-primary") + ".bak");
            Assert.AreEqual(SmileDataStatus.Ok, Storage.SaveData(new long[] { 1, 2 }, 2, "large"));
            byte[] bytes = File.ReadAllBytes(PathFor("large"));
            bytes[44] ^= 1;
            File.WriteAllBytes(PathFor("checksum"), bytes);
            bytes[44] ^= 1;
            bytes[0] |= 128;
            File.WriteAllBytes(PathFor("signature"), bytes);
            Directory.CreateDirectory(PathFor("folder"));
        }

        private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        private string PathFor(string key) => Path.Combine(_folder, Hash(key) + ".bin");
        public void AssertSavedFiles()
        {
            long[] bytes = new long[4];
            Assert.AreEqual(new SmileDataResult(3, SmileDataStatus.Ok), Storage.LoadData("Café😀", bytes));
            CollectionAssert.AreEqual(new long[] { 0, 255, 65, 0 }, bytes);
            Assert.AreEqual(new SmileDataResult(1, SmileDataStatus.Ok), Storage.LoadData("routine", bytes));
            Assert.AreEqual(7, bytes[0]);
            Assert.IsFalse(File.Exists(PathFor("invalid")));
            byte[] backup = File.ReadAllBytes(PathFor("recover") + ".bak");
            CollectionAssert.AreEqual(new byte[] { 41, 42 }, backup[44..]);
            byte[] envelope = File.ReadAllBytes(PathFor("Café😀"));
            CollectionAssert.AreEqual("SMD4"u8.ToArray(), envelope[..4]);
            Assert.HasCount(47, envelope);
            CollectionAssert.AreEqual(SHA256.HashData(envelope[44..]), envelope[12..44]);
        }

        public void Dispose()
        {
            foreach (string key in new[] { "recover", "corrupt", "missing-primary", "large", "checksum", "signature", "Café😀", "invalid", "", "routine" })
            {
                File.Delete(PathFor(key));
                File.Delete(PathFor(key) + ".bak");
            }
            Directory.Delete(PathFor("folder"));
            Directory.Delete(_folder);
            Directory.Delete(Path.GetDirectoryName(_folder)!);
        }
    }
}
