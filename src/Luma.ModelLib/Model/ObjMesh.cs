using System.Numerics;

namespace Luma.ModelLib.Model;

public sealed class ObjMesh
{
    public ObjMesh(
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<Vector2> texCoords,
        IReadOnlyList<Vector3> normals,
        IReadOnlyList<ObjFace> faces,
        IReadOnlyList<ObjPolygonFace> polygons,
        IReadOnlyList<string> groups)
    {
        Positions = positions;
        TexCoords = texCoords;
        Normals = normals;
        Faces = faces;
        Polygons = polygons;
        Groups = groups;
    }

    public IReadOnlyList<Vector3> Positions { get; }

    public IReadOnlyList<Vector2> TexCoords { get; }

    public IReadOnlyList<Vector3> Normals { get; }

    public IReadOnlyList<ObjFace> Faces { get; }

    public IReadOnlyList<ObjPolygonFace> Polygons { get; }

    public IReadOnlyList<string> Groups { get; }
}

public readonly record struct ObjFace(string GroupName, ObjVertex A, ObjVertex B, ObjVertex C);

public readonly record struct ObjPolygonFace(string GroupName, IReadOnlyList<ObjVertex> Vertices);

public readonly record struct ObjVertex(int PositionIndex, int TexCoordIndex, int NormalIndex);
