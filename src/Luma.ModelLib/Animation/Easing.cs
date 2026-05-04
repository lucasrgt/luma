namespace Luma.ModelLib.Animation;

public enum Easing
{
    Linear,
    Step,
    EaseInSine,
    EaseOutSine,
    EaseInOutSine,
    EaseInQuad,
    EaseOutQuad,
    EaseInOutQuad
}

public static class EasingFunctions
{
    public static float Apply(Easing easing, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return easing switch
        {
            Easing.Linear => t,
            Easing.Step => t < 1f ? 0f : 1f,
            Easing.EaseInSine => 1f - MathF.Cos((t * MathF.PI) / 2f),
            Easing.EaseOutSine => MathF.Sin((t * MathF.PI) / 2f),
            Easing.EaseInOutSine => -(MathF.Cos(MathF.PI * t) - 1f) / 2f,
            Easing.EaseInQuad => t * t,
            Easing.EaseOutQuad => 1f - ((1f - t) * (1f - t)),
            Easing.EaseInOutQuad => t < 0.5f
                ? 2f * t * t
                : 1f - (MathF.Pow(-2f * t + 2f, 2f) / 2f),
            _ => t
        };
    }
}
