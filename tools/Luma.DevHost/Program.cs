using Luma.Abstractions.Content;
using Luma.Abstractions.Models;
using Luma.Runtime;

Console.WriteLine("Luma DevHost starting.");
Console.WriteLine($"Base directory: {AppContext.BaseDirectory}");

LumaEntrypoints.RegisterService<ILumaContentService>(new DevContentService());
LumaEntrypoints.OnGameInit(new DevGame());

for (int i = 0; i < 5; i++)
{
    LumaEntrypoints.OnGameTick(new DevGame(), 1f / 60f);
    LumaEntrypoints.OnRenderFrame(new DevGame(), new DevRenderer(), 1f / 60f);
}

LumaEntrypoints.OnShutdown();
Console.WriteLine("Luma DevHost finished. Check luma.log next to the executable.");

internal sealed class DevGame;

internal sealed class DevRenderer;

internal sealed class DevContentService : ILumaContentService
{
    public void RegisterAnimatedBlock(LumaAnimatedBlockSpec spec)
    {
        Console.WriteLine($"[DevHost] Registered animated block: {spec.BlockId} -> {spec.Model.ModelPath}");
        foreach (LumaRecipeSpec recipe in spec.Recipes)
        {
            Console.WriteLine($"[DevHost] Recipe: {FormatRecipe(recipe, spec.BlockId)}");
        }

        PrintBehavior(spec);
        PrintAnimationEffects(spec);
    }

    public ILumaAnimationController? GetAnimationControllerAt(int x, int y, int z)
    {
        return null;
    }

    public bool TriggerAnimationAt(int x, int y, int z, string triggerName)
    {
        return false;
    }

    public ILumaBlockBehaviorController? GetBehaviorControllerAt(int x, int y, int z)
    {
        return null;
    }

    public bool TriggerBehaviorAt(int x, int y, int z, string triggerName)
    {
        return false;
    }

    private static string FormatRecipe(LumaRecipeSpec recipe, string blockId)
    {
        string ingredients = string.Join(", ", recipe.Ingredients.Select(ingredient =>
        {
            string id = ingredient.AliasId is not null
                ? $"alias:{ingredient.AliasId}"
                : ingredient.ItemId ?? "unknown";
            return $"{ingredient.Amount}x {id}";
        }));

        return $"{ingredients} -> {recipe.OutputCount}x {blockId} @ {recipe.Station}";
    }

    private static void PrintAnimationEffects(LumaAnimatedBlockSpec spec)
    {
        foreach (LumaAnimationStateSpec state in spec.Model.AnimationGraph?.States ?? [])
        {
            foreach (LumaAnimationEventSpec animationEvent in state.Events)
            {
                foreach (LumaAnimationEffectSpec effect in animationEvent.Effects)
                {
                    Console.WriteLine($"[DevHost] Event effect: {state.Name}/{animationEvent.Name} -> {effect.Kind}:{effect.Id}");
                }
            }
        }
    }

    private static void PrintBehavior(LumaAnimatedBlockSpec spec)
    {
        if (spec.Behavior is null)
        {
            return;
        }

        string states = string.Join(", ", spec.Behavior.States.Select(state =>
            string.IsNullOrWhiteSpace(state.AnimationState)
                ? state.Name
                : $"{state.Name}->{state.AnimationState}"));
        Console.WriteLine($"[DevHost] Behavior: initial={spec.Behavior.GetInitialState()?.Name}, states={states}");
    }
}
