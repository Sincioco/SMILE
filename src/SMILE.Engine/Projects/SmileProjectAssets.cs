using System.Text.RegularExpressions;

namespace SMILE.Engine;

public sealed record SmileApplicationAsset(string SourcePath, string RelativePath);

public static class SmileProjectAssets
{
    public static IReadOnlyList<SmileApplicationAsset> Resolve(SmileProject project)
    {
        string root = Path.GetDirectoryName(project.Path)!;
        var assets = new Dictionary<string, SmileApplicationAsset>(StringComparer.OrdinalIgnoreCase);
        foreach (SmileAssetInclude include in project.Assets)
        {
            string[] parts = Normalize(include);
            int wildcard = Array.FindIndex(parts, part => part.IndexOfAny(['*', '?']) >= 0);
            string[] fixedParts = wildcard < 0 ? parts : parts[..wildcard];
            string? actual = ExactPath(root, fixedParts, include);
            if (actual is null)
            {
                if (wildcard < 0) throw Failure("Explicit asset was not found: " + include.Pattern, include);
                continue;
            }
            IEnumerable<string> files;
            if (wildcard < 0)
            {
                if (!File.Exists(actual)) throw Failure("Asset must identify a file: " + include.Pattern, include);
                files = [actual];
            }
            else
            {
                string expression = "^" + string.Join("/", parts.Select(part => part == "**" ? "**" : Regex.Escape(part).Replace("\\*", "[^/]*").Replace("\\?", "[^/]")))
                    .Replace("**/", "(?:[^/]+/)*").Replace("**", ".*") + "$";
                bool recursive = wildcard < parts.Length - 1 || parts[wildcard] == "**";
                files = Directory.EnumerateFiles(actual, "*", new EnumerationOptions { RecurseSubdirectories = recursive, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false })
                    .Where(path => Regex.IsMatch(Path.GetRelativePath(root, path).Replace('\\', '/'), expression, RegexOptions.CultureInvariant));
            }
            foreach (string file in files)
            {
                string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (assets.TryGetValue(relative, out SmileApplicationAsset? existing) && existing.RelativePath != relative)
                    throw Failure("Asset paths collide when letter case is ignored: " + relative, include);
                assets[relative] = new(file, relative);
            }
        }
        return assets.Values.OrderBy(asset => asset.RelativePath, StringComparer.Ordinal).ToArray();
    }

    private static string[] Normalize(SmileAssetInclude include)
    {
        string pattern = include.Pattern.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(pattern) || pattern.StartsWith('/') || pattern.IndexOfAny(['\0', ':', '[', ']', '{', '}', '!', ';']) >= 0)
            throw Failure("Assets require a project-relative path or a *, ? or ** pattern.", include);
        var parts = new List<string>();
        foreach (string part in pattern.Split('/'))
        {
            if (part == ".") continue;
            if (part == "..")
            {
                if (parts.Count == 0) throw Failure("Asset path escapes its project directory.", include);
                parts.RemoveAt(parts.Count - 1);
            }
            else if (part.Length == 0 || (part.Contains("**", StringComparison.Ordinal) && part != "**"))
                throw Failure("Asset paths cannot contain empty segments; ** must occupy a complete segment.", include);
            else parts.Add(part);
        }
        if (parts.Count == 0) throw Failure("Asset path is empty after normalization.", include);
        return parts.ToArray();
    }

    private static string? ExactPath(string root, IEnumerable<string> parts, SmileAssetInclude include)
    {
        string path = root;
        foreach (string part in parts)
        {
            if (!Directory.Exists(path)) return null;
            string? match = Directory.EnumerateFileSystemEntries(path).FirstOrDefault(item => Path.GetFileName(item).Equals(part, StringComparison.OrdinalIgnoreCase));
            if (match is null) return null;
            if (Path.GetFileName(match) != part) throw Failure("Asset Include does not match filesystem letter case: " + include.Pattern, include);
            if ((File.GetAttributes(match) & FileAttributes.ReparsePoint) != 0) throw Failure("Project assets must be ordinary files inside the project directory.", include);
            path = match;
        }
        return path;
    }
    private static SmileInputException Failure(string message, SmileAssetInclude include) =>
        new(new("SMILE3601", DiagnosticSeverity.Error, message, include.Span));
}
