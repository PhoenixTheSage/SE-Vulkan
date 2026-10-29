using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using ClientPlugin.Rendering.Interop;
using VRageMath;
using VRageRender;
using VRageRender.Messages;

namespace ClientPlugin.Rendering.Shaders;

// Frame constants (plan 2 B2). The world frame block is written for the main view and every probe face each
// frame. FrameReflected resolves ~60 member paths through dictionary lookups and reads the postprocess and
// texture-multiplier structs field by field through FieldInfo.GetValue (one box per field). FrameWriter resolves
// the offsets once per shader layout and reads those structs through compiled getters; the bytes are identical.
public static partial class WorldConstants
{
    static readonly bool FrameVerify=Environment.GetEnvironmentVariable("SE_VULKAN_FRAME_CONSTANTS_VERIFY")=="1";
    static readonly Dictionary<ShaderMetadata,FrameWriter> FrameWriters=new();
    public static byte[] Frame(ShaderMetadata layout,MatrixD view,MatrixD projection,int width,int height,MyEnvironmentLightData light,float emission=1,MatrixD? background=null,MyRenderFogSettings? fog=null,float time=0,float delta=0,MyRenderSettings? renderSettings=null,Vector3? cameraDelta=null,MyPostprocessSettings? postprocess=null,bool specularProbes=false,float renderFraction=1,MyTextureDebugMultipliers? textureMultipliers=null)
    {
        FrameWriter writer;lock(FrameWriters)if(!FrameWriters.TryGetValue(layout,out writer))FrameWriters.Add(layout,writer=new FrameWriter(layout));
        var bytes=writer.Write(view,projection,width,height,light,emission,background,fog,time,delta,renderSettings,cameraDelta,postprocess,specularProbes,textureMultipliers);
        if(FrameVerify){
            var reference=FrameReflected(layout,view,projection,width,height,light,emission,background,fog,time,delta,renderSettings,cameraDelta,postprocess,specularProbes,renderFraction,textureMultipliers);
            if(reference.Length!=bytes.Length)throw new InvalidOperationException("Frame constants verify: size mismatch.");
            for(int i=0;i<bytes.Length;i++)if(reference[i]!=bytes[i])throw new InvalidOperationException("Frame constants verify: byte "+i+" differs.");
        }
        return bytes;
    }
    sealed class FrameWriter
    {
        enum Kind{Float,Int,Vector3}
        readonly struct PostField{public readonly int Offset;public readonly Kind Kind;public readonly Delegate Get;public PostField(int offset,Kind kind,Delegate get){Offset=offset;Kind=kind;Get=get;}}
        readonly int size;
        readonly int view,projection,skyProjection,viewProjection,invView,invProjection,invViewProjection,background;
        readonly int resolution,drs,distanceFade;
        readonly int sunColor,sunSpecular,sunDirection,sunDiffuse,sunGloss,sunSpecularFactor,ambientDiffuse,ambientSpecular;
        readonly int forwardAmbient,skyboxBrightness,shadowFadeoutEnv,atmosphereBrightness,forwardDim;
        readonly int aoDir,aoIndirect,aoPoint,aoSpot,discColor,discColor2,discInner,discOuter,discIntensity;
        readonly int skybox,ambientFar,skipLevels,shadowFadeout,tilesX,tilesNum,maxTileLights;
        readonly int bloom,fogColor,fogDensity,fogMult,fogSky,fogAtmo,clipping,wind,cameraDeltaAt,randomSeed,frameTime,frameDelta;
        readonly PostField[] post;
        readonly (int Offset,Func<MyTextureDebugMultipliers,float> Get)[] texture;
        public FrameWriter(ShaderMetadata layout)
        {
            var sample=new ReflectedConstantBuffer(layout,"Frame");size=sample.Bytes.Length;
            int F(string path,int count)=>sample.FloatOffsetOf(path,count);
            int E(string name)=>F("frame_.Environment."+name,16);int L(string name,int count)=>F("frame_.Light."+name,count);
            view=E("view_matrix");projection=E("projection_matrix");skyProjection=E("projection_matrix_for_skybox");viewProjection=E("view_projection_matrix");
            invView=E("inv_view_matrix");invProjection=E("inv_proj_matrix");invViewProjection=E("inv_view_proj_matrix");background=E("background_orientation");
            resolution=F("frame_.Screen.resolution",2);drs=F("frame_.Screen.drs",1);distanceFade=F("frame_.DistanceFade",1);
            sunColor=L("directionalLightColor",3);sunSpecular=L("directionalLightSpecularColor",3);sunDirection=L("directionalLightVec",3);
            sunDiffuse=L("directionalLightDiffuseFactor",1);sunGloss=L("directionalLightGlossFactor",1);sunSpecularFactor=L("directionalLightSpecularFactor",1);
            ambientDiffuse=L("ambientDiffuseFactor",1);ambientSpecular=L("ambientSpecularFactor",1);
            forwardAmbient=L("forwardPassAmbient",1);skyboxBrightness=L("envSkyboxBrightness",1);shadowFadeoutEnv=L("envShadowFadeout",1);atmosphereBrightness=L("envAtmosphereBrightness",1);forwardDim=L("ForwardDimDistance",1);
            aoDir=L("aoDirLight",1);aoIndirect=L("aoIndirectLight",1);aoPoint=L("aoPointLight",1);aoSpot=L("aoSpotLight",1);
            discColor=L("SunDiscColor",3);discColor2=L("SunDiscColor2",3);discInner=L("SunDiscInnerDot",1);discOuter=L("SunDiscOuterDot",1);discIntensity=L("SunDiscIntensity",1);
            skybox=L("skyboxBrightness",1);ambientFar=L("AmbientFarDistance",1);skipLevels=L("SkipIBLevels",1);shadowFadeout=L("shadowFadeout",1);
            tilesX=sample.IntegerOffsetOf("frame_.Light.tiles_x",true);tilesNum=sample.IntegerOffsetOf("frame_.Light.tiles_num",true);maxTileLights=sample.IntegerOffsetOf("frame_.Light.maxTileLights",true);
            var postFields=new List<PostField>();
            foreach(var field in typeof(MyPostprocessSettings.Layout).GetFields(BindingFlags.Public|BindingFlags.Instance))
            {
                string path="frame_.Post."+(field.Name=="Res2"?"LogLumThreshold":field.Name);
                var parameter=Expression.Parameter(typeof(object));Expression value=Expression.Field(Expression.Unbox(parameter,typeof(MyPostprocessSettings.Layout)),field);
                if(field.FieldType==typeof(Vector3))postFields.Add(new PostField(F(path,3),Kind.Vector3,Expression.Lambda<Func<object,Vector3>>(value,parameter).Compile()));
                else if(field.FieldType==typeof(int))postFields.Add(new PostField(sample.IntegerOffsetOf(path,false),Kind.Int,Expression.Lambda<Func<object,int>>(value,parameter).Compile()));
                else if(field.FieldType==typeof(float))postFields.Add(new PostField(F(path,1),Kind.Float,Expression.Lambda<Func<object,float>>(value,parameter).Compile()));
                else throw new InvalidOperationException("Unsupported postprocess constant type: "+field.Name+" "+field.FieldType);
            }
            post=postFields.ToArray();bloom=F("frame_.Post.BloomEmissiveness",1);
            var textureFields=new List<(int,Func<MyTextureDebugMultipliers,float>)>();
            foreach(var field in typeof(MyTextureDebugMultipliers).GetFields(BindingFlags.Public|BindingFlags.Instance))
                if(field.FieldType==typeof(float)){var parameter=Expression.Parameter(typeof(MyTextureDebugMultipliers));textureFields.Add((F("frame_.TextureDebugMultipliers."+field.Name,1),Expression.Lambda<Func<MyTextureDebugMultipliers,float>>(Expression.Field(parameter,field),parameter).Compile()));}
            texture=textureFields.ToArray();
            fogColor=F("frame_.Fog.color",3);fogDensity=F("frame_.Fog.density",1);fogMult=F("frame_.Fog.mult",1);fogSky=F("frame_.Fog.sky",1);fogAtmo=F("frame_.Fog.atmo",1);
            clipping=F("frame_.Foliage.clipping_scaling",4);wind=F("frame_.Foliage.wind_vec",3);cameraDeltaAt=F("frame_.Environment.cameraPositionDelta",3);
            randomSeed=F("frame_.randomSeed",1);frameTime=F("frame_.frameTime",1);frameDelta=F("frame_.frameTimeDelta",1);
        }
        static unsafe void Matrix(byte* raw,int offset,MatrixD value,float[] scratch){NativeBackend.MatrixValues(value,scratch);fixed(float* source=scratch)Buffer.MemoryCopy(source,raw+offset,64,64);}
        static unsafe void S(byte* raw,int offset,float value)=>*(float*)(raw+offset)=value;
        static unsafe void V(byte* raw,int offset,Vector3 value){float* f=(float*)(raw+offset);f[0]=value.X;f[1]=value.Y;f[2]=value.Z;}
        public unsafe byte[] Write(MatrixD view,MatrixD projection,int width,int height,MyEnvironmentLightData light,float emission,MatrixD? background,MyRenderFogSettings? fog,float time,float delta,MyRenderSettings? renderSettings,Vector3? cameraDelta,MyPostprocessSettings? postprocess,bool specularProbes,MyTextureDebugMultipliers? textureMultipliers)
        {
            var bytes=new byte[size];var matrix=matrixScratch??=new float[16];
            object postData=(postprocess??MyPostprocessSettings.Default).GetProcessedData();var textures=textureMultipliers??MyTextureDebugMultipliers.Defaults;
            fixed(byte* raw=bytes)
            {
                Matrix(raw,this.view,view,matrix);Matrix(raw,this.projection,projection,matrix);Matrix(raw,skyProjection,projection,matrix);Matrix(raw,viewProjection,view*projection,matrix);
                Matrix(raw,invView,MatrixD.Invert(view),matrix);Matrix(raw,invProjection,MatrixD.Invert(projection),matrix);Matrix(raw,invViewProjection,MatrixD.Invert(view*projection),matrix);
                Matrix(raw,this.background,MatrixD.Transpose(background??MatrixD.Identity),matrix);
                S(raw,resolution,width);S(raw,resolution+4,height);S(raw,drs,1);
                S(raw,distanceFade,(renderSettings??new MyRenderSettings()).User.DistanceFade);
                V(raw,sunColor,light.SunColorRaw);V(raw,sunSpecular,light.SunSpecularColorRaw);V(raw,sunDirection,light.SunLightDirection);
                S(raw,sunDiffuse,light.SunDiffuseFactor);S(raw,sunGloss,light.SunGlossFactor);S(raw,sunSpecularFactor,light.SunSpecularFactor);
                S(raw,ambientDiffuse,light.AmbientDiffuseFactor);S(raw,ambientSpecular,specularProbes?light.AmbientSpecularFactor:0);
                S(raw,forwardAmbient,light.AmbientForwardPass);S(raw,skyboxBrightness,light.EnvSkyboxBrightness);S(raw,shadowFadeoutEnv,light.EnvShadowFadeoutMultiplier);S(raw,atmosphereBrightness,light.EnvAtmosphereBrightness);S(raw,forwardDim,Math.Max(.001f,light.ForwardDimDistance));
                S(raw,aoDir,light.AODirLight);S(raw,aoIndirect,light.AOIndirectLight);S(raw,aoPoint,light.AOPointLight);S(raw,aoSpot,light.AOSpotLight);
                V(raw,discColor,light.SunDiscColor);V(raw,discColor2,light.SunDiscColor2);S(raw,discInner,light.SunDiscInnerDot);S(raw,discOuter,light.SunDiscOuterDot);S(raw,discIntensity,light.SunDiscIntensity);
                S(raw,skybox,light.SkyboxBrightness);S(raw,ambientFar,Math.Max(1,light.AmbientRadius));S(raw,skipLevels,specularProbes?Math.Max(0,light.SkipIBLevels):0);S(raw,shadowFadeout,light.ShadowFadeoutMultiplier);
                *(uint*)(raw+tilesX)=(uint)((width+15)/16);*(uint*)(raw+tilesNum)=(uint)(((width+15)/16)*((height+15)/16));*(uint*)(raw+maxTileLights)=PointLightCapacity((renderSettings??new MyRenderSettings()).User.LightsQuality);
                foreach(var field in post)switch(field.Kind){
                    case Kind.Vector3:V(raw,field.Offset,((Func<object,Vector3>)field.Get)(postData));break;
                    case Kind.Int:*(int*)(raw+field.Offset)=((Func<object,int>)field.Get)(postData);break;
                    default:S(raw,field.Offset,((Func<object,float>)field.Get)(postData));break;}
                S(raw,bloom,emission);
                foreach(var field in texture)S(raw,field.Offset,field.Get(textures));
                if(fog.HasValue){var f=fog.Value;Vector3 color=f.FogColor.ToVector3().ToLinearRGB();V(raw,fogColor,color);S(raw,fogDensity,f.FogDensity);S(raw,fogMult,f.FogMultiplier);S(raw,fogSky,f.FogSkybox);S(raw,fogAtmo,f.FogAtmo);}
                if(renderSettings.HasValue){var settings=renderSettings.Value;float* c=(float*)(raw+clipping);c[0]=settings.User.GrassDrawDistance;c[1]=settings.GrassGeometryScalingNearDistance*.4f;c[2]=settings.GrassGeometryScalingFarDistance*.4f;c[3]=settings.GrassGeometryDistanceScalingFactor;double angle=settings.WindAzimuth*Math.PI/180;float* w=(float*)(raw+wind);w[0]=(float)Math.Cos(angle)*settings.WindStrength;w[1]=0;w[2]=(float)Math.Sin(angle)*settings.WindStrength;}
                if(cameraDelta.HasValue)V(raw,cameraDeltaAt,cameraDelta.Value);
                S(raw,randomSeed,(float)((.314159 + time * .61803398875) % 1.0));
                S(raw,frameTime,time);S(raw,frameDelta,delta);
            }
            return bytes;
        }
    }
}
