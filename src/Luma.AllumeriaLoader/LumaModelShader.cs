using System.Reflection;
using System.Text;
using Allumeria;
using Allumeria.Rendering;
using OpenTK.Mathematics;

namespace Luma.AllumeriaLoader;

internal static class LumaModelShader
{
    private const string EntityVertexResourceSuffix = "Shaders.entity.shader.vert";
    private const string StagedEntityVertexRelativePath = "mods/luma/shaders/entity.vert";
    private const string NativeEntityVertexRelativePath = "res/shaders/entity/shader.vert";
    private const string NativeEntityFragmentRelativePath = "res/shaders/entity/shader.frag";
    private const string BackupSuffix = ".luma-original";
    private const string LumaShaderMarker = "Luma entity shader model light samples";

    private static readonly FieldInfo? WorldRendererField =
        typeof(Game).GetField("worldRenderer", BindingFlags.Instance | BindingFlags.NonPublic);

    private static Shader? shader;
    private static bool createAttempted;

    public static void PrepareFiles()
    {
        try
        {
            RestoreNativeEntityShaderIfNeeded();
            StageEntityVertexShader();
        }
        catch (Exception ex)
        {
            Logger.Error("Luma model shader file preparation failed; native entity rendering remains untouched", ex);
        }
    }

    public static Shader? Get()
    {
        if (shader is not null)
        {
            return shader;
        }

        if (createAttempted)
        {
            return null;
        }

        createAttempted = true;
        try
        {
            PrepareFiles();
            string vertexPath = GamePath(StagedEntityVertexRelativePath);
            string fragmentPath = GamePath(NativeEntityFragmentRelativePath);
            if (!File.Exists(vertexPath) || !File.Exists(fragmentPath))
            {
                Logger.Info($"Luma model shader unavailable: {vertexPath}, {fragmentPath}");
                return null;
            }

            shader = new Shader(vertexPath, fragmentPath);
            Logger.Info($"Luma model shader created from {vertexPath}");
            return shader;
        }
        catch (Exception ex)
        {
            Logger.Error("Luma model shader creation failed; falling back to native entity shader", ex);
            return null;
        }
    }

    public static void ApplyFrameUniforms(Shader shader)
    {
        shader.SetUniformMat4("view", Game.camera.viewMatrix);
        shader.SetUniformMat4("projection", Game.camera.projectionMatrix);
        shader.SetUniformVec3("viewPos", Game.camera.position);
        shader.SetUniformFloat("dissolve", 0f);
        shader.SetUniformFloat("hitFade", 0f);
        shader.SetUniform1i("texture0", 0);
        shader.SetUniform1i("texture1", 1);
        shader.SetUniform1i("texture2", 2);

        WorldRenderer? renderer = WorldRendererField?.GetValue(Game.game) as WorldRenderer;
        if (renderer is null)
        {
            shader.SetUniformFloat("fogStart", 16f);
            shader.SetUniformFloat("fogEnd", MultiChunkRenderer.renderDistance * 32 - 16);
            shader.SetUniformVec4("fogMidColor", new Vector4(0f, 0.35f, 1f, 1f));
            shader.SetUniformVec4("fogColor", new Vector4(0.5f, 0.8f, 1f, 1f));
            shader.SetUniformVec4("ambientColor", Vector4.One);
            return;
        }

        shader.SetUniformFloat("fogStart", renderer.fogStart);
        shader.SetUniformFloat("fogEnd", renderer.fogEnd);
        shader.SetUniformVec4("fogMidColor", renderer.skyColor);
        shader.SetUniformVec4("fogColor", renderer.horizonColor);
        shader.SetUniformVec4("ambientColor", renderer.ambientColor);
    }

    private static void RestoreNativeEntityShaderIfNeeded()
    {
        string targetPath = GamePath(NativeEntityVertexRelativePath);
        string backupPath = targetPath + BackupSuffix;
        if (!File.Exists(targetPath) || !File.Exists(backupPath))
        {
            return;
        }

        string current = File.ReadAllText(targetPath, Encoding.UTF8);
        if (!current.Contains(LumaShaderMarker, StringComparison.Ordinal))
        {
            return;
        }

        File.Copy(backupPath, targetPath, overwrite: true);
        Logger.Info($"Restored native Allumeria entity shader from {backupPath}");
    }

    private static void StageEntityVertexShader()
    {
        string shaderSource = ReadEmbeddedShader(EntityVertexResourceSuffix);
        string targetPath = GamePath(StagedEntityVertexRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

        if (File.Exists(targetPath) &&
            File.ReadAllText(targetPath, Encoding.UTF8) == shaderSource)
        {
            Logger.Info("Luma model shader source already staged");
            return;
        }

        File.WriteAllText(targetPath, shaderSource, Encoding.UTF8);
        Logger.Info($"Staged Luma model shader source at {targetPath}");
    }

    private static string ReadEmbeddedShader(string resourceSuffix)
    {
        Assembly assembly = typeof(LumaModelShader).Assembly;
        string resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(resourceSuffix, StringComparison.Ordinal));

        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded shader resource not found: {resourceName}");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string GamePath(string relativePath)
    {
        return Path.Combine(
            Directory.GetCurrentDirectory(),
            relativePath.Replace('/', Path.DirectorySeparatorChar));
    }
}
