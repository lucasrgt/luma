using Luma.Abstractions;

namespace Luma.Runtime;

internal sealed class FileModAssets(string rootDirectory) : IModAssets
{
    public string RootDirectory { get; } = rootDirectory;

    public bool Exists(string relativePath)
    {
        return File.Exists(Resolve(relativePath));
    }

    public Stream OpenRead(string relativePath)
    {
        return File.OpenRead(Resolve(relativePath));
    }

    private string Resolve(string relativePath)
    {
        string fullPath = Path.GetFullPath(Path.Combine(RootDirectory, relativePath));
        string root = Path.GetFullPath(RootDirectory);
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Asset path escapes root directory: {relativePath}");
        }

        return fullPath;
    }
}
