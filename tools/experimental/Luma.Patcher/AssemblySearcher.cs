using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Luma.Patcher;

internal static class AssemblySearcher
{
    public static void Search(string assemblyPath, string pattern, TextWriter writer)
    {
        using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
        StringComparison comparison = StringComparison.OrdinalIgnoreCase;

        foreach (TypeDefinition type in WalkTypes(assembly.MainModule.Types))
        {
            if (type.FullName.Contains(pattern, comparison))
            {
                writer.WriteLine($"type   {type.FullName}");
            }

            foreach (FieldDefinition field in type.Fields)
            {
                if (field.Name.Contains(pattern, comparison) || field.FieldType.FullName.Contains(pattern, comparison))
                {
                    writer.WriteLine($"field  {type.FullName}::{field.Name} : {field.FieldType.FullName}");
                }
            }

            foreach (MethodDefinition method in type.Methods)
            {
                if (method.Name.Contains(pattern, comparison) || method.ReturnType.FullName.Contains(pattern, comparison))
                {
                    writer.WriteLine($"method {MethodSignature(type, method)}");
                }

                foreach (ParameterDefinition parameter in method.Parameters)
                {
                    if (parameter.ParameterType.FullName.Contains(pattern, comparison))
                    {
                        writer.WriteLine($"param  {MethodSignature(type, method)} :: {parameter.Name}:{parameter.ParameterType.FullName}");
                    }
                }

                if (!method.HasBody)
                {
                    continue;
                }

                foreach (Instruction instruction in method.Body.Instructions)
                {
                    if (instruction.OpCode == OpCodes.Ldstr &&
                        instruction.Operand is string value &&
                        value.Contains(pattern, comparison))
                    {
                        writer.WriteLine($"ldstr  {MethodSignature(type, method)} :: \"{value}\"");
                    }

                    if (instruction.Operand is FieldReference fieldRef &&
                        (fieldRef.Name.Contains(pattern, comparison) || fieldRef.FullName.Contains(pattern, comparison)))
                    {
                        writer.WriteLine($"fieldref {MethodSignature(type, method)} :: {fieldRef.FullName}");
                    }

                    if (instruction.Operand is MethodReference methodRef &&
                        (methodRef.Name.Contains(pattern, comparison) || methodRef.FullName.Contains(pattern, comparison)))
                    {
                        writer.WriteLine($"callref {MethodSignature(type, method)} :: {methodRef.FullName}");
                    }
                }
            }
        }
    }

    private static string MethodSignature(TypeDefinition type, MethodDefinition method)
    {
        string parameters = string.Join(", ", method.Parameters.Select(p => p.ParameterType.Name));
        return $"{type.FullName}::{method.Name}({parameters})";
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
