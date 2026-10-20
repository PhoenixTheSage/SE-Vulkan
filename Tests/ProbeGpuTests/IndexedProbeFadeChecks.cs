using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Shaders;
using VRageMath;

partial class Program
{
    static unsafe void IndexedProbeFadeChecks(NativeBackend backend,VulkanShaderLibrary library,string shaderDirectory,string output,Func<uint,float,float,byte[]> capture,Func<byte[]> render,Action<float> threshold,byte[] generic,byte[] cube,byte[] deformed,byte[] empty,Action<bool,string> check)
    {
        check(backend.GpuSceneEnabled,"indexed OLD forward fade runs with the required GPU feature enabled");
        var cases=new List<(uint Type,float Threshold,float Scale,byte[] Expected)>();
        foreach(uint type in new uint[]{2,3,4}){
            var values=type==2?new[]{(10000f,1f),(1f,1f),((float)Math.Sqrt(11)/(1+.23f/10),1f),((float)Math.Sqrt(11)/(1+.53f/10),1f),((float)Math.Sqrt(11)/(1+.83f/10),1f)}:new[]{(1f,1f),(900f,350f),(10000f,350f)};
            foreach(var value in values){threshold(value.Item1);cases.Add((type,value.Item1,value.Item2,capture(2560+type,0,value.Item2)));}
        }
        // Use the production admission path; scene-enabled backends may have
        // already registered these immutable pipelines during world setup.
        backend.ConfigureGpuScene(library,shaderDirectory);
        foreach(uint type in new uint[]{2,3,4})foreach(uint role in new uint[]{0,32,64})
            check(backend.HasGpuShader(true,2560+type+role),"indexed OLD DF shader class is admitted: "+(2560+type+role));
        var referenceActor=new NativeGpuOpaqueActor{Owner=901,Generation=1,Flags=35,Scale=1,SourceCount=1};referenceActor.Basis[0]=referenceActor.Basis[4]=referenceActor.Basis[8]=1;
        var referencePart=new NativeGpuOpaquePart{IndexCount=6,Flags=2,Generation=1,Material=10};referencePart.Textures[0]=10;referencePart.Textures[1]=11;referencePart.Textures[2]=12;
        var referenceTextures=new ulong[13];referenceTextures[10]=10;referenceTextures[11]=11;referenceTextures[12]=12;
        bool numericalRejected=false;
        try{backend.InitializeGpuOpaqueReference(new[]{referenceActor},new[]{new NativeGpuOpaqueFamily{Generation=1,PartCount=1}},new[]{referencePart},15,new float[]{-1,-1,-3,1,-1,-3,1,1,-3,-1,1,-3},new uint[]{0,2,1,0,3,2},deformed,referenceTextures,30,4);}
        catch(InvalidOperationException error)when(error.Message.Contains("opaque cube is deformed",StringComparison.Ordinal)){numericalRejected=true;}
        check(numericalRejected,"standalone numerical reference retains explicit deformed-source rejection");
        uint epoch=1000;
        foreach(uint type in new uint[]{2,3,4}){
            var actor=new NativeGpuOpaqueActor{Owner=900,Generation=1,Flags=3|(type>=3?32u:0),Scale=1,SourceCount=1};
            actor.Minimum[0]=actor.Minimum[1]=actor.Minimum[2]=-100000;actor.Maximum[0]=actor.Maximum[1]=actor.Maximum[2]=100000;
            actor.Basis[0]=actor.Basis[4]=actor.Basis[8]=1;for(int i=0;i<4;i++)actor.Colour[i]=1;
            var defaults=WorldConstants.Object(backend.WorldLayouts[2560+type],MatrixD.Identity,Vector3.One,Vector3.Zero,1,0);
            ulong model=backend.RegisterGpuModel(new[]{new NativePassPart{Mesh=2,First=0,Count=6,Material=10,Shader=2560+type,Color=10,Normal=11,Extensions=12}},true);
            ulong family=backend.RegisterGpuFamily(Array.Empty<float>(),new[]{model});
            ulong source=backend.RegisterGpuSource(type==2?generic:type==3?cube:deformed,type>=3);
            var input=new NativeGpuSceneActor{Input=actor,Family=family,Source=source,SourceCount=1};for(int i=0;i<128;i++)input.Object[i]=defaults[i];
            input.Handle=backend.RegisterGpuActor(ref input,Array.Empty<NativeGpuOpaqueAppearance>());
            backend.PublishGpuScene(15,0,0);
            var views=new NativeGpuOpaqueView[15];for(int i=0;i<15;i++)views[i].Id=(uint)i;for(int face=0;face<6;face++)views[9+face].Flags=1|4|32;
            void Queue(){var frame=new NativeGpuOpaqueFrame{Epoch=epoch++,Coefficient=1,DistanceMult=1,Flags=3,SmoothCameraDistance=100,MaximumSeconds=1,MinimumSeconds=.25f};backend.QueueGpuSceneFrame(ref frame,views);}
            foreach(var value in cases.Where(x=>x.Type==type)){
                threshold(value.Threshold);actor.Scale=value.Scale;actor.Basis[0]=actor.Basis[4]=actor.Basis[8]=value.Scale;
                input.Input=actor;backend.UpdateGpuActors(new[]{input},Array.Empty<NativeGpuOpaqueAppearance>());
                for(int slot=0;slot<2;slot++){
                    Queue();render();var actual=backend.ReadbackProbe(0,5,0,32);
                    if(!actual.SequenceEqual(value.Expected)){
                        string stem=Path.Combine(output,"indexed-fade-"+type+"-"+value.Threshold+"-"+value.Scale+"-"+slot);
                        File.WriteAllBytes(stem+"-actual.rgba16f",actual);File.WriteAllBytes(stem+"-expected.rgba16f",value.Expected);File.WriteAllBytes(stem+"-empty.rgba16f",empty);
                        Console.WriteLine("INDEXED FADE diagnostic actualEmpty="+actual.SequenceEqual(empty)+" expectedEmpty="+value.Expected.SequenceEqual(empty)+" changedBytes="+actual.Zip(value.Expected,(a,b)=>a!=b).Count(v=>v));
                    }
                    check(actual.SequenceEqual(value.Expected),"indexed OLD DF forward matches actual fallback radiance: type="+type+" threshold="+value.Threshold+" scale="+value.Scale+" slot="+slot);
                }
            }
            actor.Flags&=~1u;input.Input=actor;backend.UpdateGpuActors(new[]{input},Array.Empty<NativeGpuOpaqueAppearance>());Queue();render();
            check(backend.ReadbackProbe(0,5,0,32).SequenceEqual(empty),"removing indexed OLD DF actor clears captured geometry: "+type);
            backend.ClearGpuScene();
        }
    }
}
