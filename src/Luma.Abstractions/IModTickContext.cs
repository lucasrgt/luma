namespace Luma.Abstractions;

public interface IModTickContext
{
    long TickIndex { get; }

    double DeltaSeconds { get; }

    object? GameInstance { get; }
}
