namespace Luma.Abstractions.Models;

public readonly record struct LumaModelRenderRequest(LumaVector3 Position, float Yaw = 0f)
{
    public static LumaModelRenderRequest Block(LumaVector3 position) => new(position);
}
