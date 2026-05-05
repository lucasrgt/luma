namespace Luma.Abstractions.Behaviors;

public interface ILumaBehaviorController
{
    string? CurrentState { get; }

    bool SetState(string stateName);

    bool Trigger(string triggerName);
}
