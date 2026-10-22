using System.Reflection;
using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Interop;
partial class Program
{
    static int OldParticleHostRegression(Assembly core,string output,NativeBackend backend)
    {
        const BindingFlags stat=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;const string owner="fixture.particle.oldhost";
        int checks=0;void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        object Call(string type,string method,params object[] args)=>core.GetType("ClientPlugin."+type,true).GetMethod(method,stat).Invoke(null,args);
        var type=core.GetType("ClientPlugin.Backend.GuestPostprocessStages",true);type.GetField("PreparationBound",stat).SetValue(null,true);
        Check((GuestBridge.SupportedPacketFlags&134217728)!=0&&(GuestBridge.SupportedPacketFlags&268435456)==0,"real older native host supports bit27 but lacks particle bit28");
        try{foreach(bool defines in new[]{false,true}){
            string root=Path.Combine(output,"ParticleOldHost-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(root,"Overlay/GPUParticles"));File.WriteAllText(Path.Combine(root,"anomaly.json"),"{\"id\":\""+owner+"\""+(defines?",\"defines\":[\"FIXTURE_PARTICLE_GLOBAL\"]":"")+"}");if(!defines)File.WriteAllText(Path.Combine(root,"Overlay/GPUParticles/Render.hlsl"),"Deliberately invalid before compilation");
            Call("Backend.GuestPassList","RegisterRoot",owner,root,null);Call("Backend.RenderHost","PrepareShaderGenerations");string error=(string)type.GetProperty("Error",stat).GetValue(null);Check(error?.Contains("bit28")==true,"older host actionably refuses "+(defines?"global defines":"particle overlays")+" before compilation: "+error);
            Check(new uint[]{13,256,1280}.All(id=>{var s=backend.GetPostprocessGeneration(id);return s.Active==0&&s.Prepared==0&&s.Pending==0;}),"particle refusal leaves stock generations without candidate allocation");Call("Backend.GuestPassList","UnregisterRoot",owner);
        }try{backend.ReadbackParticleBuffer(0,0,64);throw new Exception("old host readback accepted");}catch(NotSupportedException error){Check(error.Message.Contains("bit28"),"older host refuses checked particle readback before symbol lookup");}Check(backend.GetStats().ValidationErrors==0,"older particle host refusal has zero validation errors");}finally{Call("Backend.GuestPassList","UnregisterRoot",owner);}return checks;
    }
}
