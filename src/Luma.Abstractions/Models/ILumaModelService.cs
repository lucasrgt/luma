namespace Luma.Abstractions.Models;

public interface ILumaModelService
{
    ILumaAnimatedModel LoadAnimated(LumaAnimatedModelSpec spec);
}
