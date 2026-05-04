namespace Luma.Abstractions;

public interface IModAssets
{
    string RootDirectory { get; }

    Stream OpenRead(string relativePath);

    bool Exists(string relativePath);
}
