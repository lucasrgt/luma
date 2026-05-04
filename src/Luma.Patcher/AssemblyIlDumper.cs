using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Luma.Patcher;

internal static class AssemblyIlDumper
{
    public static void Dump(string assemblyPath, string typeName, string methodName, TextWriter writer)
    {
        using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
        TypeDefinition type = WalkTypes(assembly.MainModule.Types).FirstOrDefault(t => t.FullName == typeName)
            ?? throw new InvalidOperationException($"Type not found: {typeName}");

        List<MethodDefinition> matches = type.Methods
            .Where(m => m.Name == methodName || $"{m.Name}({string.Join(", ", m.Parameters.Select(p => p.ParameterType.Name))})" == methodName)
            .ToList();

        if (matches.Count == 0)
        {
            throw new InvalidOperationException($"Method not found: {typeName}::{methodName}");
        }

        foreach (MethodDefinition method in matches)
        {
            writer.WriteLine($"{type.FullName}::{method.Name}({string.Join(", ", method.Parameters.Select(p => p.ParameterType.FullName))})");
            if (!method.HasBody)
            {
                writer.WriteLine("  <no body>");
                continue;
            }

            foreach (Instruction instruction in method.Body.Instructions)
            {
                writer.WriteLine($"  IL_{instruction.Offset:X4}: {instruction.OpCode,-12} {FormatOperand(instruction.Operand)}");
            }

            writer.WriteLine();
        }
    }

    private static string FormatOperand(object? operand)
    {
        return operand switch
        {
            null => string.Empty,
            MethodReference method => method.FullName,
            FieldReference field => field.FullName,
            TypeReference type => type.FullName,
            Instruction instruction => $"IL_{instruction.Offset:X4}",
            Instruction[] instructions => string.Join(", ", instructions.Select(i => $"IL_{i.Offset:X4}")),
            _ => operand.ToString() ?? string.Empty
        };
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
}
