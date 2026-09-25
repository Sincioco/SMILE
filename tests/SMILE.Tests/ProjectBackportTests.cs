using SMILE.Engine;

namespace SMILE.Tests;

[TestClass]
[TestCategory("ProjectBackport")]
public sealed class ProjectBackportTests
{
    [TestMethod]
    [DataRow("org.example.app", true)]
    [DataRow("a.b", true)]
    [DataRow("org.example-game2", true)]
    [DataRow("Example.App", false)]
    [DataRow("org.2app", false)]
    [DataRow("org.app-", false)]
    [DataRow("org..app", false)]
    [DataRow("single", false)]
    [DataRow("org.é", false)]
    public void Application_identity_matches_the_authority_grammar(string value, bool expected) =>
        Assert.AreEqual(expected, SmileApplicationIdentity.IsValid(value));

    [TestMethod]
    public void Project_sources_select_startup_and_skip_other_startup_only_sources()
    {
        SmileProject project = SmileProject.Parse("sample.smileproj", """
<SmileProject><PropertyGroup><StartupFile>Second.smile</StartupFile><OutputName>MyGame</OutputName></PropertyGroup>
<ItemGroup><SmileSource Include="First.smile" StartupOnly="true"/><SmileSource Include="Support.smile"/><SmileSource Include="Second.smile" StartupOnly="true"/></ItemGroup></SmileProject>
""");
        Assert.AreEqual("Second.smile|Support.smile", string.Join("|", project.CompilationSources.Select(source => source.Include)));
        Assert.AreEqual("MyGame", project.EffectiveApplicationId);
        Assert.AreEqual("org.example.app", SmileApplicationIdentity.Resolve(project, "org.example.app"));
    }

    [TestMethod]
    public void Project_identity_has_one_owner_and_explicit_overrides_must_agree()
    {
        SmileProject project = SmileProject.Parse("sample.smileproj", "<SmileProject><PropertyGroup><ApplicationId>org.example.app</ApplicationId></PropertyGroup></SmileProject>");
        Assert.AreEqual("org.example.app", SmileApplicationIdentity.Resolve(project, null));
        Assert.AreEqual("org.example.app", SmileApplicationIdentity.Resolve(project, "org.example.app"));
        Assert.Throws<SmileInputException>(() => SmileApplicationIdentity.Resolve(project, "org.example.other"));
        Assert.Throws<SmileInputException>(() => SmileProject.Parse("sample.smilelibproj", "<SmileProject><PropertyGroup><LibraryName>Example</LibraryName><Version>1.0.0</Version><ApplicationId>org.example.app</ApplicationId></PropertyGroup><ItemGroup><SmileSource Include='Module.smile'/></ItemGroup></SmileProject>"));
        Assert.Throws<SmileInputException>(() => SmileProject.Parse("sample.smileproj", "<SmileProject><PropertyGroup><ApplicationId>a.b</ApplicationId><ApplicationId>a.b</ApplicationId></PropertyGroup></SmileProject>"));
    }

    [TestMethod]
    public void Provider_visibility_requires_direct_references()
    {
        SmileSource[] sources = [new("main.smile", "Import Dependency As Dep", "application"),
            new("library.smile", "Module Library\nImport Dependency As Dep\nEnd Module", "library"),
            new("dependency.smile", "Module Dependency\nEnd Module", "dependency")];
        var references = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["application"] = new HashSet<string> { "library" },
            ["library"] = new HashSet<string> { "dependency" }
        };
        BindResult result = new SmileTranspiler().BindSources(sources, references: references);
        Assert.IsTrue(result.Diagnostics.Any(item => item.Code == "SMILE3528" && item.Span.SourcePath == "main.smile"));
        references["application"] = new HashSet<string> { "library", "dependency" };
        Assert.IsTrue(new SmileTranspiler().BindSources(sources, references: references).Success);
    }
}
