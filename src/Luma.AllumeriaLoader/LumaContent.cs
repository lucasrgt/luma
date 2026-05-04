using Allumeria.Blocks.BlockEntities;
using Allumeria.Blocks.Blocks;
using Allumeria.Items;
using Allumeria.Items.Crafting;

namespace Luma.AllumeriaLoader;

internal static class LumaPreviewContent
{
    private static bool registered;
    private static bool assetsLoaded;
    private static bool recipeAdded;

    public static MegaCrusherBlock? MegaCrusher { get; private set; }

    public static TestRotorBlock? TestRotor { get; private set; }

    public static LightOccluderBlock? LightOccluder { get; private set; }

    public static async Task InstallAsync()
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
                Logger.Error($"Luma preview content install attempt {attempt} failed", ex);
            }

            await Task.Delay(250).ConfigureAwait(false);
        }

            Logger.Info("Luma preview content install timed out");
    }

    private static bool TryInstall(int attempt)
    {
        if (!registered)
        {
            if (!CanRegisterBlocks())
            {
                return false;
            }

            EnsureBlockCapacity(3);
            EnsureItemCapacity(3);

            MegaCrusher = new MegaCrusherBlock();
            TestRotor = new TestRotorBlock();
            LightOccluder = new LightOccluderBlock();

            if (!BlockEntity.entityToByte.ContainsKey(typeof(MegaCrusherBlockEntity)))
            {
                BlockEntity.RegisterBlockEntity(typeof(MegaCrusherBlockEntity));
            }

            if (!BlockEntity.entityToByte.ContainsKey(typeof(TestRotorBlockEntity)))
            {
                BlockEntity.RegisterBlockEntity(typeof(TestRotorBlockEntity));
            }

            MegaCrusherModelCache.Initialize();
            TestRotorModelCache.Initialize();

            registered = true;
            Logger.Info($"Registered Mega Crusher block id={MegaCrusher.intID}, item={MegaCrusher.item.itemID} on attempt {attempt}");
            Logger.Info($"Registered Luma Rotor block id={TestRotor.intID}, item={TestRotor.item.itemID} on attempt {attempt}");
            Logger.Info($"Registered Luma light occluder block id={LightOccluder.intID}");
        }

        TryLoadAssets();

        if (!recipeAdded)
        {
            if (CraftingRecipe.recipes is null || CraftingRecipe.recipes.Count == 0)
            {
                return false;
            }

            AddWoodRecipe(MegaCrusher!.item, 1, "Mega Crusher");
            AddWoodRecipe(TestRotor!.item, 1, "Luma Rotor");
            AddWoodRecipe(Block.lamp_white.item, 8, "White Lamp");
            AddWoodRecipe(Block.lamp_red.item, 8, "Red Lamp");
            AddWoodRecipe(Block.lamp_green.item, 8, "Green Lamp");
            AddWoodRecipe(Block.lamp_blue.item, 8, "Blue Lamp");
            AddWoodRecipe(Block.lamp_cyan.item, 8, "Cyan Lamp");
            AddWoodRecipe(Block.lamp_magenta.item, 8, "Magenta Lamp");
            AddWoodRecipe(Block.lamp_yellow.item, 8, "Yellow Lamp");
            AddWoodRecipe(Block.torch.item, 8, "Torch");
            AddWoodRecipe(Block.white_torch.item, 8, "White Torch");
            AddWoodRecipe(Block.ice_torch.item, 8, "Ice Torch");
            AddWoodRecipe(Block.ritual_torch.item, 8, "Ritual Torch");

            recipeAdded = true;
            Logger.Info("Added Luma test recipes: 1x any planks -> Mega Crusher, Luma Rotor, and colored lights");
        }

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

    private static void TryLoadAssets()
    {
        if (assetsLoaded || MegaCrusher is null || TestRotor is null)
        {
            return;
        }

        try
        {
            MegaCrusher.LoadAssets();
            MegaCrusher.item.LoadAssets();
            TestRotor.LoadAssets();
            TestRotor.item.LoadAssets();
            assetsLoaded = true;
            Logger.Info("Luma preview block/item atlas assets loaded");
        }
        catch
        {
            // The atlas may not exist yet during early boot. A later install pass will retry.
        }
    }

    private static void AddWoodRecipe(Item resultItem, int amount, string label)
    {
        CraftingRecipe.recipes.RemoveAll(recipe => IsWoodTestRecipe(recipe, resultItem));
        CraftingRecipe.recipes.Add(
            new CraftingRecipe(new ItemStack(resultItem, amount), CraftingStation.inventory)
                .AddReq(new RecipeEntry(RecipeAlias.any_planks, 1)));
        Logger.Info($"Added recipe: 1x any planks -> {amount}x {label}");
    }

    private static bool IsWoodTestRecipe(CraftingRecipe recipe, Item resultItem)
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
}
