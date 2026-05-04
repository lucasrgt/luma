using Mono.Cecil;

namespace Luma.Patcher;

internal static class AssemblyTypeDumper
{
    public static void Dump(string assemblyPath, string typeName, TextWriter writer)
    {
        using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
        TypeDefinition type = WalkTypes(assembly.MainModule.Types).FirstOrDefault(t => t.FullName == typeName)
            ?? throw new InvalidOperationException($"Type not found: {typeName}");

        writer.WriteLine($"type {type.FullName}");
        writer.WriteLine();
        writer.WriteLine("fields:");
        foreach (FieldDefinition field in type.Fields)
        {
            writer.WriteLine($"  {MemberFlags(field)} {field.FieldType.FullName} {field.Name}");
        }

        writer.WriteLine();
        writer.WriteLine("methods:");
        foreach (MethodDefinition method in type.Methods)
        {
            string parameters = string.Join(", ", method.Parameters.Select(p => $"{p.ParameterType.FullName} {p.Name}"));
            writer.WriteLine($"  {MemberFlags(method)} {method.ReturnType.FullName} {method.Name}({parameters})");
        }
    }

    private static string MemberFlags(FieldDefinition field)
    {
        var flags = new List<string> { Access(field) };
        if (field.IsStatic)
        {
            flags.Add("static");
        }

        if (field.IsInitOnly)
        {
            flags.Add("readonly");
        }

        return string.Join(' ', flags);
    }

    private static string MemberFlags(MethodDefinition method)
    {
        var flags = new List<string> { Access(method) };
        if (method.IsStatic)
        {
            flags.Add("static");
        }

        if (method.IsVirtual)
        {
            flags.Add("virtual");
        }

        if (method.IsNewSlot)
        {
            flags.Add("newslot");
        }

        return string.Join(' ', flags);
    }

    private static string Access(FieldDefinition field)
    {
        if (field.IsPublic) return "public";
        if (field.IsFamily) return "protected";
        if (field.IsPrivate) return "private";
        if (field.IsAssembly) return "internal";
        return "unknown";
    }

    private static string Access(MethodDefinition method)
    {
        if (method.IsPublic) return "public";
        if (method.IsFamily) return "protected";
        if (method.IsPrivate) return "private";
        if (method.IsAssembly) return "internal";
        return "unknown";
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
