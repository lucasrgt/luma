using Allumeria;
using Allumeria.Blocks.BlockEntities;
using Allumeria.Blocks.Blocks;
using Allumeria.ChunkManagement;
using Allumeria.Items;
using Allumeria.Items.Crafting;
using Luma.Abstractions.Content;
using Luma.Abstractions.Models;

namespace Luma.AllumeriaLoader;

internal sealed class AllumeriaContentService : ILumaContentService
{
    private readonly object gate = new();
    private readonly List<RegisteredAnimatedBlock> animatedBlocks = [];

    public static AllumeriaContentService Instance { get; } = new();

    private AllumeriaContentService()
    {
    }

    public void RegisterAnimatedBlock(LumaAnimatedBlockSpec spec)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spec.BlockId);
        ArgumentException.ThrowIfNullOrWhiteSpace(spec.DisplayName);
        ArgumentNullException.ThrowIfNull(spec.Model);

        if (spec.RecipeOutputCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(spec), "RecipeOutputCount must be at least 1.");
        }

        lock (gate)
        {
            if (animatedBlocks.Any(block => block.Spec.BlockId.Equals(spec.BlockId, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"Animated block already registered: {spec.BlockId}");
            }

            animatedBlocks.Add(new RegisteredAnimatedBlock(spec));
        }

        Logger.Info($"Queued animated block {spec.BlockId} ({spec.DisplayName}).");
    }

    public async Task InstallAsync()
    {
        for (var attempt = 1; attempt <= 200; attempt++)
        {
            try
            {
                if (TryInstall(attempt))
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Luma content install attempt {attempt} failed", ex);
            }

            await Task.Delay(250).ConfigureAwait(false);
        }

        Logger.Info("Luma content install timed out");
    }

    private bool TryInstall(int attempt)
    {
        RegisteredAnimatedBlock[] snapshot;
        lock (gate)
        {
            snapshot = [.. animatedBlocks];
        }

        if (snapshot.Length == 0)
        {
            return true;
        }

        if (!CanRegisterBlocks())
        {
            return false;
        }

        EnsureBlockCapacity(snapshot.Count(block => block.Block is null));
        EnsureItemCapacity(snapshot.Count(block => block.Block is null));

        if (!BlockEntity.entityToByte.ContainsKey(typeof(PublicAnimatedModelBlockEntity)))
        {
            BlockEntity.RegisterBlockEntity(typeof(PublicAnimatedModelBlockEntity));
        }

        foreach (RegisteredAnimatedBlock entry in snapshot)
        {
            if (entry.Block is null)
            {
                entry.Block = new PublicAnimatedModelBlock(entry.Spec);
                Logger.Info($"Registered Luma animated block id={entry.Block.intID}, item={entry.Block.item.itemID}: {entry.Spec.BlockId}");
            }
        }

        if (CraftingRecipe.recipes is null || CraftingRecipe.recipes.Count == 0)
        {
            return false;
        }

        foreach (RegisteredAnimatedBlock entry in snapshot)
        {
            if (entry.Block is null || entry.RecipeAdded || !entry.Spec.AddWoodRecipe)
            {
                continue;
            }

            AddWoodRecipe(entry.Block.item, entry.Spec.RecipeOutputCount, entry.Spec.DisplayName);
            entry.RecipeAdded = true;
        }

        Logger.Info($"Installed Luma content registrations on attempt {attempt}.");
        return true;
    }

    private static bool CanRegisterBlocks()
    {
        return Block.blocks is not null
            && Block.blocksByString is not null
            && Item.items is not null
            && Block.furnace is not null
            && BlockEntity.entityToByte is not null;
    }

    private static void AddWoodRecipe(Item resultItem, int amount, string label)
    {
        CraftingRecipe.recipes.RemoveAll(recipe => IsWoodRecipe(recipe, resultItem));
        CraftingRecipe.recipes.Add(
            new CraftingRecipe(new ItemStack(resultItem, amount), CraftingStation.inventory)
                .AddReq(new RecipeEntry(RecipeAlias.any_planks, 1)));
        Logger.Info($"Added wood recipe: 1x any planks -> {amount}x {label}");
    }

    private static bool IsWoodRecipe(CraftingRecipe recipe, Item resultItem)
    {
        try
        {
            if (recipe.result.GetItem() != resultItem ||
                recipe.requiredStation != CraftingStation.inventory ||
                recipe.requiredItems.Count != 1)
            {
                return false;
            }

            RecipeEntry entry = recipe.requiredItems[0];
            return entry.useAlias &&
                entry.alias == RecipeAlias.any_planks &&
                entry.amount == 1;
        }
        catch
        {
            return false;
        }
    }

    private static void EnsureBlockCapacity(int additional)
    {
        if (Block.totalBlockCount + additional < Block.blocks.Length)
        {
            return;
        }

        int newCapacity = Math.Max(Block.blocks.Length * 2, Block.totalBlockCount + additional + 16);
        Array.Resize(ref Block.blocks, newCapacity);
        Logger.Info($"Grew block registry to {newCapacity}");
    }

    private static void EnsureItemCapacity(int additional)
    {
        int required = Math.Max(Item.totalItemCount + additional, Item.lastID + additional);
        if (required < Item.items.Length)
        {
            return;
        }

        int newCapacity = Math.Max(Item.items.Length * 2, required + 16);
        Array.Resize(ref Item.items, newCapacity);
        Logger.Info($"Grew item registry to {newCapacity}");
    }

    private sealed class RegisteredAnimatedBlock(LumaAnimatedBlockSpec spec)
    {
        public LumaAnimatedBlockSpec Spec { get; } = spec;

        public PublicAnimatedModelBlock? Block { get; set; }

        public bool RecipeAdded { get; set; }
    }
}

internal sealed class PublicAnimatedModelBlock : AnimatedModelBlock<PublicAnimatedModelBlockEntity>
{
    public PublicAnimatedModelBlock(LumaAnimatedBlockSpec spec)
        : base(spec.BlockId, spec.DisplayName, spec.Description, spec.ItemSprite)
    {
        ModelSpec = spec.Model;
    }

    public LumaAnimatedModelSpec ModelSpec { get; }
}

internal sealed class PublicAnimatedModelBlockEntity : AnimatedModelBlockEntity
{
    private AllumeriaAnimatedModel? model;

    protected override ILumaAnimatedModel Model => throw new NotSupportedException();

    protected override bool TryGetModel(out ILumaAnimatedModel model)
    {
        if (this.model is not null)
        {
            model = this.model;
            return true;
        }

        model = null!;
        World? world = Allumeria.Game.gameState?.worldManager?.world;
        if (world?.chunkManager.GetBlockIfExists(posX, posY, posZ) is not PublicAnimatedModelBlock block)
        {
            return false;
        }

        this.model = AllumeriaModelRegistry.RegisterAnimatedInstance(block.ModelSpec);
        model = this.model;
        return true;
    }

    public override void OnDelete(World world)
    {
        if (model is not null)
        {
            AllumeriaModelRegistry.UnregisterAnimated(model);
            model = null;
        }

        base.OnDelete(world);
    }
}
