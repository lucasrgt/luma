using Allumeria;
using System.Linq.Expressions;
using System.Reflection;
using Luma.Runtime;

namespace Luma.AllumeriaLoader;

internal static class AllumeriaRuntimeBridge
{
    private static int updateFrames;
    private static int renderFrames;

    public static async Task InstallAsync()
    {
        for (var attempt = 1; attempt <= 200; attempt++)
        {
            object? game = Game.game;
            if (game is not null)
            {
                TrySubscribe(game, "UpdateFrame", nameof(OnUpdateFrame));
                TrySubscribe(game, "RenderFrame", nameof(OnRenderFrame));
                Logger.Info($"Allumeria runtime bridge installed on attempt {attempt}");
                return;
            }

            await Task.Delay(100).ConfigureAwait(false);
        }

        Logger.Info("Allumeria runtime bridge timed out waiting for Game.game");
    }

    private static void TrySubscribe(object target, string eventName, string callbackName)
    {
        EventInfo? eventInfo = target.GetType().GetEvent(eventName, BindingFlags.Public | BindingFlags.Instance);
        if (eventInfo?.EventHandlerType is null)
        {
            Logger.Info($"Allumeria runtime bridge could not find event {eventName}");
            return;
        }

        MethodInfo callback = typeof(AllumeriaRuntimeBridge).GetMethod(callbackName, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(AllumeriaRuntimeBridge), callbackName);
        MethodInfo invoke = eventInfo.EventHandlerType.GetMethod("Invoke")
            ?? throw new MissingMethodException(eventInfo.EventHandlerType.FullName, "Invoke");

        ParameterExpression[] parameters = invoke.GetParameters()
            .Select(parameter => Expression.Parameter(parameter.ParameterType))
            .ToArray();

        Delegate handler = Expression.Lambda(eventInfo.EventHandlerType, Expression.Call(callback), parameters).Compile();
        eventInfo.AddEventHandler(target, handler);
        Logger.Info($"Allumeria runtime bridge subscribed to {eventName}");
    }

    private static void OnUpdateFrame()
    {
        updateFrames++;
        if (updateFrames is 1 or 300)
        {
            Logger.Info($"OpenTK UpdateFrame observed: {updateFrames}");
        }

        AllumeriaModelRegistry.UpdateAll();
        RuntimeHost.Instance.Tick(Game.game, 0d);
    }

    private static void OnRenderFrame()
    {
        renderFrames++;
        if (renderFrames is 1 or 300)
        {
            Logger.Info($"OpenTK RenderFrame observed: {renderFrames}");
        }

        RuntimeHost.Instance.Render(Game.game, null, 0d);
    }
}
