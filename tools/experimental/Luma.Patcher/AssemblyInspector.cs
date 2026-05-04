using Mono.Cecil;

namespace Luma.Patcher;

internal static class AssemblyInspector
{
    private static readonly string[] InterestingNames =
    [
        "Program",
        "Game",
        "Tick",
        "Update",
        "Render",
        "Draw",
        "Register",
        "Content",
        "Load"
    ];

    public static void Inspect(string assemblyPath, TextWriter writer)
    {
        using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
        ModuleDefinition module = assembly.MainModule;

        writer.WriteLine($"Assembly: {assembly.Name.Name}");
        writer.WriteLine($"Version:  {assembly.Name.Version}");
        writer.WriteLine($"Module:   {module.Name}");
        writer.WriteLine($"MVID:     {module.Mvid}");
        writer.WriteLine();
        writer.WriteLine("Candidate methods:");

        foreach (TypeDefinition type in WalkTypes(module.Types))
        {
            if (!IsInteresting(type.FullName))
            {
                continue;
            }

            foreach (MethodDefinition method in type.Methods.Where(m => IsInteresting(m.Name)))
            {
                string parameters = string.Join(", ", method.Parameters.Select(p => p.ParameterType.Name));
                writer.WriteLine($"  {type.FullName}::{method.Name}({parameters})");
            }
        }
    }

    private static IEnumerable<TypeDefinition> WalkTypes(IEnumerable<TypeDefinition> types)
    {
        foreach (TypeDefinition type in types)
        {
            yield return type;
            foreach (TypeDefinition nested in WalkTypes(type.NestedTypes))
            {
                yield return nested;
            }
        }
    }

    private static bool IsInteresting(string value)
    {
        return InterestingNames.Any(name => value.Contains(name, StringComparison.OrdinalIgnoreCase));
    }
}
