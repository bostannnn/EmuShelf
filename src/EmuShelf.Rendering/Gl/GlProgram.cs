using System.Numerics;
using Silk.NET.OpenGL;

namespace EmuShelf.Rendering.Gl;

/// <summary>A linked shader program with its uniform locations cached.</summary>
/// <remarks>
/// Attribute locations are assigned with glBindAttribLocation before linking rather than declared
/// with <c>layout(location=)</c> in the shader: explicit attribute locations need GLSL 330, and the
/// core profile Avalonia asks for on macOS can be 150.
/// </remarks>
public sealed class GlProgram : IDisposable
{
    /// <summary>Attribute slots, matched by <see cref="GlMesh"/>'s vertex layout.</summary>
    public const uint PositionAttribute = 0;

    /// <inheritdoc cref="PositionAttribute"/>
    public const uint NormalAttribute = 1;

    /// <inheritdoc cref="PositionAttribute"/>
    public const uint TexCoordAttribute = 2;

    private readonly GL _gl;
    private readonly Dictionary<string, int> _uniforms = new(StringComparer.Ordinal);
    private uint _handle;

    private GlProgram(GL gl, uint handle)
    {
        _gl = gl;
        _handle = handle;
    }

    public static GlProgram Create(GL gl, string vertexSource, string fragmentSource, string label)
    {
        var vertex = CompileShader(gl, ShaderType.VertexShader, vertexSource, label);
        var fragment = CompileShader(gl, ShaderType.FragmentShader, fragmentSource, label);

        var handle = gl.CreateProgram();
        gl.AttachShader(handle, vertex);
        gl.AttachShader(handle, fragment);

        gl.BindAttribLocation(handle, PositionAttribute, "aPosition");
        gl.BindAttribLocation(handle, NormalAttribute, "aNormal");
        gl.BindAttribLocation(handle, TexCoordAttribute, "aTexCoord");

        gl.LinkProgram(handle);

        gl.GetProgram(handle, ProgramPropertyARB.LinkStatus, out var status);
        if (status == 0)
        {
            var log = gl.GetProgramInfoLog(handle);
            gl.DeleteProgram(handle);
            gl.DeleteShader(vertex);
            gl.DeleteShader(fragment);
            throw new InvalidOperationException($"Linking shader program '{label}' failed: {log}");
        }

        // Detach before delete so the driver can drop the compiled objects immediately.
        gl.DetachShader(handle, vertex);
        gl.DetachShader(handle, fragment);
        gl.DeleteShader(vertex);
        gl.DeleteShader(fragment);

        return new GlProgram(gl, handle);
    }

    private static uint CompileShader(GL gl, ShaderType type, string source, string label)
    {
        var handle = gl.CreateShader(type);
        gl.ShaderSource(handle, source);
        gl.CompileShader(handle);

        gl.GetShader(handle, ShaderParameterName.CompileStatus, out var status);
        if (status != 0)
        {
            return handle;
        }

        var log = gl.GetShaderInfoLog(handle);
        gl.DeleteShader(handle);
        throw new InvalidOperationException(
            $"Compiling the {type} of '{label}' failed: {log}\n--- source ---\n{Number(source)}");
    }

    // Driver logs report errors by line, which is useless against a source that had a header
    // injected, so number the lines when reporting a failure.
    private static string Number(string source)
    {
        var lines = source.Split('\n');
        return string.Join('\n', lines.Select((line, i) => $"{i + 1,4}: {line.TrimEnd()}"));
    }

    public void Use() => _gl.UseProgram(_handle);

    public int Location(string name)
    {
        if (_uniforms.TryGetValue(name, out var cached))
        {
            return cached;
        }

        var location = _gl.GetUniformLocation(_handle, name);
        _uniforms[name] = location;
        return location;
    }

    public void Set(string name, int value)
    {
        var location = Location(name);
        if (location >= 0)
        {
            _gl.Uniform1(location, value);
        }
    }

    public void Set(string name, float value)
    {
        var location = Location(name);
        if (location >= 0)
        {
            _gl.Uniform1(location, value);
        }
    }

    public void Set(string name, Vector2 value)
    {
        var location = Location(name);
        if (location >= 0)
        {
            _gl.Uniform2(location, value.X, value.Y);
        }
    }

    public void Set(string name, Vector3 value)
    {
        var location = Location(name);
        if (location >= 0)
        {
            _gl.Uniform3(location, value.X, value.Y, value.Z);
        }
    }

    public void Set(string name, Vector4 value)
    {
        var location = Location(name);
        if (location >= 0)
        {
            _gl.Uniform4(location, value.X, value.Y, value.Z, value.W);
        }
    }

    public void Set(string name, Matrix4x4 value)
    {
        var location = Location(name);
        if (location < 0)
        {
            return;
        }

        // System.Numerics matrices are laid out for row-vector multiplication. GLSL uses column
        // vectors, so letting GL read this row-major memory as column-major (transpose=false)
        // supplies the mathematical transpose the shader needs. OpenGL ES also requires false.
        Span<float> values =
        [
            value.M11, value.M12, value.M13, value.M14,
            value.M21, value.M22, value.M23, value.M24,
            value.M31, value.M32, value.M33, value.M34,
            value.M41, value.M42, value.M43, value.M44,
        ];
        _gl.UniformMatrix4(location, 1, false, values);
    }

    /// <summary>Uploads the upper-left 3x3 of <paramref name="value"/> as a mat3.</summary>
    public void SetMatrix3(string name, Matrix4x4 value)
    {
        var location = Location(name);
        if (location < 0)
        {
            return;
        }

        Span<float> values =
        [
            value.M11, value.M12, value.M13,
            value.M21, value.M22, value.M23,
            value.M31, value.M32, value.M33,
        ];
        _gl.UniformMatrix3(location, 1, false, values);
    }

    public void Dispose()
    {
        if (_handle == 0)
        {
            return;
        }

        _gl.DeleteProgram(_handle);
        _handle = 0;
    }
}
