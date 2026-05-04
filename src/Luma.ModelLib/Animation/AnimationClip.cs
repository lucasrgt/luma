using System.Numerics;

namespace Luma.ModelLib.Animation;

public sealed class AnimationClip
{
    public AnimationClip(string name, double lengthSeconds, bool loop, IReadOnlyDictionary<string, BoneChannels> bones)
    {
        Name = name;
        LengthSeconds = lengthSeconds;
        Loop = loop;
        Bones = bones;
    }

    public string Name { get; }

    public double LengthSeconds { get; }

    public bool Loop { get; }

    public IReadOnlyDictionary<string, BoneChannels> Bones { get; }
}

public sealed class BoneChannels
{
    public IReadOnlyList<VectorKeyframe> Rotation { get; init; } = [];

    public IReadOnlyList<VectorKeyframe> Position { get; init; } = [];

    public IReadOnlyList<VectorKeyframe> Scale { get; init; } = [];
}

public readonly record struct VectorKeyframe(double TimeSeconds, Vector3 Value, Easing Easing);
