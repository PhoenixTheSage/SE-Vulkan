using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VRage.Import;
using VRageMath;
using VRageMath.PackedVector;
using VRageRender.Import;
using VRageRender.Animations;

namespace FixtureAssets;

// Private test assets, written with the installed engine's own MWM exporter.
public static class MeshDecalFixtureAssets
{
    public static void Texture(string path,byte r,byte g,byte b,byte a)
    {
        using(var writer=new BinaryWriter(File.Create(path))){
            writer.Write(0x20534444u);writer.Write(124u);writer.Write(0x100fu);
            // Installed DDS header probing insists on at least 148 bytes even
            // for legacy headers; a 1x1 RGBA DDS (132 bytes) falls through to
            // ImageSharp. Use the same 16x16 form as the glass fixture.
            writer.Write(16u);writer.Write(16u);writer.Write(64u);writer.Write(0u);writer.Write(1u);
            for(int i=0;i<11;i++)writer.Write(0u);
            foreach(uint value in new uint[]{32,0x41,0,32,0xff,0xff00,0xff0000,0xff000000,0x1000,0,0,0,0})writer.Write(value);
            for(int i=0;i<256;i++)writer.Write(new[]{r,g,b,a});
        }
    }
    public static string Model(string root,MyMeshDrawTechnique technique,uint role,byte alpha=128,bool overlap=false,bool blue=false,byte? cutoutAlpha=null,bool reverseWinding=false,bool splitWinding=false,bool deformed=false,bool singleLayer=false,bool neutral=false,MyLODDescriptor[] identityLods=null,string geometryDataAsset=null)
    {
        if(cutoutAlpha.HasValue&&!overlap)throw new ArgumentException("Cutout ordering fixture requires overlapping decals.");
        Directory.CreateDirectory(root);string key=technique+"-"+role+"-"+alpha+(overlap?"-overlap":"")+(blue?"-blue":"")+(cutoutAlpha.HasValue?"-cutout-"+cutoutAlpha.Value:"")+(reverseWinding?"-back":"")+(splitWinding?"-split":"")+(deformed?"-deformed":"")+(singleLayer?"-single":"")+(neutral?"-neutral":"");
        string Tex(string name,byte r,byte g,byte b,byte a){string path=Path.Combine(root,name+".dds");Texture(path,r,g,b,a);return path;}
        string gray=Tex("gray",180,180,180,0),baseNormal=Tex("flat",128,128,255,100),baseExt=Tex("base-add",255,0,0,0);
        string red=neutral?Tex("neutral",180,180,180,0):Tex(blue?"blue":"red",(byte)(blue?0:255),0,(byte)(blue?255:0),255),normal=Tex("normal",128,200,255,220),ext=Tex("add",64,128,0,0),mask=Tex("mask-"+alpha,alpha,alpha,alpha,alpha);
        var background=new MyMaterialDescriptor("FixtureBase-"+key){TechniqueEnum=MyMeshDrawTechnique.MESH};
        background.Textures["ColorMetalTexture"]=gray;background.Textures["NormalGlossTexture"]=baseNormal;background.Textures["AddMapsTexture"]=baseExt;
        var decal=new MyMaterialDescriptor("FixtureDecal-"+key){TechniqueEnum=technique};
        if((role&1)!=0)decal.Textures["ColorMetalTexture"]=red;
        if((role&2)!=0)decal.Textures["NormalGlossTexture"]=normal;
        if((role&4)!=0)decal.Textures["AddMapsTexture"]=ext;
        decal.Textures["AlphamaskTexture"]=mask;
        var parts=new List<MyMeshPartInfo>{
            new MyMeshPartInfo{m_MaterialDesc=background,Technique=MyMeshDrawTechnique.MESH,m_indices=new List<int>{0,2,1,0,3,2}},
            new MyMeshPartInfo{m_MaterialDesc=decal,Technique=technique,m_indices=new List<int>{4,6,5,4,7,6}}};
        if(singleLayer)parts.RemoveAt(0);
        if(overlap){
            var other=technique==MyMeshDrawTechnique.DECAL?MyMeshDrawTechnique.DECAL_NOPREMULT:MyMeshDrawTechnique.DECAL;
            var second=new MyMaterialDescriptor("FixtureSecond-"+key){TechniqueEnum=other};
            if((role&1)!=0)second.Textures["ColorMetalTexture"]=Tex("blue",0,0,255,255);
            if((role&2)!=0)second.Textures["NormalGlossTexture"]=Tex("second-normal",200,128,255,180);
            if((role&4)!=0)second.Textures["AddMapsTexture"]=Tex("second-add",128,64,0,0);
            second.Textures["AlphamaskTexture"]=mask;
            parts.Add(new MyMeshPartInfo{m_MaterialDesc=second,Technique=other,m_indices=new List<int>{8,10,9,8,11,10}});
        }
        if(cutoutAlpha.HasValue){
            var cutout=new MyMaterialDescriptor("FixtureCutout-"+key){TechniqueEnum=MyMeshDrawTechnique.DECAL_CUTOUT};
            cutout.Textures["ColorMetalTexture"]=Tex("green",0,255,0,0);cutout.Textures["NormalGlossTexture"]=baseNormal;cutout.Textures["AddMapsTexture"]=baseExt;
            byte a=cutoutAlpha.Value;cutout.Textures["AlphamaskTexture"]=Tex("mask-cutout-"+a,a,a,a,a);
            // Author cutout before both blends. Stock technique ordering draws
            // it last, without replacing the opaque depth beneath these layers.
            parts.Insert(1,new MyMeshPartInfo{m_MaterialDesc=cutout,Technique=MyMeshDrawTechnique.DECAL_CUTOUT,m_indices=new List<int>{12,14,13,12,15,14}});
        }
        var positions=new List<HalfVector4>();var uv=new List<HalfVector2>();
        foreach(float z in cutoutAlpha.HasValue?new[]{0f,.015625f,.03125f,.046875f}:overlap?new[]{0f,.015625f,.03125f}:new[]{0f,.015625f})foreach(var p in new[]{new Vector2(-1,-1),new Vector2(1,-1),new Vector2(1,1),new Vector2(-1,1)}){
            positions.Add(VF_Packer.PackPosition(new Vector3(p.X,p.Y,z)));uv.Add(new HalfVector2((p.X+1)/2,(1-p.Y)/2));}
        if(splitWinding){
            int original=positions.Count;
            for(int i=0;i<original;i++){
                var v=VF_Packer.UnpackPosition(positions[i]);var right=v;right.X=v.X*.5f+.5f;v.X=v.X*.5f-.5f;
                positions[i]=VF_Packer.PackPosition(v);positions.Add(VF_Packer.PackPosition(right));uv.Add(uv[i]);
            }
            foreach(var part in parts){int count=part.m_indices.Count;for(int i=0;i<count;i+=3){part.m_indices.Add(part.m_indices[i]+original);part.m_indices.Add(part.m_indices[i+2]+original);part.m_indices.Add(part.m_indices[i+1]+original);}}
        }
        if(reverseWinding)foreach(var part in parts)for(int i=0;i<part.m_indices.Count;i+=3){int swap=part.m_indices[i+1];part.m_indices[i+1]=part.m_indices[i+2];part.m_indices[i+2]=swap;}
        Byte4[] Packed(Vector3 value)=>Enumerable.Repeat(new Byte4{PackedValue=VF_Packer.PackNormal(value)},positions.Count).ToArray();
        string model=Path.Combine(root,key+".mwm");
        using(var exporter=new MyModelExporter(model)){
            exporter.ExportData("Debug",new[]{"Version:01157002","Private mesh decal parity fixture"});exporter.StartCacheWrite();var index=new Dictionary<string,int>();
            void Tag(string name,Action write){index.Add(name,exporter.GetCachePosition());write();}
            Tag("Vertices",()=>exporter.ExportData("Vertices",positions.ToArray()));Tag("Normals",()=>exporter.ExportData("Normals",Packed(Vector3.Backward)));
            Tag("Tangents",()=>exporter.ExportData("Tangents",Packed(Vector3.Right)));Tag("Binormals",()=>exporter.ExportData("Binormals",Packed(Vector3.Down)));
            Tag("TexCoords0",()=>exporter.ExportData("TexCoords0",uv.ToArray()));Tag("TexCoords1",()=>exporter.ExportData("TexCoords1",Array.Empty<HalfVector2>()));
            Tag("BlendIndices",()=>exporter.ExportData("BlendIndices",deformed?new Vector4I[positions.Count]:Array.Empty<Vector4I>()));Tag("BlendWeights",()=>exporter.ExportData("BlendWeights",deformed?Enumerable.Repeat(new Vector4(1,0,0,0),positions.Count).ToArray():Array.Empty<Vector4>()));
            Tag("MeshParts",()=>exporter.ExportData("MeshParts",parts));Tag("Bones",()=>exporter.ExportData("Bones",Array.Empty<MyModelBone>()));
            Tag("Sections",()=>exporter.ExportData("Sections",new List<MyMeshSectionInfo>()));Tag("LODs",()=>exporter.ExportData("LODs",identityLods??Array.Empty<MyLODDescriptor>()));
            if(geometryDataAsset!=null)Tag("GeometryDataAsset",()=>exporter.ExportData("GeometryDataAsset",geometryDataAsset));
            Tag("BoundingBox",()=>exporter.ExportData("BoundingBox",new BoundingBox(new Vector3(-1,-1,0),new Vector3(1,1,cutoutAlpha.HasValue?.046875f:overlap?.03125f:.015625f))));
            Tag("BoundingSphere",()=>exporter.ExportData("BoundingSphere",new BoundingSphere(new Vector3(0,0,.0078125f),1.415f)));
            Tag("PatternScale",()=>exporter.ExportFloat("PatternScale",1));Tag("RescaleFactor",()=>exporter.ExportFloat("RescaleFactor",1));
            Tag("UseChannelTextures",()=>exporter.ExportBool("UseChannelTextures",true));Tag("SwapWindingOrder",()=>exporter.ExportBool("SwapWindingOrder",false));
            Tag("Animations",()=>exporter.ExportData("Animations",new ModelAnimations()));Tag("BoneMapping",()=>exporter.ExportData("BoneMapping",Array.Empty<Vector3I>()));
            Tag("Dummies",()=>exporter.ExportData("Dummies",new Dictionary<string,MyModelDummy>()));Tag("HavokCollisionGeometry",()=>exporter.ExportData("HavokCollisionGeometry",Array.Empty<byte>()));
            Tag("ModelInfo",()=>exporter.ExportData("ModelInfo",new MyModelInfo(parts.Sum(part=>part.m_indices.Count)/3,positions.Count,new Vector3(2,2,cutoutAlpha.HasValue?.046875f:overlap?.03125f:.015625f))));
            exporter.StopCacheWrite();exporter.WriteIndexDictionary(index);exporter.FlushCache();
        }
        return model;
    }
}
