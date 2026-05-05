using Luma.Abstractions.Models;

namespace Luma.Abstractions.Content;

public interface ILumaContentService
{
    void RegisterAnimatedBlock(LumaAnimatedBlockSpec spec);

    ILumaAnimationController? GetAnimationControllerAt(int x, int y, int z);

    bool TriggerAnimationAt(int x, int y, int z, string triggerName);

    ILumaMachineController? GetMachineControllerAt(int x, int y, int z);

    bool TriggerMachineAt(int x, int y, int z, string triggerName);
}

public interface ILumaMachineController
{
    string? CurrentState { get; }

    bool SetState(string stateName);

    bool Trigger(string triggerName);
}

public sealed class LumaAnimatedBlockSpec
{
    public required string BlockId { get; init; }

    public required string DisplayName { get; init; }

    public string Description { get; init; } = string.Empty;

    public required LumaAnimatedModelSpec Model { get; init; }

    public string ItemSprite { get; init; } = "furnace";

    public bool AddWoodRecipe { get; init; } = true;

    public int RecipeOutputCount { get; init; } = 1;

    public IReadOnlyList<LumaRecipeSpec> Recipes { get; init; } = [];

    public LumaMachineSpec? Machine { get; init; }
}

public sealed class LumaRecipeSpec
{
    public string Station { get; init; } = "inventory";

    public int OutputCount { get; init; } = 1;

    public IReadOnlyList<LumaRecipeIngredientSpec> Ingredients { get; init; } = [];
}

public sealed class LumaRecipeIngredientSpec
{
    public string? ItemId { get; init; }

    public string? AliasId { get; init; }

    public int Amount { get; init; } = 1;
}

public sealed class LumaMachineSpec
{
    public string? InitialState { get; init; }

    public IReadOnlyList<LumaMachineStateSpec> States { get; init; } = [];

    public IReadOnlyList<LumaMachineTransitionSpec> Transitions { get; init; } = [];

    public LumaMachineStateSpec? FindState(string stateName)
    {
        foreach (LumaMachineStateSpec state in States)
        {
            if (state.Name.Equals(stateName, StringComparison.Ordinal))
            {
                return state;
            }
        }

        return null;
    }

    public LumaMachineStateSpec? GetInitialState()
    {
        if (!string.IsNullOrWhiteSpace(InitialState))
        {
            LumaMachineStateSpec? state = FindState(InitialState);
            if (state is not null)
            {
                return state;
            }
        }

        return States.Count > 0 ? States[0] : null;
    }
}

public sealed class LumaMachineStateSpec
{
    public required string Name { get; init; }

    public string? AnimationState { get; init; }

    public string? Payload { get; init; }
}

public sealed class LumaMachineTransitionSpec
{
    public required string Trigger { get; init; }

    public required string From { get; init; }

    public required string To { get; init; }
}
