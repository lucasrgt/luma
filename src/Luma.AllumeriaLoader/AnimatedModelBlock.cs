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
    , ILightOcclusionOwner
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

    protected virtual LightOcclusionVolume LightOcclusionVolume => LightOcclusionVolume.Empty;

    protected virtual LightOcclusionVolume LightOcclusionCleanupVolume => LightOcclusionVolume;

    public override bool IsFaceSolid(AxisDir dir, uint thisMetadata) => false;

    public override bool DoesThisOcclude(Block block, uint metadata, AxisDir dir) => false;

    public override void OnPlace(PlayerEntity player, int x, int y, int z, World world)
    {
        base.OnPlace(player, x, y, z, world);
        world.chunkManager.AddBlockEntity(new TBlockEntity(), x, y, z);
        EnsureLightOcclusion(x, y, z, world);
        Logger.Info($"{item.translatedName} block placed at {x}, {y}, {z}");
    }

    public override void OnBreak(PlayerEntity player, int x, int y, int z, World world, uint metadata)
    {
        ClearLightOcclusion(x, y, z, world);
        base.OnBreak(player, x, y, z, world, metadata);
    }

    public override void OnDelete(int x, int y, int z, World world)
    {
        ClearLightOcclusion(x, y, z, world);
        base.OnDelete(x, y, z, world);
    }

    public void EnsureLightOcclusion(int x, int y, int z, World world)
    {
        if (LumaPreviewContent.LightOccluder is null)
        {
            return;
        }

        HashSet<(int X, int Y, int Z)> desiredOffsets = LightOcclusionVolume.EnumerateOffsets().ToHashSet();
        RemoveStaleLightOccluders(x, y, z, world, desiredOffsets);

        if (LightOcclusionVolume.IsEmpty)
        {
            return;
        }

        foreach ((int offsetX, int offsetY, int offsetZ) in desiredOffsets)
        {
            int targetX = x + offsetX;
            int targetY = y + offsetY;
            int targetZ = z + offsetZ;
            Block? targetBlock = world.chunkManager.GetBlockIfExists(targetX, targetY, targetZ);
            if (targetBlock is null ||
                (!ReferenceEquals(targetBlock, Block.empty) && !IsLightOccluder(targetBlock)))
            {
                continue;
            }

            world.chunkManager.SetBlockWithUpdateAndLight(
                targetX,
                targetY,
                targetZ,
                LumaPreviewContent.LightOccluder);
        }
    }

    private void ClearLightOcclusion(int x, int y, int z, World world)
    {
        if (LightOcclusionCleanupVolume.IsEmpty || LumaPreviewContent.LightOccluder is null)
        {
            return;
        }

        foreach ((int offsetX, int offsetY, int offsetZ) in LightOcclusionCleanupVolume.EnumerateOffsets())
        {
            int targetX = x + offsetX;
            int targetY = y + offsetY;
            int targetZ = z + offsetZ;
            Block? targetBlock = world.chunkManager.GetBlockIfExists(targetX, targetY, targetZ);
            if (targetBlock is null || !IsLightOccluder(targetBlock))
            {
                continue;
            }

            world.chunkManager.SetBlockWithUpdateAndLight(
                targetX,
                targetY,
                targetZ,
                Block.empty);
        }
    }

    private void RemoveStaleLightOccluders(
        int x,
        int y,
        int z,
        World world,
        HashSet<(int X, int Y, int Z)> desiredOffsets)
    {
        if (LightOcclusionCleanupVolume.IsEmpty)
        {
            return;
        }

        foreach ((int offsetX, int offsetY, int offsetZ) in LightOcclusionCleanupVolume.EnumerateOffsets())
        {
            if (desiredOffsets.Contains((offsetX, offsetY, offsetZ)))
            {
                continue;
            }

            int targetX = x + offsetX;
            int targetY = y + offsetY;
            int targetZ = z + offsetZ;
            Block? targetBlock = world.chunkManager.GetBlockIfExists(targetX, targetY, targetZ);
            if (targetBlock is null || !IsLightOccluder(targetBlock))
            {
                continue;
            }

            world.chunkManager.SetBlockWithUpdateAndLight(
                targetX,
                targetY,
                targetZ,
                Block.empty);
        }
    }

    private static bool IsLightOccluder(Block block)
    {
        return LumaPreviewContent.LightOccluder is not null &&
            ReferenceEquals(block, LumaPreviewContent.LightOccluder);
    }
}

internal abstract class AnimatedModelBlockEntity : BlockEntity
{
    private bool lightOcclusionEnsured;

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
        EnsureLightOcclusion();
        if (TryGetModel(out ILumaAnimatedModel model))
        {
            model.RenderBlock(new LumaVector3(posX, posY, posZ));
        }
    }

    private void EnsureLightOcclusion()
    {
        if (lightOcclusionEnsured)
        {
            return;
        }

        lightOcclusionEnsured = true;
        World? world = Game.gameState?.worldManager?.world;
        Block? block = world?.chunkManager.GetBlockIfExists(posX, posY, posZ);
        if (world is not null && block is ILightOcclusionOwner owner)
        {
            owner.EnsureLightOcclusion(posX, posY, posZ, world);
        }
    }
}

internal sealed class LightOccluderBlock : Block
{
    public const string BlockId = "luma.light_occluder";

    public LightOccluderBlock()
        : base(BlockId)
    {
        MakeTransparent();
        SetTexture("transparent");
        SetBlockModel(new BlockModelQuads());
        SetColliderType(Collider.ColliderType.NoPhysics);
        DisableItemDrops();
        Hide();

        canWalkThrough = true;
        blocksLight = true;
        blockHorizontalLight = true;
        blocksFluid = false;
        ignoreOcclusionChecks = true;
        skipSmoothLighting = true;
    }

    public override bool IsFaceSolid(AxisDir dir, uint thisMetadata) => false;

    public override bool DoesThisOcclude(Block block, uint metadata, AxisDir dir) => false;
}

internal interface ILightOcclusionOwner
{
    void EnsureLightOcclusion(int x, int y, int z, World world);
}

internal readonly record struct LightOcclusionVolume(
    int MinX,
    int MinY,
    int MinZ,
    int MaxX,
    int MaxY,
    int MaxZ)
{
    public static LightOcclusionVolume Empty { get; } = new(1, 1, 1, 0, 0, 0);

    public bool IsEmpty => MaxX < MinX || MaxY < MinY || MaxZ < MinZ;

    public IEnumerable<(int X, int Y, int Z)> EnumerateOffsets()
    {
        for (int y = MinY; y <= MaxY; y++)
        {
            for (int z = MinZ; z <= MaxZ; z++)
            {
                for (int x = MinX; x <= MaxX; x++)
                {
                    yield return (x, y, z);
                }
            }
        }
    }
}
