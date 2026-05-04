using Allumeria;
using System.Linq.Expressions;
using System.Reflection;
using Luma.Abstractions.Content;
using Luma.Abstractions.Models;
using Luma.Runtime;

namespace Luma.AllumeriaLoader;

public sealed class LumaExternalLoader : IExternalLoader
{
    public void Init()
    {
        try
        {
            Logger.Info("Init");
            Game.VERSION = $"{Game.VERSION}/luma";

            LumaModelShader.PrepareFiles();
            RuntimeHost.Instance.AddService<ILumaModelService>(AllumeriaModelRegistry.ModelService);
            RuntimeHost.Instance.AddService<ILumaContentService>(AllumeriaContentService.Instance);
            RuntimeHost.Instance.Initialize(Game.game);
            _ = AllumeriaContentService.Instance.InstallAsync();
            if (IsPreviewContentEnabled())
            {
                _ = LumaPreviewContent.InstallAsync();
                Logger.Info("Luma preview content bootstrap scheduled");
            }
            else
            {
                Logger.Info("Luma preview content disabled; set LUMA_PREVIEW_CONTENT=1 to enable Mega Crusher debug recipes");
            }

            _ = NativeFrameProbe.InstallAsync();
            Logger.Info("Luma runtime initialized");
            Logger.Info("Native frame probe scheduled");
        }
        catch (Exception ex)
        {
            Logger.Error("Loader init failed", ex);
            throw;
        }
    }

    private static bool IsPreviewContentEnabled()
    {
        string? value = Environment.GetEnvironmentVariable("LUMA_PREVIEW_CONTENT");
        return value is "1" or "true" or "TRUE" or "on" or "ON";
    }
}

internal static class NativeFrameProbe
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
                Logger.Info($"Native frame probe installed on attempt {attempt}");
                return;
            }

            await Task.Delay(100).ConfigureAwait(false);
        }

        Logger.Info("Native frame probe timed out waiting for Game.game");
    }

    private static void TrySubscribe(object target, string eventName, string callbackName)
    {
        EventInfo? eventInfo = target.GetType().GetEvent(eventName, BindingFlags.Public | BindingFlags.Instance);
        if (eventInfo?.EventHandlerType is null)
        {
            Logger.Info($"Native frame probe could not find event {eventName}");
            return;
        }

        MethodInfo callback = typeof(NativeFrameProbe).GetMethod(callbackName, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(NativeFrameProbe), callbackName);
        MethodInfo invoke = eventInfo.EventHandlerType.GetMethod("Invoke")
            ?? throw new MissingMethodException(eventInfo.EventHandlerType.FullName, "Invoke");

        ParameterExpression[] parameters = invoke.GetParameters()
            .Select(parameter => Expression.Parameter(parameter.ParameterType))
            .ToArray();

        Delegate handler = Expression.Lambda(eventInfo.EventHandlerType, Expression.Call(callback), parameters).Compile();
        eventInfo.AddEventHandler(target, handler);
        Logger.Info($"Native frame probe subscribed to {eventName}");
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

internal static class Logger
{
    private static readonly object Lock = new();

    private static string LogPath => Path.Combine(Directory.GetCurrentDirectory(), "luma-loader.log");

    public static void Info(string message)
    {
        Write("INFO", message, null);
    }

    public static void Error(string message, Exception ex)
    {
        Write("ERROR", message, ex);
    }

    private static void Write(string level, string message, Exception? ex)
    {
        lock (Lock)
        {
            File.AppendAllText(
                LogPath,
                $"[{DateTimeOffset.Now:O}] [{level}] {message}{Environment.NewLine}{ex}{Environment.NewLine}");
        }
    }
}
