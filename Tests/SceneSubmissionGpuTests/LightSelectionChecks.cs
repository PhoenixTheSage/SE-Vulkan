using System.Reflection;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using VRage.Render.Scene;
using VRage.Render.Scene.Components;
using VRageMath;
using VRageRender;
using VRageRender.Messages;

partial class Program
{
    sealed class LightPacketCapture
    {
        public Delegate Original;public NativeLight[] Packets=Array.Empty<NativeLight>();
        public int Invoke(IntPtr context,NativeLight[] source,uint count){Packets=source.Take((int)count).ToArray();return (int)Original.DynamicInvoke(context,source,count);}
        public int[] Ids(bool spot)=>Packets.Where(p=>((p.Flags&1)!=0)==spot).Select(p=>(int)Math.Round(p.ColorIntensity[0]*4096)).OrderBy(id=>id).ToArray();
    }
    sealed class SpotPacketCapture
    {
        public Delegate Original;public NativeSpot[] Packets=Array.Empty<NativeSpot>();
        public int Invoke(IntPtr context,uint resolution,NativeSpot[] source,uint count){Packets=source.Take((int)count).ToArray();return (int)Original.DynamicInvoke(context,resolution,source,count);}
    }
    static void LightSelectionChecks(NativeBackend backend,VulkanScene scene,MatrixD projection,Func<byte[]> render,Action<bool,string> check)
    {
        var settings=scene.RenderSettings;settings.DrawNonMergeInstanced=settings.DrawMergeInstanced=settings.DrawGroups=true;settings.UseIncrementalCulling=true;settings.CullGroupsThreshold=512;settings.IncrementalCullFrames=10;settings.IncrementalCullingTreeFallbackThreshold=.5f;scene.RenderSettings=settings;
        scene.Process(new MyRenderMessageSetCameraViewMatrix{ViewMatrix=MatrixD.Identity,ProjectionMatrix=(Matrix)projection,ProjectionFarMatrix=(Matrix)projection,CameraPosition=Vector3D.Zero});
        var api=typeof(NativeBackend).GetField("api",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);var field=api.GetType().GetField("SetLights",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public);
        var capture=new LightPacketCapture{Original=(Delegate)field.GetValue(api)};field.SetValue(api,Delegate.CreateDelegate(field.FieldType,capture,typeof(LightPacketCapture).GetMethod(nameof(LightPacketCapture.Invoke))));
        var spotApi=typeof(NativeBackend).GetField("spotApi",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(backend);var spotField=spotApi.GetType().GetField("Set",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public);
        var spotCapture=new SpotPacketCapture{Original=(Delegate)spotField.GetValue(spotApi)};spotField.SetValue(spotApi,Delegate.CreateDelegate(spotField.FieldType,spotCapture,typeof(SpotPacketCapture).GetMethod(nameof(SpotPacketCapture.Invoke))));
        var created=new List<MyLightComponent>();var parents=new List<MyActor>();var dataById=new Dictionary<int,UpdateRenderLightData>();
        MyLightComponent Add(int id,Vector3D position,float range=1,bool point=true,bool spot=false,float pointIntensity=1,float spotIntensity=1,float offset=0,Vector3? direction=null,MyActor parent=null){
            var light=scene.ActorFactory.CreateLight("spatial light "+id);light.Owner.SetID((uint)(200000+id));if(parent!=null)scene.SetActorParent(parent,light.Owner,null);
            var layout=new MyLightLayout{Color=new Vector3(id/4096f),Range=range,Falloff=1,GlossFactor=1,DiffuseFactor=1};
            var data=new UpdateRenderLightData{Position=position,PointLightOn=point,SpotLightOn=spot,PointIntensity=pointIntensity,SpotIntensity=spotIntensity,PointOffset=offset,PointLight=layout,AabbChanged=true,PositionChanged=true,SpotLight=new MySpotLightLayout{Direction=direction??Vector3.Forward,Up=Vector3.Up,ApertureCos=.99f,Light=layout}};
            light.UpdateData(ref data);dataById[id]=data;created.Add(light);return light;
        }
        void Update(MyLightComponent light,int id,Vector3D position,float intensity=1){var data=dataById[id];data.Position=position;data.PointIntensity=intensity;data.PositionChanged=data.AabbChanged=true;light.UpdateData(ref data);dataById[id]=data;}
        void Draw(){render();render();}
        void Points(int[] ids,string name)=>check(capture.Ids(false).SequenceEqual(ids.OrderBy(id=>id)),name+"; queued="+string.Join(",",capture.Ids(false)));
        MyActor Parent(Vector3D position){var actor=new MyActor(scene);actor.Construct("light query group");actor.SetRoot(true);var matrix=MatrixD.CreateTranslation(position);actor.SetMatrix(ref matrix);actor.SetVisibility(true);parents.Add(actor);return actor;}
        try{
            var center=Add(100,new Vector3D(0,0,-5));Add(101,new Vector3D(100,0,-5));Add(102,new Vector3D(8,0,-5),4);Add(103,new Vector3D(0,0,20));Add(104,new Vector3D(0,0,15),offset:20);
            var weak=Add(105,new Vector3D(0,0,-5),pointIntensity:.0005f);Add(106,new Vector3D(0,0,-5),point:true,spot:true,pointIntensity:.0005f);var weakSpot=Add(107,new Vector3D(0,0,-5),point:true,spot:true,spotIntensity:.0005f);
            Add(108,new Vector3D(40,0,-5),60,point:false,spot:true,direction:Vector3.Left);Add(109,new Vector3D(40,0,-5),20,point:false,spot:true,direction:Vector3.Right);
            Draw();Points(new[]{100,102,104,107},"root frustum query includes intersecting/offset bounds and suppresses disjoint and weak points");
            check(capture.Ids(true).SequenceEqual(new[]{106,108}),"spot cone bounds rotate into the view and effective intensity flags suppress weak spots");
            check(weak.Data.PointLightOn&&weakSpot.Data.SpotLightOn,"effective light gates preserve the authored shared Data contract");
            check(capture.Packets.Any(p=>(p.Flags&1)==0&&(int)Math.Round(p.ColorIntensity[0]*4096)==104&&Math.Abs(p.PositionRange[2]+5)<1e-6),"point offset controls both candidate bounds and queued world position");
            Update(weak,105,new Vector3D(0,0,-5));Draw();Points(new[]{100,102,104,105,107},"weak point activation rebuilds spatial membership");
            Update(center,100,new Vector3D(100,0,-5));Draw();Points(new[]{102,104,105,107},"moved root light leaves the frustum without destruction");
            weakSpot.Owner.SetVisibility(false);Draw();Points(new[]{102,104,105},"hidden light leaves source collection");
            foreach(var light in created)light.Owner.Destruct();created.Clear();Draw();Points(Array.Empty<int>(),"removed roots release all source proxies");
            var ambientSettings=MyEnvironmentProbeData.Default;ambientSettings.AmbientScale=1;ambientSettings.AmbientMinClamp=0;ambientSettings.AmbientMaxClamp=100;
            Add(110,Vector3D.Zero,offset:100);Draw();
            check(scene.GatherProbeAmbient(5,ambientSettings)==0,"probe ambient sphere query excludes a root whose offset bounds miss the gather volume");
            foreach(var light in created)light.Owner.Destruct();created.Clear();
            Add(111,Vector3D.Zero);Draw();
            check(Math.Abs(scene.GatherProbeAmbient(5,ambientSettings)-111/4096f/25/1.25f)<1e-7,"probe ambient sphere query retains authored falloff for an included root");
            foreach(var light in created)light.Owner.Destruct();created.Clear();Draw();
            var ambientParent=Parent(Vector3D.Zero);Add(112,Vector3D.Zero,offset:100,parent:ambientParent);Add(113,Vector3D.Zero,parent:ambientParent);Add(114,Vector3D.Zero,pointIntensity:.0005f,parent:ambientParent);Draw();
            check(Math.Abs(scene.GatherProbeAmbient(5,ambientSettings)-(112+113)/4096f/25/1.25f)<1e-7,"probe ambient gathers whole intersecting groups while effective callbacks exclude weak children");
            ambientParent.SetVisibility(false);Draw();check(scene.GatherProbeAmbient(5,ambientSettings)==0,"probe ambient preserves hidden-parent visibility");
            foreach(var light in created)light.Owner.Destruct();created.Clear();ambientParent.Destruct();parents.Clear();Draw();
            check(scene.GatherProbeAmbient(5,ambientSettings)==0,"probe ambient query drops destroyed group history");
            var parent=Parent(new Vector3D(0,0,-10));var left=Add(201,new Vector3D(-2,0,0),parent:parent);Add(202,new Vector3D(2,0,0),parent:parent);var outside=Add(203,new Vector3D(60,0,0),parent:parent);
            Draw();Points(new[]{201,202},"partial group query uses child-local bounds");
            Update(outside,203,Vector3D.Zero);Draw();Points(new[]{201,202,203},"incremental group query admits newly visible children");
            Update(left,201,new Vector3D(100,0,0));Draw();Points(new[]{202,203},"incremental group query removes moved children");
            foreach(var mode in new[]{(false,512),(false,0),(true,0),(true,512)}){var value=scene.RenderSettings;value.UseIncrementalCulling=mode.Item1;value.CullGroupsThreshold=mode.Item2;value.IncrementalCullingTreeFallbackThreshold=1;scene.RenderSettings=value;Draw();Points(new[]{202,203},"group brute/tree/incremental policy preserves visible membership "+mode);}
            parent.SetVisibility(false);Draw();Points(Array.Empty<int>(),"hidden parent group suppresses its lights");parent.SetVisibility(true);Draw();Points(new[]{202,203},"restored parent group recovers child membership");
            var contained=Parent(new Vector3D(0,0,-20));Add(301,new Vector3D(-1,0,0),parent:contained);Add(302,new Vector3D(1,0,0),parent:contained);Draw();Points(new[]{202,203,301,302},"contained groups append their complete light collection");
            var gates=scene.RenderSettings;gates.DrawGroups=false;scene.RenderSettings=gates;Draw();Points(Array.Empty<int>(),"stock DrawGroups gate excludes manual light groups");
            foreach(var light in created)light.Owner.Destruct();created.Clear();foreach(var parentActor in parents)parentActor.Destruct();parents.Clear();Draw();Points(Array.Empty<int>(),"group removal clears light query history");
            gates.DrawGroups=true;gates.User.LightsQuality=MyRenderQualityEnum.LOW;scene.RenderSettings=gates;
            for(int i=0;i<270;i++)Add(500+i,new Vector3D(0,0,-(2+i*.05)));
            for(int i=0;i<40;i++)Add(1000+i,new Vector3D(0,0,-(2+i*.1)),point:false,spot:true);
            Draw();
            var query=typeof(VulkanScene).GetField("lightQuery",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(scene);
            var roots=(System.Collections.IEnumerable)query.GetType().GetField("Points").GetValue(query);
            var rootIds=roots.Cast<MyLightComponent>().Select(item=>(int)item.Owner.ID-200000).ToArray();
            check(rootIds.Length==270,"production frustum query retains all root candidates before budget selection");
            Points(rootIds.Take(256).ToArray(),"overloaded roots preserve stock spatial-query order instead of global nearest order");
            check(capture.Ids(true).SequenceEqual(Enumerable.Range(1000,32)),"actual spot overload selects the nearest 32 independently of full point budget");
            foreach(var light in created)light.Owner.Destruct();created.Clear();Draw();Points(Array.Empty<int>(),"overloaded root removal releases candidate history");
            var spotSettings=scene.RenderSettings;spotSettings.User.ShadowQuality=MyShadowsQuality.LOW;scene.RenderSettings=spotSettings;
            var projected=Add(1200,new Vector3D(0,0,-2),20,point:false,spot:true);var projectedData=dataById[1200];projectedData.CastShadows=true;projected.UpdateData(ref projectedData);Draw();
            var expected=MatrixD.CreateLookAt(new Vector3D(0,0,-2),new Vector3D(0,0,-3),Vector3D.Up)*(MatrixD)Matrix.CreatePerspectiveFieldOfView(2*(float)Math.Acos(.99f),1,.5f,4);
            var expectedValues=NativeBackend.MatrixValues(expected);
            check(spotCapture.Packets.Length==1&&spotCapture.Packets[0].Projection.Zip(expectedValues,(a,b)=>Math.Abs(a-b)).Max()<1e-6,"spot cookie and shadow packet uses stock fixed near plane and quality-scaled far plane");
            object BuildLights()=>typeof(VulkanScene).GetField("buildLights",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(scene);ulong Redraw(){var lights=BuildLights();return (ulong)lights.GetType().GetField("SpotStatic").GetValue(lights);}
            check(Redraw()==0&&spotCapture.Packets[0].Flags==1,"steady shadowed spotlight retains its cached map");
            var shadowSettings=scene.ShadowSettings;shadowSettings.ReflectorShadowDistanceLow=.125f;scene.ShadowSettings=shadowSettings;render();
            expected=MatrixD.CreateLookAt(new Vector3D(0,0,-2),new Vector3D(0,0,-3),Vector3D.Up)*(MatrixD)Matrix.CreatePerspectiveFieldOfView(2*(float)Math.Acos(.99f),1,.5f,2.5f);
            check(spotCapture.Packets[0].Projection.Zip(NativeBackend.MatrixValues(expected),(a,b)=>Math.Abs(a-b)).Max()<1e-6&&Redraw()==1,"current reflector settings change projection and invalidate the cached shadow map");
            var buildLights=BuildLights();var live=(System.Collections.IList)buildLights.GetType().GetField("Spots").GetValue(buildLights);
            check(live.Count==1&&(float)live[0].GetType().GetField("Range").GetValue(live[0])==2.5f,"spot caster eligibility uses the same clipped reach as depth replay");
            render();check(Redraw()==0,"new spotlight projection becomes reusable after redraw");
            shadowSettings.ReflectorShadowDistanceLow=.12501f;scene.ShadowSettings=shadowSettings;render();check(Redraw()==1,"sub-centimeter far-plane changes still invalidate cached spotlight depth");
            shadowSettings.ReflectorShadowDistanceLow=0;scene.ShadowSettings=shadowSettings;render();
            expected=MatrixD.CreateLookAt(new Vector3D(0,0,-2),new Vector3D(0,0,-3),Vector3D.Up)*(MatrixD)Matrix.CreatePerspectiveFieldOfView(2*(float)Math.Acos(.99f),1,.5f,1.5f);
            check(spotCapture.Packets[0].Flags==0&&live.Count==0&&spotCapture.Packets[0].Projection.Zip(NativeBackend.MatrixValues(expected),(a,b)=>Math.Abs(a-b)).Max()<1e-6,"zero shadow range disables depth work and retains stock minimum cookie projection");
            shadowSettings.ReflectorShadowDistanceMedium=.22f;shadowSettings.ReflectorShadowDistanceHigh=.33f;shadowSettings.ReflectorShadowDistanceExtreme=.44f;scene.ShadowSettings=shadowSettings;
            foreach(var quality in new[]{(MyShadowsQuality.MEDIUM,.22f),(MyShadowsQuality.HIGH,.33f),(MyShadowsQuality.EXTREME,.44f),(MyShadowsQuality.DISABLED,0f)}){
                spotSettings.User.ShadowQuality=quality.Item1;scene.RenderSettings=spotSettings;render();
                expected=MatrixD.CreateLookAt(new Vector3D(0,0,-2),new Vector3D(0,0,-3),Vector3D.Up)*(MatrixD)Matrix.CreatePerspectiveFieldOfView(2*(float)Math.Acos(.99f),1,.5f,Math.Max(quality.Item2*20,1.5f));
                check(spotCapture.Packets[0].Flags==(quality.Item2>0?1u:0u)&&spotCapture.Packets[0].Projection.Zip(NativeBackend.MatrixValues(expected),(a,b)=>Math.Abs(a-b)).Max()<1e-6,"spot projection consumes the current reflector setting for "+quality.Item1);
            }
            projected.Owner.Destruct();created.Clear();Draw();
            check(backend.GetStats().ValidationErrors==0,"light discovery and source lifecycle have zero core/synchronization validation errors");
        }finally{field.SetValue(api,capture.Original);spotField.SetValue(spotApi,spotCapture.Original);foreach(var light in created)light.Owner.Destruct();foreach(var parent in parents)parent.Destruct();}
    }
}
