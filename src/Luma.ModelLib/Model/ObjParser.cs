using System.Globalization;
using System.Numerics;

namespace Luma.ModelLib.Model;

public static class ObjParser
{
    public static ObjMesh Parse(string objText)
    {
        var positions = new List<Vector3>();
        var texCoords = new List<Vector2>();
        var normals = new List<Vector3>();
        var faces = new List<ObjFace>();
        var polygons = new List<ObjPolygonFace>();
        var groups = new List<string>();
        var seenGroups = new HashSet<string>(StringComparer.Ordinal);
        string currentGroup = "default";
        AddGroup(currentGroup, groups, seenGroups);

        using var reader = new StringReader(objText);
        while (reader.ReadLine() is { } line)
        {
            line = line.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (parts[0])
            {
                case "v" when parts.Length >= 4:
                    positions.Add(new Vector3(ParseFloat(parts[1]), ParseFloat(parts[2]), ParseFloat(parts[3])));
                    break;
                case "vt" when parts.Length >= 3:
                    texCoords.Add(new Vector2(ParseFloat(parts[1]), ParseFloat(parts[2])));
                    break;
                case "vn" when parts.Length >= 4:
                    normals.Add(Vector3.Normalize(new Vector3(ParseFloat(parts[1]), ParseFloat(parts[2]), ParseFloat(parts[3]))));
                    break;
                case "o" or "g" when parts.Length >= 2:
                    currentGroup = parts[1];
                    AddGroup(currentGroup, groups, seenGroups);
                    break;
                case "f" when parts.Length >= 4:
                    ObjVertex[] polygon = ParsePolygon(
                        parts.AsSpan(1),
                        positions.Count,
                        texCoords.Count,
                        normals.Count);
                    polygons.Add(new ObjPolygonFace(currentGroup, polygon));
                    AddTriangulatedFace(currentGroup, polygon, faces);
                    break;
            }
        }

        return new ObjMesh(positions, texCoords, normals, faces, polygons, groups);
    }

    private static void AddGroup(string groupName, List<string> groups, HashSet<string> seenGroups)
    {
        if (seenGroups.Add(groupName))
        {
            groups.Add(groupName);
        }
    }

    private static ObjVertex[] ParsePolygon(
        ReadOnlySpan<string> vertices,
        int positionCount,
        int texCoordCount,
        int normalCount)
    {
        var polygon = new ObjVertex[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            polygon[i] = ParseVertex(vertices[i], positionCount, texCoordCount, normalCount);
        }

        return polygon;
    }

    private static void AddTriangulatedFace(
        string groupName,
        IReadOnlyList<ObjVertex> vertices,
        List<ObjFace> faces)
    {
        ObjVertex first = vertices[0];
        for (int i = 1; i < vertices.Count - 1; i++)
        {
            faces.Add(new ObjFace(
                groupName,
                first,
                vertices[i],
                vertices[i + 1]));
        }
    }

    private static ObjVertex ParseVertex(string token, int positionCount, int texCoordCount, int normalCount)
    {
        string[] parts = token.Split('/');
        int position = ParseIndex(parts, 0, positionCount);
        int texCoord = ParseIndex(parts, 1, texCoordCount);
        int normal = ParseIndex(parts, 2, normalCount);
        return new ObjVertex(position, texCoord, normal);
    }

    private static int ParseIndex(string[] parts, int index, int count)
    {
        if (index >= parts.Length || parts[index].Length == 0)
        {
            return -1;
        }

        int raw = int.Parse(parts[index], CultureInfo.InvariantCulture);
        return raw < 0 ? count + raw : raw - 1;
    }

    private static float ParseFloat(string value)
    {
        return float.Parse(value, CultureInfo.InvariantCulture);
    }
}
