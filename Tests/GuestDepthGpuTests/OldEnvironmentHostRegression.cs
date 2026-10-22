using System.Reflection;
using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Interop;

partial class Program
{
    static int OldEnvironmentHostRegression(Assembly core,string output,NativeBackend backend)
    {
        const BindingFlags stat=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        int checks=0;void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        object Call(string type,string method,params object[] args)=>core.GetType("ClientPlugin."+type,true).GetMethod(method,stat).Invoke(null,args);
        var type=core.GetType("ClientPlugin.Backend.GuestPostprocessStages",true);type.GetField("PreparationBound",stat).SetValue(null,true);
        Check((GuestBridge.SupportedPacketFlags&8388608)!=0&&(GuestBridge.SupportedPacketFlags&50331648)==0,"real older native host has bit23 but lacks environment owner bits24/25");
        const string owner="fixture.environment.oldhost";
        try {
            foreach(var item in new[]{("Overlay/Atmosphere/AtmosphereGBuffer.hlsl","bit24"),("Overlay/Foliage/Foliage.hlsl","bit25"),("Overlay/Decals/Decals.hlsl","bit26"),("defines","bit24")}){
                string root=Path.Combine(output,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
                File.WriteAllText(Path.Combine(root,"anomaly.json"),"{\"id\":\""+owner+"\""+(item.Item1=="defines"?",\"defines\":[\"FIXTURE_ENVIRONMENT\"]":"")+"}");
                if(item.Item1!="defines"){string file=Path.Combine(root,item.Item1);Directory.CreateDirectory(Path.GetDirectoryName(file));File.WriteAllText(file,"Deliberately invalid: capability refusal must precede compilation");}
                Call("Backend.GuestPassList","RegisterRoot",owner,root,null);Call("Backend.RenderHost","PrepareShaderGenerations");
                string error=(string)type.GetProperty("Error",stat).GetValue(null);Check(error?.Contains(item.Item2)==true,"older host actionably refuses "+item.Item1+" before source compilation; error="+error);
                Check(new uint[]{13,256}.All(id=>{var s=backend.GetPostprocessGeneration(id);return s.Active==0&&s.Prepared==0&&s.Pending==0;}),"refused environment request leaves native owners at stock without candidate allocation");
                Call("Backend.GuestPassList","UnregisterRoot",owner);
            }
            Check(backend.GetStats().ValidationErrors==0,"older environment host refusal has zero validation errors");
        }finally{Call("Backend.GuestPassList","UnregisterRoot",owner);}
        return checks;
    }
}
