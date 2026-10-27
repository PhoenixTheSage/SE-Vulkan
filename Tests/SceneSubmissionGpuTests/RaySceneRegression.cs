using System.Reflection;
using ClientPlugin.Rendering.Scene;
using VRage.Library.Utils;
using VRageMath;
using VRageRender;
using VRageRender.Messages;

partial class Program
{
    // Audit P3 step 3: the Vulkan RayScene v1 producer fills frames that SE-RT's own Core SceneAssembler accepts.
    static int RaySceneRegression(VulkanScene scene,string content,Action<bool,string> check)
    {
        int checks=0;void Check(bool ok,string text){check(ok,text);checks++;}
        var sert=Assembly.LoadFrom(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../SE-RT/ClientPlugin/bin/Release/net10.0/SERT.dll")));
        var assembler=sert.GetType("SERT.Core.SceneAssembler",true)!;
        object Build(object[] frame)=>assembler.GetMethod("Build")!.Invoke(null,new object[]{frame})!;
        ulong Signature(object[] frame)=>(ulong)assembler.GetMethod("Signature")!.Invoke(null,new object[]{frame})!;
        void Refresh(object geometry,object[] frame)=>assembler.GetMethod("Refresh")!.Invoke(null,new object[]{geometry,frame});
        T Field<T>(object o,string n)=>(T)o.GetType().GetField(n)!.GetValue(o)!;
        var capture=typeof(VulkanScene).GetMethod("CaptureRayScene",BindingFlags.Instance|BindingFlags.NonPublic)!;
        // A distant camera proves the double-precision camera-relative transforms SE-RT expects.
        var camera=new Vector3D(1e6,-2e5,3e5);scene.SetCamera(camera,MyTimeSpan.Zero,1f/60);scene.LatchFrame(); // the capture reads the latched (Build) view
        string armor=Path.Combine(content,"Models/Cubes/large/ArmorCenter.mwm"),shelf=Path.Combine(content,"Models/Cubes/Large/Shelf_1.mwm");
        scene.Process(new MyRenderMessageCreateRenderEntity{ID=9001,Model=armor,DebugName="ray armor",WorldMatrix=MatrixD.CreateTranslation(camera+new Vector3D(0,0,-5)),Flags=RenderFlags.Visible|RenderFlags.CastShadows});
        scene.Process(new MyRenderMessageCreateRenderEntity{ID=9002,Model=shelf,DebugName="ray shelf",WorldMatrix=MatrixD.CreateTranslation(camera+new Vector3D(3,0,-6)),Flags=RenderFlags.Visible|RenderFlags.CastShadows});
        scene.Process(new MyRenderMessageCreateRenderEntity{ID=9003,Model=armor,DebugName="ray far",WorldMatrix=MatrixD.CreateTranslation(camera+new Vector3D(0,0,-400)),Flags=RenderFlags.Visible|RenderFlags.CastShadows});
        scene.Process(new MyRenderMessageCreateRenderLight{ID=9010,DebugName="ray spot"});
        var layout=new MyLightLayout{Range=20,Color=new Vector3(1,.5f,.25f),Falloff=1,DiffuseFactor=1,GlossFactor=.5f};
        scene.Process(new MyRenderMessageUpdateRenderLight{Data=new UpdateRenderLightData{ID=9010,Position=camera+new Vector3D(0,2,0),SpotLightOn=true,SpotIntensity=1,
            SpotLight=new MySpotLightLayout{Direction=Vector3.Forward,Up=Vector3.Up,ApertureCos=.8f,Light=layout}}});
        object[] Capture()=>(object[])capture.Invoke(scene,null)!;
        object[] Frame(object[] c,uint index)=>new object[]{1,1,index,c[5],c[0],c[1],c[2],null!,new[]{64,64},new float[12],new float[16],new float[16],Array.Empty<object[]>(),c[3],c[4],null!};
        var first=Capture();var meshes=(object[][])first[0];var instances=(object[][])first[1];var lights=(float[])first[2];
        var ours=instances.Where(i=>(uint)i[0]>=9001&&(uint)i[0]<=9003).ToArray();
        Check(ours.Length==2&&ours.All(i=>(uint)i[0]!=9003),"actors within 256 m are captured and the 400 m actor is excluded");
        var armorRow=ours.Single(i=>(uint)i[0]==9001);var world=(float[])armorRow[2];var box=(float[])armorRow[4];
        Check(Math.Abs(world[12])<1e-4&&Math.Abs(world[13])<1e-4&&Math.Abs(world[14]+5)<1e-4&&Math.Abs((box[2]+box[5])/2+5)<.01f,"instances are camera-relative (row-major translation and bounds) at a 1e6 m camera");
        var armorMesh=meshes[(int)armorRow[1]];var verts=(float[])armorMesh[1];var indices=(int[])armorMesh[2];var parts=(object[][])armorMesh[3];
        Check(verts.Length%16==0&&parts.All(p=>(int)p[0]+(int)p[1]<=indices.Length&&(int)p[1]%3==0)&&(bool)armorMesh[4]==false,"mesh records carry 16-float vertices and in-range part index ranges");
        int spot=Enumerable.Range(0,lights.Length/16).Single(i=>BitConverter.ToUInt32(BitConverter.GetBytes(lights[i*16+13]),0)==9010);
        Check(lights[spot*16+12]==1&&Math.Abs(lights[spot*16+11]-.8f)<1e-6&&Math.Abs(lights[spot*16+1]-2)<1e-4&&lights[spot*16+3]==20&&lights[spot*16+4]==1,"spotlight packs kind, cone, camera-relative position, range and colour");

        // SE-RT's own assembler builds BLAS/TLAS from the Vulkan frame.
        var frame=Frame(first,1);var geometry=Build(frame);
        int expectedTriangles=ours.Sum(i=>((object[][])meshes[(int)i[1]][3]).Sum(p=>(int)p[1])/3);
        var triangles=Field<Array>(geometry,"Triangles");var built=Field<Array>(geometry,"Instances");var materials=Field<Array>(geometry,"Materials");var nodes=Field<Array>(geometry,"Nodes");
        int ourMaterials=ours.Sum(i=>((object[])i[3]).Length),allMaterials=instances.Sum(i=>((object[])i[3]).Length);
        Console.WriteLine("RAYSCENE meshes="+meshes.Length+" instances="+instances.Length+" triangles="+triangles.Length+" materials="+materials.Length+" nodes="+nodes.Length);
        Check(built.Length==instances.Length&&materials.Length==allMaterials&&triangles.Length>=expectedTriangles&&nodes.Length>0&&Field<uint>(geometry,"TlasRoot")<nodes.Length,"SE-RT SceneAssembler builds the Vulkan frame: every instance, material and triangle with a TLAS");

        // Stable topology and boxed texture identities: an unchanged recapture refreshes without material churn.
        var again=Frame(Capture(),2);ulong signature=Signature(frame);
        Check(Signature(again)==signature,"an unchanged recapture keeps SE-RT's topology signature");
        uint Revision(int i)=>Field<uint>(materials.GetValue(i)!,"Revision");
        var revisions=Enumerable.Range(0,materials.Length).Select(Revision).ToArray();Refresh(geometry,again);materials=Field<Array>(geometry,"Materials");
        Check(Enumerable.Range(0,materials.Length).All(i=>Revision(i)==revisions[i]),"refreshing an unchanged frame keeps every material revision (texture objects compare by reference)");

        // Moving an actor refreshes its world transform without a rebuild.
        var move=new MyRenderMessageUpdateRenderObject{ID=9001};move.Data.WorldMatrix=MatrixD.CreateTranslation(camera+new Vector3D(1,0,-5));move.Data.HasWorldMatrix=true;scene.Process(move);
        var moved=Frame(Capture(),3);Check(Signature(moved)==signature,"moving an actor keeps the topology signature");
        Refresh(geometry,moved);var instanceRows=Field<Array>(geometry,"Instances");var sources=Field<object[][]>(geometry,"InstanceSources");
        int at=Array.FindIndex(sources,s=>(uint)s[0]==9001);var refreshedWorld=Field<System.Numerics.Matrix4x4>(instanceRows.GetValue(at)!,"World");
        Check(Math.Abs(refreshedWorld.M41-1)<1e-4&&Math.Abs(refreshedWorld.M43+5)<1e-4,"Refresh carries the moved camera-relative transform");

        // A per-material colour override reaches the refreshed material.
        var armorPart=(string)((object[])((object[])((object[])armorRow[3])[0])[0])[3];
        scene.Process(new MyRenderMessageUpdateColorEmissivity{ID=9001,MaterialName=VRage.Utils.MyStringId.GetOrCompute(armorPart),DiffuseColor=Color.Red,Emissivity=3});
        var tinted=Frame(Capture(),4);var tintedRow=((object[][])tinted[5]).Single(i=>(uint)i[0]==9001);var multiplier=(float[])((object[])((object[])tintedRow[3])[0])[1];
        Check(multiplier[0]==1&&multiplier[1]==0&&multiplier[3]==3,"material colour and emissivity overrides reach the variant multiplier");
        Refresh(geometry,tinted);materials=Field<Array>(geometry,"Materials");var baseIndex=(int)Field<uint>(instanceRows.GetValue(at)!,"MaterialBase");
        var emission=Field<System.Numerics.Vector3>(materials.GetValue(baseIndex)!,"Emission");
        Check(Math.Abs(emission.X-3)<1e-6&&Revision(baseIndex)!=revisions[baseIndex],"Refresh applies the override and bumps that material's revision");
        foreach(uint id in new uint[]{9001,9002,9003})scene.Process(new MyRenderMessageRemoveRenderObject{ID=id});
        return checks;
    }
}
