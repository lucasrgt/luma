using System.Reflection;
using System.Runtime.Loader;
using Luma.Abstractions;

namespace Luma.Runtime;

public sealed class RuntimeHost
{
    private readonly object gate = new();
    private readonly List<LoadedMod> mods = new();
    private readonly RuntimeServiceRegistry services = new();
    private RuntimeModContext? context;
    private bool initialized;
    private long tickIndex;
    private long frameIndex;

    public static RuntimeHost Instance { get; } = new();

    private RuntimeHost()
    {
    }

    public void AddService<TService>(TService service)
        where TService : class
    {
        lock (gate)
        {
            services.Add(service);
            context?.Logger.Info($"Registered runtime service {typeof(TService).FullName}.");
        }
    }

    public void Initialize(object? gameInstance)
    {
        lock (gate)
        {
            if (initialized)
            {
                return;
            }

            string gameDirectory = AppContext.BaseDirectory;
            string modsDirectory = Path.Combine(gameDirectory, "mods");
            Directory.CreateDirectory(modsDirectory);

            var logger = new FileModLogger(Path.Combine(gameDirectory, "luma.log"));
            context = new RuntimeModContext(
                gameDirectory,
                modsDirectory,
                logger,
                new FileModAssets(Path.Combine(gameDirectory, "assets")),
                services);

            logger.Info("Luma runtime booting.");
            logger.Info($"Game directory: {gameDirectory}");
            logger.Info($"Mods directory: {modsDirectory}");

            LoadMods(modsDirectory, logger);
            initialized = true;

            foreach (LoadedMod mod in mods)
            {
                try
                {
                    logger.Info($"Initializing mod {mod.Id} ({mod.Name} {mod.Version}).");
                    mod.Instance.Init(context);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, $"Mod {mod.Id} failed during Init.");
                }
            }

            logger.Info($"Luma runtime ready. Loaded mods: {mods.Count}.");
        }
    }

    public void Tick(object? gameInstance, double deltaSeconds)
    {
        RuntimeModContext? current = EnsureInitialized(gameInstance);
        if (current is null)
        {
            return;
        }

        var tick = new RuntimeTickContext(++tickIndex, deltaSeconds, gameInstance);
        foreach (LoadedMod mod in mods)
        {
            try
            {
                mod.Instance.Tick(tick);
            }
            catch (Exception ex)
            {
                current.Logger.Error(ex, $"Mod {mod.Id} failed during Tick.");
            }
        }
    }

    public void Render(object? gameInstance, object? renderer, double deltaSeconds)
    {
        RuntimeModContext? current = EnsureInitialized(gameInstance);
        if (current is null)
        {
            return;
        }

        var render = new RuntimeRenderContext(++frameIndex, deltaSeconds, gameInstance, renderer);
        foreach (LoadedMod mod in mods)
        {
            try
            {
                mod.Instance.Render(render);
            }
            catch (Exception ex)
            {
                current.Logger.Error(ex, $"Mod {mod.Id} failed during Render.");
            }
        }
    }

    public void Shutdown()
    {
        RuntimeModContext? current = context;
        foreach (LoadedMod mod in mods)
        {
            try
            {
                mod.Instance.Shutdown();
            }
            catch (Exception ex)
            {
                current?.Logger.Error(ex, $"Mod {mod.Id} failed during Shutdown.");
            }
        }
    }

    private RuntimeModContext? EnsureInitialized(object? gameInstance)
    {
        if (!initialized)
        {
            Initialize(gameInstance);
        }

        return context;
    }

    private void LoadMods(string modsDirectory, IModLogger logger)
    {
        foreach (string dllPath in Directory.EnumerateFiles(modsDirectory, "*.dll", SearchOption.TopDirectoryOnly))
        {
            if (ShouldSkipRuntimeAssembly(dllPath))
            {
                continue;
            }

            try
            {
                Assembly assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(dllPath));
                foreach (Type type in assembly.GetTypes())
                {
                    if (!typeof(IAllumeriaMod).IsAssignableFrom(type) || type.IsAbstract || type.GetConstructor(Type.EmptyTypes) is null)
                    {
                        continue;
                    }

                    var attribute = type.GetCustomAttribute<LumaModAttribute>();
                    string id = attribute?.Id ?? type.FullName ?? type.Name;
                    string name = attribute?.Name ?? type.Name;
                    string version = attribute?.Version ?? "0.0.0";

                    var instance = (IAllumeriaMod)Activator.CreateInstance(type)!;
                    mods.Add(new LoadedMod(id, name, version, instance));
                    logger.Info($"Discovered mod {id} from {Path.GetFileName(dllPath)}.");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Failed to load mod assembly {dllPath}.");
            }
        }
    }

    private static bool ShouldSkipRuntimeAssembly(string dllPath)
    {
        string fileName = Path.GetFileName(dllPath);
        return fileName.Equals("Loader.dll", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("Luma.Abstractions.dll", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("Luma.Runtime.dll", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("Luma.ModelLib.dll", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record LoadedMod(string Id, string Name, string Version, IAllumeriaMod Instance);
}
