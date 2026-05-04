namespace Luma.Abstractions;

public interface IAllumeriaMod
{
    void Init(IModContext context);

    void Tick(IModTickContext context)
    {
    }

    void Render(IModRenderContext context)
    {
    }

    void Shutdown()
    {
    }
}
