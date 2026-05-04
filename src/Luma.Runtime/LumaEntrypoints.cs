namespace Luma.Runtime;

public static class LumaEntrypoints
{
    public static void RegisterService<TService>(TService service)
        where TService : class
    {
        RuntimeHost.Instance.AddService(service);
    }

    public static void OnGameInit()
    {
        RuntimeHost.Instance.Initialize(null);
    }

    public static void OnGameInit(object? gameInstance)
    {
        RuntimeHost.Instance.Initialize(gameInstance);
    }

    public static void OnGameTick()
    {
        RuntimeHost.Instance.Tick(null, 0d);
    }

    public static void OnGameTick(object? gameInstance)
    {
        RuntimeHost.Instance.Tick(gameInstance, 0d);
    }

    public static void OnGameTick(object? gameInstance, float deltaSeconds)
    {
        RuntimeHost.Instance.Tick(gameInstance, deltaSeconds);
    }

    public static void OnRenderFrame()
    {
        RuntimeHost.Instance.Render(null, null, 0d);
    }

    public static void OnRenderFrame(object? renderer)
    {
        RuntimeHost.Instance.Render(null, renderer, 0d);
    }

    public static void OnRenderFrame(object? gameInstance, object? renderer)
    {
        RuntimeHost.Instance.Render(gameInstance, renderer, 0d);
    }

    public static void OnRenderFrame(object? gameInstance, object? renderer, float deltaSeconds)
    {
        RuntimeHost.Instance.Render(gameInstance, renderer, deltaSeconds);
    }

    public static void OnShutdown()
    {
        RuntimeHost.Instance.Shutdown();
    }
}
