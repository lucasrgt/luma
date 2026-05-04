using Luma.Abstractions.Models;

namespace Luma.AllumeriaLoader;

internal sealed class TestRotorBlock : AnimatedModelBlock<TestRotorBlockEntity>
{
    public const string BlockId = "luma.test_rotor";

    public TestRotorBlock()
        : base(
            BlockId,
            "Luma Rotor",
            "Small animated model portability test.")
    {
    }
}

internal sealed class TestRotorBlockEntity : AnimatedModelBlockEntity
{
    protected override ILumaAnimatedModel Model => TestRotorModelCache.Model;
}
