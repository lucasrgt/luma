using Luma.Abstractions.Models;

namespace Luma.AllumeriaLoader;

internal sealed class MegaCrusherBlock : AnimatedModelBlock<MegaCrusherBlockEntity>
{
    public const string BlockId = "luma.mega_crusher";

    public MegaCrusherBlock()
        : base(
            BlockId,
            "Mega Crusher",
            "Luma animated machine prototype.")
    {
    }

    protected override LightOcclusionVolume LightOcclusionVolume => LightOcclusionVolume.Empty;

    protected override LightOcclusionVolume LightOcclusionCleanupVolume { get; } = new(0, 0, 0, 3, 3, 3);
}

internal sealed class MegaCrusherBlockEntity : AnimatedModelBlockEntity
{
    protected override ILumaAnimatedModel Model => MegaCrusherModelCache.Model;
}
