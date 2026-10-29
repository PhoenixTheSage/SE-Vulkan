using System;
using ClientPlugin.Rendering.Interop;
using VRageMath;
using VRageRender;
using VRageRender.Messages;

namespace ClientPlugin.Rendering.Shaders;

public static partial class WorldConstants
{
    // Member paths are built once per (prefix, name): Frame runs for the main view and every probe face.
    static readonly System.Collections.Concurrent.ConcurrentDictionary<(string Prefix,string Name),string> MemberPaths=new();
    static string MemberPath(string prefix,string name)=>MemberPaths.GetOrAdd((prefix,name),static key=>key.Prefix+key.Name);
    [ThreadStatic] static float[] matrixScratch;
    public static uint PointLightCapacity(MyRenderQualityEnum quality)=>quality switch {
        MyRenderQualityEnum.LOW=>256,MyRenderQualityEnum.NORMAL=>512,
        MyRenderQualityEnum.HIGH=>2048,MyRenderQualityEnum.EXTREME=>4096,
        _=>throw new ArgumentOutOfRangeException(nameof(quality))};
    // Reference builder: resolves every member by path and reads the post and texture settings by reflection.
    // FrameWriter writes the same bytes; SE_VULKAN_FRAME_CONSTANTS_VERIFY=1 compares the two on every call.
    internal static byte[] FrameReflected(ShaderMetadata layout,MatrixD view,MatrixD projection,int width,int height,MyEnvironmentLightData light,float emission=1,MatrixD? background=null,MyRenderFogSettings? fog=null,float time=0,float delta=0,MyRenderSettings? renderSettings=null,Vector3? cameraDelta=null,MyPostprocessSettings? postprocess=null,bool specularProbes=false,float renderFraction=1,MyTextureDebugMultipliers? textureMultipliers=null)
    {
        var result=new ReflectedConstantBuffer(layout,"Frame");
        var matrix=matrixScratch??=new float[16];
        void Matrix(string name,MatrixD value){NativeBackend.MatrixValues(value,matrix);result.Set(MemberPath("frame_.Environment.",name),matrix);}
        Matrix("view_matrix",view);Matrix("projection_matrix",projection);Matrix("projection_matrix_for_skybox",projection);Matrix("view_projection_matrix",view*projection);
        Matrix("inv_view_matrix",MatrixD.Invert(view));Matrix("inv_proj_matrix",MatrixD.Invert(projection));Matrix("inv_view_proj_matrix",MatrixD.Invert(view*projection));// Vanilla writes this one matrix without its usual CPU transpose; -Zpr needs the inverse storage convention.
        Matrix("background_orientation",MatrixD.Transpose(background??MatrixD.Identity));
        // Vulkan allocates exact-sized world targets. Stock SSAO multiplies its
        // screen coordinates by drs; applying the output fraction again would
        // reconstruct positions from a second, incorrectly scaled viewport.
        result.Set("frame_.Screen.resolution",width,height);result.Set("frame_.Screen.drs",1);
        result.Set("frame_.DistanceFade",(renderSettings??new MyRenderSettings()).User.DistanceFade);
        void Scalar(string name,float value)=>result.Set(MemberPath("frame_.Light.",name),value);
        void Vector(string name,Vector3 value)=>result.Set(MemberPath("frame_.Light.",name),value.X,value.Y,value.Z);
        Vector("directionalLightColor",light.SunColorRaw);Vector("directionalLightSpecularColor",light.SunSpecularColorRaw);Vector("directionalLightVec",light.SunLightDirection);
        Scalar("directionalLightDiffuseFactor",light.SunDiffuseFactor);Scalar("directionalLightGlossFactor",light.SunGlossFactor);Scalar("directionalLightSpecularFactor",light.SunSpecularFactor);
        Scalar("ambientDiffuseFactor",light.AmbientDiffuseFactor);Scalar("ambientSpecularFactor",specularProbes?light.AmbientSpecularFactor:0);
        Scalar("forwardPassAmbient",light.AmbientForwardPass);Scalar("envSkyboxBrightness",light.EnvSkyboxBrightness);Scalar("envShadowFadeout",light.EnvShadowFadeoutMultiplier);Scalar("envAtmosphereBrightness",light.EnvAtmosphereBrightness);Scalar("ForwardDimDistance",Math.Max(.001f,light.ForwardDimDistance));
        Scalar("aoDirLight",light.AODirLight);Scalar("aoIndirectLight",light.AOIndirectLight);Scalar("aoPointLight",light.AOPointLight);Scalar("aoSpotLight",light.AOSpotLight);
        Vector("SunDiscColor",light.SunDiscColor);Vector("SunDiscColor2",light.SunDiscColor2);Scalar("SunDiscInnerDot",light.SunDiscInnerDot);Scalar("SunDiscOuterDot",light.SunDiscOuterDot);Scalar("SunDiscIntensity",light.SunDiscIntensity);
        Scalar("skyboxBrightness",light.SkyboxBrightness);Scalar("AmbientFarDistance",Math.Max(1,light.AmbientRadius));Scalar("SkipIBLevels",specularProbes?Math.Max(0,light.SkipIBLevels):0);Scalar("shadowFadeout",light.ShadowFadeoutMultiplier);
        result.SetUInt("frame_.Light.tiles_x",(uint)((width+15)/16));result.SetUInt("frame_.Light.tiles_num",(uint)(((width+15)/16)*((height+15)/16)));result.SetUInt("frame_.Light.maxTileLights",PointLightCapacity((renderSettings??new MyRenderSettings()).User.LightsQuality));
        object post=(postprocess??MyPostprocessSettings.Default).GetProcessedData();
        foreach(var field in typeof(MyPostprocessSettings.Layout).GetFields(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Instance))
        {string path=MemberPath("frame_.Post.",field.Name=="Res2"?"LogLumThreshold":field.Name);object value=field.GetValue(post);if(value is Vector3 v)result.Set(path,v.X,v.Y,v.Z);else if(value is int n)result.SetInt(path,n);else result.Set(path,(float)value);}
        result.Set("frame_.Post.BloomEmissiveness",emission);
        var texture=textureMultipliers??MyTextureDebugMultipliers.Defaults;
        foreach(var field in typeof(MyTextureDebugMultipliers).GetFields(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Instance))
            if(field.FieldType==typeof(float))result.Set(MemberPath("frame_.TextureDebugMultipliers.",field.Name),(float)field.GetValue(texture));
        // Scene submission already adds ColorizeHSV to each object's key color.
        // Leave the frame vector zero to preserve that existing ownership.
        if(fog.HasValue){var f=fog.Value;Vector3 color=f.FogColor.ToVector3().ToLinearRGB();result.Set("frame_.Fog.color",color.X,color.Y,color.Z);result.Set("frame_.Fog.density",f.FogDensity);result.Set("frame_.Fog.mult",f.FogMultiplier);result.Set("frame_.Fog.sky",f.FogSkybox);result.Set("frame_.Fog.atmo",f.FogAtmo);}
        if(renderSettings.HasValue){var settings=renderSettings.Value;result.Set("frame_.Foliage.clipping_scaling",settings.User.GrassDrawDistance,settings.GrassGeometryScalingNearDistance*.4f,settings.GrassGeometryScalingFarDistance*.4f,settings.GrassGeometryDistanceScalingFactor);double angle=settings.WindAzimuth*Math.PI/180;result.Set("frame_.Foliage.wind_vec",(float)Math.Cos(angle)*settings.WindStrength,0,(float)Math.Sin(angle)*settings.WindStrength);}
        if(cameraDelta.HasValue){var d=cameraDelta.Value;result.Set("frame_.Environment.cameraPositionDelta",d.X,d.Y,d.Z);}result.Set("frame_.randomSeed", (float)((.314159 + time * .61803398875) % 1.0));
        result.Set("frame_.frameTime",time);result.Set("frame_.frameTimeDelta",delta);
        return result.Bytes;
    }
    public static byte[] Object(ShaderMetadata layout,MatrixD world,Vector3 tint,Vector3 hsv,float emissivity,float dither,float[] bones=null,float? alphaY=null,bool pooled=false,uint lod=0)
    {
        var result=pooled?new ReflectedConstantBuffer(layout,"Object",ReflectedConstantBuffer.Rent(layout,"Object")):new ReflectedConstantBuffer(layout,"Object");
        result.Set("object_.matrix_row0",(float)world.M11,(float)world.M21,(float)world.M31,(float)world.M41);
        result.Set("object_.matrix_row1",(float)world.M12,(float)world.M22,(float)world.M32,(float)world.M42);
        result.Set("object_.matrix_row2",(float)world.M13,(float)world.M23,(float)world.M33,(float)world.M43);
        result.Set("object_.color_mul",tint.X,tint.Y,tint.Z);result.Set("object_.key_color",hsv.X,hsv.Y,hsv.Z);result.Set("object_.emissive",emissivity);result.Set("object_.custom_alpha",dither,alphaY??0);
        result.SetUInt("object_.LOD",lod);
        if(bones!=null)result.Set("object_.bone_matrix",bones);
        return result.Bytes;
    }
    public readonly struct MatrixRows { public readonly int Row0, Row1, Row2; public MatrixRows(int row0, int row1, int row2) { Row0 = row0; Row1 = row1; Row2 = row2; } }
    static readonly System.Collections.Generic.Dictionary<ShaderMetadata, MatrixRows> Rows = new System.Collections.Generic.Dictionary<ShaderMetadata, MatrixRows>();
    public static MatrixRows ObjectRows(ShaderMetadata layout)
    {
        MatrixRows rows;
        if (Rows.TryGetValue(layout, out rows)) return rows;
        var sample = new ReflectedConstantBuffer(layout, "Object");
        rows = new MatrixRows(sample.OffsetOf("object_.matrix_row0"), sample.OffsetOf("object_.matrix_row1"), sample.OffsetOf("object_.matrix_row2"));
        Rows.Add(layout, rows);
        return rows;
    }
    public static unsafe void WriteMatrix(byte[] bytes, MatrixRows rows, MatrixD world)
    {
        fixed (byte* raw = bytes)
        {
            WriteRow(raw + rows.Row0, (float)world.M11, (float)world.M21, (float)world.M31, (float)world.M41);
            WriteRow(raw + rows.Row1, (float)world.M12, (float)world.M22, (float)world.M32, (float)world.M42);
            WriteRow(raw + rows.Row2, (float)world.M13, (float)world.M23, (float)world.M33, (float)world.M43);
        }
    }
    static unsafe void WriteRow(byte* destination, float a, float b, float c, float d)
    {
        float* f = (float*)destination;
        f[0] = a; f[1] = b; f[2] = c; f[3] = d;
    }
}
