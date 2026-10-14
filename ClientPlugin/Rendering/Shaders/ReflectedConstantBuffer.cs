using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ClientPlugin.Rendering.Shaders;

// Writes copied bytes by declared SPIR-V member paths. Offsets are relative at
// every nesting level; matrices are supplied in the compiler's DX row layout.
public sealed class ReflectedConstantBuffer
{
    private readonly struct Member { public readonly int Offset, Size; public readonly string Type; public Member(int offset, int size, string type) { Offset = offset; Size = size; Type = type; } }
    private static readonly Dictionary<(ShaderMetadata Metadata, string Name), ShaderDescriptor> Descriptors = new Dictionary<(ShaderMetadata, string), ShaderDescriptor>();
    private static readonly Dictionary<(ShaderDescriptor Descriptor, string Path), Member> Members = new Dictionary<(ShaderDescriptor, string), Member>();
    private readonly ShaderDescriptor descriptor;
    public byte[] Bytes { get; }
    static readonly Dictionary<int, Stack<byte[]>> Pool = new Dictionary<int, Stack<byte[]>>();
    public ReflectedConstantBuffer(ShaderMetadata metadata, string name) : this(metadata, name, null) { }
    public ReflectedConstantBuffer(ShaderMetadata metadata, string name, byte[] rented)
    {
        descriptor = Descriptor(metadata, name);
        int size = checked((int)descriptor.BlockSize);
        if (rented == null) Bytes = new byte[size];
        else if (rented.Length != size) throw new InvalidDataException("Rented constant block has the wrong size.");
        else Bytes = rented;
    }
    static ShaderDescriptor Descriptor(ShaderMetadata metadata, string name)
    {
        var key = (metadata, name);
        if (!Descriptors.TryGetValue(key, out ShaderDescriptor descriptor))
        {
            descriptor = metadata.Descriptors.Single(d => d.Name == name && d.DescriptorType == "UniformBuffer");
            Descriptors.Add(key, descriptor);
        }
        return descriptor;
    }
    public static byte[] Rent(ShaderMetadata metadata, string name)
    {
        int size = checked((int)Descriptor(metadata, name).BlockSize);
        if (Pool.TryGetValue(size, out Stack<byte[]> stack) && stack.Count > 0)
        {
            byte[] rented = stack.Pop();
            Array.Clear(rented, 0, size);
            return rented;
        }
        return new byte[size];
    }
    public static void Recycle(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0) return;
        if (!Pool.TryGetValue(bytes.Length, out Stack<byte[]> stack)) Pool.Add(bytes.Length, stack = new Stack<byte[]>());
        if (stack.Count < 16384) stack.Push(bytes);
    }
    public int OffsetOf(string path) => Locate(path).Offset;
    // Resolve and validate immutable shader fields once for specialized writers.
    internal int FloatOffsetOf(string path, int count)
    {
        Member member = Locate(path);
        if (count * 4 != member.Size || !member.Type.StartsWith("float32", StringComparison.Ordinal)
            || member.Offset < 0 || member.Offset > Bytes.Length - member.Size)
            throw new InvalidDataException("Reflected constant type/size mismatch: " + path);
        return member.Offset;
    }
    public unsafe void Set(string path, float a) => Write(path, 1, a, 0, 0, 0);
    public unsafe void Set(string path, float a, float b) => Write(path, 2, a, b, 0, 0);
    public unsafe void Set(string path, float a, float b, float c) => Write(path, 3, a, b, c, 0);
    public unsafe void Set(string path, float a, float b, float c, float d) => Write(path, 4, a, b, c, d);
    public unsafe void Set(string path, float[] values)
    {
        Member member = Locate(path);
        if (values == null || values.Length * 4 != member.Size || !member.Type.StartsWith("float32", StringComparison.Ordinal))
            throw new InvalidDataException("Reflected constant type/size mismatch: " + path);
        fixed (byte* destination = Bytes)
        fixed (float* source = values)
            Buffer.MemoryCopy(source, destination + member.Offset, member.Size, member.Size);
    }
    private unsafe void Write(string path, int count, float a, float b, float c, float d)
    {
        Member member = Locate(path);
        if (count * 4 != member.Size || !member.Type.StartsWith("float32", StringComparison.Ordinal))
            throw new InvalidDataException("Reflected constant type/size mismatch: " + path);
        float* source = stackalloc float[4] { a, b, c, d };
        fixed (byte* destination = Bytes)
            Buffer.MemoryCopy(source, destination + member.Offset, count * 4, count * 4);
    }
    public void SetUInt(string path, uint value) => WriteInteger(path, BitConverter.GetBytes(value), true);
    public void SetInt(string path,int value) => WriteInteger(path,BitConverter.GetBytes(value),false);
    private void WriteInteger(string path, byte[] bytes, bool unsigned)
    {
        Member member = Locate(path);
        if (bytes.Length != member.Size || (unsigned ? member.Type != "uint32" : member.Type != "int32"))
            throw new InvalidDataException("Reflected constant type/size mismatch: " + path);
        Buffer.BlockCopy(bytes, 0, Bytes, member.Offset, bytes.Length);
    }
    private Member Locate(string path)
    {
        var key = (descriptor, path);
        if (Members.TryGetValue(key, out Member cached)) return cached;
        int offset = 0; ShaderBlockMember member = null; List<ShaderBlockMember> members = descriptor.Members;
        foreach (string part in path.Split('.'))
        {
            member = members.SingleOrDefault(m => m.Name == part) ?? throw new InvalidDataException("Unknown reflected constant: " + path);
            offset = checked(offset + (int)member.Offset); members = member.Members;
        }
        if (member == null || member.Members.Count != 0 || member.RuntimeArray)
            throw new InvalidDataException("Reflected constant type/size mismatch: " + path);
        var located = new Member(offset, checked((int)member.Size), member.Type);
        Members.Add(key, located);
        return located;
    }
}
