using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using VRage.FileSystem;
using VRageRender;
using VRageRender.Import;
using VRageRender.Messages;

namespace ClientPlugin.Rendering.Content;

internal readonly struct StockOldMaterialDescription
{
    public readonly StockOldMaterialIdentity Identity;
    public readonly bool UsesGeometryArray;
    public StockOldMaterialDescription(StockOldMaterialIdentity identity,bool usesGeometryArray){Identity=identity;UsesGeometryArray=usesGeometryArray;}
}

internal readonly struct StockOldTextureHeader
{
    public readonly int Width,Height,Mips,ArraySize,Format;
    public StockOldTextureHeader(int width,int height,int mips,int arraySize,int format){Width=width;Height=height;Mips=mips;ArraySize=arraySize;Format=format;}
}

/// <summary>
/// CPU import-to-identity boundary for OLD materials. Never initialize the
/// render proxy/settings or call FileTextures/geometry-array managers or stock
/// material registration. The stock image-description reader is CPU-only.
/// </summary>
internal sealed class StockOldMaterialDescriptions
{
    const BindingFlags flags=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static readonly Assembly stock=typeof(MyRenderSettings1Extensions).Assembly;
    static readonly Type utils=stock.GetType("VRage.Render11.Resources.MyResourceUtils",true);
    static readonly char[] invalidGenerated={'(',')','<','>','|','\\','/','\0','\t',' ','.',','};
    static readonly Func<string,string,string> fullPath=ResolveTexturePath;
    static readonly MethodInfo makeSrgb=utils.GetMethod("MakeSrgb",flags);
    static readonly Type formatType=makeSrgb.ReturnType;
    static readonly Func<int,int> mipLevels=(Func<int,int>)Delegate.CreateDelegate(typeof(Func<int,int>),utils.GetMethod("GetMipLevels",flags));
    static readonly MethodInfo readHeader=stock.GetType("SharpDX.Toolkit.Graphics.Image",true).GetMethod("LoadImageDescription",flags);
    // MyFileTextureParamsManager retains its header dictionary across devices
    // and sessions. Keep the production metadata cache at that same lifetime.
    static readonly Dictionary<string,StockOldTextureHeader> sharedHeaders=new(StringComparer.Ordinal);
    static readonly string[] channels={"ColorMetal","NormalGloss","Extension","Alphamask"};
    static readonly string[] defaults={"BC7_UNorm_SRgb","BC7_UNorm","BC7_UNorm_SRgb","BC4_UNorm"};
    readonly Dictionary<string,StockOldTextureHeader> headers;
    readonly Func<string,StockOldTextureHeader?> loadHeader;

    public StockOldMaterialDescriptions(Func<string,StockOldTextureHeader?> loader=null)
    {
        loadHeader=loader??ReadHeader;headers=loader==null?sharedHeaders:new(StringComparer.Ordinal);
    }

    static string ResolveTexturePath(string name,string contentPath)
    {
        // MyResourceUtils.GetTextureFullPath / MyRenderProxy's character test,
        // without the latter type's renderer-dependent static initialization.
        if(string.IsNullOrEmpty(name))return string.Empty;
        if(name.IndexOfAny(invalidGenerated)<0)return name;
        string path=null;
        if(Path.IsPathRooted(name))path=name;
        else {
            string Normalize(string value)=>Path.GetFullPath(new Uri(value).LocalPath).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar).ToLowerInvariant();
            if(!string.IsNullOrEmpty(contentPath)&&Normalize(contentPath)!=Normalize(MyFileSystem.ContentPath)){
                string candidate=Path.Combine(contentPath,name);if(MyFileSystem.FileExists(candidate))path=candidate;
            }
            if(path==null)path=Path.Combine(MyFileSystem.ContentPath,name);
        }
        return Path.GetFullPath(VRage.Library.Utils.PathUtils.Normalize(path));
    }

    static StockOldTextureHeader? ReadHeader(string path)
    {
        try {
            using var stream=MyFileSystem.OpenRead(path);if(stream==null)return null;
            object description=readHeader.Invoke(null,new object[]{stream,path});if(description==null)return null;
            var type=description.GetType();int Field(string name)=>Convert.ToInt32(type.GetField(name).GetValue(description));
            return new StockOldTextureHeader(Field("Width"),Field("Height"),Field("MipLevels"),Field("ArraySize"),Field("Format"));
        }catch{return null;} // Stock's header cache likewise retains a failed read as default metadata.
    }

    bool TryHeader(string path,out StockOldTextureHeader header)
    {
        header=default;if(string.IsNullOrEmpty(path))return false;path=fullPath(path,null);
        lock(headers){
            if(headers.TryGetValue(path,out header))return true;
            header=loadHeader(path)??default;headers[path]=header;
            return header.Mips!=0;
        }
    }

    static string ContentPath(string file)
    {
        if(Path.IsPathRooted(file)){int at=file.ToLower().IndexOf("models");if(at>=0)return file.Substring(0,at);}
        return null;
    }

    static string[] Paths(MyMaterialDescriptor material,string contentPath,bool impostor)
    {
        string Texture(string name)=>material.Textures.TryGetValue(name,out string value)?value:null;
        string normal=Texture("NormalGlossTexture");
        if(impostor&&material.Facing.ToString()=="Impostor"&&!material.Textures.ContainsKey("NormalGlossTexture")){
            string color=material.Textures["ColorMetalTexture"];string extension=Path.GetExtension(color);normal=color.Substring(0,color.Length-extension.Length)+"_normal_depth"+extension;
        }
        return new[]{fullPath(Texture("ColorMetalTexture"),contentPath),fullPath(normal,contentPath),fullPath(Texture("AddMapsTexture"),contentPath),fullPath(Texture("AlphamaskTexture"),contentPath)};
    }

    (int X,int Y) DefaultSize(string[] paths)
    {
        var sizes=new (int X,int Y)[4];for(int i=0;i<4;i++)if(TryHeader(paths[i],out var header))sizes[i]=(header.Width,header.Height);
        var size=sizes[0];if(size==(0,0)){for(int i=1;i<4;i++)size=(Math.Max(size.X,sizes[i].X),Math.Max(size.Y,sizes[i].Y));}
        return size==(0,0)?(1024,1024):size;
    }

    string ArrayName(string path,int channel,(int X,int Y) fallback)
    {
        int width,height,mips;object format;
        if(TryHeader(path,out var header)){
            width=header.Width;height=header.Height;mips=header.Mips;format=Enum.ToObject(formatType,header.Format);
            if(channel!=1)format=makeSrgb.Invoke(null,new[]{format});
        }else{width=fallback.X;height=fallback.Y;mips=mipLevels(Math.Max(width,height));format=Enum.Parse(formatType,defaults[channel]);}
        return string.Format("{0}-{1}x{2},{3}:{4}.dds",channels[channel],width,height,mips,format);
    }

    public StockOldMaterialIdentity[] Describe(string assetFile,IReadOnlyList<MyMaterialDescriptor> materials,bool geometryArrays)
    {
        var descriptions=DescribeParts(assetFile,materials,geometryArrays);var result=new StockOldMaterialIdentity[descriptions.Length];
        for(int i=0;i<result.Length;i++)result[i]=descriptions[i].Identity;return result;
    }

    public static StockOldMaterialIdentity ChangeTextures(StockOldMaterialIdentity source,MyTextureChange change)=>new(source.Name,
        change.ColorMetalFileName==null?source.Color:fullPath(change.ColorMetalFileName,null),
        change.NormalGlossFileName==null?source.Normal:fullPath(change.NormalGlossFileName,null),
        change.ExtensionsFileName==null?source.Extensions:fullPath(change.ExtensionsFileName,null),
        change.AlphamaskFileName==null?source.Alpha:fullPath(change.AlphamaskFileName,null),source.Technique);

    public StockOldMaterialDescription[] DescribeParts(string assetFile,IReadOnlyList<MyMaterialDescriptor> materials,bool geometryArrays)
    {
        // MyMeshData.DoImport warms texture-header/array keys before
        // MyMeshes.CreatePartInfos allocates any material identities.
        if(geometryArrays){string imported=Path.IsPathRooted(assetFile)?assetFile:Path.Combine(MyFileSystem.ContentPath,assetFile);
            foreach(var material in materials)if(material!=null&&material.Textures.ContainsKey("ColorMetalTexture")&&material.Textures.ContainsKey("NormalGlossTexture")){
                var paths=Paths(material,ContentPath(imported),false);var size=DefaultSize(paths);for(int c=0;c<4;c++)ArrayName(paths[c],c,size);
            }
        }
        var result=new StockOldMaterialDescription[materials.Count];
        for(int i=0;i<result.Length;i++){
            var material=materials[i];if(material==null)continue;
            var paths=Paths(material,ContentPath(assetFile),true);bool arrays=geometryArrays;
            if(arrays)for(int c=0;c<4;c++)if(TryHeader(paths[c],out var header)&&header.ArraySize>1){arrays=false;break;}
            if(arrays){var size=DefaultSize(paths);for(int c=0;c<4;c++)if(c!=3||!string.IsNullOrEmpty(paths[c]))paths[c]=ArrayName(paths[c],c,size);}
            result[i]=new StockOldMaterialDescription(new StockOldMaterialIdentity(material.MaterialName,paths[0],paths[1],paths[2],paths[3],material.TechniqueEnum),arrays);
        }
        return result;
    }
}
