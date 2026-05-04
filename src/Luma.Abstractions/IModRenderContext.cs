namespace Luma.Abstractions;

public interface IModRenderContext
{
    long FrameIndex { get; }

    double DeltaSeconds { get; }

    object? GameInstance { get; }

    object? Renderer { get; }
}
