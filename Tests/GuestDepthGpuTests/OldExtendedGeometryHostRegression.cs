using System.Reflection;
using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Interop;
partial class Program
{
    static int OldExtendedGeometryHostRegression(Assembly core,string output,NativeBackend backend)
    {
        const BindingFlags stat=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;const string owner="fixture.extended-geometry.oldhost";
        int checks=0;void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        object Call(string type,string method,params object[] args)=>core.GetType("ClientPlugin."+type,true).GetMethod(method,stat).Invoke(null,args);
        var type=core.GetType("ClientPlugin.Backend.GuestPostprocessStages",true);type.GetField("PreparationBound",stat).SetValue(null,true);
        Check((GuestBridge.SupportedPacketFlags&unchecked((int)0x80000000))!=0&&backend.NativeOwnerFeatures()==0,"real older native host supports classic bit31 but lacks checked native-cell/GPU capability extension");
        try{foreach(string kind in new[]{"overlay","injection","defines"}){
            string root=Path.Combine(output,"ExtendedGeometryOldHost-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);File.WriteAllText(Path.Combine(root,"anomaly.json"),"{\"id\":\""+owner+"\",\"exclusive\":[\"GBuffer\"]"+(kind=="defines"?",\"defines\":[\"FIXTURE_PROBE_GLOBAL\"]":"")+"}");if(kind!="defines"){string file=Path.Combine(root,kind=="overlay"?"Overlay/Geometry/Passes/GBuffer/PixelStage.hlsli":"Inject/GBuffer/Probe.hlsli");Directory.CreateDirectory(Path.GetDirectoryName(file));File.WriteAllText(file,"Deliberately invalid before compilation");}
            Call("Backend.GuestPassList","RegisterRoot",owner,root,null);Call("Backend.RenderHost","PrepareShaderGenerations");string error=(string)type.GetProperty("Error",stat).GetValue(null);Check(error?.Contains("capability extension")==true,"older host actionably refuses "+kind+" before compilation: "+error);
            Check(new uint[]{13,256,1792}.All(id=>{var s=backend.GetPostprocessGeneration(id);return s.Active==0&&s.Prepared==0&&s.Pending==0;}),"geometry refusal leaves stock generations without candidate allocation");Call("Backend.GuestPassList","UnregisterRoot",owner);
        }Check(backend.GetStats().ValidationErrors==0,"older geometry host refusal has zero validation errors");}finally{Call("Backend.GuestPassList","UnregisterRoot",owner);}return checks;
    }
}
