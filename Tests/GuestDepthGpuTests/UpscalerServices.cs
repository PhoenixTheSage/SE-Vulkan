using System.Reflection;
using System.Runtime.InteropServices;
partial class Program
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int NgxSharpnessFn(float sharpness);
    [DllImport("kernel32",CharSet=CharSet.Unicode)]static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32",CharSet=CharSet.Ansi)]static extern IntPtr GetProcAddress(IntPtr module,string name);
    // Audit P1/P2: owner-qualified display requests (union, caller attribution, owner release) and DLSS sharpness.
    static int UpscalerServices(Assembly core)
    {
        const BindingFlags stat=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;int checks=0;
        void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        var display=core.GetType("ClientPlugin.Shaders.DisplayServices",true);
        object Call(string name,params object[] args)=>display.GetMethod(name,stat,null,args.Select(a=>a.GetType()).ToArray(),null).Invoke(null,args);
        int Owner(string owner)=>(int)Call("OwnerFlags",owner);int Union()=>(int)display.GetProperty("Flags",stat).GetValue(null);
        string self=typeof(Program).Assembly.GetName().Name;
        Check(display.GetMethod("Request")!=null&&display.GetMethod("Release")!=null,"plugins' GetMethod(\"Request\"/\"Release\") lookups stay unambiguous");
        try{
            // Plugins call through reflection; the request belongs to the calling assembly, not Anomaly or the runtime.
            display.GetMethod("Request").Invoke(null,new object[]{4});
            Check(Owner(self)==4&&Union()==4,"reflected Request(4) is attributed to the calling plugin assembly ("+self+")");
            Call("RequestFor","fixture.fsr",4);Call("RequestFor","fixture.fg",2);
            display.GetMethod("Release").Invoke(null,new object[]{4});
            Check(Owner(self)==0&&Owner("fixture.fsr")==4&&Union()==6,"a plugin releasing bit 4 keeps another owner's bit 4 (union 6)");
            Call("ReleaseFor","fixture.fsr",4);Check(Union()==2,"releasing the last holder clears the bit");
            Call("ReleaseOwner","fixture.fg");Check(Union()==0&&(string)display.GetProperty("Status",stat).GetValue(null)=="display=none","ReleaseOwner drops every bit the owner held");
        }finally{Call("ReleaseOwner",self);Call("ReleaseOwner","fixture.fsr");Call("ReleaseOwner","fixture.fg");}
        var export=GetProcAddress(GetModuleHandle("se_vulkan_native.dll"),"sevk_ngx_set_sharpness");
        Check(export!=IntPtr.Zero,"renderer exports sevk_ngx_set_sharpness");
        var sharpness=Marshal.GetDelegateForFunctionPointer<NgxSharpnessFn>(export);
        Check(sharpness(.35f)==0&&sharpness(0f)==0&&sharpness(1.5f)!=0&&sharpness(float.NaN)!=0,"DLSS sharpness accepts [0,1] and rejects out-of-range values");
        return checks;
    }
}
