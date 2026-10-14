using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Content;
using ClientPlugin.Diagnostics;
using HarmonyLib;
using VRage.Render.Particles;


using VRageMath;
using VRageRender;
using VRageRender.Messages;
namespace ClientPlugin.Rendering.Scene;
// Reuse stock CPU effect curves and emitter slot allocation, while replacing its D3D texture-array upload.
internal sealed partial class VulkanParticles : IDisposable
{
    private readonly NativeBackend backend;private readonly VulkanScene scene;
    private readonly MyGPUEmitterLayoutData[] emitters=new MyGPUEmitterLayoutData[1024];
    private const ulong Atlas=0x700000001;private string[] files=Array.Empty<string>();
    private readonly Harmony hooks=new Harmony("PhoenixTheSage.SE-Vulkan.ParticleCPU");
    internal static VulkanParticles Current;
    public int Effects => (int)StockParticles.Get(StockParticles.Get(StockParticles.Manager,"m_effects"),"Count");public bool Paused => (bool)StockParticles.StaticGet(StockParticles.Common,"IsPaused");public long Updates {get;private set;}
    public VulkanParticles(NativeBackend backend,VulkanScene scene,MySharedData shared)
    {
        this.backend=backend;this.scene=scene;ParticleCpuContract.CheckRuntime();Current=this;
        try {
        var texture=AccessTools.Method(StockParticles.Emitters,"UpdateTextureArray",Type.EmptyTypes);var light=AccessTools.Method(StockParticles.Light,"Update",Type.EmptyTypes);var close=AccessTools.Method(StockParticles.Light,"CloseLight",Type.EmptyTypes);
        if(close==null||close.ReturnType!=typeof(void)||texture==null||texture.ReturnType!=typeof(void)||light==null||light.ReturnType!=typeof(void)||AccessTools.Method(StockParticles.Emitters,"GetTextureArrayFileList",Type.EmptyTypes)==null)throw new MissingMethodException("Stock CPU particle bridge changed.");
        hooks.Patch(close,prefix:new HarmonyMethod(typeof(VulkanParticles),nameof(CloseLightPrefix)));hooks.Patch(texture,prefix:new HarmonyMethod(typeof(VulkanParticles),nameof(SkipTextureUpload)));hooks.Patch(light,prefix:new HarmonyMethod(typeof(VulkanParticles),nameof(UpdateLightPrefix)));
        StockParticles.StaticSet(StockParticles.Render,"SharedData",shared);StockParticles.StaticSet(StockParticles.Render,"Settings",MyRenderSettings.Default);StockParticles.StaticSet(StockParticles.Render,"CalculateGravityInPoint",new Func<VRageMath.Vector3D,VRageMath.Vector3>(p=>scene.CalculateGravityInPoint(p)));StockParticles.StaticCall(StockParticles.Emitters,"Init");
        VulkanDiagnostics.Set("Particle CPU bridge",SubsystemState.Ready,"Stock effect/emitter curves retained; D3D atlas upload replaced by Vulkan array upload. Installed IL fingerprints, runtime signatures and competing hook owners checked.");
        }catch{Current=null;hooks.UnpatchAll(hooks.Id);throw;}
    }
    private static bool CloseLightPrefix(object __instance){if(Current==null)return true;Current.CloseParticleLight(new StockLight(__instance));return false;}
    private static bool SkipTextureUpload()=>Current==null;
    private static bool UpdateLightPrefix(object __instance){if(Current==null)return true;Current.UpdateStockLight(new StockLight(__instance));return false;}
    public bool Process(MyRenderMessageBase message)
    {
        if(message is MyRenderMessageCreateParticleEffect c){StockParticles.Call(StockParticles.Manager,"Create",c.ID,c.Data,c.DebugName);return true;}
        if(message is MyRenderMessageUpdateParticleEffect u){var state=u.State;UpdateState(state);return true;}
        if(message is MyRenderMessageAddToParticleTextureArray a){StockParticles.StaticCall(StockParticles.Emitters,"AddToParticleTextureArray",new HashSet<string>(a.Files));return true;}
        if(message is MyRenderMessagePauseTimer p){StockParticles.StaticSet(StockParticles.Common,"IsPaused",p.pause);return true;}return false;
    }
    public void Draw(Vector3D camera,float delta,MyRenderSettings settings)
    {
        StockParticles.StaticSet(StockParticles.Render,"Settings",settings);StockParticles.StaticCall(StockParticles.Emitters,"UpdateParticleSettings");var matrices=StockParticles.Get(StockParticles.StaticGet(StockParticles.Render,"Environment"),"Matrices");StockParticles.Set(matrices,"CameraPosition",camera);
        StockParticles.StaticCall(StockParticles.Common,"SetConstFrameTimeDelta",delta>0?Math.Min(delta,.066f):-1);StockParticles.StaticCall(StockParticles.Common,"UpdateTimers");StockParticles.Call(StockParticles.Manager,"Update");StockParticles.Call(StockParticles.Manager,"Draw");
        var gather=new object[]{emitters,null,null};int count=(int)StockParticles.StaticCall(StockParticles.Emitters,"Gather",gather);string[] next=(string[])StockParticles.StaticCall(StockParticles.Emitters,"GetTextureArrayFileList");
        if(next.Length>256)throw new InvalidOperationException("Stock particle atlas exceeds its 8-bit texture index.");
        if(next.Length>0&&!files.SequenceEqual(next)){backend.BuildVoxelArray(Atlas,1024,true,next.Select(f=>scene.TextureCache.GetTexture(f)).ToArray());files=next;}
        backend.QueueParticles(emitters,count,next.Length>0?Atlas:0);Updates++;
    }
    private void UpdateState(MyParticleEffectState state){var effect=StockParticles.Call(StockParticles.Manager,"Get",state.ID);if(effect!=null)StockParticles.Call(effect,"UpdateState",state);}
    public void Clear(){StockParticles.Call(StockParticles.Manager,"UnloadData");StockParticles.StaticCall(StockParticles.Emitters,"OnSessionEnd");StockParticles.Call(StockParticles.StaticGet(StockParticles.Emitters,"m_textureArrayIndices"),"Clear");Array.Clear(emitters,0,emitters.Length);backend.QueueParticles(null,0,0,true);if(files.Length>0)backend.RemoveResource(5,Atlas);files=Array.Empty<string>();}
    public void Dispose(){Clear();Current=null;hooks.UnpatchAll(hooks.Id);}
}
