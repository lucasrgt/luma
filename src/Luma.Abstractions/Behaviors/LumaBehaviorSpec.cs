namespace Luma.Abstractions.Behaviors;

public sealed class LumaBehaviorSpec
{
    public string? InitialState { get; init; }

    public IReadOnlyList<LumaBehaviorStateSpec> States { get; init; } = [];

    public IReadOnlyList<LumaBehaviorTransitionSpec> Transitions { get; init; } = [];

    public LumaBehaviorStateSpec? FindState(string stateName)
    {
        foreach (LumaBehaviorStateSpec state in States)
        {
            if (state.Name.Equals(stateName, StringComparison.Ordinal))
            {
                return state;
            }
        }

        return null;
    }

    public LumaBehaviorStateSpec? GetInitialState()
    {
        if (!string.IsNullOrWhiteSpace(InitialState))
        {
            LumaBehaviorStateSpec? state = FindState(InitialState);
            if (state is not null)
            {
                return state;
            }
        }

        return States.Count > 0 ? States[0] : null;
    }
}

public sealed class LumaBehaviorStateSpec
{
    public required string Name { get; init; }

    public string? AnimationState { get; init; }

    public string? Payload { get; init; }
}

public sealed class LumaBehaviorTransitionSpec
{
    public required string Trigger { get; init; }

    public required string From { get; init; }

    public required string To { get; init; }
}
