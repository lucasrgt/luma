namespace Luma.Abstractions;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class LumaModAttribute : Attribute
{
    public LumaModAttribute(string id, string name, string version)
    {
        Id = id;
        Name = name;
        Version = version;
    }

    public string Id { get; }

    public string Name { get; }

    public string Version { get; }
}
