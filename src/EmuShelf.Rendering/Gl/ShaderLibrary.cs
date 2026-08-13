using System.Reflection;
using System.Text;

namespace EmuShelf.Rendering.Gl;

/// <summary>Which GLSL dialect the host's context speaks.</summary>
/// <remarks>
/// EmuShelf ships on three platforms and Avalonia hands us a different context on each: ANGLE
/// (OpenGL ES 3.0) on Windows, a desktop core profile on macOS, and either one on Linux. Rather
/// than keep two copies of every shader, the sources are written to the intersection of GLSL ES
/// 3.00 and desktop GLSL 1.50 and only the version header differs.
/// </remarks>
public enum GlslDialect
{
    /// <summary>Desktop OpenGL core profile.</summary>
    Desktop,

    /// <summary>OpenGL ES 3.0, as served by ANGLE.</summary>
    Es300,
}

/// <summary>Loads the embedded shader sources and stamps the right header on them.</summary>
public static class ShaderLibrary
{
    private const string ResourcePrefix = "EmuShelf.Rendering.Shaders.";

    private static readonly Dictionary<string, string> Cache = new(StringComparer.Ordinal);

    /// <summary>
    /// Reads one shader source and prefixes the version (plus, on ES, the precision declarations
    /// that dialect requires and desktop GLSL merely tolerates).
    /// </summary>
    /// <param name="name">File name without extension, e.g. <c>pbr.frag</c>.</param>
    /// <param name="dialect">Which header to stamp.</param>
    /// <param name="majorVersion">Context major version, used to pick 150 vs 330 on desktop.</param>
    /// <param name="minorVersion">Context minor version.</param>
    /// <param name="includes">Sources pasted in ahead of this one, for the shared cubemap helpers.</param>
    public static string Load(
        string name,
        GlslDialect dialect,
        int majorVersion = 3,
        int minorVersion = 3,
        params string[] includes)
    {
        var builder = new StringBuilder();

        if (dialect == GlslDialect.Es300)
        {
            builder.AppendLine("#version 300 es");
            // ES 3.00 leaves float and sampler precision undefined in fragment shaders. Everything
            // here reflects an environment map, so mediump banding would be visible as stepping in
            // the softbox falloff.
            builder.AppendLine("precision highp float;");
            builder.AppendLine("precision highp int;");
            builder.AppendLine("precision highp sampler2D;");
            builder.AppendLine("precision highp samplerCube;");
        }
        else
        {
            var supports330 = majorVersion > 3 || (majorVersion == 3 && minorVersion >= 3);
            builder.AppendLine(supports330 ? "#version 330 core" : "#version 150");
        }

        foreach (var include in includes)
        {
            builder.AppendLine(ReadSource(include));
        }

        builder.AppendLine(ReadSource(name));
        return builder.ToString();
    }

    private static string ReadSource(string name)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(name, out var cached))
            {
                return cached;
            }

            var resource = ResourcePrefix + name + ".glsl";
            using var stream = typeof(ShaderLibrary).Assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException(
                    $"Shader resource '{resource}' is missing from EmuShelf.Rendering.");

            using var reader = new StreamReader(stream, Encoding.UTF8);
            var source = reader.ReadToEnd();
            Cache[name] = source;
            return source;
        }
    }
}
