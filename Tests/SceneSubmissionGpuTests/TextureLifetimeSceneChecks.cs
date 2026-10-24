using System.Collections;
using System.Reflection;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using VRage.Utils;
using VRageRender.Messages;

partial class Program
{
    // Live crash: an LCD's generated texture was destroyed while a retained (prepared) submission still named its
    // handle, and native QueueWorld refused "world draw references missing material texture". Handles now stay
    // valid for the session: destroyed generated textures keep a placeholder that re-creation reuses, and unloaded
    // file textures reload into their handle.
    static void TextureLifetimeSceneChecks(NativeBackend backend,VulkanScene scene,Func<byte[]> retained,Action<bool,string> check)
    {
        var actors=(IDictionary)typeof(VulkanScene).GetField("actors",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(scene)!;
        var armor=actors[10u]!;var model=armor.GetType().GetField("Model")!.GetValue(armor)!;
        var part=((IEnumerable)model.GetType().GetField("Parts")!.GetValue(model)!).Cast<object>().First();
        string material=(string)part.GetType().GetField("MaterialName")!.GetValue(part)!,color=(string)part.GetType().GetField("ColorTexture")!.GetValue(part)!;
        var cache=scene.TextureCache;
        byte[] Solid(byte r,byte g,byte b)=>Enumerable.Range(0,16).SelectMany(_=>new[]{r,g,b,(byte)255}).ToArray();
        void Retexture(string file)=>scene.Process(new MyRenderMessageChangeMaterialTexture{RenderObjectID=10,
            Changes=new Dictionary<MyStringId,MyTextureChange>{{MyStringId.GetOrCompute(material),new MyTextureChange{ColorMetalFileName=file}}}});
        int at=(32*64+32)*4;
        ulong lcd=cache.PutGenerated("fixture.lcd",4,4,Solid(255,0,0));Retexture("fixture.lcd");
        retained();var red=retained();
        check(red[at]>red[at+1]+20,"a block material retextured to a generated LCD texture renders it through the retained submission");
        cache.RemoveGenerated("fixture.lcd");
        string failure=null;byte[] gone=null;try{gone=retained();retained();}catch(Exception e){failure=e.Message;}
        check(failure==null&&gone!=null,"destroying the generated texture leaves retained submissions drawable (was: "+(failure??"none")+")");
        ulong again=cache.PutGenerated("fixture.lcd",4,4,Solid(255,0,0));
        check(again==lcd,"re-creating a destroyed generated texture reuses its handle");
        retained();check(retained().SequenceEqual(red),"the re-created texture reaches the cached submission without a rebuild");
        cache.RemoveGenerated("fixture.lcd");
        Retexture(color);retained();var stock=retained();
        long generation=cache.Generation;cache.Unload(color);
        failure=null;try{retained();}catch(Exception e){failure=e.Message;}
        check(failure==null&&cache.Generation>generation,"unloading a file texture a retained submission uses keeps it drawable and advances the cache generation");
        retained();check(retained().SequenceEqual(stock),"the next frame reloads the unloaded texture into its handle and restores the image");
        check(backend.GetStats().ValidationErrors==0,"texture lifetime changes raise zero validation errors");
    }
}
