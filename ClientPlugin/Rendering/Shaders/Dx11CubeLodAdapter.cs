using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ClientPlugin.Rendering.Shaders;

// D3D sample_l is isotropic even with an anisotropic sampler. Vulkan hardware
// can apply anisotropy to the same explicit-LOD cube operation. On the owned
// stock contract, s2 is isotropic linear/clamp and s3 is material anisotropy.
// Cube addressing is face/seam based, so s2 supplies the required sample_l
// behavior without changing s3's implicit or explicit-gradient operations.
// This is the portable isotropic mapping, not proof of an identical vendor
// anisotropic filter kernel. Hardware DX11 differs from WARP on the measured
// RTX driver; exact hardware parity remains under investigation.
internal static class Dx11CubeLodAdapter
{
    internal static byte[] Apply(byte[] bytes)
    {
        if(bytes==null || bytes.Length<20 || bytes.Length%4!=0 || BitConverter.ToUInt32(bytes,0)!=0x07230203)throw new InvalidDataException("Invalid cube-LOD SPIR-V header.");
        var words=new uint[bytes.Length/4];Buffer.BlockCopy(bytes,0,words,0,bytes.Length);
        var instructions=new List<uint[]>();var types=new Dictionary<uint,uint[]>();var variables=new Dictionary<uint,uint[]>();var loads=new Dictionary<uint,uint[]>();var sampled=new Dictionary<uint,uint[]>();
        var bindings=new Dictionary<uint,uint>();var sets=new Dictionary<uint,uint>();
        for(int at=5;at<words.Length;){int n=checked((int)(words[at]>>16));uint op=words[at]&65535;if(n<1 || n>words.Length-at)throw new InvalidDataException("Malformed cube-LOD SPIR-V instruction.");
            var i=new uint[n];Array.Copy(words,at,i,0,n);instructions.Add(i);
            if(op>=19 && op<=39 && n>=2)types[i[1]]=i;
            else if(op==59 && n>=4)variables[i[2]]=i;
            else if(op==61 && n>=4)loads[i[2]]=i;
            else if(op==86 && n==5)sampled[i[2]]=i;
            else if(op==71 && n==4){if(i[2]==33)bindings[i[1]]=i[3];else if(i[2]==34)sets[i[1]]=i[3];}
            at+=n;
        }
        bool SamplerVariable(uint id,uint binding)=>variables.TryGetValue(id,out var v) && v[3]==0 && bindings.TryGetValue(id,out var b) && b==binding && sets.TryGetValue(id,out var set) && set==0 && types.TryGetValue(v[1],out var p) && (p[0]&65535)==32 && p.Length==4 && types.TryGetValue(p[3],out var t) && (t[0]&65535)==26;
        var targets=new Dictionary<uint[],uint[]>();uint sourceSampler=0;
        foreach(var i in instructions){
            // OpImageSampleExplicitLod's Lod operand, not its Grad operand.
            if((i[0]&65535)!=88 || i.Length<7 || (i[5]&2)==0 || !sampled.TryGetValue(i[3],out var s) || !loads.TryGetValue(s[4],out var load) || !SamplerVariable(load[3],259))continue;
            if(!types.TryGetValue(s[1],out var sampledType) || (sampledType[0]&65535)!=27 || !types.TryGetValue(sampledType[2],out var imageType) || (imageType[0]&65535)!=25 || imageType[3]!=3)continue;
            if(sourceSampler!=0 && sourceSampler!=load[3])throw new InvalidDataException("Ambiguous stock cube sampler binding.");sourceSampler=load[3];targets.Add(i,s);
        }
        if(targets.Count==0)return bytes;
        if(words[1]<0x00010400)throw new InvalidDataException("Cube-LOD adaptation requires SPIR-V 1.4 global interfaces.");
        var existing=variables.Keys.Where(id=>SamplerVariable(id,258)).ToArray();if(existing.Length>1)throw new InvalidDataException("Ambiguous isotropic cube sampler binding.");
        uint next=words[3];uint Id(){if(next==uint.MaxValue)throw new InvalidDataException("Cube-LOD ID bound overflow.");return next++;}
        bool inject=existing.Length==0;
        if(inject && bindings.Any(pair=>pair.Value==258 && sets.TryGetValue(pair.Key,out var set) && set==0))throw new InvalidDataException("Stock isotropic sampler binding is occupied by another resource.");
        uint linear=inject?Id():existing[0];
        // Preserve original combined image/sampler values: an implicit/Grad
        // instruction may share one with an explicit-LOD instruction.
        var result=new List<uint>(words.Length+targets.Count*9+16);for(int i=0;i<5;i++)result.Add(words[i]);
        bool decorated=!inject,declared=!inject;
        foreach(var instruction in instructions){var i=instruction;uint op=i[0]&65535;
            if(!decorated && op>=19 && op<=39){result.AddRange(new uint[]{(4u<<16)|71,linear,34,0,(4u<<16)|71,linear,33,258});decorated=true;}
            if(!declared && op==54){result.AddRange(new uint[]{(4u<<16)|59,variables[sourceSampler][1],linear,0});declared=true;}
            if(inject && op==15){i=(uint[])i.Clone();i[0]+=1u<<16;result.AddRange(i);result.Add(linear);continue;}
            if(targets.TryGetValue(instruction,out var combined)){
                uint loadId=Id(),combinedId=Id();uint samplerType=loads[combined[4]][1];
                result.AddRange(new uint[]{(4u<<16)|61,samplerType,loadId,linear,(5u<<16)|86,combined[1],combinedId,combined[3],loadId});
                i=(uint[])i.Clone();i[3]=combinedId;
            }
            result.AddRange(i);
        }
        if(!decorated || !declared)throw new InvalidDataException("Missing cube-LOD declaration section.");
        result[3]=next;byte[] output=new byte[checked(result.Count*4)];Buffer.BlockCopy(result.ToArray(),0,output,0,output.Length);return output;
    }
}
