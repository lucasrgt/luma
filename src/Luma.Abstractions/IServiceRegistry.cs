namespace Luma.Abstractions;

public interface IServiceRegistry
{
    void Add<TService>(TService service)
        where TService : class;

    TService? Get<TService>()
        where TService : class;
}
