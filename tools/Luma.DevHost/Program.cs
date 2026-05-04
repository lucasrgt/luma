using Luma.Runtime;

Console.WriteLine("Luma DevHost starting.");
Console.WriteLine($"Base directory: {AppContext.BaseDirectory}");

LumaEntrypoints.OnGameInit(new DevGame());

for (int i = 0; i < 5; i++)
{
    LumaEntrypoints.OnGameTick(new DevGame(), 1f / 60f);
    LumaEntrypoints.OnRenderFrame(new DevGame(), new DevRenderer(), 1f / 60f);
}

LumaEntrypoints.OnShutdown();
Console.WriteLine("Luma DevHost finished. Check luma.log next to the executable.");

internal sealed class DevGame;

internal sealed class DevRenderer;
