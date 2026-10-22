using System.Reflection;
using ClientPlugin.Rendering.Interop;

partial class Program
{
    static int OldBindingHostRegression(Assembly core,string output,NativeBackend backend)
    {
        const BindingFlags stat=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        int checks=0;void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
        object Call(string type,string method,params object[] args)=>core.GetType("ClientPlugin."+type,true).GetMethod(method,stat).Invoke(null,args);
        core.GetType("ClientPlugin.Backend.GuestPostprocessStages",true).GetField("PreparationBound",stat).SetValue(null,true);
        var type=core.GetType("ClientPlugin.Backend.GuestPostprocessStages",true);
        string root=Path.Combine(output,"OldBindingPack"),file=Path.Combine(root,"Overlay/Lighting.Dir/LightDir.hlsl");
        Directory.CreateDirectory(Path.GetDirectoryName(file));File.WriteAllText(Path.Combine(root,"anomaly.json"),"{\"id\":\"fixture.old.bindings\"}");
        Check((ClientPlugin.Rendering.GuestBridge.SupportedPacketFlags&4194304)==0&&(ClientPlugin.Rendering.GuestBridge.SupportedPacketFlags&1048576)!=0,"older native host supports lighting but lacks bit22 resource transport");
        try{
            foreach(string source in new[]{
                "cbuffer Extras:register(b6){float4 Value;}float4 __pixel_shader():SV_Target{return Value;}",
                "Texture2D<float4> Image:register(t6);float4 __pixel_shader():SV_Target{return Image.Load(int3(0,0,0));}"}){
                File.WriteAllText(file,source);Call("Backend.GuestPassList","RegisterRoot","fixture.old.bindings",root,null);
                Call("Backend.RenderHost","PrepareShaderGenerations");
                string error=(string)type.GetProperty("Error",stat).GetValue(null);
                Check(error!=null&&error.Contains("bit22"),"older host refuses catalog/extras with an actionable coordinated-update error");
                var state=backend.GetPostprocessGeneration(256);
                Check(state.Active==0&&state.Prepared==0&&state.Pending==0,"refused resource generation retains stock with no native candidate allocation");
            }
            Check(backend.GetStats().ValidationErrors==0,"older-host resource refusal reports zero validation errors");
        }
        finally{Call("Backend.GuestPassList","UnregisterRoot","fixture.old.bindings");}
        return checks;
    }
}
