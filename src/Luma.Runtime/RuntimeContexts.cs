using Luma.Abstractions;

namespace Luma.Runtime;

internal sealed class RuntimeModContext(
    string gameDirectory,
    string modsDirectory,
    IModLogger logger,
    IModAssets assets,
    IServiceRegistry services) : IModContext
{
    public string GameDirectory { get; } = gameDirectory;

    public string ModsDirectory { get; } = modsDirectory;

    public IModLogger Logger { get; } = logger;

    public IModAssets Assets { get; } = assets;

    public IServiceRegistry Services { get; } = services;
}

internal sealed class RuntimeTickContext(long tickIndex, double deltaSeconds, object? gameInstance) : IModTickContext
{
    public long TickIndex { get; } = tickIndex;

    public double DeltaSeconds { get; } = deltaSeconds;

    public object? GameInstance { get; } = gameInstance;
}

internal sealed class RuntimeRenderContext(
    long frameIndex,
    double deltaSeconds,
    object? gameInstance,
    object? renderer) : IModRenderContext
{
    public long FrameIndex { get; } = frameIndex;

    public double DeltaSeconds { get; } = deltaSeconds;

    public object? GameInstance { get; } = gameInstance;

    public object? Renderer { get; } = renderer;
}
