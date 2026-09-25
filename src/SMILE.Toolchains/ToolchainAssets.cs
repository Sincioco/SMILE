using SMILE.Engine;

namespace SMILE.Toolchains;

public abstract partial class ToolchainBase
{
    // Every run gets a fresh workspace. Publish all binary/text assets before
    // launch; a failed copy never starts a partially populated application.
    protected static async Task CopyAssetsAsync(GeneratedProgram program, string directory, CancellationToken cancellationToken)
    {
        string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (SmileApplicationAsset asset in program.Assets)
        {
            string destination = Path.GetFullPath(Path.Combine(root, asset.RelativePath));
            if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Application asset escaped its output directory.");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var source = new FileStream(asset.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
            await source.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }
    }
}
