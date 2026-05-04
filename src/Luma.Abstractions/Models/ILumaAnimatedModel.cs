namespace Luma.Abstractions.Models;

public interface ILumaAnimatedModel
{
    string Name { get; }

    void Update();

    bool SetAnimation(string animationName, bool loop = true, float stepSeconds = 1f / 60f);

    void PauseAnimation();

    void RestartAnimation(float stepSeconds = 1f / 60f);

    void RenderBlock(LumaVector3 position);

    void Render(LumaModelRenderRequest request);
}
