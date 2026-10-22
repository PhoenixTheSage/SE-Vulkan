using System.Reflection;
using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Interop;
partial class Program
{
    static int OldOitHostRegression(Assembly core,string output,NativeBackend backend)
    {
        const BindingFlags stat=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;const string owner="fixture.oit.oldhost";
        int checks=0;void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        object Call(string type,string method,params object[] args)=>core.GetType("ClientPlugin."+type,true).GetMethod(method,stat).Invoke(null,args);
        var type=core.GetType("ClientPlugin.Backend.GuestPostprocessStages",true);type.GetField("PreparationBound",stat).SetValue(null,true);
        Check((GuestBridge.SupportedPacketFlags&536870912)!=0&&(GuestBridge.SupportedPacketFlags&1073741824)==0,"real older native host supports bit29 but lacks OIT bit30");
        try{foreach(bool defines in new[]{false,true}){
            string root=Path.Combine(output,"OitOldHost-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(root,"Overlay/Transparent/OIT"));File.WriteAllText(Path.Combine(root,"anomaly.json"),"{\"id\":\""+owner+"\""+(defines?",\"defines\":[\"FIXTURE_PROBE_GLOBAL\"]":"")+"}");if(!defines)File.WriteAllText(Path.Combine(root,"Overlay/Transparent/OIT/Resolve.hlsl"),"Deliberately invalid before compilation");
            Call("Backend.GuestPassList","RegisterRoot",owner,root,null);Call("Backend.RenderHost","PrepareShaderGenerations");string error=(string)type.GetProperty("Error",stat).GetValue(null);Check(error?.Contains("bit30")==true,"older host actionably refuses "+(defines?"global defines":"OIT overlays")+" before compilation: "+error);
            Check(new uint[]{13,256,1792}.All(id=>{var s=backend.GetPostprocessGeneration(id);return s.Active==0&&s.Prepared==0&&s.Pending==0;}),"OIT refusal leaves stock generations without candidate allocation");Call("Backend.GuestPassList","UnregisterRoot",owner);
        }Check(backend.GetStats().ValidationErrors==0,"older OIT host refusal has zero validation errors");}finally{Call("Backend.GuestPassList","UnregisterRoot",owner);}return checks;
    }
}
