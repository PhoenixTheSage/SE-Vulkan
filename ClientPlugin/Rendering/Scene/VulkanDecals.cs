using System;
using System.Collections.Generic;
using System.Linq;
using ClientPlugin.Rendering.Interop;
using VRage;
using VRage.Render.Scene;
using VRageMath;
using VRageRender;
using VRageRender.Messages;
namespace ClientPlugin.Rendering.Scene;
internal sealed class VulkanDecals
{
    private sealed class Decal {public uint Id;public uint[] Parents;public MyDecalTopoData Topo;public MyDecalFlags Flags;public string Material;public int Index,Expiry;public bool Trail;public float Distance;public double Fade=double.PositiveInfinity,LastSeen;}
    private readonly Dictionary<uint,Decal> decals=new Dictionary<uint,Decal>();private readonly Dictionary<string,MyDecalMaterialDesc[]> materials=new Dictionary<string,MyDecalMaterialDesc[]>(StringComparer.Ordinal);
    private readonly NativeBackend backend;private readonly VulkanScene scene;private MyDecalGlobals globals;private double time;
    public int Count=>decals.Count;public long Submitted {get;private set;}
    public VulkanDecals(NativeBackend backend,VulkanScene scene){this.backend=backend;this.scene=scene;}
    public bool Process(MyRenderMessageBase message)
    {
        if(message is MyRenderMessageRegisterScreenDecalsMaterials m){foreach(var p in m.MaterialDescriptions)materials[p.Key]=p.Value.ToArray();return true;}
        if(message is MyRenderMessageSetDecalGlobals g){globals=g.Globals;globals.DecalQueueSize=(int[])g.Globals.DecalQueueSize?.Clone();globals.DecalTrailsQueueSize=(int[])g.Globals.DecalTrailsQueueSize?.Clone();return true;}
        if(message is MyRenderMessageCreateScreenDecal c){if(c.ParentIDs==null||c.ParentIDs.Length==0)return true;if(decals.Count>=32768&&!decals.ContainsKey(c.ID))throw new InvalidOperationException("Copied decal residency exceeds its 32768-entry budget.");decals[c.ID]=new Decal{Id=c.ID,Parents=(uint[])c.ParentIDs.Clone(),Topo=c.TopoData,Flags=c.Flags,Material=c.Material,Index=c.MaterialIndex,Expiry=c.TimeUntilLive<0?int.MaxValue:c.TimeUntilLive,Trail=c.IsTrail,Distance=c.RenderSqDistance>0?c.RenderSqDistance:10000,LastSeen=time};return true;}
        if(message is MyRenderMessageUpdateScreenDecal u){foreach(var p in u.Decals)if(decals.TryGetValue(p.ID,out var d)){d.Topo.MatrixCurrent=p.Transform;d.Topo.WorldPosition=p.Position;}return true;}
        if(message is MyRenderMessageRemoveDecal r){if(r.Immediately)decals.Remove(r.ID);else if(decals.TryGetValue(r.ID,out var d))d.Fade=Math.Min(d.Fade,time+6);return true;}
        if(message is MyRenderMessageClearScreenDecals){Clear();return true;}return false;
    }
    public void Observe(MyRenderMessageBase message)
    {
        if(message is MyRenderMessageRemoveRenderObject r){foreach(uint id in decals.Values.Where(d=>d.Parents.Contains(r.ID)).Select(d=>d.Id).ToArray())decals.Remove(id);}
        if(message is MyRenderMessageSetCharacterTransforms t){foreach(var b in t.BoneDecalUpdates)if(decals.TryGetValue(b.DecalID,out var d)){Matrix skin=default;for(int i=0;i<4;++i){float w=d.Topo.BoneWeights[i];if(w==0)break;skin+=scene.GetDecalBone(t.CharacterID,d.Topo.BoneIndices[i],t.BoneAbsoluteTransforms)*w;}d.Topo.MatrixCurrent=d.Topo.MatrixBinding*skin;}}
        if(message is MyRenderMessageUpdateRenderCubeInstanceBuffer c){foreach(var update in c.DecalsData)if(update.InstanceIndex>=0&&update.InstanceIndex<c.InstanceData.Count&&decals.TryGetValue(update.DecalId,out var d)){var cube=c.InstanceData[update.InstanceIndex];if(!cube.EnableSkinning)continue;Matrix deform=cube.ConstructDeformedCubeInstanceMatrix(ref d.Topo.BoneIndices,ref d.Topo.BoneWeights,out Matrix local);d.Topo.MatrixCurrent=d.Topo.MatrixBinding*Matrix.Invert(local)*deform;}}
    }
    public void Draw(Vector3D camera,double now,long gameplayFrame,int quality)
    {
        time=now;int q=Math.Max(0,Math.Min(3,quality));for(int trail=0;trail<2;++trail){int[] limits=trail==0?globals.DecalQueueSize:globals.DecalTrailsQueueSize;int limit=limits!=null&&limits.Length>q?limits[q]:trail==0?1024:2048;var eligible=decals.Values.Where(d=>d.Trail==(trail!=0)&&!d.Flags.HasFlag(MyDecalFlags.IgnoreRenderLimits)&&double.IsPositiveInfinity(d.Fade)).ToArray();for(int i=0;i<eligible.Length-limit;++i)eligible[i].Fade=time+6;}
        foreach(var d in decals.Values.ToArray())
        {
            if(d.Expiry<=gameplayFrame)d.Fade=Math.Min(d.Fade,time+6);if(d.Fade<=time){decals.Remove(d.Id);continue;}
            if(d.Flags.HasFlag(MyDecalFlags.Closed))continue;
            MatrixD world=d.Topo.MatrixCurrent;if(d.Flags.HasFlag(MyDecalFlags.World))world.Translation=d.Topo.WorldPosition;else{var parent=MyIDTracker<MyActor>.FindByID(d.Parents[0]);if(parent==null){decals.Remove(d.Id);continue;}if(!VulkanScene.IsVisible(parent)){if(!d.Flags.HasFlag(MyDecalFlags.IgnoreOffScreenDeletion)&&time-d.LastSeen>40)decals.Remove(d.Id);continue;}world*=parent.WorldMatrix;}
            double distance=Vector3D.DistanceSquared(world.Translation,camera);float factor=q==0?.11111f:q==1?.44444f:1;
            if(distance>d.Distance*factor||backend.DecalPackets.Count>=16384){if(!d.Flags.HasFlag(MyDecalFlags.IgnoreOffScreenDeletion)&&time-d.LastSeen>40)decals.Remove(d.Id);continue;}d.LastSeen=time;
            if(!materials.TryGetValue(d.Material??string.Empty,out var list)||d.Index<0||d.Index>=list.Length)continue;var material=list[d.Index];uint role=!string.IsNullOrEmpty(material.ColorMetalTexture)?1u:0u;if(!string.IsNullOrEmpty(material.NormalmapTexture))role|=2;if(!string.IsNullOrEmpty(material.ExtensionsTexture)&&role==3)role|=4;if(d.Flags.HasFlag(MyDecalFlags.Transparent))role=8;if(role==0)continue;
            world.Translation-=camera;var data=new byte[128];void F(int offset,float[] values)=>Buffer.BlockCopy(values,0,data,offset,values.Length*4);
            F(0,new[]{(float)world.M11,(float)world.M21,(float)world.M31,(float)world.M41,(float)world.M12,(float)world.M22,(float)world.M32,(float)world.M42,(float)world.M13,(float)world.M23,(float)world.M33,(float)world.M43});
            float fade=(float)Math.Min(1,(d.Fade-time)/6)*(1-MathHelper.Clamp((float)(distance/(d.Distance*factor*.9)),0,1));F(48,new[]{fade});F(64,NativeBackend.MatrixValues(MatrixD.Invert(world)));
            backend.DecalPackets.Add(new NativeDecalDraw{Size=176,Role=role,Constants=data,Alpha=scene.TextureCache.GetTexture(material.AlphamaskTexture,false),Color=scene.TextureCache.GetTexture(material.ColorMetalTexture),Normal=scene.TextureCache.GetTexture(material.NormalmapTexture,false),Extensions=scene.TextureCache.GetTexture(material.ExtensionsTexture,false)});Submitted++;
        }
    }
    public void Clear(){decals.Clear();backend.DecalPackets.Clear();}
}
