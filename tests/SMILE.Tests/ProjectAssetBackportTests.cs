using System.IO;
using SMILE.Desktop;
using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Tests;

[TestClass]
[TestCategory("ProjectBackport")]
public sealed class ProjectAssetBackportTests
{
    private static string CreateProject(string include = "assets/**/*.txt")
    {
        string root = Path.Combine(Path.GetTempPath(), "SMILE", "Runs", "project-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        File.WriteAllText(Path.Combine(root, "assets", "letters.txt"), "ABCD");
        File.WriteAllText(Path.Combine(root, "Program.smile"), "Import Sample.Storage As DataApi\nLoad Text File \"assets/letters.txt\" Into DataApi.Bytes Count DataApi.ByteCount\nPrint DataApi.ByteCount; \":\"; DataApi.Bytes[0]; \":\"; DataApi.Bytes[3]");
        File.WriteAllText(Path.Combine(root, "Data.smile"), "Module Sample.Storage\nPublic Dim Bytes[8] As Number\nPublic Dim ByteCount As Number\nEnd Module");
        string path = Path.Combine(root, "Example.smileproj");
        File.WriteAllText(path, $"<SmileProject><PropertyGroup><ApplicationId>org.smile.project-example</ApplicationId></PropertyGroup><ItemGroup><SmileSource Include='Program.smile'/><SmileSource Include='Data.smile'/><Asset Include='{include}'/></ItemGroup></SmileProject>");
        return path;
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
    public async Task Project_assets_are_published_beside_each_native_program(TargetLanguage target)
    {
        string path = CreateProject(), identity = "org.smile.project-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(path, File.ReadAllText(path).Replace("org.smile.project-example", identity));
        string startup = Path.Combine(Path.GetDirectoryName(path)!, "Program.smile");
        File.AppendAllText(startup, "\nDim Saved As Number\nSaved = 9\nSave Saved To \"project-id\"\n");
        SmileCompilationInput input = SmileCompilationInput.Load(path);
        Assert.AreEqual(identity, input.ProgramName);
        TranspileResult transpile = input.Transpile([target]).Single();
        Assert.IsTrue(transpile.Success, string.Join("\n", transpile.Diagnostics));
        Assert.AreEqual("assets/letters.txt", transpile.GeneratedProgram!.Assets.Single().RelativePath);
        BuildRunResult run = await ToolchainRegistry.CreateDefault().Get(target).BuildAndRunAsync(transpile.GeneratedProgram, CancellationToken.None);
        Assert.IsTrue(run.Success, run.BuildOutput + run.StandardError);
        Assert.AreEqual("4:65:68\n", run.StandardOutput.Replace("\r\n", "\n"));
        Assert.AreEqual(9L, new SmilePersistentStorage(identity).LoadNumber("project-id", -1));
        string folder = Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA")!, "SMILE", "Games", identity.Replace('.', '_'));
        File.Delete(Path.Combine(folder, "project-id.txt"));
        Directory.Delete(folder);
    }

    [TestMethod]
    [DataRow("../escape.txt")]
    [DataRow("assets/missing.txt")]
    [DataRow("Assets/letters.txt")]
    [DataRow("assets/a**.txt")]
    public void Invalid_asset_includes_report_project_locations(string include)
    {
        string path = CreateProject(include);
        SmileInputException error = Assert.Throws<SmileInputException>(() => SmileCompilationInput.Load(path));
        Assert.AreEqual(path, error.Diagnostic.Span.SourcePath);
    }

    [TestMethod]
    public void Desktop_project_snapshots_preserve_editor_text_and_reload_support_sources()
    {
        string path = CreateProject();
        var opened = DesktopProjectSession.Open(path);
        Assert.IsNotNull(opened.Session);
        Assert.AreEqual(Path.Combine(Path.GetDirectoryName(path)!, "Program.smile"), opened.SourcePath);
        string current = "Import Sample.Storage As DataApi\nPrint DataApi.ByteCount";
        SmileCompilationInput snapshot = opened.Session.Snapshot(current);
        Assert.AreEqual(current, snapshot.Sources[0].Text);
        Assert.AreEqual("org.smile.project-example", snapshot.ProgramName);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(path)!, "Data.smile"), "Module Sample.Storage\nPublic Const ByteCount = 19\nEnd Module");
        Assert.AreEqual("19\n", new SmileEvaluator().EvaluateSources(opened.Session.Snapshot(current).Sources).Output);
        Assert.AreEqual(current, SmileSourceFormatter.Format(snapshot.Sources[0], snapshot.Sources).Source);
        string xml = File.ReadAllText(path).Replace("<ApplicationId>", "<StartupFile>Other.smile</StartupFile><ApplicationId>").Replace("Include='Program.smile'", "Include='Other.smile'");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(path)!, "Other.smile"), "Print 1");
        File.WriteAllText(path, xml);
        Assert.Throws<InvalidDataException>(() => opened.Session.Snapshot(current));
    }
}
