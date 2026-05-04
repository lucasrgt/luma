using Luma.Abstractions.Models;

namespace Luma.Abstractions.Content;

public interface ILumaContentService
{
    void RegisterAnimatedBlock(LumaAnimatedBlockSpec spec);
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
}
