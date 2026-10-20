using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Xml.Linq;
using ClientPlugin.Rendering.Interop;
using HarmonyLib;
using Sandbox.Game.World;
using VRage.FileSystem;
using VRage.Import;
using VRageMath;
using VRageMath.PackedVector;
using VRageRender;
using VRageRender.Import;
using VRageRender.Messages;

namespace CreativeFixtureHarness;

public sealed partial class Plugin
{
    bool glassOverlapMode;int glassOverlapStage;double glassOverlapWait,overlapNextObservation;
    [ThreadStatic] static HashSet<uint> overlapDxVisible;
    [ThreadStatic] static int overlapDxOldParts;
    [ThreadStatic] static int overlapDxNewParts;
    readonly List<OverlapPane> overlapPanes=new List<OverlapPane>();
    readonly List<uint> overlapDecals=new List<uint>();
    uint[] overlapIds=Array.Empty<uint>();
    double[] overlapDepths;
    OverlapObservation overlapObservation;string overlapObserverFailure;long overlapObservationTick;
    static readonly Vector3D OverlapCamera=new Vector3D(1000,1000,1000);
    sealed class OverlapPane {public uint Id;public bool Old;public int Column,Layer;public double DistanceSquared;public Vector3D Center;}
    sealed class OverlapObservation {
        public long Tick;public int OldParts,NewParts,Near,Far,SelectedNear,SelectedFar;public uint Passes;public ulong ValidationErrors;
        public float[] Distances=Array.Empty<float>();
        public byte[] Frame;public string Settings;
    }
    static void ObserveGlassOverlapScene(object __instance){lightingScene=__instance;}
    static string OverlapSettings(MyRenderSettings settings,IEnumerable<byte[]> queuedMaterials=null)
    {
        var root=new XElement("GlassInputs",new XAttribute("ColorMultiplier",settings.TransparentColorMultiplier),new XAttribute("ReflectivityMultiplier",settings.TransparentReflectivityMultiplier),new XAttribute("FresnelMultiplier",settings.TransparentFresnelMultiplier),new XAttribute("GlossMultiplier",settings.TransparentGlossMultiplier));
        foreach(var material in MyTransparentMaterials.Materials.Where(m=>m.Id.ToString()=="GlassOutside"||m.Id.ToString()=="GlassInside")){
            var row=new XElement("Definition",new XAttribute("Name",material.Id.ToString()));
            foreach(string field in new[]{"Texture","GlossTexture","Color","ColorAdd","ShadowMultiplier","LightMultiplier","Reflectivity","Fresnel","ReflectionShadow","Gloss","GlossTextureAdd","SpecularColorFactor","TriangleFaceCulling"})row.Add(new XAttribute(field,Convert.ToString(FlareValue(material,field),CultureInfo.InvariantCulture)));
            root.Add(row);
        }
        if(queuedMaterials!=null)foreach(string bytes in queuedMaterials.Select(Convert.ToBase64String).Distinct().OrderBy(s=>s,StringComparer.Ordinal))root.Add(new XElement("NativeQueuedMaterial",bytes));
        return root.ToString();
    }
    static uint OverlapOwner(object row)=>Convert.ToUInt32(FlareValue(FlareValue(FlareValue(FlareValue(row,"RenderProxy"),"Parent"),"Owner"),"ID"));
    static void ObserveGlassOverlapVulkan(NativeBackend __instance)
    {
        var owner=lightingOwner;if(owner==null||!owner.glassOverlapMode)return;
        try{
            var observation=new OverlapObservation{Tick=Interlocked.Increment(ref owner.overlapObservationTick),ValidationErrors=__instance.GetStats().ValidationErrors};
            var distances=new List<float>();var materials=new List<byte[]>();uint[] masks=(uint[])AccessTools.Method(typeof(NativeBackend),"GlassDecalDepthFlags").Invoke(__instance,null);int i=0;
            foreach(object tuple in (IEnumerable)FlareValue(__instance,"glassPackets")){
                materials.Add((byte[])FlareValue(tuple,"Item2"));
                uint flags=(uint)FlareValue(FlareValue(tuple,"Item1"),"Flags")|masks[i++];
                if((flags&2)!=0){observation.OldParts++;object d=FlareValue(tuple,"Item3");if(d!=null&&!float.IsInfinity((float)d))distances.Add((float)d);}
                else observation.NewParts++;
                if((flags&4)!=0)observation.SelectedFar++;if((flags&8)!=0)observation.SelectedNear++;observation.Passes|=flags&48;
            }
            observation.Distances=distances.OrderBy(x=>x).ToArray();observation.Near=distances.Count(x=>x<2.25f);observation.Far=distances.Count-observation.Near;
            if(lightingWorldFrame!=null)observation.Frame=(byte[])lightingWorldFrame.Clone();
            if(lightingScene!=null)observation.Settings=OverlapSettings((MyRenderSettings)FlareValue(lightingScene,"RenderSettings"),materials);
            Volatile.Write(ref owner.overlapObservation,observation);
        }catch(Exception error){Volatile.Write(ref owner.overlapObserverFailure,error.ToString());}
    }
    static void ObserveGlassOverlapDx11Begin()
    {overlapDxVisible=new HashSet<uint>();overlapDxOldParts=overlapDxNewParts=0;}
    static void ObserveGlassOverlapDx11OldBegin()
    {
        var owner=lightingOwner;if(owner==null||!owner.glassOverlapMode)return;
        try{
            var ids=new HashSet<uint>(Volatile.Read(ref owner.overlapIds));var visible=new HashSet<uint>();int parts=0;
            foreach(object row in (IEnumerable)FlareValue(AccessTools.TypeByName("VRageRender.MyTransparentModelRenderer"),"Renderables")){uint id=OverlapOwner(row);if(ids.Contains(id)){visible.Add(id);parts++;}}
            // Render() clears this source list after building sorted candidates.
            overlapDxVisible=visible;overlapDxOldParts=parts;
        }catch(Exception error){Volatile.Write(ref owner.overlapObserverFailure,error.ToString());}
    }
    static void ObserveGlassOverlapDx11NewBegin(object __instance)
    {
        var owner=lightingOwner;if(owner==null||!owner.glassOverlapMode)return;
        try{
            int count=0;
            foreach(object group in (IEnumerable)FlareValue(FlareValue(__instance,"TransparentRenderData"),"InstanceLodGroups")){
                var parts=FlareValue(FlareValue(FlareValue(group,"Lod"),"PreprocessedParts"),"TransparentParts") as ICollection;
                if(parts!=null)count+=parts.Count*Convert.ToInt32(FlareValue(group,"InstancesCount"));
            }
            overlapDxNewParts=count;
        }catch(Exception error){Volatile.Write(ref owner.overlapObserverFailure,error.ToString());}
    }
    static void ObserveGlassOverlapDx11()
    {
        var owner=lightingOwner;if(owner==null||!owner.glassOverlapMode)return;
        try{
            var observation=new OverlapObservation{Tick=Interlocked.Increment(ref owner.overlapObservationTick),OldParts=overlapDxOldParts,NewParts=overlapDxNewParts};
            var renderer=AccessTools.TypeByName("VRageRender.MyTransparentModelRenderer");var visible=overlapDxVisible??new HashSet<uint>();
            var distances=new List<float>();
            foreach(object tuple in (IEnumerable)FlareValue(renderer,"m_squaredDistances"))if(visible.Contains(OverlapOwner(FlareValue(tuple,"Item1"))))distances.Add((float)FlareValue(tuple,"Item2"));
            observation.Distances=distances.OrderBy(x=>x).ToArray();observation.Near=distances.Count(x=>x<2.25f);observation.Far=distances.Count-observation.Near;
            // Record the installed loop's selected counts, not an inferred fixed loop.
            observation.SelectedFar=Math.Max(0,observation.Far-observation.Near);observation.SelectedNear=observation.Near;
            observation.Passes=(observation.Far>0?16u:0u)|(observation.Near>0?32u:0u);
            object frame=FlareValue(AccessTools.TypeByName("VRageRender.MyCommon"),"FrameConstantsData");int size=Marshal.SizeOf(frame);var memory=Marshal.AllocHGlobal(size);
            try{Marshal.StructureToPtr(frame,memory,false);observation.Frame=new byte[size];Marshal.Copy(memory,observation.Frame,0,size);}finally{Marshal.FreeHGlobal(memory);}
            observation.Settings=OverlapSettings((MyRenderSettings)FlareValue(AccessTools.TypeByName("VRageRender.MyRender11"),"Settings"));
            Volatile.Write(ref owner.overlapObservation,observation);
        }catch(Exception error){Volatile.Write(ref owner.overlapObserverFailure,error.ToString());}
    }
    void BuildGlassOverlap()
    {
        // EmptyWorld may retain an undefined disabled rotating-sun direction.
        // Use the same controlled environment as the established lighting fixture.
        var towardSun=Vector3.Normalize(new Vector3(.3f,.5f,1));
        MySector.SunProperties.BaseSunDirectionNormalized=towardSun;
        MySector.SunProperties.SunDirectionNormalized=towardSun;
        string model="Models/Cubes/Large/HalfWindow.mwm",path=Path.Combine(MyFileSystem.ContentPath,model);
        var importer=new MyModelImporter();var requestedTags=new[]{"Vertices","MeshParts","GeometryDataAsset"};importer.ImportData(path,requestedTags);var tags=importer.GetTagData();
        // The installed model is a geometry stub; follow its authored asset
        // reference exactly as the production content cache does.
        if(tags.TryGetValue("GeometryDataAsset",out object geometry)){string file=new MyLODDescriptor{Model=(string)geometry}.GetModelAbsoluteFilePath(path);importer.ImportData(file,requestedTags);tags=importer.GetTagData();}
        if(!tags.ContainsKey("Vertices")||!tags.ContainsKey("MeshParts"))throw new InvalidDataException("Installed glass geometry has no vertices/parts.");
        var positions=(HalfVector4[])tags["Vertices"];var parts=(List<MyMeshPartInfo>)tags["MeshParts"];
        Vector3D center=Vector3D.Zero,normal=Vector3D.Zero;double radius=0;
        foreach(var part in parts.Where(p=>p.Technique==MyMeshDrawTechnique.GLASS&&p.GetMaterialName().IndexOf("Outside",StringComparison.OrdinalIgnoreCase)>=0))
            for(int i=0;i+2<part.m_indices.Count;i+=3){
                Vector3D a=VF_Packer.UnpackPosition(positions[part.m_indices[i]]),b=VF_Packer.UnpackPosition(positions[part.m_indices[i+1]]),c=VF_Packer.UnpackPosition(positions[part.m_indices[i+2]]);
                Vector3D cross=Vector3D.Cross(b-a,c-a);if(cross.LengthSquared()<1e-8)continue;var centroid=(a+b+c)/3;
                double r=Math.Min(Vector3D.Cross(b-a,centroid-a).Length()/(b-a).Length(),Math.Min(Vector3D.Cross(c-b,centroid-b).Length()/(c-b).Length(),Vector3D.Cross(a-c,centroid-c).Length()/(a-c).Length()));
                if(r>radius){radius=r;center=centroid;normal=Vector3D.Normalize(cross);}
            }
        if(radius<.01)throw new InvalidOperationException("Installed HalfWindow has no usable outside glass triangle.");
        var basis=MatrixD.CreateWorld(center,-normal,Math.Abs(normal.Y)<.9?Vector3D.Up:Vector3D.Right);
        var toPanel=MatrixD.Invert(basis);var originOffset=toPanel.Translation*(.12/radius);
        double left=(new Vector3D(-.6,0,-1)+originOffset).Length(),right=(new Vector3D(.6,0,-1)+originOffset).Length();
        double maximum=Math.Max(left,right),minimum=Math.Min(left,right);
        overlapDepths=new[]{.6/maximum,1.2/maximum,3/minimum};
        new XDocument(new XElement("GlassOverlapGeometry",new XAttribute("Radius",radius),new XAttribute("Center",center),new XAttribute("Normal",normal),new XAttribute("OriginOffsetPerSurfaceDepth",originOffset),new XAttribute("LeftOriginDistancePerSurfaceDepth",left),new XAttribute("RightOriginDistancePerSurfaceDepth",right),new XAttribute("SurfaceDepths",string.Join(" ",overlapDepths.Select(d=>d.ToString("R",CultureInfo.InvariantCulture)))))).Save(Path.Combine(reports,"glass-overlap-geometry.xml"));
        if(overlapDepths[0]<.06||overlapDepths[1]-overlapDepths[0]<.06)throw new InvalidOperationException("Stock model origin needs a different framing to keep both near glass surfaces outside the 0.05m peel tolerance; inspect glass-overlap-geometry.xml.");
        var manifest=new XElement("GlassOverlap",new XAttribute("Schema",1),new XAttribute("Backend",expectedBackend),new XAttribute("SceneRoi","40 160 560 160"),new XAttribute("Scope","Isolated static stock-model source stacks; left OLD, center NEW, right mixed. Full-frame PNGs; numeric scene ROI excludes HUD. No Anomaly or particle scenario."),new XAttribute("Model",model),new XAttribute("TriangleRadius",radius),new XAttribute("TriangleCenter",center),new XAttribute("TriangleNormal",normal));
        for(int column=0;column<3;column++)for(int layer=0;layer<3;layer++){
            bool old=column==0||(column==2&&layer==0);double depth=overlapDepths[layer];
            var panelCenter=OverlapCamera+new Vector3D((column-1)*.6*depth,0,-depth);
            var world=MatrixD.Invert(basis)*MatrixD.CreateScale(depth*.12/radius)*MatrixD.CreateTranslation(panelCenter);
            double distance=((Vector3)(world.Translation-OverlapCamera)).LengthSquared();
            if(old&&((layer<2&&distance>=2.25)||(layer==2&&distance<2.25)))throw new InvalidOperationException("Authored model origin does not satisfy the intended stock OLD distance group.");
            uint id=MyRenderProxy.CreateRenderEntity("Glass overlap "+column+"/"+layer,model,world,MyMeshDrawTechnique.MESH,RenderFlags.Visible|(old?RenderFlags.ForceOldPipeline:0),(CullingOptions)0,Color.White,Vector3.Zero);
            overlapPanes.Add(new OverlapPane{Id=id,Old=old,Column=column,Layer=layer,DistanceSquared=distance,Center=panelCenter});
            manifest.Add(new XElement("Pane",new XAttribute("Id",id),new XAttribute("Column",column),new XAttribute("Layer",layer),new XAttribute("Source",old?"OLD":"NEW"),new XAttribute("DistanceSquared",distance),new XAttribute("World",world)));
        }
        Volatile.Write(ref overlapIds,overlapPanes.Select(p=>p.Id).ToArray());
        new XDocument(manifest).Save(Path.Combine(reports,"glass-overlap-inputs.xml"));
        Camera(OverlapCamera,OverlapCamera+Vector3D.Forward);
    }
    static void OverlapTexture(string path,Func<int,int,byte[]> pixel)
    {
        using(var w=new BinaryWriter(File.Create(path))){
            foreach(uint value in new uint[]{0x20534444,124,0x100f,16,16,64,0,1})w.Write(value);
            for(int i=0;i<11;i++)w.Write(0u);
            foreach(uint value in new uint[]{32,0x41,0,32,0xff,0xff00,0xff0000,0xff000000,0x1000,0,0,0,0})w.Write(value);
            for(int y=0;y<16;y++)for(int x=0;x<16;x++)w.Write(pixel(x,y));
        }
    }
    void AddOverlapDecals()
    {
        string alpha=Path.Combine(reports,"glass-overlap-alpha.dds"),normal=Path.Combine(reports,"glass-overlap-normal.dds");
        OverlapTexture(alpha,(x,y)=>{byte v=(byte)((x-7.5)*(x-7.5)+(y-7.5)*(y-7.5)<=49?255:0);return new[]{v,v,v,v};});
        OverlapTexture(normal,(x,y)=>new byte[]{128,128,255,128});
        var materials=new Dictionary<string,List<MyDecalMaterialDesc>>();
        for(int layer=0;layer<3;layer++){
            string color=Path.Combine(reports,"glass-overlap-color-"+layer+".dds");int c=layer;
            OverlapTexture(color,(x,y)=>new byte[]{(byte)(c==1?255:0),(byte)(c==0?255:0),(byte)(c==2?255:0),255});
            materials["GlassOverlap"+layer]=new List<MyDecalMaterialDesc>{new MyDecalMaterialDesc{ColorMetalTexture=color,AlphamaskTexture=alpha,NormalmapTexture=normal}};
        }
        MyRenderProxy.RegisterDecals(materials);
        foreach(var pane in overlapPanes){
            double depth=overlapDepths[pane.Layer];
            Matrix decal=Matrix.CreateScale((float)(depth*.07),(float)(depth*.07),(float)((overlapDepths[1]-overlapDepths[0])*.25));
            var topo=new MyDecalTopoData{MatrixBinding=decal,MatrixCurrent=decal,WorldPosition=pane.Center+new Vector3D((pane.Layer-1)*depth*.065,0,0)};
            string material="GlassOverlap"+pane.Layer;
            overlapDecals.Add(MyRenderProxy.CreateDecal(new[]{pane.Id},ref topo,MyDecalFlags.World|MyDecalFlags.Transparent,material,material,0,500,false,int.MaxValue));
        }
    }
    void SaveOverlapObservation(string name,OverlapObservation observation)
    {
        if(observation.Frame==null||observation.Frame.Length!=1280||observation.Settings==null)throw new InvalidOperationException("Actual glass frame/material inputs were not observed.");
        var frameType=AccessTools.Field(AccessTools.TypeByName("VRageRender.MyCommon"),"FrameConstantsData").FieldType;
        int sunOffset=Marshal.OffsetOf(frameType,"EnvironmentLight").ToInt32()+Marshal.OffsetOf(typeof(MyEnvironmentLightData),"SunLightDirection").ToInt32();
        var actualSun=new Vector3(BitConverter.ToSingle(observation.Frame,sunOffset),BitConverter.ToSingle(observation.Frame,sunOffset+4),BitConverter.ToSingle(observation.Frame,sunOffset+8));
        var expectedSun=-Vector3.Normalize(new Vector3(.3f,.5f,1));
        if(!actualSun.IsValid()||Vector3.DistanceSquared(actualSun,expectedSun)>1e-10f)throw new InvalidOperationException("Glass capture requires the actual finite pinned sun direction: "+actualSun);
        File.WriteAllBytes(Path.Combine(reports,name+".frame.bin"),observation.Frame);File.WriteAllText(Path.Combine(reports,name+".materials.xml"),observation.Settings);
        new XDocument(new XElement("GlassFrameInput",new XAttribute("SunOffset",sunOffset),new XAttribute("SunX",actualSun.X.ToString("R",CultureInfo.InvariantCulture)),new XAttribute("SunY",actualSun.Y.ToString("R",CultureInfo.InvariantCulture)),new XAttribute("SunZ",actualSun.Z.ToString("R",CultureInfo.InvariantCulture)),new XAttribute("Scope","Observed CPU main-frame constants; offset derived from installed stock layouts."))).Save(Path.Combine(reports,name+".frame.xml"));
        new XDocument(new XElement("GlassOverlapObservation",new XAttribute("Schema",1),new XAttribute("Backend",expectedBackend),new XAttribute("Tick",observation.Tick),new XAttribute("OldVisibleParts",observation.OldParts),new XAttribute("NewVisibleParts",observation.NewParts),new XAttribute("OldCandidates",observation.Distances.Length),new XAttribute("OldNear",observation.Near),new XAttribute("OldFar",observation.Far),new XAttribute("SelectedNear",observation.SelectedNear),new XAttribute("SelectedFar",observation.SelectedFar),new XAttribute("RequestedPassFlags",observation.Passes),new XAttribute("ValidationErrors",observation.ValidationErrors),new XAttribute("Scope","Latest completed renderer source observation; source state is held until screenshot delivery. DX11 selected counts derive from the authenticated installed loop; Vulkan flags are the actual queued flags."),new XElement("Distances",string.Join(" ",observation.Distances.Select(d=>d.ToString("R",CultureInfo.InvariantCulture)))))).Save(Path.Combine(reports,name+".xml"));
    }
    void ExerciseGlassOverlap(double seconds)
    {
        string error=Volatile.Read(ref overlapObserverFailure);if(error!=null)throw new InvalidOperationException("Glass source observer failed: "+error);
        var observation=Volatile.Read(ref overlapObservation);
        if(glassOverlapStage==0&&seconds>=3){BuildGlassOverlap();glassOverlapWait=seconds+10;glassOverlapStage=1;Report("Isolated OLD/NEW/mixed glass stacks created.");return;}
        if(glassOverlapStage==0)return; // The controlled environment is not initialized yet.
        if(observation==null||seconds<glassOverlapWait)return;
        if(seconds>=overlapNextObservation){SaveOverlapObservation("glass-overlap-latest",observation);overlapNextObservation=seconds+2;}
        if(observation.ValidationErrors!=0)throw new InvalidOperationException("Glass overlap validation errors: "+observation.ValidationErrors);
        bool visible=observation.OldParts==8&&observation.NewParts==10;
        if(glassOverlapStage==1&&visible&&observation.Distances.Length==0){SaveCaptureQuality();SaveOverlapObservation("glass-overlap-baseline",observation);Capture("glass-overlap-baseline");glassOverlapStage=2;return;}
        if(glassOverlapStage==2&&File.Exists(Path.Combine(reports,"glass-overlap-baseline.png"))){AddOverlapDecals();glassOverlapWait=seconds+3;glassOverlapStage=3;return;}
        if(glassOverlapStage==3&&visible&&observation.Distances.Length==8&&observation.Near==6&&observation.Far==2){
            if(observation.SelectedNear!=6||observation.SelectedFar!=0||observation.Passes!=48)throw new InvalidOperationException("Glass overlap did not exercise the installed OLD selection bound.");
            SaveOverlapObservation("glass-overlap-decals",observation);Capture("glass-overlap-decals");glassOverlapStage=4;return;
        }
        if(glassOverlapStage==4&&File.Exists(Path.Combine(reports,"glass-overlap-decals.png"))){foreach(uint id in overlapDecals)MyRenderProxy.RemoveDecal(id,true);overlapDecals.Clear();glassOverlapWait=seconds+3;glassOverlapStage=5;return;}
        if(glassOverlapStage==5&&visible&&observation.Distances.Length==0){SaveOverlapObservation("glass-overlap-restored",observation);Capture("glass-overlap-restored");glassOverlapStage=6;return;}
        if(glassOverlapStage==6&&File.Exists(Path.Combine(reports,"glass-overlap-restored.png"))){foreach(uint id in overlapIds)MyRenderProxy.RemoveRenderObject(id,MyRenderProxy.ObjectType.Entity);glassOverlapWait=seconds+3;glassOverlapStage=7;return;}
        if(glassOverlapStage==7&&observation.OldParts==0&&observation.NewParts==0){
            SaveOverlapObservation("glass-overlap-cleanup",observation);completed=true;completedAt=clock.Elapsed.TotalSeconds;
            File.WriteAllText(Path.Combine(reports,"fixture-completed.txt"),DateTime.UtcNow.ToString("u"));Report("Isolated glass source states, capture delivery and render-object cleanup completed; visual comparison remains pending.");
        }
    }
}
