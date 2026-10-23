using System.Reflection;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using ClientPlugin.Rendering.Shaders;
using VRageMath;
using VRageRender;

partial class Program
{
    // Stage-7 HDR display tenancy: a Display-policy AfterUpscale program grades pre-tonemap HDR at the tone position into
    // FP16 (as DX11 does in MyToneMapping.Run's place); FXAA and chromatic then run on that image. Without an HDR
    // display the stock tonemap stands and the program never runs.
    static int HdrTenantRegression(Assembly core,string output,NativeBackend backend,MatrixD projection,MyEnvironmentLightData light)
    {
        const BindingFlags stat=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;const string owner="fixture.hdr.tenant";
        int checks=0;void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        object Call(string type,string method,params object[] args)=>core.GetType("ClientPlugin."+type,true).GetMethod(method,stat).Invoke(null,args);
        object Add(string id,string slot,string source,string compose){
            string file=Path.Combine(output,id+".hlsl");File.WriteAllText(file,source);
            Call("Backend.GuestPassList","AddProgram",id,slot,file,compose);
            return ((System.Collections.IEnumerable)Call("Backend.GuestPassList","Snapshot")).Cast<object>().Single(p=>(string)p.GetType().GetField("Id").GetValue(p)==id);
        }
        void Set(object p,string field,object value)=>p.GetType().GetField(field).SetValue(p,value);
        // The writer pins pre-tonemap HDR to 0.5; the grade writes scene*2 + (3,0,0), which only FP16 can hold.
        object writer=Add(owner+".writer","BeforeTonemap","cbuffer Inputs:register(b7){float4 Value;}float4 main():SV_Target{return Value;}","Replace");
        Set(writer,"Uniforms",new float[]{.5f,.5f,.5f,1});
        object grade=Add(owner+".grade","AfterUpscale",
            "Texture2D<float4> Scene:register(t0);Texture2D<float4> AvgLum:register(t4);cbuffer Inputs:register(b7){float4 Value;}"+
            "float4 main(float4 p:SV_Position):SV_Target{float e=AvgLum.Load(int3(0,0,0)).g;return float4(Scene.Load(int3(p.xy,0)).rgb*Value.x+float3(Value.y,0,0),isfinite(e)?1:0);}","Replace");
        Set(grade,"Policy",8);Set(grade,"Uniforms",new float[]{2,3,0,0});
        Call("Backend.GuestCompiler","CompilePending",Path.Combine(Path.GetDirectoryName(core.Location)!,"../../../../Assets"));
        var programs=core.GetType("ClientPlugin.Backend.GuestProgram",true);
        var submit=Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(typeof(IReadOnlyList<>).MakeGenericType(programs)),core.GetType("ClientPlugin.Backend.GuestBridgeClient",true).GetMethod("Submit",stat));
        var post=new VulkanPostprocess();post.Hbao.Enabled=false;post.Ssao.Enabled=false;post.Settings.BloomEnabled=false;post.Settings.EnableEyeAdaptation=false;
        const int size=64;int centre=(32*size+32)*4;uint service=0;
        void Frame(bool fxaa=false){
            Call("Backend.GuestColor","SyncDisplay");backend.BeginPacketCollection();
            post.Configure(backend,size,size,fxaa,view:MatrixD.Identity,projection:projection);
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,size,size,light,postprocess:post.Settings),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"HDR tenant frame begins");
            Call("Backend.GuestFrame","Capture");service=(uint)Call("Backend.GuestFrame","U32",340);
            Call("Backend.SceneView","Set",size,size,0d,0d,0d,0f,0f,-1f);Call("Backend.GuestPassList","SubmitFrame",submit);backend.EndFrame();
        }
        void Frames(int count,bool fxaa=false){for(int i=0;i<count;i++)Frame(fxaa);}
        bool Near(float a,float b,float t=.01f)=>Math.Abs(a-b)<=t;
        bool Tenant()=>core.GetType("ClientPlugin.Backend.GuestColor",true).GetProperty("HasDisplay",stat).GetValue(null) is true;
        try{
            Set(grade,"Enabled",false);Frames(3);var baseline=backend.ReadbackFxaaInput(false,size,size);
            Check(!Tenant()&&(service&32)==0,"no Display program: no tenant request and no grade");
            Set(grade,"Enabled",true);Frames(3);
            Check(Tenant()&&(service&16)!=0,"an enabled Display-policy AfterUpscale program requests display bit 16");
            Check((service&32)==0&&backend.ReadbackFxaaInput(false,size,size).SequenceEqual(baseline),"without an HDR display the program never runs and the stock tonemap stands");
            var before=backend.GetPostprocessStats();backend.ForceHdrDisplay(true);Frames(3);var after=backend.GetPostprocessStats();
            Check((service&32)!=0,"with an HDR display the renderer reports the tenant grade (service bit 32)");
            var graded=backend.ReadbackGradedOutput(false,size,size);
            Console.WriteLine("HDR graded centre="+graded[centre]+","+graded[centre+1]+","+graded[centre+2]+","+graded[centre+3]);
            Check(Near(graded[centre],4)&&Near(graded[centre+1],1)&&Near(graded[centre+2],1),"the grade reads pre-tonemap HDR and keeps scRGB above 1 in FP16");
            Check(graded[centre+3]==1,"avgLuminance (t4) binds a finite exposure value at the tone position");
            Check(after.ToneDispatches-before.ToneDispatches==3,"the grade takes the tone stage's place once per frame");
            // Default settings keep the vignette on, so chromatic may run; its centre pixel keeps the graded value.
            var final=backend.ReadbackGradedOutput(true,size,size);Console.WriteLine("HDR final centre="+final[centre]+" chromatic="+(after.ChromaticDispatches-before.ChromaticDispatches));
            Check(Near(final[centre],graded[centre],.05f)&&final[centre]>1.5f,"the final FP16 post output carries the graded image");
            before=backend.GetPostprocessStats();Frames(2,true);after=backend.GetPostprocessStats();final=backend.ReadbackGradedOutput(true,size,size);
            Console.WriteLine("HDR fxaa centre="+final[centre]+" passes="+(after.FxaaPasses-before.FxaaPasses));
            Check(after.FxaaPasses-before.FxaaPasses==2&&Near(final[centre],4,.05f),"FXAA runs on the FP16 graded image without clamping it");
            var settings=post.Settings;settings.Data.ChromaticFactor=.5f;post.Settings=settings;
            backend.ForceHdrDisplay(false);before=backend.GetPostprocessStats();Frames(2);after=backend.GetPostprocessStats();
            Console.WriteLine("HDR stock chromatic dispatches="+(after.ChromaticDispatches-before.ChromaticDispatches));backend.ForceHdrDisplay(true);
            before=backend.GetPostprocessStats();Frames(2);after=backend.GetPostprocessStats();final=backend.ReadbackGradedOutput(true,size,size);
            ulong chromatic=after.ChromaticDispatches-before.ChromaticDispatches;Console.WriteLine("HDR chromatic dispatches="+chromatic+" centre="+final[centre]);
            Check(chromatic==2&&final[centre]>1.5f,"chromatic aberration runs on the FP16 graded image");
            settings.Data.ChromaticFactor=0;post.Settings=settings;
            backend.ForceHdrDisplay(false);Frames(3);
            Check((service&32)==0&&backend.ReadbackFxaaInput(false,size,size).SequenceEqual(baseline),"losing the HDR display restores the exact stock tonemap");
            Set(grade,"Enabled",false);Frames(2);Check(!Tenant()&&(service&16)==0,"disabling the program releases display bit 16");
            Check(backend.GetStats().ValidationEnabled!=0&&backend.GetStats().ValidationErrors==0,"HDR display tenancy raises zero core/synchronization errors");
        }finally{backend.ForceHdrDisplay(false);Set(grade,"Enabled",false);Set(writer,"Enabled",false);Call("Backend.GuestColor","SyncDisplay");}
        return checks;
    }
}
