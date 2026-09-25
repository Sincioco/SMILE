using System.IO;
using System.IO.Compression;
using System.Text;
using SMILE.Engine;

namespace SMILE.Tests;

[TestClass]
[TestCategory("LibraryBackport")]
public sealed class LibraryBackportTests
{
    internal const string LibrarySource = """
Module Example.Library
    Public Const Greeting = "Hello"
    Public Dim Values[2] As Number
    Public Dim State As Number
    Public Const Fraction = 1.25
    Public Enum State
        Ready = 1
        Alias = 1
    End Enum
    Public Type Point
        X As Number
        Labels[2] As Text
        Function Read() As Number
            Return Me.X
        End Function
    End Type
    Public Class Box
        Private Stored As Number
        Public Tag As Text
        Sub New(Optional Value As Number = 5)
            Me.Stored = Value
        End Sub
        Property Value As Number
            Get
                Return Me.Stored
            End Get
            Set
                Me.Stored = Value
            End Set
        End Property
    End Class
    Public Function Echo(Optional TextValue As Text = "é", Optional Value As State = State.Alias) As Text
        Return TextValue
    End Function
End Module
""";

    internal static string CreateProject(string root, string name = "Sample", string? source = null, string references = "")
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, name + ".smile"), source ?? LibrarySource);
        string path = Path.Combine(root, name + ".smilelibproj");
        File.WriteAllText(path, $"<SmileProject><PropertyGroup><LibraryName>{name}</LibraryName><Version>1.0.0</Version></PropertyGroup><ItemGroup><SmileSource Include='{name}.smile'/>{references}</ItemGroup></SmileProject>");
        return path;
    }
    private static string Workspace() => Path.Combine(Path.GetTempPath(), "SMILE", "Runs", "library-tests", Guid.NewGuid().ToString("N"));

    [TestMethod]
    public void Package_is_deterministic_source_owned_and_consumable()
    {
        string root = Workspace(), project = CreateProject(root);
        SmileCompilationInput input = SmileCompilationInput.Load(project);
        string first = Path.Combine(root, "first.smilelib"), second = Path.Combine(root, "second.smilelib");
        SmileLibraryPackage.Write(first, input);
        SmileLibraryPackage.Write(second, input);
        CollectionAssert.AreEqual(File.ReadAllBytes(first), File.ReadAllBytes(second));
        string startup = Path.Combine(root, "Program.smile");
        File.WriteAllText(startup, "Import Example.Library As Lib\nDim Box As New Lib.Box()\nPrint Lib.Greeting; \":\"; Box.Value; \":\"; Lib.Echo()");
        SmileCompilationInput application = SmileCompilationInput.Load(startup, libraries: [first]);
        EvaluationResult result = new SmileEvaluator().EvaluateSources(application.Sources);
        Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
        Assert.AreEqual("Hello:5:é\n", result.Output);
        using var zip = ZipFile.OpenRead(first);
        Assert.IsTrue(zip.Entries.All(entry => entry.LastWriteTime.Year == 1980));
        Assert.IsTrue(zip.Entries.All(entry => entry.FullName == "manifest.json" || entry.FullName == "api/public-symbols.json" || entry.FullName == "src/Sample.smile"));
    }

    [TestMethod]
    [DataRow("metadata")]
    [DataRow("hash")]
    [DataRow("payload")]
    [DataRow("path")]
    [DataRow("version")]
    public void Package_rejects_untrusted_payload_or_metadata(string change)
    {
        string root = Workspace(), project = CreateProject(root), package = Path.Combine(root, "sample.smilelib");
        SmileLibraryPackage.Write(package, SmileCompilationInput.Load(project));
        using (var zip = ZipFile.Open(package, ZipArchiveMode.Update))
        {
            string name = change switch { "metadata" => "api/public-symbols.json", "hash" => "src/Sample.smile", "version" => "manifest.json", "path" => "../escape.smile", _ => "run.exe" };
            string content = "changed";
            if (zip.GetEntry(name) is { } old)
            {
                using (var reader = new StreamReader(old.Open())) content = reader.ReadToEnd();
                old.Delete();
                content = change == "version" ? content.Replace("\"formatVersion\": 7", "\"formatVersion\": 6") : content + " ";
            }
            using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
            writer.Write(content);
        }
        string startup = Path.Combine(root, "Program.smile");
        File.WriteAllText(startup, "Print 1");
        Assert.Throws<InvalidDataException>(() => SmileCompilationInput.Load(startup, libraries: [package]));
    }

    [TestMethod]
    public void Packages_require_exact_explicit_dependencies_and_direct_imports()
    {
        string root = Workspace();
        string dependency = CreateProject(root, "Dependency", "Module Dependency\nPublic Const Value = 7\nPublic Type Point\nX As Number\nEnd Type\nEnd Module");
        string library = CreateProject(root, "Library", "Module Library\nImport Dependency As Dep\nPublic Function Read() As Number\nReturn Dep.Value\nEnd Function\nPublic Function Echo(Value As Dep.Point) As Dep.Point\nReturn Value\nEnd Function\nEnd Module", "<SmileProjectReference Include='Dependency.smilelibproj'/>");
        string dependencyPackage = Path.Combine(root, "Dependency.smilelib"), libraryPackage = Path.Combine(root, "Library.smilelib");
        SmileLibraryPackage.Write(dependencyPackage, SmileCompilationInput.Load(dependency));
        SmileLibraryPackage.Write(libraryPackage, SmileCompilationInput.Load(library));
        string startup = Path.Combine(root, "Program.smile");
        File.WriteAllText(startup, "Import Library As Lib\nImport Dependency As Dep\nDim Position As Dep.Point\nPosition.X = 12\nPosition = Lib.Echo(Position)\nPrint Lib.Read(); \":\"; Position.X");
        Assert.Throws<InvalidDataException>(() => SmileCompilationInput.Load(startup, libraries: [libraryPackage]));
        SmileCompilationInput application = SmileCompilationInput.Load(startup, libraries: [libraryPackage, dependencyPackage]);
        Assert.IsTrue(application.Bind().Success);
        Assert.AreEqual("7:12\n", new SmileEvaluator().EvaluateSources(application.Sources).Output);
        Assert.Throws<InvalidDataException>(() => SmileCompilationInput.Load(startup, libraries: [dependency, dependencyPackage]));
    }
}
