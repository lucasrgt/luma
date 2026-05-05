namespace Luma.Abstractions.Models;

public sealed class LumaAnimatedModelSpec
{
    public required string Name { get; init; }

    public required string AssetRoot { get; init; }

    public required string ModelPath { get; init; }

    public required string TexturePath { get; init; }

    public string? ChunkManifestPath { get; init; }

    public string? InitialAnimation { get; init; }

    public bool LoopInitialAnimation { get; init; } = true;

    public float AnimationStepSeconds { get; init; } = 1f / 60f;

    public LumaAnimationGraphSpec? AnimationGraph { get; init; }
}
