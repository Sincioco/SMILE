using System.IO;
using SMILE.Desktop;

namespace SMILE.Tests;

[TestClass]
public sealed class AppErrorReporterTests
{
    [TestMethod]
    public void Reporter_writes_detailed_log_and_returns_path()
    {
        string root = Path.Combine(Path.GetTempPath(), "SMILE-Reporter-Test-" + Guid.NewGuid());
        try
        {
            var reporter = new AppErrorReporter(
                preferredLogRoot: root,
                fallbackLogRoot: Path.Combine(root, "fallback"),
                sessionId: "test-session");

            string path = reporter.Report(
                "Build & Run",
                new InvalidOperationException("boom"),
                "C",
                "Building",
                sourceRevision: 42);

            string log = File.ReadAllText(path);
            StringAssert.Contains(log, "Session ID: test-session");
            StringAssert.Contains(log, "Operation: Build & Run");
            StringAssert.Contains(log, "Target: C");
            StringAssert.Contains(log, "Stage: Building");
            StringAssert.Contains(log, "Source revision: 42");
            StringAssert.Contains(log, "InvalidOperationException");
            StringAssert.Contains(log, "boom");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [TestMethod]
    public void Reporter_falls_back_and_never_throws()
    {
        string root = Path.Combine(Path.GetTempPath(), "SMILE-Reporter-Test-" + Guid.NewGuid());
        string preferredFile = Path.Combine(root, "not-a-directory");
        string fallback = Path.Combine(root, "fallback");
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(preferredFile, "blocks directory creation");

            var reporter = new AppErrorReporter(preferredFile, fallback, sessionId: "fallback-session");
            string path = reporter.Report("Operation", new IOException("io failed"));

            StringAssert.Contains(path, fallback);
            StringAssert.Contains(File.ReadAllText(path), "fallback-session");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
