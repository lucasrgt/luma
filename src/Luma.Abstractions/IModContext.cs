namespace Luma.Abstractions;

public interface IModContext
{
    string GameDirectory { get; }

    string ModsDirectory { get; }

    IModLogger Logger { get; }

    IModAssets Assets { get; }

    IServiceRegistry Services { get; }
}
