namespace Luma.ModelLib.Model;

public sealed class ModelSpec
{
    public required string ModelPath { get; init; }

    public required string TexturePath { get; init; }

    public string? AnimationPath { get; init; }
}
