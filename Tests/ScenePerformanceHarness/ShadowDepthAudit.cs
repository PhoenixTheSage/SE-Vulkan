using System.Runtime.InteropServices;
using System.Text.Json;
using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Interop;
using HarmonyLib;
using VRageRender;
namespace ScenePerformanceHarness;

public sealed partial class Plugin {
    static string shadowDepthDirectory;
    static void CaptureShadowDepth(object renderer){
        if(shadowDepthDirectory==null)return;Directory.CreateDirectory(shadowDepthDirectory);
        try{
            if(renderer is MyVulkanRender){
                object shadows=Member(renderer,"shadows");var settings=(VRageRender.MyShadowsSettings)Member(shadows,"Settings");var native=(NativeBackend)BackendField.GetValue(renderer);
                int resolution=Convert.ToInt32(Member(shadows,"lastResolution"));
                for(uint c=0;c<settings.Data.CascadesCount;c++){float[] depth=native.ReadbackShadow(c,resolution);byte[] bytes=new byte[depth.Length*4];System.Buffer.BlockCopy(depth,0,bytes,0,bytes.Length);File.WriteAllBytes(Path.Combine(shadowDepthDirectory,$"cascade-{c}.f32"),bytes);}
                File.WriteAllText(Path.Combine(shadowDepthDirectory,"manifest.json"),JsonSerializer.Serialize(new{Backend="Vulkan",Resolution=resolution,Count=settings.Data.CascadesCount,WorldMatrices=AuditValue(Member(shadows,"worldMatrices")),UpdateMask=Member(shadows,"UpdateMask"),ValidMask=Member(shadows,"ValidMask"),Scope="Paused explicit completed D32 readback. Diagnostic GPU wait; never ordinary benchmark."},new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));
            }else{
                object manager=AccessTools.Field(AccessTools.TypeByName("VRage.Render11.Common.MyManagers"),"Shadows").GetValue(null);object cascades=Member(manager,"ShadowCascades"),array=Member(cascades,"CascadeShadowmapArray");
                // Keep this shared harness free of DX11 assembly references: resolve the
                // installed objects only after the DX11-only capture has been requested.
                dynamic source=Member(array,"Resource");dynamic device=source.Device;dynamic context=device.ImmediateContext;
                dynamic description=source.Description;
                description.Usage=(dynamic)Enum.Parse(((object)description.Usage).GetType(),"Staging");
                description.BindFlags=(dynamic)Enum.Parse(((object)description.BindFlags).GetType(),"None");
                description.CpuAccessFlags=(dynamic)Enum.Parse(((object)description.CpuAccessFlags).GetType(),"Read");
                description.OptionFlags=(dynamic)Enum.Parse(((object)description.OptionFlags).GetType(),"None");
                dynamic staging=Activator.CreateInstance(((object)source).GetType(),new object[]{device,description});
                using((IDisposable)staging){
                    context.CopyResource(source,staging);
                    dynamic mode=Enum.Parse(AccessTools.TypeByName("SharpDX.Direct3D11.MapMode"),"Read");
                    dynamic flags=Enum.Parse(AccessTools.TypeByName("SharpDX.Direct3D11.MapFlags"),"None");
                    for(int c=0;c<(int)description.ArraySize;c++){
                        int subresource=c*(int)description.MipLevels;dynamic mapped=context.MapSubresource(staging,subresource,mode,flags);
                        try{int width=description.Width,height=description.Height;byte[] bytes=new byte[width*height*4];for(int y=0;y<height;y++)Marshal.Copy(IntPtr.Add((IntPtr)mapped.DataPointer,y*(int)mapped.RowPitch),bytes,y*width*4,width*4);File.WriteAllBytes(Path.Combine(shadowDepthDirectory,$"cascade-{c}.f32"),bytes);}finally{context.UnmapSubresource(staging,subresource);}
                    }
                }
                File.WriteAllText(Path.Combine(shadowDepthDirectory,"manifest.json"),JsonSerializer.Serialize(new{Backend="Dx11",Resolution=(int)description.Width,Count=(int)description.ArraySize,States=AuditValue(Member(cascades,"m_cascadeStates")),Scope="Paused installed stock depth-array readback after Present, paired with retained cascade state. Diagnostic GPU wait; never ordinary benchmark."},new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));
            }
        }catch(Exception error){File.WriteAllText(Path.Combine(shadowDepthDirectory,"failure.txt"),error.ToString());}
    }
}
