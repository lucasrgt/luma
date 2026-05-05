namespace Luma.Abstractions.Models;

public interface ILumaAnimatedModel
{
    string Name { get; }

    ILumaAnimationController Animation { get; }

    void Update();

    bool SetAnimation(string animationName, bool loop = true, float stepSeconds = 1f / 60f);

    void PauseAnimation();

    void RestartAnimation(float stepSeconds = 1f / 60f);

    void RenderBlock(LumaVector3 position);

    void Render(LumaModelRenderRequest request);
}

public interface ILumaAnimationController
{
    string? CurrentState { get; }

    string? CurrentAnimation { get; }

    bool SetState(string stateName);

    bool Trigger(string triggerName);

    void Pause();

    void Resume();

    void Restart(float? stepSeconds = null);

    IReadOnlyList<LumaAnimationEvent> DrainEvents();

    bool SetBoneOverride(string boneName, LumaBoneOverrideSpec boneOverride);

    bool ClearBoneOverride(string boneName);

    void ClearBoneOverrides();
}
