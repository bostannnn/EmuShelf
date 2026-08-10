using EmuShelf.Rendering.Models;
using Silk.NET.OpenGL;

namespace EmuShelf.Rendering.Gl;

/// <summary>One primitive's vertex and index buffers, plus the VAO describing their layout.</summary>
public sealed class GlMesh : IDisposable
{
    private readonly GL _gl;
    private uint _vao;
    private uint _vbo;
    private uint _ebo;

    private GlMesh(GL gl, uint vao, uint vbo, uint ebo, uint indexCount, int materialIndex)
    {
        _gl = gl;
        _vao = vao;
        _vbo = vbo;
        _ebo = ebo;
        IndexCount = indexCount;
        MaterialIndex = materialIndex;
    }

    public uint IndexCount { get; }

    public int MaterialIndex { get; }

    public static unsafe GlMesh Upload(GL gl, MeshGeometry geometry)
    {
        var vao = gl.GenVertexArray();
        gl.BindVertexArray(vao);

        var vbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        fixed (float* vertices = geometry.Vertices)
        {
            gl.BufferData(
                BufferTargetARB.ArrayBuffer,
                (nuint)(geometry.Vertices.Length * sizeof(float)),
                vertices,
                BufferUsageARB.StaticDraw);
        }

        var ebo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, ebo);
        fixed (uint* indices = geometry.Indices)
        {
            gl.BufferData(
                BufferTargetARB.ElementArrayBuffer,
                (nuint)(geometry.Indices.Length * sizeof(uint)),
                indices,
                BufferUsageARB.StaticDraw);
        }

        var stride = (uint)(MeshGeometry.FloatsPerVertex * sizeof(float));
        gl.EnableVertexAttribArray(GlProgram.PositionAttribute);
        gl.VertexAttribPointer(GlProgram.PositionAttribute, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
        gl.EnableVertexAttribArray(GlProgram.NormalAttribute);
        gl.VertexAttribPointer(GlProgram.NormalAttribute, 3, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));
        gl.EnableVertexAttribArray(GlProgram.TexCoordAttribute);
        gl.VertexAttribPointer(GlProgram.TexCoordAttribute, 2, VertexAttribPointerType.Float, false, stride, (void*)(6 * sizeof(float)));

        gl.BindVertexArray(0);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, 0);

        return new GlMesh(gl, vao, vbo, ebo, (uint)geometry.Indices.Length, geometry.MaterialIndex);
    }

    public unsafe void Draw()
    {
        _gl.BindVertexArray(_vao);
        _gl.DrawElements(PrimitiveType.Triangles, IndexCount, DrawElementsType.UnsignedInt, (void*)0);
        _gl.BindVertexArray(0);
    }

    public void Dispose()
    {
        if (_vao == 0)
        {
            return;
        }

        _gl.DeleteVertexArray(_vao);
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteBuffer(_ebo);
        _vao = 0;
        _vbo = 0;
        _ebo = 0;
    }
}
