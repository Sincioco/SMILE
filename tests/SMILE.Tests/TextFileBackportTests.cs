using System.IO;
using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Tests;

[TestClass]
[TestCategory("TextFileBackport")]
public sealed class TextFileBackportTests
{
    private const string Source = """
Option Explicit
Dim Bytes[12] As Number
Dim Small[2] As Number
Dim Large[4105] As Number
Dim ByteCount As Number
Dim Calls As Number
Dim pathlib As Number
Dim smileLoadTextFile As Number
pathlib = 5
smileLoadTextFile = 6
Print pathlib + smileLoadTextFile
Bytes[11] = 99
Load Text File GetPath() Into Bytes Count ByteCount
Print ByteCount; ":"; Calls; ":"; Bytes[0]; ":"; Bytes[1]; ":"; Bytes[5]; ":"; Bytes[6]; ":"; Bytes[11]
Load Text File "text-load/plain.txt" Into Small Count ByteCount
Print ByteCount; ":"; Small[0]; ":"; Small[1]
Load Text File "text-load/short.txt" Into Bytes Count ByteCount
Print ByteCount; ":"; Bytes[0]; ":"; Bytes[1]; ":"; Bytes[11]
Load Text File "text-load/empty.txt" Into Bytes Count ByteCount
Print ByteCount; ":"; Bytes[0]
Bytes[0] = 99
Load Text File "text-load/missing.txt" Into Bytes Count ByteCount
Print ByteCount; ":"; Bytes[0]
Bytes[0] = 99
Load Text File "text-load" Into Bytes Count ByteCount
Print ByteCount; ":"; Bytes[0]
Load Text File "../text-load/plain.txt" Into Bytes Count ByteCount
Print ByteCount
Load Text File "/text-load/plain.txt" Into Bytes Count ByteCount
Print ByteCount
Load Text File "https://example.invalid/file" Into Bytes Count ByteCount
Print ByteCount
Load Text File "text-load/large.txt" Into Large Count ByteCount
Print ByteCount; ":"; Large[4100]; ":"; Large[4101]
Print ReadLocal()
Print ByteCount
End Program
Function GetPath() As Text
    Calls = Calls + 1
    Return "text-load\\nested/.././" + "data-é.txt"
End Function
Function ReadLocal() As Number
    Dim LocalBytes[4] As Number
    Load Text File "text-load/plain.txt" Into LocalBytes Count ByteCount
    Return ByteCount + LocalBytes[2]
End Function
""";
    private const string Expected = "11\n7:1:65:240:0:66:0\n2:88:89\n1:81:0:0\n0:0\n0:0\n0:0\n0\n0\n0\n4101:65:0\n93\n3\n";

    private static readonly IReadOnlyDictionary<string, string> Assets = new Dictionary<string, string>
    {
        ["text-load/data-é.txt"] = "\uFEFFA😀\0B",
        ["text-load/plain.txt"] = "XYZ",
        ["text-load/short.txt"] = "Q",
        ["text-load/empty.txt"] = "",
        ["text-load/large.txt"] = new string('A', 4101)
    };

    [TestMethod]
    [TestCategory("MissionGuardrail")]
    public void Text_file_evaluation_matches_the_bounded_byte_contract()
    {
        var host = new FixtureFiles();
        EvaluationResult result = new SmileEvaluator().Evaluate(Source, new SmileEvaluationOptions(Files: host));
        Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
        Assert.AreEqual(Expected, result.Output);
        Assert.AreEqual(8, host.Opens);
        SmileFormatResult formatted = SmileSourceFormatter.Format(Source);
        Assert.IsTrue(formatted.Success, string.Join("\n", formatted.Diagnostics));
        Assert.AreEqual(Expected, new SmileEvaluator().Evaluate(formatted.FormattedSource, new SmileEvaluationOptions(Files: new FixtureFiles())).Output);
    }

    [TestMethod]
    [DataRow("Dim Bytes[2] As Number\nLoad Text File 3 Into Bytes Count ByteCount")]
    [DataRow("Dim Bytes[2] As Number\nLoad Text File \" \" Into Bytes Count ByteCount")]
    [DataRow("Dim Bytes[2, 2] As Number\nLoad Text File \"x\" Into Bytes Count ByteCount")]
    [DataRow("Dim Bytes[2] As Double\nLoad Text File \"x\" Into Bytes Count ByteCount")]
    [DataRow("Dim Bytes[2] As Number\nConst ByteCount = 2\nLoad Text File \"x\" Into Bytes Count ByteCount")]
    [DataRow("Load Text File \"x\" Into Missing Count ByteCount")]
    public void Text_file_invalid_types_and_locations_are_rejected(string source) =>
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
    public async Task Text_file_program_runs_on_native_file_apis(TargetLanguage target)
    {
        TranspileResult transpile = new SmileTranspiler().Transpile(Source, target);
        Assert.IsTrue(transpile.Success, string.Join("\n", transpile.Diagnostics));
        GeneratedProgram program = transpile.GeneratedProgram!;
        string assetPrefix = target is TargetLanguage.CSharp ? "bin/Debug/net10.0/" : "";
        program = program with { Files = program.Files.Concat(Assets.Select(pair => new GeneratedFile(assetPrefix + pair.Key, pair.Value, IsPrimary: false))).ToArray() };
        BuildRunResult result = await ToolchainRegistry.CreateDefault().Get(target).BuildAndRunAsync(program, CancellationToken.None);
        Assert.IsTrue(result.Success, $"{target}: {result.BuildOutput}\n{result.StandardError}");
        Assert.AreEqual(Expected, result.StandardOutput.Replace("\r\n", "\n"), target.ToString());
        Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(result.BuildOutput, @"(?im)\bwarning(?:\s+[A-Z]+\d+)?\s*:"), result.BuildOutput);
    }

    private sealed class FixtureFiles : ISmileFileHost
    {
        public int Opens { get; private set; }
        public Stream? OpenRead(string relativePath)
        {
            Opens++;
            return Assets.TryGetValue(relativePath, out string? contents)
                ? new MemoryStream(System.Text.Encoding.UTF8.GetBytes(contents)) : null;
        }
    }

    [TestMethod]
    public void A_read_failure_discards_partial_bytes_and_closes_the_stream()
    {
        const string source = "Dim Bytes[4] As Number\nLoad Text File \"broken.txt\" Into Bytes Count ByteCount\nPrint ByteCount; \":\"; Bytes[0]";
        var host = new FailingFiles();
        EvaluationResult result = new SmileEvaluator().Evaluate(source, new SmileEvaluationOptions(Files: host));
        Assert.IsTrue(result.Success);
        Assert.AreEqual("0:0\n", result.Output);
        Assert.IsFalse(host.Stream.CanRead);
    }

    private sealed class FailingFiles : ISmileFileHost
    {
        public Stream Stream { get; } = new FailingStream();
        public Stream OpenRead(string relativePath) => Stream;
    }

    private sealed class FailingStream() : MemoryStream(new byte[] { 65, 66, 67, 68 })
    {
        public override int Read(Span<byte> buffer) => Position >= 3
            ? throw new IOException("Injected read failure.") : base.Read(buffer);
    }
}
