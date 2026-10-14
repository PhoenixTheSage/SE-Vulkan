using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ClientPlugin.Rendering.Shaders;

// This reader exports the layouts needed by managed content translation. Native
// pipeline creation independently checks SPIRV-Reflect's descriptor information.
public static class SpirvMetadataReader
{
    public static void Reflect(byte[] bytes, ShaderMetadata metadata)
    {
        if (metadata == null) throw new ArgumentNullException(nameof(metadata));
        new Module(bytes).Populate(metadata);
    }

    private sealed class TypeNode
    {
        public int Op;
        public uint[] Args;
    }
    private sealed class Variable
    {
        public uint Type;
        public uint Id;
        public uint Storage;
    }
    private sealed class Module
    {
        private readonly Dictionary<uint, TypeNode> types = new Dictionary<uint, TypeNode>();
        private readonly Dictionary<uint, uint> constants = new Dictionary<uint, uint>();
        private readonly Dictionary<uint, string> names = new Dictionary<uint, string>();
        private readonly Dictionary<ulong, string> memberNames = new Dictionary<ulong, string>();
        private readonly Dictionary<uint, Dictionary<uint, uint>> decorations = new Dictionary<uint, Dictionary<uint, uint>>();
        private readonly Dictionary<ulong, Dictionary<uint, uint>> memberDecorations = new Dictionary<ulong, Dictionary<uint, uint>>();
        private readonly List<Variable> variables = new List<Variable>();
        private readonly List<string> entryPoints = new List<string>();
        private readonly List<uint> capabilities = new List<uint>();
        private readonly List<string> extensions = new List<string>();
        private readonly Dictionary<string, uint[]> entries = new Dictionary<string, uint[]>(StringComparer.Ordinal);
        private readonly Dictionary<uint, uint[]> localSizes = new Dictionary<uint, uint[]>();
        private readonly uint bound;

        public Module(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 20 || bytes.Length % 4 != 0) throw new InvalidDataException("Truncated SPIR-V module.");
            var words = new uint[bytes.Length / 4];
            Buffer.BlockCopy(bytes, 0, words, 0, bytes.Length);
            if (!BitConverter.IsLittleEndian) for (int i = 0; i < words.Length; i++) words[i] = Swap(words[i]);
            if (words[0] != 0x07230203 || words[4] != 0) throw new InvalidDataException("Invalid SPIR-V header.");
            bound = words[3];
            if (bound == 0 || bound > 16 * 1024 * 1024) throw new InvalidDataException("Invalid SPIR-V ID bound.");
            for (int offset = 5; offset < words.Length;)
            {
                int count = (int)(words[offset] >> 16), op = (int)(words[offset] & 0xffff);
                if (count == 0 || count > words.Length - offset) throw new InvalidDataException("Invalid SPIR-V instruction length.");
                var a = new uint[count - 1];
                Array.Copy(words, offset + 1, a, 0, a.Length);
                if (op == 5 && a.Length >= 2) names[Id(a[0])] = ReadString(a, 1);
                else if (op == 6 && a.Length >= 3) memberNames[Key(Id(a[0]), a[1])] = ReadString(a, 2);
                else if (op == 15 && a.Length >= 3)
                {
                    string entry = ReadString(a, 2); entryPoints.Add(entry); entries.Add(entry, new[] { a[0], a[1] });
                }
                else if (op == 17 && a.Length == 1) capabilities.Add(a[0]);
                else if (op == 10 && a.Length >= 1) extensions.Add(ReadString(a, 0));
                else if (op == 16 && a.Length == 5 && a[1] == 17) localSizes[a[0]] = a.Skip(2).ToArray();
                else if ((op >= 20 && op <= 33 || op == 5341) && a.Length >= 1)
                    types.Add(Id(a[0]), new TypeNode { Op = op, Args = a.Skip(1).ToArray() });
                else if (op == 43 && a.Length >= 3)
                {
                    if (a.Length > 3 && a[3] != 0) throw new InvalidDataException("Array lengths above 32 bits are unsupported.");
                    constants[Id(a[1])] = a[2];
                }
                else if (op == 59 && a.Length >= 3) variables.Add(new Variable { Type = Id(a[0]), Id = Id(a[1]), Storage = a[2] });
                else if ((op == 71 || op == 332) && a.Length >= 2) Decorate(decorations, Id(a[0]), a[1], a.Length > 2 ? a[2] : 1);
                else if (op == 72 && a.Length >= 3) Decorate(memberDecorations, Key(Id(a[0]), a[1]), a[2], a.Length > 3 ? a[3] : 1);
                offset += count;
            }
        }

        public void Populate(ShaderMetadata metadata)
        {
            if (!entryPoints.Contains(metadata.EntryPoint)) throw new InvalidDataException("Compiled SPIR-V has no requested entry point: " + metadata.EntryPoint);
            metadata.Descriptors.Clear(); metadata.Interface.Clear(); metadata.PushConstants.Clear();
            metadata.Capabilities = capabilities.Distinct().OrderBy(c => c).ToList();
            metadata.Extensions = extensions.Distinct().OrderBy(e => e, StringComparer.Ordinal).ToList();
            metadata.ExecutionModel = entries[metadata.EntryPoint][0];
            metadata.WorkgroupSizeX = metadata.WorkgroupSizeY = metadata.WorkgroupSizeZ = 0;
            uint[] localSize;
            if (localSizes.TryGetValue(entries[metadata.EntryPoint][1], out localSize))
            { metadata.WorkgroupSizeX = localSize[0]; metadata.WorkgroupSizeY = localSize[1]; metadata.WorkgroupSizeZ = localSize[2]; }
            foreach (Variable variable in variables)
            {
                TypeNode pointer = Type(variable.Type);
                if (pointer.Op != 32 || pointer.Args.Length != 2) throw new InvalidDataException("Variable has no pointer type.");
                uint typeId = pointer.Args[1];
                if (variable.Storage == 1 || variable.Storage == 3)
                {
                    if (Has(variable.Id, 30)) metadata.Interface.Add(new ShaderInterfaceVariable
                    {
                        Name = Name(variable.Id), Location = Decoration(variable.Id, 30),
                        Direction = variable.Storage == 1 ? "Input" : "Output", Type = TypeName(typeId),
                        Flat = Has(variable.Id, 14), NoPerspective = Has(variable.Id, 13), Centroid = Has(variable.Id, 16), Sample = Has(variable.Id, 17)
                    });
                    continue;
                }
                if (variable.Storage == 9)
                {
                    metadata.PushConstants.Add(new ShaderPushConstant { Name = Name(variable.Id), Size = Size(typeId, null, 0), Members = Members(typeId, 0) });
                    continue;
                }
                if (variable.Storage != 0 && variable.Storage != 2 && variable.Storage != 12) continue;
                if (!Has(variable.Id, 33) || !Has(variable.Id, 34)) throw new InvalidDataException("A shader resource has no binding or descriptor set.");
                var descriptor = new ShaderDescriptor { Name = Name(variable.Id), Set = Decoration(variable.Id, 34), Binding = Decoration(variable.Id, 33) };
                TypeNode type = Type(typeId);
                while (type.Op == 28 || type.Op == 29)
                {
                    if (type.Op == 29) { descriptor.RuntimeArray = true; descriptor.DescriptorCount = 0; }
                    else if (!descriptor.RuntimeArray) descriptor.DescriptorCount = checked(descriptor.DescriptorCount * Constant(type.Args[1]));
                    typeId = type.Args[0]; type = Type(typeId);
                }
                if (variable.Storage == 2 || variable.Storage == 12)
                {
                    if (type.Op != 30) throw new InvalidDataException("Buffer resource is not a structure.");
                    descriptor.DescriptorType = variable.Storage == 12 || Has(typeId, 3) ? "StorageBuffer" : "UniformBuffer";
                    descriptor.BlockSize = Size(typeId, null, 0);
                    descriptor.Members = Members(typeId, 0);
                }
                else if (type.Op == 26) descriptor.DescriptorType = "Sampler";
                else if (type.Op == 27) descriptor.DescriptorType = "CombinedImageSampler";
                else if (type.Op == 25 && type.Args.Length >= 7)
                {
                    uint dimension = type.Args[1], sampled = type.Args[5];
                    descriptor.DescriptorType = dimension == 6 ? "InputAttachment" : dimension == 5
                        ? (sampled == 2 ? "StorageTexelBuffer" : "UniformTexelBuffer") : sampled == 2 ? "StorageImage" : "SampledImage";
                }
                else if (type.Op == 5341) descriptor.DescriptorType = "AccelerationStructure";
                else throw new InvalidDataException("Unsupported SPIR-V descriptor type opcode " + type.Op + ".");
                MapRegister(descriptor);
                if (Has(variable.Id, 5634))
                {
                    uint counter = Decoration(variable.Id, 5634);
                    descriptor.HasCounter = true; descriptor.CounterBinding = Decoration(counter, 33);
                }
                foreach (var owner in decorations.Where(d => d.Value.ContainsKey(5634) && d.Value[5634] == variable.Id)) descriptor.CounterFor = Name(owner.Key);
                // Counter-only calls can eliminate the owner's data descriptor.
                // DXC keeps its reflected logical name on the live counter.
                if (descriptor.RegisterClass == 'c' && descriptor.CounterFor == null && descriptor.Name.StartsWith("counter.var.", StringComparison.Ordinal)) descriptor.CounterFor = descriptor.Name.Substring("counter.var.".Length);
                if (descriptor.RegisterClass == 'c' && descriptor.CounterFor == null) throw new InvalidDataException("A reserved counter descriptor has no source UAV owner.");
                if (metadata.Descriptors.Any(d => d.Set == descriptor.Set && d.Binding == descriptor.Binding))
                    throw new InvalidDataException("Overlapping descriptor binding: set " + descriptor.Set + ", binding " + descriptor.Binding + " (" + metadata.Descriptors.First(d => d.Set == descriptor.Set && d.Binding == descriptor.Binding).Name + " / " + descriptor.Name + ").");
                metadata.Descriptors.Add(descriptor);
            }
            metadata.Descriptors = metadata.Descriptors.OrderBy(d => d.Set).ThenBy(d => d.Binding).ToList();
            metadata.Interface = metadata.Interface.OrderBy(d => d.Direction).ThenBy(d => d.Location).ToList();
        }

        private static void MapRegister(ShaderDescriptor descriptor)
        {
            uint band = descriptor.Binding / VulkanShaderCompiler.RegisterBandSize;
            if (band > 4) throw new InvalidDataException("Descriptor binding lies outside the reserved b/t/s/u/counter ranges.");
            descriptor.RegisterClass = "btsuc"[(int)band];
            descriptor.RegisterIndex = descriptor.Binding % VulkanShaderCompiler.RegisterBandSize;
            string kind = descriptor.DescriptorType;
            bool valid = kind == "UniformBuffer" ? band == 0 : kind == "Sampler" ? band == 2
                : kind == "StorageBuffer" ? band == 1 || band == 3 || band == 4
                : kind == "StorageImage" || kind == "StorageTexelBuffer" ? band == 3 : band == 1;
            if (!valid) throw new InvalidDataException("Descriptor kind does not match its HLSL register range: " + descriptor.Name + ".");
        }

        private List<ShaderBlockMember> Members(uint id, int depth)
        {
            GuardDepth(depth);
            TypeNode type = Type(id);
            while (type.Op == 28 || type.Op == 29) { id = type.Args[0]; type = Type(id); }
            var result = new List<ShaderBlockMember>();
            if (type.Op != 30) return result;
            for (uint i = 0; i < type.Args.Length; i++)
            {
                Dictionary<uint, uint> layout;
                memberDecorations.TryGetValue(Key(id, i), out layout);
                if (layout == null || !layout.ContainsKey(35)) throw new InvalidDataException("Buffer member has no explicit offset.");
                uint memberType = type.Args[i];
                TypeNode member = Type(memberType);
                string memberName;
                memberNames.TryGetValue(Key(id, i), out memberName);
                result.Add(new ShaderBlockMember
                {
                    Name = memberName ?? "member" + i, Type = TypeName(memberType), Offset = layout[35], Size = Size(memberType, layout, depth + 1),
                    ArrayStride = Has(memberType, 6) ? Decoration(memberType, 6) : 0,
                    MatrixStride = Value(layout, 7), RowMajor = layout.ContainsKey(4), RuntimeArray = member.Op == 29,
                    Members = Members(memberType, depth + 1)
                });
            }
            return result;
        }

        private uint Size(uint id, Dictionary<uint, uint> layout, int depth)
        {
            GuardDepth(depth);
            TypeNode t = Type(id);
            switch (t.Op)
            {
                case 20: return 4;
                case 21: case 22: return t.Args[0] / 8;
                case 23: return checked(Size(t.Args[0], null, depth + 1) * t.Args[1]);
                case 24:
                    uint stride = Value(layout, 7);
                    if (stride == 0) throw new InvalidDataException("Buffer matrix has no MatrixStride.");
                    uint vectors = layout != null && layout.ContainsKey(4) ? Type(t.Args[0]).Args[1] : t.Args[1];
                    return checked(stride * vectors);
                case 28:
                    if (!Has(id, 6)) throw new InvalidDataException("Buffer array has no ArrayStride.");
                    return checked(Decoration(id, 6) * Constant(t.Args[1]));
                case 29: return 0;
                case 30:
                    uint end = 0;
                    foreach (ShaderBlockMember member in Members(id, depth + 1)) end = Math.Max(end, checked(member.Offset + member.Size));
                    return end;
                default: throw new InvalidDataException("Unsupported buffer member opcode " + t.Op + ".");
            }
        }

        private string TypeName(uint id)
        {
            TypeNode t = Type(id);
            switch (t.Op)
            {
                case 20: return "bool";
                case 21: return (t.Args[1] == 0 ? "uint" : "int") + t.Args[0];
                case 22: return "float" + t.Args[0];
                case 23: return TypeName(t.Args[0]) + "x" + t.Args[1];
                case 24: return TypeName(t.Args[0]) + "x" + t.Args[1];
                case 28: return TypeName(t.Args[0]) + "[" + Constant(t.Args[1]) + "]";
                case 29: return TypeName(t.Args[0]) + "[]";
                case 30: return Name(id);
                default: return "OpType" + t.Op;
            }
        }
        private TypeNode Type(uint id) { TypeNode t; if (!types.TryGetValue(id, out t)) throw new InvalidDataException("Unknown SPIR-V type " + id + "."); return t; }
        private uint Constant(uint id) { uint value; if (!constants.TryGetValue(id, out value)) throw new InvalidDataException("Descriptor/array size needs specialization or an unsupported constant."); return value; }
        private string Name(uint id) { string name; return names.TryGetValue(id, out name) ? name : "id" + id; }
        private bool Has(uint id, uint decoration) { Dictionary<uint, uint> map; return decorations.TryGetValue(id, out map) && map.ContainsKey(decoration); }
        private uint Decoration(uint id, uint decoration) { return decorations[id][decoration]; }
        private uint Id(uint id) { if (id == 0 || id >= bound) throw new InvalidDataException("SPIR-V ID is outside its bound."); return id; }
        private static uint Value(Dictionary<uint, uint> map, uint key) { uint value; return map != null && map.TryGetValue(key, out value) ? value : 0; }
        private static void GuardDepth(int depth) { if (depth > 48) throw new InvalidDataException("Recursive/deep SPIR-V layout."); }
        private static ulong Key(uint id, uint member) { return (ulong)id << 32 | member; }
        private static uint Swap(uint x) { return x >> 24 | (x >> 8 & 0xff00) | (x << 8 & 0xff0000) | x << 24; }
        private static void Decorate<T>(Dictionary<T, Dictionary<uint, uint>> maps, T key, uint kind, uint value)
        {
            Dictionary<uint, uint> map;
            if (!maps.TryGetValue(key, out map)) maps[key] = map = new Dictionary<uint, uint>();
            map[kind] = value;
        }
        private static string ReadString(uint[] words, int start)
        {
            var bytes = new List<byte>();
            for (int i = start; i < words.Length; i++) for (int shift = 0; shift < 32; shift += 8)
            {
                byte b = (byte)(words[i] >> shift);
                if (b == 0) return Encoding.UTF8.GetString(bytes.ToArray());
                bytes.Add(b);
            }
            throw new InvalidDataException("Unterminated SPIR-V string.");
        }
    }
}
