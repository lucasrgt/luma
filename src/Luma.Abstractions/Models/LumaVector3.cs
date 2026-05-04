namespace Luma.Abstractions.Models;

public readonly record struct LumaVector3(float X, float Y, float Z)
{
    public static LumaVector3 FromBlock(int x, int y, int z) => new(x, y, z);
}
