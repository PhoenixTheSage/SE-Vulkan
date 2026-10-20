using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using ClientPlugin.Rendering.Shaders;
using FixtureAssets;
using HarmonyLib;
using Sandbox.Game.World;
using VRage.Render.Scene;
using VRageMath;
using VRageRender;
using VRageRender.Import;
using VRageRender.Messages;

namespace CreativeFixtureHarness;

public sealed partial class Plugin
{
    bool meshDecalMode,meshDecalOverlap,meshDecalCutout,meshRasterMode,meshOldOrder,meshInstanceColor,meshInstanceMaterialColor,meshInstanceFade;int meshDecalStage;double meshDecalWait;uint[] meshDecalIds=Array.Empty<uint>();
    readonly List<uint> meshDecalCreated=new List<uint>();
    readonly List<uint> meshInstanceBuffers=new List<uint>();
    sealed class MeshObservation {public int Actors,Old,New,Packets,Cutouts,Instances,FadePackets;public ulong Errors;public string Failure;}
    MeshObservation meshObservation;int meshPacketCount,meshOldCount,meshNewCount,meshCutoutCount,meshInstanceCount,meshFadeCount;
    readonly HashSet<ulong> meshDecalOwnedMeshes=new HashSet<ulong>();
    int meshCapture,meshInputs;
    static void ObserveMeshDecalScene(VulkanScene __instance,NativeBackend __0)
    {
        var owner=lightingOwner;if(owner==null||!owner.meshDecalMode)return;
        try{
            lightingScene=__instance;
            var packets=(List<WorldPacket>)AccessTools.Field(typeof(NativeBackend),"worldPackets").GetValue(__0);
            var actors=(IDictionary)AccessTools.Field(typeof(VulkanScene),"actors").GetValue(__instance);int old=0,newer=0,instanced=0;
            foreach(uint id in Volatile.Read(ref owner.meshDecalIds))if(actors.Contains(id)){
                var actor=actors[id];var model=FlareValue(actor,"Model");
                if(model!=null)foreach(var part in (IEnumerable)FlareValue(model,"Parts"))owner.meshDecalOwnedMeshes.Add((ulong)FlareValue(part,"Mesh"));
                if((uint)FlareValue(actor,"InstanceBuffer")!=uint.MaxValue)instanced++;
                var flags=(RenderFlags)FlareValue(actor,"Flags");if((flags&RenderFlags.ForceOldPipeline)!=0)old++;else newer++;
            }
            owner.meshInstanceCount=instanced;owner.meshFadeCount=packets.Count(p=>owner.meshDecalOwnedMeshes.Contains(p.Mesh)&&p.Pipeline>=2560&&p.Pipeline<3408);owner.meshOldCount=old;owner.meshNewCount=newer;owner.meshPacketCount=packets.Count(p=>owner.meshDecalOwnedMeshes.Contains(p.Mesh)&&WorldShaderVariants.IsBlendedDecal(p.Pipeline));owner.meshCutoutCount=packets.Count(p=>owner.meshDecalOwnedMeshes.Contains(p.Mesh)&&WorldShaderVariants.IsCutout(p.Pipeline));
        }catch(Exception e){Volatile.Write(ref owner.meshObservation,new MeshObservation{Failure=e.ToString()});}
    }
    static void ObserveMeshDecalVulkan(NativeBackend __instance)
    {
        var owner=lightingOwner;if(owner==null||!owner.meshDecalMode)return;
        var previous=Volatile.Read(ref owner.meshObservation);if(previous?.Failure!=null)return;
        try{
            Volatile.Write(ref owner.meshObservation,new MeshObservation{Actors=owner.meshOldCount+owner.meshNewCount,Old=owner.meshOldCount,New=owner.meshNewCount,Packets=owner.meshPacketCount,Cutouts=owner.meshCutoutCount,Instances=owner.meshInstanceCount,FadePackets=owner.meshFadeCount,Errors=__instance.GetStats().ValidationErrors});
            if(Interlocked.CompareExchange(ref owner.meshCapture,3,1)==1){
                string folder=Path.Combine(owner.reports,"mesh-buffers");Directory.CreateDirectory(folder);
                string[] names={"color-lod.rgba8-srgb","normal-ao.a2b10g10r10","metal-gloss-emissive.rgba8","depth.d32f"};
                for(uint i=0;i<4;i++)File.WriteAllBytes(Path.Combine(folder,names[i]),__instance.ReadbackWorldGbuffer(i,640,480));
                var hdr=__instance.ReadbackLightingStage(3,640,480);var bytes=new byte[hdr.Length*4];Buffer.BlockCopy(hdr,0,bytes,0,bytes.Length);File.WriteAllBytes(Path.Combine(folder,"hdr.rgba32f"),bytes);
                for(uint i=0;i<2;i++)File.WriteAllBytes(Path.Combine(folder,i==0?"screen-ao.r8":"sun-visibility.r8"),__instance.ReadbackLightingInput(i,640,480));
                if(lightingWorldFrame==null)throw new InvalidOperationException("Mesh fixture main frame was not observed.");File.WriteAllBytes(Path.Combine(folder,"frame.bin"),lightingWorldFrame);
                SaveLightingProbes(__instance,folder);if(lightingHbaoConstants==null)throw new InvalidOperationException("Mesh fixture HBAO constants were not observed.");File.WriteAllBytes(Path.Combine(folder,"hbao-constants.bin"),lightingHbaoConstants);
                if(owner.meshOldOrder)File.WriteAllText(Path.Combine(owner.reports,"mesh-old-resource-identities.xml"),((VulkanScene)lightingScene).StockOldIdentitySnapshot(Volatile.Read(ref owner.meshDecalIds)));
                new XDocument(new XElement("MeshBuffers",new XAttribute("Backend","Vulkan"),new XAttribute("Width",640),new XAttribute("Height",480),new XAttribute("Scope","Completed held-scene GBuffer/depth, end-frame linear HDR, lighting inputs and actual probe resources."))).Save(Path.Combine(folder,"complete.xml"));
                Volatile.Write(ref owner.meshCapture,2);Interlocked.Exchange(ref lightingPending,0);
            }
        }
        catch(Exception e){Volatile.Write(ref owner.meshObservation,new MeshObservation{Failure=e.ToString()});}
    }
    static void ObserveMeshDecalDx11()
    {
        var owner=lightingOwner;if(owner==null||!owner.meshDecalMode)return;
        if(Volatile.Read(ref owner.meshObservation)?.Failure!=null)return;
        try{
            int count=0,old=0,newer=0;
            foreach(uint id in Volatile.Read(ref owner.meshDecalIds)){
                var actor=MyIDTracker<MyActor>.FindByID(id);if(actor==null)continue;count++;
                var components=(VRage.Library.Collections.MyIndexedComponentContainer<VRage.Render.Scene.Components.MyActorComponent>)FlareValue(actor,"m_components");
                for(int i=0;i<components.Count;i++){var component=components[i];
                    if(component.GetType().Name=="MyRenderableComponent")old++;
                    if(component.GetType().Name=="MyInstanceComponent")newer++;
                }
            }
            Volatile.Write(ref owner.meshObservation,new MeshObservation{Actors=count,Old=old,New=newer});
        }catch(Exception e){Volatile.Write(ref owner.meshObservation,new MeshObservation{Failure=e.ToString()});}
    }
    static void CaptureMeshDecalDx11Inputs(object[] __args)
    {
        var owner=lightingOwner;if(owner==null||Volatile.Read(ref owner.meshCapture)!=1||Interlocked.CompareExchange(ref owner.meshInputs,1,0)!=0)return;
        try{lightingDx11Inputs.Invoke(null,new[]{__args[0],__args[1],__args[2]});owner.SaveMeshDecalOldSortKeys();}
        catch(Exception e){Volatile.Write(ref owner.meshObservation,new MeshObservation{Failure=e.ToString()});}
    }
    void SaveMeshDecalOldSortKeys()
    {
        var root=new XElement("OldDecalSortKeys",new XAttribute("Scope","Actual current OLD CullProxy keys. Installed MyRenderCullResultFlat.SortComparer compares these unsigned keys globally."));
        foreach(uint id in Volatile.Read(ref meshDecalIds)){
            var actor=MyIDTracker<MyActor>.FindByID(id);if(actor==null)continue;
            var components=(VRage.Library.Collections.MyIndexedComponentContainer<VRage.Render.Scene.Components.MyActorComponent>)FlareValue(actor,"m_components");
            for(int i=0;i<components.Count;i++)if(components[i].GetType().Name=="MyRenderableComponent"){
                var cull=FlareValue(components[i],"CullProxy");var keys=(ulong[])FlareValue(cull,"SortingKeys");var proxies=(Array)FlareValue(cull,"RenderableProxies");
                if(keys.Length!=proxies.Length)throw new InvalidOperationException("OLD fixture sort keys are not settled.");
                for(int j=0;j<keys.Length;j++){
                    var proxy=proxies.GetValue(j);root.Add(new XElement("Part",new XAttribute("Actor",id),new XAttribute("Part",FlareValue(proxy,"PartIndex")),new XAttribute("InstanceCount",FlareValue(proxy,"InstanceCount")),new XAttribute("Technique",FlareValue(proxy,"Technique")),new XAttribute("SortKey",keys[j]),new XAttribute("Mesh",FlareValue(FlareValue(proxy,"Mesh"),"Index")),new XAttribute("Material",FlareValue(FlareValue(proxy,"Material"),"Index"))));
                }
            }
        }
        new XDocument(root).Save(Path.Combine(reports,"mesh-decals-old-sort.xml"));
    }
    static void CaptureMeshDecalDx11Buffers(object[] __args)
    {
        var owner=lightingOwner;if(owner==null||Volatile.Read(ref owner.meshInputs)!=1||Interlocked.CompareExchange(ref owner.meshCapture,3,1)!=1)return;
        try{lightingDx11Capture.Invoke(null,new object[]{owner.reports,__args[0],LightingCopySource});SaveLightingHbaoConstants();if(owner.meshOldOrder)owner.VerifyOldPartIdentities();Volatile.Write(ref owner.meshCapture,2);Interlocked.Exchange(ref lightingPending,0);}
        catch(Exception e){Volatile.Write(ref owner.meshObservation,new MeshObservation{Failure=e.ToString()});}
    }
    void BuildMeshDecals()
    {
        var towardSun=Vector3.Normalize(new Vector3(.3f,.5f,1));MySector.SunProperties.BaseSunDirectionNormalized=towardSun;MySector.SunProperties.SunDirectionNormalized=towardSun;
        var camera=new Vector3D(1000,1000,1000);var manifest=new XElement("MeshDecalFixture",new XAttribute("Schema",1),new XAttribute("Overlap",meshDecalOverlap),new XAttribute("Cutout",meshDecalCutout),new XAttribute("Raster",meshRasterMode),new XAttribute("InstanceColor",meshInstanceColor),new XAttribute("Backend",expectedBackend),new XAttribute("Camera",camera),new XAttribute("TowardSun",towardSun),new XAttribute("Scope",meshRasterMode?"Split front/back MWM geometry across MESH, ALPHA_MASKED_SINGLE_SIDED, DECAL_CUTOUT and DECAL; OLD/NEW admission and actor no-cull flags; held static camera.":"Exported MWM geometry/materials, four texture bundles, both alpha conventions, forced OLD and normal admission; held static camera."));
        manifest.SetAttributeValue("Hdr16",LightingQuality().HqTarget);
        manifest.SetAttributeValue("ImmediateProbes",Environment.GetEnvironmentVariable("SE_VULKAN_MESH_IMMEDIATE_PROBES")=="1");
        if(meshInstanceColor){BuildMeshInstanceColor(camera,manifest);return;}
        if(meshRasterMode){BuildMeshRaster(camera,manifest);return;}
        if(meshOldOrder){BuildMeshOldOrder(camera,manifest);return;}
        int row=0;foreach(var technique in new[]{MyMeshDrawTechnique.DECAL,MyMeshDrawTechnique.DECAL_NOPREMULT})foreach(bool old in new[]{true,false}){
            int column=0;foreach(uint role in new uint[]{1,2,3,7}){
                string model=MeshDecalFixtureAssets.Model(Path.Combine(reports,"MeshDecalAssets"),technique,role,overlap:meshDecalOverlap,cutoutAlpha:meshDecalCutout?(byte?)(column%2==0?0:255):null);
                var center=camera+new Vector3D((column-1.5)*2.3,.6+(1.5-row)*2.3,-8);
                var world=MatrixD.CreateScale(.9)*MatrixD.CreateTranslation(center);
                uint id=MyRenderProxy.CreateRenderEntity("Mesh decal fixture "+row+"/"+column,model,world,MyMeshDrawTechnique.MESH,RenderFlags.Visible|(old?RenderFlags.ForceOldPipeline:0),(CullingOptions)0,Color.White,Vector3.Zero);
                meshDecalCreated.Add(id);manifest.Add(new XElement("Panel",new XAttribute("Id",id),new XAttribute("Row",row),new XAttribute("Column",column),new XAttribute("Technique",technique),new XAttribute("TextureRole",role),new XAttribute("OldRequested",old),new XAttribute("Model",model),new XAttribute("World",world)));column++;
            }row++;
        }
        Volatile.Write(ref meshDecalIds,meshDecalCreated.ToArray());new XDocument(manifest).Save(Path.Combine(reports,"mesh-decals-assets.xml"));Camera(camera,camera+Vector3D.Forward);
    }
    void BuildMeshInstanceColor(Vector3D camera,XElement manifest)
    {
        var settings=MyRenderProxy.Settings;settings.User.DistanceFade=meshInstanceFade?8.5f:3000;MyRenderProxy.SwitchRenderSettings(settings);
        manifest.SetAttributeValue("ActiveFade",meshInstanceFade);
        manifest.SetAttributeValue("FadeThreshold",settings.User.DistanceFade);
        manifest.SetAttributeValue("ColorUpdates",true);
        manifest.SetAttributeValue("MaterialColors",meshInstanceMaterialColor);
        manifest.SetAttributeValue("Scope","Neutral material under explicit white/red/green/blue entity diffuse-color updates"+(meshInstanceMaterialColor?" and material tint":"")+"; rigid OLD, genuine generic OLD, rigid NEW, and genuine generic OLD with "+(meshInstanceFade?"active":"inactive")+" distance fade.");
        var colors=new[]{Color.White,Color.Red,Color.Green,Color.Blue};
        string model=MeshDecalFixtureAssets.Model(Path.Combine(reports,"MeshDecalAssets"),MyMeshDrawTechnique.MESH,7,255,neutral:true);
        var importer=new MyModelImporter();importer.ImportData(model,new[]{"MeshParts"});
        var materialNames=((List<MyMeshPartInfo>)importer.GetTagData()["MeshParts"]).Select(part=>part.m_MaterialDesc.MaterialName).Distinct().ToArray();
        for(int row=0;row<4;row++)for(int column=0;column<4;column++){
            bool generic=row==1||row==3,old=row!=2,fade=row==3;
            var center=camera+new Vector3D((column-1.5)*2.3,.6+(1.5-row)*2.3,-8);var world=MatrixD.CreateScale(.9)*MatrixD.CreateTranslation(center);
            uint id=MyRenderProxy.CreateRenderEntity("Instance color fixture "+row+"/"+column,model,world,MyMeshDrawTechnique.MESH,RenderFlags.Visible|(old?RenderFlags.ForceOldPipeline:0)|(fade?RenderFlags.DistanceFade:0),(CullingOptions)0,colors[column],Vector3.Zero);
            meshDecalCreated.Add(id);
            if(generic){uint buffer=MyRenderProxy.CreateRenderInstanceBuffer("Instance color source",MyRenderInstanceBufferType.Generic);meshInstanceBuffers.Add(buffer);MyRenderProxy.UpdateRenderInstanceBufferRange(buffer,new[]{new VRage.MyInstanceData(Matrix.Identity)},0,true);MyRenderProxy.SetInstanceBuffer(id,buffer,0,1,new BoundingBox(new Vector3(-1,-1,0),new Vector3(1,1,.015625f)));}
            MyRenderProxy.UpdateRenderEntity(id,colors[column],null);
            if(meshInstanceMaterialColor)foreach(string materialName in materialNames)MyRenderProxy.UpdateColorEmissivity(id,0,materialName,colors[column],1);
            manifest.Add(new XElement("Panel",new XAttribute("Id",id),new XAttribute("Row",row),new XAttribute("Column",column),new XAttribute("Technique",MyMeshDrawTechnique.MESH),new XAttribute("TextureRole",7),new XAttribute("OldRequested",old),new XAttribute("Generic",generic),new XAttribute("DistanceFade",fade),new XAttribute("ActorColor",colors[column]),new XAttribute("MaterialColor",meshInstanceMaterialColor?colors[column]:Color.White),new XAttribute("Model",model),new XAttribute("World",world)));
        }
        Volatile.Write(ref meshDecalIds,meshDecalCreated.ToArray());new XDocument(manifest).Save(Path.Combine(reports,"mesh-decals-assets.xml"));Camera(camera,camera+Vector3D.Forward);
    }
    void BuildMeshRaster(Vector3D camera,XElement manifest)
    {
        var techniques=new[]{MyMeshDrawTechnique.MESH,MyMeshDrawTechnique.ALPHA_MASKED_SINGLE_SIDED,MyMeshDrawTechnique.DECAL_CUTOUT,MyMeshDrawTechnique.DECAL};
        for(int row=0;row<4;row++)for(int column=0;column<4;column++){
            bool old=column<2,noCull=column%2==1;var technique=techniques[row];
            string model=MeshDecalFixtureAssets.Model(Path.Combine(reports,"MeshDecalAssets"),technique,7,255,splitWinding:true);
            var center=camera+new Vector3D((column-1.5)*2.3,.6+(1.5-row)*2.3,-8);var world=MatrixD.CreateScale(.9)*MatrixD.CreateTranslation(center);
            uint id=MyRenderProxy.CreateRenderEntity("Material raster fixture "+row+"/"+column,model,world,MyMeshDrawTechnique.MESH,RenderFlags.Visible|(old?RenderFlags.ForceOldPipeline:0)|(noCull?RenderFlags.NoBackFaceCulling:0),(CullingOptions)0,Color.White,Vector3.Zero);
            meshDecalCreated.Add(id);manifest.Add(new XElement("Panel",new XAttribute("Id",id),new XAttribute("Row",row),new XAttribute("Column",column),new XAttribute("Technique",technique),new XAttribute("TextureRole",7),new XAttribute("OldRequested",old),new XAttribute("NoCull",noCull),new XAttribute("Model",model),new XAttribute("World",world)));
        }
        Volatile.Write(ref meshDecalIds,meshDecalCreated.ToArray());new XDocument(manifest).Save(Path.Combine(reports,"mesh-decals-assets.xml"));Camera(camera,camera+Vector3D.Forward);
    }
    void SaveMeshObservation(string name,MeshObservation observation)
    {new XDocument(new XElement("MeshDecalObservation",new XAttribute("Schema",1),new XAttribute("Backend",expectedBackend),new XAttribute("Actors",observation.Actors),new XAttribute("Old",observation.Old),new XAttribute("New",observation.New),new XAttribute("BlendedPackets",observation.Packets),new XAttribute("CutoutPackets",observation.Cutouts),new XAttribute("GenericActors",observation.Instances),new XAttribute("FadePackets",observation.FadePackets),new XAttribute("ValidationErrors",observation.Errors),new XAttribute("Scope","DX11 counts actual admitted actor component types; Vulkan counts source flags and queued blended world packets. Appearance is verified separately from PNGs."))).Save(Path.Combine(reports,name+".xml"));}
    void ExerciseMeshDecals(double seconds)
    {
        var observation=Volatile.Read(ref meshObservation);if(observation?.Failure!=null)throw new InvalidOperationException("Mesh decal observer: "+observation.Failure);
        if(observation!=null&&observation.Errors!=0)throw new InvalidOperationException("Mesh decal validation errors: "+observation.Errors);
        if(meshDecalStage==0&&seconds>=3){BuildMeshDecals();meshDecalWait=seconds+15;meshDecalStage=1;Report("Authored mesh-decal models created.");return;}
        if(meshDecalStage==1&&observation!=null&&seconds>=meshDecalWait){
            string log=Path.Combine(userData,"VRageRender-DirectX11.log");
            if(File.Exists(log))using(var stream=new FileStream(log,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))using(var reader=new StreamReader(stream)){
                string line;while((line=reader.ReadLine())!=null)if(line.IndexOf("MeshDecalAssets",StringComparison.OrdinalIgnoreCase)>=0&&line.IndexOf("Error",StringComparison.OrdinalIgnoreCase)>=0)throw new InvalidOperationException("The stock renderer rejected a mesh-decal fixture texture; inspect its render log.");
            }
            SaveMeshObservation("mesh-decals-latest",observation);
            if(observation.Actors!=(meshOldOrder?12:16)||observation.Old!=(meshOldOrder?12:meshInstanceColor?12:8)||observation.New!=(meshOldOrder?0:meshInstanceColor?4:8)||(expectedBackend.ToString()=="Vulkan"&&(observation.Packets!=(meshOldOrder?0:meshInstanceColor?0:meshRasterMode?4:meshDecalOverlap?32:16)||observation.Cutouts!=(meshOldOrder?8:meshInstanceColor?0:meshRasterMode?4:meshDecalCutout?16:0)||(meshInstanceColor&&(observation.Instances!=8||observation.FadePackets!=8)))))return;
            SaveCaptureQuality();SaveMeshObservation("mesh-decals-capture",observation);Interlocked.Exchange(ref lightingPending,1);Volatile.Write(ref meshCapture,1);Capture("mesh-decals");meshDecalStage=2;return;
        }
        if(meshDecalStage==2&&Volatile.Read(ref meshCapture)==2&&File.Exists(Path.Combine(reports,"mesh-decals.png"))){foreach(uint id in meshDecalCreated)MyRenderProxy.RemoveRenderObject(id,MyRenderProxy.ObjectType.Entity);foreach(uint buffer in meshInstanceBuffers)MyRenderProxy.RemoveRenderObject(buffer,MyRenderProxy.ObjectType.InstanceBuffer);meshDecalWait=seconds+3;meshDecalStage=3;return;}
        if(meshDecalStage==3&&observation!=null&&seconds>=meshDecalWait&&observation.Actors==0&&observation.Packets==0&&observation.Cutouts==0){
            SaveMeshObservation("mesh-decals-cleanup",observation);completed=true;completedAt=clock.Elapsed.TotalSeconds;File.WriteAllText(Path.Combine(reports,"fixture-completed.txt"),DateTime.UtcNow.ToString("u"));Report("Mesh-decal admission, capture delivery and render-object cleanup completed; paired appearance review remains separate.");
        }
    }
}
