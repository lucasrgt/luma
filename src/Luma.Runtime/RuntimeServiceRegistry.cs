using Luma.Abstractions;

namespace Luma.Runtime;

internal sealed class RuntimeServiceRegistry : IServiceRegistry
{
    private readonly Dictionary<Type, object> services = new();

    public void Add<TService>(TService service)
        where TService : class
    {
        services[typeof(TService)] = service;
    }

    public TService? Get<TService>()
        where TService : class
    {
        return services.TryGetValue(typeof(TService), out object? service)
            ? (TService)service
            : null;
    }
}
