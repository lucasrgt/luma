namespace Luma.Abstractions.Models;

public sealed class LumaAnimationGraphSpec
{
    public string? InitialState { get; init; }

    public IReadOnlyList<LumaAnimationStateSpec> States { get; init; } = [];

    public IReadOnlyList<LumaAnimationTransitionSpec> Transitions { get; init; } = [];

    public LumaAnimationStateSpec? FindState(string stateName)
    {
        foreach (LumaAnimationStateSpec state in States)
        {
            if (state.Name.Equals(stateName, StringComparison.Ordinal))
            {
                return state;
            }
        }

        return null;
    }

    public LumaAnimationStateSpec? GetInitialState()
    {
        if (!string.IsNullOrWhiteSpace(InitialState))
        {
            LumaAnimationStateSpec? state = FindState(InitialState);
            if (state is not null)
            {
                return state;
            }
        }

        return States.Count > 0 ? States[0] : null;
    }
}

public sealed class LumaAnimationStateSpec
{
    public required string Name { get; init; }

    public string? Animation { get; init; }

    public bool Loop { get; init; } = true;

    public bool AutoPlay { get; init; } = true;

    public float StepSeconds { get; init; } = 1f / 60f;
}

public sealed class LumaAnimationTransitionSpec
{
    public required string Trigger { get; init; }

    public required string From { get; init; }

    public required string To { get; init; }
}
