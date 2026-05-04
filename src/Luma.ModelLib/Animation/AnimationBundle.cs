using System.Numerics;

namespace Luma.ModelLib.Animation;

public sealed class AnimationBundle
{
    public AnimationBundle(
        string formatVersion,
        IReadOnlyDictionary<string, Vector3> pivots,
        IReadOnlyDictionary<string, string> childMap,
        IReadOnlyDictionary<string, AnimationClip> clips)
    {
        FormatVersion = formatVersion;
        Pivots = pivots;
        ChildMap = childMap;
        Clips = clips;
    }

    public string FormatVersion { get; }

    public IReadOnlyDictionary<string, Vector3> Pivots { get; }

    public IReadOnlyDictionary<string, string> ChildMap { get; }

    public IReadOnlyDictionary<string, AnimationClip> Clips { get; }
}
