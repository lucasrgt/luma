using Allumeria;
using Allumeria.Blocks.BlockEntities;
using Allumeria.Blocks.Blocks;
using Allumeria.ChunkManagement;
using Allumeria.DataManagement;
using Allumeria.DataManagement.Saving;
using Allumeria.Items;
using Allumeria.Items.Crafting;
using Luma.Abstractions.Behaviors;
using Luma.Abstractions.Content;
using Luma.Abstractions.Models;
using System.Reflection;

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

        ValidateRecipes(spec);
        ValidateBehavior(spec);

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

    public ILumaAnimationController? GetAnimationControllerAt(int x, int y, int z)
    {
        World? world = Allumeria.Game.gameState?.worldManager?.world;
        if (world?.chunkManager.GetBlockEntityAt(x, y, z, out BlockEntity entity) != true ||
            entity is not PublicAnimatedModelBlockEntity animatedEntity)
        {
            return null;
        }

        return animatedEntity.GetAnimationController();
    }

    public bool TriggerAnimationAt(int x, int y, int z, string triggerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(triggerName);
        return GetAnimationControllerAt(x, y, z)?.Trigger(triggerName) == true;
    }

    public ILumaBehaviorController? GetBehaviorControllerAt(int x, int y, int z)
    {
        World? world = Allumeria.Game.gameState?.worldManager?.world;
        if (world?.chunkManager.GetBlockEntityAt(x, y, z, out BlockEntity entity) != true ||
            entity is not PublicAnimatedModelBlockEntity animatedEntity)
        {
            return null;
        }

        return animatedEntity.GetBehaviorController();
    }

    public bool TriggerBehaviorAt(int x, int y, int z, string triggerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(triggerName);
        return GetBehaviorControllerAt(x, y, z)?.Trigger(triggerName) == true;
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
            if (entry.Block is null || entry.RecipeAdded)
            {
                continue;
            }

            InstallRecipes(entry.Block.item, entry.Spec);
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

    private static void ValidateRecipes(LumaAnimatedBlockSpec spec)
    {
        foreach (LumaRecipeSpec recipe in spec.Recipes)
        {
            if (recipe.OutputCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(spec), $"Recipe output count for {spec.BlockId} must be at least 1.");
            }

            if (recipe.Ingredients.Count == 0)
            {
                throw new ArgumentException($"Recipe for {spec.BlockId} must declare at least one ingredient.");
            }

            foreach (LumaRecipeIngredientSpec ingredient in recipe.Ingredients)
            {
                bool hasItem = !string.IsNullOrWhiteSpace(ingredient.ItemId);
                bool hasAlias = !string.IsNullOrWhiteSpace(ingredient.AliasId);
                if (hasItem == hasAlias)
                {
                    throw new ArgumentException($"Recipe ingredient for {spec.BlockId} must set exactly one of ItemId or AliasId.");
                }

                if (ingredient.Amount < 1)
                {
                    throw new ArgumentOutOfRangeException(nameof(spec), $"Recipe ingredient amount for {spec.BlockId} must be at least 1.");
                }
            }
        }
    }

    private static void ValidateBehavior(LumaAnimatedBlockSpec spec)
    {
        LumaBehaviorSpec? behavior = spec.Behavior;
        if (behavior is null)
        {
            return;
        }

        if (behavior.States.Count == 0)
        {
            throw new ArgumentException($"Behavior spec for {spec.BlockId} must declare at least one state.");
        }

        var states = new HashSet<string>(StringComparer.Ordinal);
        foreach (LumaBehaviorStateSpec state in behavior.States)
        {
            if (string.IsNullOrWhiteSpace(state.Name))
            {
                throw new ArgumentException($"Behavior spec for {spec.BlockId} contains a state with no name.");
            }

            if (!states.Add(state.Name))
            {
                throw new ArgumentException($"Behavior spec for {spec.BlockId} contains duplicate state '{state.Name}'.");
            }

            if (!string.IsNullOrWhiteSpace(state.AnimationState) &&
                spec.Model.AnimationGraph?.FindState(state.AnimationState) is null)
            {
                throw new ArgumentException($"Behavior state '{state.Name}' for {spec.BlockId} references missing animation state '{state.AnimationState}'.");
            }
        }

        if (!string.IsNullOrWhiteSpace(behavior.InitialState) && !states.Contains(behavior.InitialState))
        {
            throw new ArgumentException($"Behavior spec for {spec.BlockId} references missing InitialState '{behavior.InitialState}'.");
        }

        foreach (LumaBehaviorTransitionSpec transition in behavior.Transitions)
        {
            if (string.IsNullOrWhiteSpace(transition.Trigger))
            {
                throw new ArgumentException($"Behavior spec for {spec.BlockId} contains a transition with no trigger.");
            }

            if (string.IsNullOrWhiteSpace(transition.From))
            {
                throw new ArgumentException($"Behavior transition '{transition.Trigger}' for {spec.BlockId} has no From state.");
            }

            if (!transition.From.Equals("*", StringComparison.Ordinal) && !states.Contains(transition.From))
            {
                throw new ArgumentException($"Behavior transition '{transition.Trigger}' for {spec.BlockId} references missing From state '{transition.From}'.");
            }

            if (string.IsNullOrWhiteSpace(transition.To) || !states.Contains(transition.To))
            {
                throw new ArgumentException($"Behavior transition '{transition.Trigger}' for {spec.BlockId} references missing To state '{transition.To}'.");
            }
        }
    }

    private static void InstallRecipes(Item resultItem, LumaAnimatedBlockSpec spec)
    {
        IReadOnlyList<LumaRecipeSpec> recipes = spec.Recipes.Count > 0
            ? spec.Recipes
            : spec.AddWoodRecipe
                ? [new LumaRecipeSpec
                {
                    OutputCount = spec.RecipeOutputCount,
                    Ingredients =
                    [
                        new LumaRecipeIngredientSpec
                        {
                            AliasId = "any_planks",
                            Amount = 1
                        }
                    ]
                }]
                : [];

        if (recipes.Count == 0)
        {
            return;
        }

        ResolvedLumaRecipe[] resolvedRecipes = [.. recipes.Select(recipeSpec =>
            new ResolvedLumaRecipe(recipeSpec, ResolveCraftingStation(recipeSpec.Station)))];

        foreach (CraftingStation station in resolvedRecipes.Select(recipe => recipe.Station).Distinct())
        {
            CraftingRecipe.recipes.RemoveAll(recipe => recipe.result.GetItem() == resultItem && recipe.requiredStation == station);
        }

        foreach (ResolvedLumaRecipe resolvedRecipe in resolvedRecipes)
        {
            LumaRecipeSpec recipeSpec = resolvedRecipe.Spec;
            CraftingStation station = resolvedRecipe.Station;
            var recipe = new CraftingRecipe(new ItemStack(resultItem, recipeSpec.OutputCount), station);
            foreach (LumaRecipeIngredientSpec ingredient in recipeSpec.Ingredients)
            {
                recipe.AddReq(ResolveRecipeEntry(ingredient));
            }

            Logger.Info($"Added Luma recipe: {FormatRecipe(recipe)}");
        }
    }

    private static CraftingStation ResolveCraftingStation(string stationId)
    {
        string resolvedStationId = string.IsNullOrWhiteSpace(stationId)
            ? "inventory"
            : stationId;
        foreach (CraftingStation station in CraftingStation.stations)
        {
            if (station.strID.Equals(resolvedStationId, StringComparison.Ordinal))
            {
                return station;
            }
        }

        throw new InvalidOperationException($"Unknown crafting station: {resolvedStationId}");
    }

    private static RecipeEntry ResolveRecipeEntry(LumaRecipeIngredientSpec ingredient)
    {
        if (!string.IsNullOrWhiteSpace(ingredient.AliasId))
        {
            return new RecipeEntry(ResolveRecipeAlias(ingredient.AliasId), ingredient.Amount);
        }

        if (!string.IsNullOrWhiteSpace(ingredient.ItemId))
        {
            return new RecipeEntry(ResolveItem(ingredient.ItemId), ingredient.Amount);
        }

        throw new InvalidOperationException("Recipe ingredient must set ItemId or AliasId.");
    }

    private static RecipeAlias ResolveRecipeAlias(string aliasId)
    {
        foreach (FieldInfo field in typeof(RecipeAlias).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is RecipeAlias alias &&
                alias.strID.Equals(aliasId, StringComparison.Ordinal))
            {
                return alias;
            }
        }

        throw new InvalidOperationException($"Unknown recipe alias: {aliasId}");
    }

    private static Item ResolveItem(string itemId)
    {
        if (Block.blocksByString.TryGetValue(itemId, out Block? block))
        {
            return block.item;
        }

        foreach (Item? item in Item.items)
        {
            if (item is not null && item.strID.Equals(itemId, StringComparison.Ordinal))
            {
                return item;
            }
        }

        throw new InvalidOperationException($"Unknown item or block id: {itemId}");
    }

    private static string FormatRecipe(CraftingRecipe recipe)
    {
        string requirements = string.Join(
            ", ",
            recipe.requiredItems.Select(entry => entry.useAlias
                ? $"{entry.amount}x alias:{entry.alias.strID}"
                : $"{entry.amount}x {entry.item.strID}"));
        return $"{requirements} -> {recipe.result.amount}x {recipe.result.GetItem().strID} @ {recipe.requiredStation.strID}";
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

    private sealed record ResolvedLumaRecipe(LumaRecipeSpec Spec, CraftingStation Station);
}

internal sealed class PublicAnimatedModelBlock : AnimatedModelBlock<PublicAnimatedModelBlockEntity>
{
    public PublicAnimatedModelBlock(LumaAnimatedBlockSpec spec)
        : base(spec.BlockId, spec.DisplayName, spec.Description, spec.ItemSprite)
    {
        Spec = spec;
    }

    public LumaAnimatedBlockSpec Spec { get; }

    public LumaAnimatedModelSpec ModelSpec => Spec.Model;
}

internal sealed class PublicAnimatedModelBlockEntity : AnimatedModelBlockEntity
{
    private const string AnimationStateTag = "lumaAnimationState";
    private const string BehaviorStateTag = "lumaBehaviorState";
    private const string LegacyMachineStateTag = "lumaMachineState";

    private AllumeriaAnimatedModel? model;
    private string? savedAnimationState;
    private string? behaviorState;
    private string? savedBehaviorState;

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

        EnsureBehaviorState(block.Spec);
        this.model = AllumeriaModelRegistry.RegisterAnimatedInstance(block.ModelSpec);
        if (block.Spec.Behavior is not null)
        {
            ApplyBehaviorStateAnimation(block.Spec);
        }
        else if (!string.IsNullOrWhiteSpace(savedAnimationState))
        {
            _ = this.model.Animation.SetState(savedAnimationState);
            savedAnimationState = null;
        }

        model = this.model;
        return true;
    }

    public ILumaAnimationController? GetAnimationController()
    {
        return TryGetModel(out ILumaAnimatedModel model)
            ? model.Animation
            : null;
    }

    public ILumaBehaviorController? GetBehaviorController()
    {
        LumaAnimatedBlockSpec? spec = GetBlockSpec();
        return spec?.Behavior is null
            ? null
            : new PublicAnimatedModelBehaviorController(this);
    }

    public override void WriteBytes(ListTag tag)
    {
        base.WriteBytes(tag);
        string? currentState = model?.Animation.CurrentState ?? savedAnimationState;
        if (!string.IsNullOrWhiteSpace(currentState))
        {
            tag.AddTag(new StringTag(AnimationStateTag, currentState));
        }

        string? currentBehaviorState = behaviorState ?? savedBehaviorState;
        if (!string.IsNullOrWhiteSpace(currentBehaviorState))
        {
            tag.AddTag(new StringTag(BehaviorStateTag, currentBehaviorState));
        }
    }

    public override void ReadBytes(ListTag tag, PaletteConstructor constructedPalette)
    {
        base.ReadBytes(tag, constructedPalette);
        if (tag.FindTag(AnimationStateTag, out DataTag stateTag))
        {
            savedAnimationState = stateTag.GetValue() as string;
        }

        if (tag.FindTag(BehaviorStateTag, out DataTag behaviorStateTag) ||
            tag.FindTag(LegacyMachineStateTag, out behaviorStateTag))
        {
            savedBehaviorState = behaviorStateTag.GetValue() as string;
        }
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

    private LumaAnimatedBlockSpec? GetBlockSpec()
    {
        World? world = Allumeria.Game.gameState?.worldManager?.world;
        return world?.chunkManager.GetBlockIfExists(posX, posY, posZ) is PublicAnimatedModelBlock block
            ? block.Spec
            : null;
    }

    private void EnsureBehaviorState(LumaAnimatedBlockSpec spec)
    {
        LumaBehaviorSpec? behavior = spec.Behavior;
        if (behavior is null || !string.IsNullOrWhiteSpace(behaviorState))
        {
            return;
        }

        string? candidate = savedBehaviorState;
        if (!string.IsNullOrWhiteSpace(candidate) && behavior.FindState(candidate) is not null)
        {
            behaviorState = candidate;
        }
        else
        {
            behaviorState = behavior.GetInitialState()?.Name;
        }

        savedBehaviorState = null;
    }

    private bool SetBehaviorState(string stateName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateName);

        LumaAnimatedBlockSpec? spec = GetBlockSpec();
        LumaBehaviorStateSpec? state = spec?.Behavior?.FindState(stateName);
        if (spec is null || state is null)
        {
            return false;
        }

        behaviorState = state.Name;
        ApplyBehaviorStateAnimation(spec);
        return true;
    }

    private bool TriggerBehavior(string triggerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(triggerName);

        LumaAnimatedBlockSpec? spec = GetBlockSpec();
        LumaBehaviorSpec? behavior = spec?.Behavior;
        if (spec is null || behavior is null)
        {
            return false;
        }

        EnsureBehaviorState(spec);
        foreach (LumaBehaviorTransitionSpec transition in behavior.Transitions)
        {
            if (!transition.Trigger.Equals(triggerName, StringComparison.Ordinal) ||
                !MatchesBehaviorTransitionSource(transition.From))
            {
                continue;
            }

            return SetBehaviorState(transition.To);
        }

        return false;
    }

    private bool MatchesBehaviorTransitionSource(string from)
    {
        return from.Equals("*", StringComparison.Ordinal) ||
            from.Equals(behaviorState, StringComparison.Ordinal);
    }

    private void ApplyBehaviorStateAnimation(LumaAnimatedBlockSpec spec)
    {
        LumaBehaviorSpec? behavior = spec.Behavior;
        if (behavior is null || string.IsNullOrWhiteSpace(behaviorState))
        {
            return;
        }

        LumaBehaviorStateSpec? state = behavior.FindState(behaviorState);
        if (!string.IsNullOrWhiteSpace(state?.AnimationState) &&
            TryGetModel(out ILumaAnimatedModel behaviorModel))
        {
            _ = behaviorModel.Animation.SetState(state.AnimationState);
        }
    }

    private sealed class PublicAnimatedModelBehaviorController(PublicAnimatedModelBlockEntity entity) : ILumaBehaviorController
    {
        public string? CurrentState
        {
            get
            {
                LumaAnimatedBlockSpec? spec = entity.GetBlockSpec();
                if (spec is not null)
                {
                    entity.EnsureBehaviorState(spec);
                }

                return entity.behaviorState;
            }
        }

        public bool SetState(string stateName)
        {
            return entity.SetBehaviorState(stateName);
        }

        public bool Trigger(string triggerName)
        {
            return entity.TriggerBehavior(triggerName);
        }
    }
}
