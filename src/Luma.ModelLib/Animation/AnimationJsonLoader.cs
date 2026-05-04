using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace Luma.ModelLib.Animation;

public static class AnimationJsonLoader
{
    public static AnimationBundle Load(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        string formatVersion = root.TryGetProperty("format_version", out JsonElement format)
            ? format.GetString() ?? "1.0"
            : "1.0";

        Dictionary<string, Vector3> pivots = ReadVectorMap(root, "pivots");
        Dictionary<string, string> childMap = ReadStringMap(root, "childMap");
        Dictionary<string, AnimationClip> clips = ReadClips(root);

        return new AnimationBundle(formatVersion, pivots, childMap, clips);
    }

    private static Dictionary<string, Vector3> ReadVectorMap(JsonElement root, string property)
    {
        var result = new Dictionary<string, Vector3>(StringComparer.Ordinal);
        if (!root.TryGetProperty(property, out JsonElement map) || map.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (JsonProperty entry in map.EnumerateObject())
        {
            result[entry.Name] = ReadVector(entry.Value);
        }

        return result;
    }

    private static Dictionary<string, string> ReadStringMap(JsonElement root, string property)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!root.TryGetProperty(property, out JsonElement map) || map.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (JsonProperty entry in map.EnumerateObject())
        {
            string? value = entry.Value.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                result[entry.Name] = value;
            }
        }

        return result;
    }

    private static Dictionary<string, AnimationClip> ReadClips(JsonElement root)
    {
        var result = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
        if (!root.TryGetProperty("animations", out JsonElement animations) || animations.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (JsonProperty clipEntry in animations.EnumerateObject())
        {
            JsonElement clipElement = clipEntry.Value;
            bool loop = ReadLoop(clipElement);
            double length = clipElement.TryGetProperty("length", out JsonElement lengthElement)
                ? lengthElement.GetDouble()
                : 1d;

            var bones = new Dictionary<string, BoneChannels>(StringComparer.Ordinal);
            if (clipElement.TryGetProperty("bones", out JsonElement bonesElement) &&
                bonesElement.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty boneEntry in bonesElement.EnumerateObject())
                {
                    bones[boneEntry.Name] = ReadBoneChannels(boneEntry.Value);
                }
            }

            result[clipEntry.Name] = new AnimationClip(clipEntry.Name, length, loop, bones);
        }

        return result;
    }

    private static bool ReadLoop(JsonElement clipElement)
    {
        if (!clipElement.TryGetProperty("loop", out JsonElement loopElement))
        {
            return false;
        }

        return loopElement.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => string.Equals(loopElement.GetString(), "loop", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static BoneChannels ReadBoneChannels(JsonElement boneElement)
    {
        return new BoneChannels
        {
            Rotation = ReadKeyframes(boneElement, "rotation"),
            Position = ReadKeyframes(boneElement, "position"),
            Scale = ReadKeyframes(boneElement, "scale")
        };
    }

    private static IReadOnlyList<VectorKeyframe> ReadKeyframes(JsonElement boneElement, string channel)
    {
        if (!boneElement.TryGetProperty(channel, out JsonElement keyframesElement) ||
            keyframesElement.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var keyframes = new List<VectorKeyframe>();
        foreach (JsonProperty keyframeEntry in keyframesElement.EnumerateObject())
        {
            double time = double.Parse(keyframeEntry.Name, CultureInfo.InvariantCulture);
            JsonElement keyframe = keyframeEntry.Value;
            Vector3 value = keyframe.TryGetProperty("value", out JsonElement valueElement)
                ? ReadVector(valueElement)
                : Vector3.Zero;
            Easing easing = keyframe.TryGetProperty("interp", out JsonElement interpElement)
                ? ParseEasing(interpElement.GetString())
                : Easing.Linear;

            keyframes.Add(new VectorKeyframe(time, value, easing));
        }

        keyframes.Sort((left, right) => left.TimeSeconds.CompareTo(right.TimeSeconds));
        return keyframes;
    }

    private static Vector3 ReadVector(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() < 3)
        {
            return Vector3.Zero;
        }

        return new Vector3(
            element[0].GetSingle(),
            element[1].GetSingle(),
            element[2].GetSingle());
    }

    private static Easing ParseEasing(string? value)
    {
        return value switch
        {
            "step" => Easing.Step,
            "easeInSine" => Easing.EaseInSine,
            "easeOutSine" => Easing.EaseOutSine,
            "easeInOutSine" => Easing.EaseInOutSine,
            "easeInQuad" => Easing.EaseInQuad,
            "easeOutQuad" => Easing.EaseOutQuad,
            "easeInOutQuad" => Easing.EaseInOutQuad,
            _ => Easing.Linear
        };
    }
}
