using Allumeria;
using Allumeria.ChunkManagement;
using Allumeria.ChunkManagement.Lighting;
using OpenTK.Mathematics;
using System.Globalization;
using System.Text;

namespace Luma.AllumeriaLoader;

internal static class AllumeriaLightSampler
{
    private static readonly Vector4 FallbackLight = new(15f, 15f, 15f, 15f);

    private static readonly (int X, int Y, int Z, float Weight)[] LargeBlockOffsets =
    [
        (0, 1, 0, 8f),
        (0, 0, 0, 2f),
        (0, 2, 0, 2f),
        (1, 1, 0, 0.25f),
        (-1, 1, 0, 0.25f),
        (0, 1, 1, 0.25f),
        (0, 1, -1, 0.25f)
    ];

    public static Vector4 SampleLargeBlock(Vector3 position)
    {
        return SampleLargeBlockDetailed(
            position,
            AllumeriaLightSampleSettings.Default,
            includeDebugSamples: false).Light;
    }

    public static AllumeriaLightSampleResult SampleLargeBlockDetailed(
        Vector3 position,
        AllumeriaLightSampleSettings settings,
        bool includeDebugSamples)
    {
        return SampleWeightedAverage(position, LargeBlockOffsets, settings, includeDebugSamples);
    }

    private static AllumeriaLightSampleResult SampleWeightedAverage(
        Vector3 position,
        IReadOnlyList<(int X, int Y, int Z, float Weight)> offsets,
        AllumeriaLightSampleSettings settings,
        bool includeDebugSamples)
    {
        World? world = Game.gameState?.worldManager?.world;
        ChunkManager? chunkManager = world?.chunkManager;
        if (chunkManager is null)
        {
            return new AllumeriaLightSampleResult(
                FallbackLight,
                FallbackLight,
                includeDebugSamples ? "world unavailable; using fallback fullbright" : null);
        }

        int baseX = (int)MathF.Floor(position.X);
        int baseY = (int)MathF.Floor(position.Y);
        int baseZ = (int)MathF.Floor(position.Z);

        StringBuilder? debugSamples = includeDebugSamples ? new StringBuilder() : null;
        float r = 0f;
        float g = 0f;
        float b = 0f;
        float s = 0f;
        float totalWeight = 0f;
        foreach ((int offsetX, int offsetY, int offsetZ, float weight) in offsets)
        {
            if (weight <= 0f)
            {
                continue;
            }

            LightValue sample = chunkManager.GetLightIfExistsRaw(
                baseX + offsetX,
                baseY + offsetY,
                baseZ + offsetZ);

            if (debugSamples is not null)
            {
                if (debugSamples.Length > 0)
                {
                    debugSamples.Append("; ");
                }

                debugSamples.Append(FormattableString.Invariant(
                    $"({baseX + offsetX},{baseY + offsetY},{baseZ + offsetZ}) w={weight:0.##} raw=({sample.R},{sample.G},{sample.B},{sample.S})"));
            }

            r += sample.R * weight;
            g += sample.G * weight;
            b += sample.B * weight;
            s += sample.S * weight;
            totalWeight += weight;
        }

        if (totalWeight <= 0f)
        {
            return new AllumeriaLightSampleResult(FallbackLight, FallbackLight, debugSamples?.ToString());
        }

        Vector4 raw = new(
            r / totalWeight,
            g / totalWeight,
            b / totalWeight,
            s / totalWeight);
        Vector4 balanced = settings.BalanceTint
            ? BalanceTint(raw, settings.TintStrength)
            : raw;

        return new AllumeriaLightSampleResult(raw, balanced, debugSamples?.ToString());
    }

    private static Vector4 BalanceTint(Vector4 light, float tintStrength)
    {
        tintStrength = Math.Clamp(tintStrength, 0f, 1f);

        float max = MathF.Max(light.X, MathF.Max(light.Y, light.Z));
        if (max <= 0.001f)
        {
            return ClampLight(light);
        }

        float min = MathF.Min(light.X, MathF.Min(light.Y, light.Z));
        float chromaRatio = (max - min) / max;
        float neutralWeight = Math.Clamp((chromaRatio - 0.15f) / 0.85f, 0f, 1f) * (1f - tintStrength);
        if (neutralWeight <= 0.001f)
        {
            return ClampLight(light);
        }

        float mean = (light.X + light.Y + light.Z) / 3f;
        float neutral = MathF.Max(mean, max * 0.65f);
        return ClampLight(new Vector4(
            Lerp(light.X, neutral, neutralWeight),
            Lerp(light.Y, neutral, neutralWeight),
            Lerp(light.Z, neutral, neutralWeight),
            light.W));
    }

    private static Vector4 ClampLight(Vector4 light)
    {
        return new Vector4(
            Math.Clamp(light.X, 0f, 15f),
            Math.Clamp(light.Y, 0f, 15f),
            Math.Clamp(light.Z, 0f, 15f),
            Math.Clamp(light.W, 0f, 15f));
    }

    private static float Lerp(float a, float b, float amount)
    {
        return a + ((b - a) * amount);
    }

    public static string FormatLight(Vector4 light)
    {
        return string.Create(CultureInfo.InvariantCulture, $"({light.X:0.##}, {light.Y:0.##}, {light.Z:0.##}, {light.W:0.##})");
    }
}

internal readonly record struct AllumeriaLightSampleSettings(bool BalanceTint, float TintStrength)
{
    public static AllumeriaLightSampleSettings Default { get; } = new(BalanceTint: true, TintStrength: 0.45f);
}

internal readonly record struct AllumeriaLightSampleResult(Vector4 RawLight, Vector4 Light, string? DebugSamples);
