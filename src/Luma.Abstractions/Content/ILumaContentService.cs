using Luma.Abstractions.Behaviors;
using Luma.Abstractions.Models;

namespace Luma.Abstractions.Content;

public interface ILumaContentService
{
    void RegisterAnimatedBlock(LumaAnimatedBlockSpec spec);

    ILumaAnimationController? GetAnimationControllerAt(int x, int y, int z);

    bool TriggerAnimationAt(int x, int y, int z, string triggerName);

    ILumaBehaviorController? GetBehaviorControllerAt(int x, int y, int z);

    bool TriggerBehaviorAt(int x, int y, int z, string triggerName);
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

    public LumaBehaviorSpec? Behavior { get; init; }
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
