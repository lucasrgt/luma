using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Luma.Patcher;

internal sealed class AssemblyPatcher
{
    public PatchReport Patch(string inputAssemblyPath, string manifestPath, string outputAssemblyPath)
    {
        PatchManifest manifest = PatchManifest.Load(manifestPath);

        var parameters = new ReaderParameters
        {
            ReadSymbols = false,
            InMemory = true
        };

        using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(inputAssemblyPath, parameters);
        ModuleDefinition module = assembly.MainModule;

        ValidateModule(module, manifest);

        int applied = 0;
        int skipped = 0;
        foreach (HookSpec hook in manifest.Hooks)
        {
            MethodDefinition target = FindTarget(module, hook);
            MethodReference runtimeCall = BuildRuntimeCall(module, manifest, hook);

            if (ContainsHook(target, runtimeCall))
            {
                skipped++;
                continue;
            }

            ApplyHook(target, module, hook, runtimeCall);
            applied++;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputAssemblyPath)) ?? ".");
        assembly.Write(outputAssemblyPath);

        return new PatchReport(applied, skipped, Path.GetFullPath(outputAssemblyPath));
    }

    private static void ValidateModule(ModuleDefinition module, PatchManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.ExpectedModuleMvid))
        {
            return;
        }

        if (!Guid.TryParse(manifest.ExpectedModuleMvid, out Guid expected))
        {
            throw new InvalidOperationException($"Invalid manifest MVID: {manifest.ExpectedModuleMvid}");
        }

        if (module.Mvid != expected)
        {
            throw new InvalidOperationException($"Game assembly MVID mismatch. Expected {expected}, found {module.Mvid}.");
        }
    }

    private static MethodDefinition FindTarget(ModuleDefinition module, HookSpec hook)
    {
        TypeDefinition type = module.GetType(hook.TargetType)
            ?? throw new InvalidOperationException($"Hook {hook.Name}: target type not found: {hook.TargetType}");

        MethodDefinition? method = type.Methods.FirstOrDefault(m =>
            m.Name == hook.TargetMethod &&
            (hook.TargetParameterCount is null || m.Parameters.Count == hook.TargetParameterCount));

        return method
            ?? throw new InvalidOperationException($"Hook {hook.Name}: target method not found: {hook.TargetType}::{hook.TargetMethod}");
    }

    private static MethodReference BuildRuntimeCall(ModuleDefinition module, PatchManifest manifest, HookSpec hook)
    {
        var runtimeAssembly = new AssemblyNameReference(manifest.RuntimeAssemblyName, new Version(1, 0, 0, 0));
        var runtimeType = new TypeReference(manifest.RuntimeNamespace, manifest.RuntimeTypeName, module, runtimeAssembly);
        var method = new MethodReference(hook.RuntimeMethod, module.TypeSystem.Void, runtimeType)
        {
            HasThis = false
        };

        if (hook.ArgumentMode is HookArgumentMode.This or HookArgumentMode.FirstArgument)
        {
            method.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
        }

        return method;
    }

    private static bool ContainsHook(MethodDefinition target, MethodReference runtimeCall)
    {
        if (!target.HasBody)
        {
            return false;
        }

        string runtimeType = runtimeCall.DeclaringType.FullName;
        return target.Body.Instructions.Any(instruction =>
            instruction.Operand is MethodReference method &&
            method.Name == runtimeCall.Name &&
            method.DeclaringType.FullName == runtimeType);
    }

    private static void ApplyHook(MethodDefinition target, ModuleDefinition module, HookSpec hook, MethodReference runtimeCall)
    {
        if (!target.HasBody)
        {
            throw new InvalidOperationException($"Hook {hook.Name}: target method has no body.");
        }

        ILProcessor il = target.Body.GetILProcessor();
        switch (hook.Insert)
        {
            case HookInsertMode.Start:
                InsertBefore(target, module, hook, runtimeCall, il, target.Body.Instructions[0]);
                break;
            case HookInsertMode.BeforeReturn:
                foreach (Instruction ret in target.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToArray())
                {
                    InsertBefore(target, module, hook, runtimeCall, il, ret);
                }

                break;
            default:
                throw new InvalidOperationException($"Hook {hook.Name}: unsupported insert mode {hook.Insert}.");
        }
    }

    private static void InsertBefore(
        MethodDefinition target,
        ModuleDefinition module,
        HookSpec hook,
        MethodReference runtimeCall,
        ILProcessor il,
        Instruction anchor)
    {
        foreach (Instruction instruction in BuildCallInstructions(target, module, hook, runtimeCall))
        {
            il.InsertBefore(anchor, instruction);
        }
    }

    private static IEnumerable<Instruction> BuildCallInstructions(
        MethodDefinition target,
        ModuleDefinition module,
        HookSpec hook,
        MethodReference runtimeCall)
    {
        switch (hook.ArgumentMode)
        {
            case HookArgumentMode.None:
                break;
            case HookArgumentMode.This:
                if (target.IsStatic)
                {
                    throw new InvalidOperationException($"Hook {hook.Name}: cannot pass this from a static method.");
                }

                yield return Instruction.Create(OpCodes.Ldarg_0);
                break;
            case HookArgumentMode.FirstArgument:
                if (target.Parameters.Count == 0)
                {
                    throw new InvalidOperationException($"Hook {hook.Name}: target has no first argument.");
                }

                ParameterDefinition parameter = target.Parameters[0];
                yield return Instruction.Create(OpCodes.Ldarg, parameter);
                if (parameter.ParameterType.IsValueType)
                {
                    yield return Instruction.Create(OpCodes.Box, module.ImportReference(parameter.ParameterType));
                }

                break;
            default:
                throw new InvalidOperationException($"Hook {hook.Name}: unsupported argument mode {hook.ArgumentMode}.");
        }

        yield return Instruction.Create(OpCodes.Call, runtimeCall);
    }
}

internal sealed record PatchReport(int HooksApplied, int HooksSkipped, string OutputPath);
