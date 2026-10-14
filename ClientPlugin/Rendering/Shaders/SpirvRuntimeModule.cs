using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ClientPlugin.Rendering.Shaders;

// DXC -fspv-reflect emits HLSL semantic/user-type/counter annotations. Their
// Google extension declarations need optional Vulkan device extensions even
// though they have no execution semantics. Keep the authenticated catalog for
// reflection; remove only these annotations from the driver-facing copy.
public static class SpirvRuntimeModule
{
    public static byte[] FromArtifact(VulkanShaderArtifact artifact)
    {
        byte[] bytes = File.ReadAllBytes(artifact.SpirvPath);
        using (var sha = SHA256.Create())
            if (BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant() != artifact.Layout.SpirvSha256)
                throw new InvalidDataException("Shader artifact changed before native pipeline creation.");
        return Prepare(bytes);
    }
    // D3D register namespaces are stage-local. Particle VS t0/t1 alias PS depth/atlas.
    // Move every VS descriptor into a disjoint range on an authenticated executable copy.
    public static byte[] RebaseVertexBindings(byte[] bytes)
    {
        if(bytes==null||bytes.Length<20||bytes.Length%4!=0||BitConverter.ToUInt32(bytes,0)!=0x07230203)throw new InvalidDataException("Invalid relocated SPIR-V header.");
        var output=(byte[])bytes.Clone();for(int i=20;i<output.Length;){uint instruction=BitConverter.ToUInt32(output,i);int length=checked((int)(instruction>>16)*4);if(length<4||length>output.Length-i)throw new InvalidDataException("Malformed relocated SPIR-V.");if((instruction&65535)==71&&length==16&&BitConverter.ToUInt32(output,i+8)==33){uint binding=BitConverter.ToUInt32(output,i+12);if(binding>=1024)throw new InvalidDataException("SPIR-V binding exceeds stage range.");Buffer.BlockCopy(BitConverter.GetBytes(binding+1024),0,output,i+12,4);}i+=length;}return output;
    }
    public static byte[] Prepare(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 20 || bytes.Length % 4 != 0 || BitConverter.ToUInt32(bytes, 0) != 0x07230203)
            throw new InvalidDataException("Invalid runtime SPIR-V header.");
        var words = new uint[bytes.Length / 4]; Buffer.BlockCopy(bytes, 0, words, 0, bytes.Length);
        var result = new List<uint>(words.Length); for (int i = 0; i < 5; ++i) result.Add(words[i]);
        for (int offset = 5; offset < words.Length;)
        {
            int length = (int)(words[offset] >> 16); uint op = words[offset] & 65535;
            if (length <= 0 || length > words.Length - offset) throw new InvalidDataException("Invalid runtime SPIR-V instruction length.");
            bool remove = false;
            if (op == 10) // OpExtension
            {
                int start = (offset + 1) * 4, limit = (offset + length) * 4, end = start;
                while (end < limit && bytes[end] != 0) ++end;
                if (end == limit) throw new InvalidDataException("Unterminated SPIR-V extension name.");
                string name = Encoding.UTF8.GetString(bytes, start, end - start);
                remove = name == "SPV_GOOGLE_hlsl_functionality1" || name == "SPV_GOOGLE_user_type";
            }
            else if (op == 5632 && length >= 4) remove = words[offset + 2] == 5635 || words[offset + 2] == 5636; // OpDecorateString: HlslSemantic / UserType
            else if (op == 5633 && length >= 5) remove = words[offset + 3] == 5635 || words[offset + 3] == 5636; // OpMemberDecorateString
            else if (op == 332 && length >= 4) remove = words[offset + 2] == 5634; // OpDecorateId: CounterBuffer link (not the actual UAV/counter binding)
            if (!remove) for (int i = 0; i < length; ++i) result.Add(words[offset + i]);
            offset += length;
        }
        byte[] output = new byte[checked(result.Count * 4)]; Buffer.BlockCopy(result.ToArray(), 0, output, 0, output.Length); return output;
    }
}
