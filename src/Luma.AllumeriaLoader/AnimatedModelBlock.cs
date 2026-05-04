using Allumeria;
using Allumeria.Blocks.BlockEntities;
using Allumeria.Blocks.BlockModels;
using Allumeria.Blocks.Blocks;
using Allumeria.ChunkManagement;
using Allumeria.EntitySystem;
using Allumeria.EntitySystem.Entities;
using Luma.Abstractions.Models;

namespace Luma.AllumeriaLoader;

internal abstract class AnimatedModelBlock<TBlockEntity> : Block
    where TBlockEntity : BlockEntity, new()
{
    protected AnimatedModelBlock(
        string blockId,
        string translatedName,
        string translatedDescription,
        string itemSprite = "furnace")
        : base(blockId)
    {
        MakeTransparent();
        SetTexture("transparent");
        SetBlockModel(new BlockModelQuads());
        SetColliderType(Collider.ColliderType.NoPhysics);
        SetItemSprite(itemSprite);

        item.translatedName = translatedName;
        item.translatedDesc = translatedDescription;

        hasBlockEntity = true;
        canWalkThrough = true;
        blocksLight = false;
        blockHorizontalLight = false;
        blocksFluid = false;
        ignoreOcclusionChecks = true;
        skipSmoothLighting = true;
        decorationScore = 100f;
    }

    public override bool IsFaceSolid(AxisDir dir, uint thisMetadata) => false;

    public override bool DoesThisOcclude(Block block, uint metadata, AxisDir dir) => false;

    public override void OnPlace(PlayerEntity player, int x, int y, int z, World world)
    {
        base.OnPlace(player, x, y, z, world);
        world.chunkManager.AddBlockEntity(new TBlockEntity(), x, y, z);
        Logger.Info($"{item.translatedName} block placed at {x}, {y}, {z}");
    }
}

internal abstract class AnimatedModelBlockEntity : BlockEntity
{
    protected AnimatedModelBlockEntity()
    {
        hasRenderer = true;
    }

    protected abstract ILumaAnimatedModel Model { get; }

    protected virtual bool TryGetModel(out ILumaAnimatedModel model)
    {
        model = Model;
        return true;
    }

    public override void Render()
    {
        if (TryGetModel(out ILumaAnimatedModel model))
        {
            model.RenderBlock(new LumaVector3(posX, posY, posZ));
        }
    }
}
