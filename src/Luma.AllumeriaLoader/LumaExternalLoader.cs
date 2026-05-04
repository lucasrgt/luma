using Allumeria;
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
            _ = AllumeriaRuntimeBridge.InstallAsync();
            Logger.Info("Luma runtime initialized");
            Logger.Info("Allumeria runtime bridge scheduled");
        }
        catch (Exception ex)
        {
            Logger.Error("Loader init failed", ex);
            throw;
        }
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
