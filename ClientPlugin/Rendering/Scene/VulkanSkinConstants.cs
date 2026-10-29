using System;
using ClientPlugin.Rendering.Content;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Shaders;
using VRageMath;

namespace ClientPlugin.Rendering.Scene;

public sealed partial class VulkanScene
{
    // Skinned opaque parts: the Object constants differ per frame only in the
    // world matrix and the bone palette. Each skin part keeps a template built
    // by WorldConstants.Object (identity matrix, no bones) and the palette of
    // the SkinMatrices array it was built from; bone updates replace that
    // array, so a reference match means the palette still holds.
    // SE_VULKAN_SKIN_CONSTANTS_CACHE=1, or the SkinConstantsCacheEnabled lane toggle.
    internal sealed class SkinConstantEntry
    {
        public VulkanModel Model;public VulkanModelPart Part;public VulkanSkinPart Skin;public ShaderMetadata Layout;
        public Vector3 Tint,Hsv;public float Emissive,LodAlpha,Dither;public uint Lod;
        public byte[] Template;public WorldConstants.MatrixRows Rows;public int BoneOffset;
        public Matrix[] Source;public readonly float[] Palette=new float[60*16];
    }
    public bool SkinConstantsCacheEnabled {get;set;}=System.Environment.GetEnvironmentVariable("SE_VULKAN_SKIN_CONSTANTS_CACHE")=="1";
    // Byte-identical to WorldConstants.Object(layout,world,tint,hsv,emissive,actor.LodAlpha,palette,actor.Dithering,pooled:true,actor.RenderLod).
    unsafe byte[] SkinConstants(ActorData actor,int ordinal,VulkanModelPart part,VulkanSkinPart skin,ShaderMetadata layout,MatrixD world,Vector3 tint,Vector3 hsv,float emissive)
    {
        var entries=actor.SkinConstants;
        if(entries==null||entries.Length<=ordinal){Array.Resize(ref entries,Math.Max(ordinal+1,entries==null?4:entries.Length*2));actor.SkinConstants=entries;}
        var entry=entries[ordinal];
        if(entry==null||!ReferenceEquals(entry.Model,actor.RenderModel)||!ReferenceEquals(entry.Part,part)||!ReferenceEquals(entry.Skin,skin)||!ReferenceEquals(entry.Layout,layout)
            ||entry.Tint!=tint||entry.Hsv!=hsv||entry.Emissive!=emissive||entry.LodAlpha!=actor.LodAlpha||entry.Dither!=actor.Dithering||entry.Lod!=actor.RenderLod)
        {
            var template=WorldConstants.Object(layout,MatrixD.Identity,tint,hsv,emissive,actor.LodAlpha,null,actor.Dithering,false,actor.RenderLod);
            int boneOffset=new ReflectedConstantBuffer(layout,"Object").FloatOffsetOf("object_.bone_matrix",60*16);
            entries[ordinal]=entry=new SkinConstantEntry{Model=actor.RenderModel,Part=part,Skin=skin,Layout=layout,Tint=tint,Hsv=hsv,Emissive=emissive,LodAlpha=actor.LodAlpha,Dither=actor.Dithering,Lod=actor.RenderLod,
                Template=template,Rows=WorldConstants.ObjectRows(layout),BoneOffset=boneOffset};
        }
        if(!ReferenceEquals(entry.Source,actor.SkinMatrices)){
            var row=boneRowScratch??=new float[16];var bones=skin.Bones;var matrices=actor.SkinMatrices;
            for(int i=0;i<60;++i){Matrix bone=i<bones.Length && bones[i]<matrices.Length?matrices[bones[i]]:Matrix.Identity;NativeBackend.MatrixValues(bone,row);Array.Copy(row,0,entry.Palette,i*16,16);}
            entry.Source=matrices;
        }
        var constants=ReflectedConstantBuffer.Rent(entry.Template.Length);
        Buffer.BlockCopy(entry.Template,0,constants,0,entry.Template.Length);
        WorldConstants.WriteMatrix(constants,entry.Rows,world);
        fixed(byte* raw=constants)fixed(float* palette=entry.Palette)Buffer.MemoryCopy(palette,raw+entry.BoneOffset,60*16*4,60*16*4);
        return constants;
    }
}
