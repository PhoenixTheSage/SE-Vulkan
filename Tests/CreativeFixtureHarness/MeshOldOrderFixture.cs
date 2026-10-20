using System.IO;
using System.Threading;
using System.Xml.Linq;
using FixtureAssets;
using VRageMath;
using VRageRender;
using VRageRender.Import;

namespace CreativeFixtureHarness;

public sealed partial class Plugin
{
    void VerifyOldPartIdentities()
    {
        lightingDx11Capture.DeclaringType.GetMethod("VerifyOldPartIdentities",System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static).Invoke(null,new object[]{reports});
    }
    void BuildMeshOldOrder(Vector3D camera,XElement manifest)
    {
        manifest.SetAttributeValue("OldResourceOrder",true);
        manifest.SetAttributeValue("Scope","OLD cutout models preloaded A then B; actor creation forward, reverse and A recreation. Stock global sort keys and held GBuffer/depth captures establish the oracle.");
        string root=Path.Combine(reports,"MeshDecalAssets");
        string background=MeshDecalFixtureAssets.Model(Path.Combine(root,"base"),MyMeshDrawTechnique.MESH,1,255,singleLayer:true,neutral:true);
        string red=MeshDecalFixtureAssets.Model(Path.Combine(root,"A"),MyMeshDrawTechnique.DECAL_CUTOUT,1,255,singleLayer:true);
        string blue=MeshDecalFixtureAssets.Model(Path.Combine(root,"B"),MyMeshDrawTechnique.DECAL_CUTOUT,1,255,singleLayer:true,blue:true);
        foreach(string model in new[]{background,red,blue})MyRenderProxy.PreloadModel(model,1,true);
        uint Create(string model,Vector3D center,string label){uint id=MyRenderProxy.CreateRenderEntity("OLD resource order "+label,model,MatrixD.CreateScale(.9)*MatrixD.CreateTranslation(center),MyMeshDrawTechnique.MESH,RenderFlags.Visible|RenderFlags.ForceOldPipeline,(CullingOptions)0,Color.White,Vector3.Zero);meshDecalCreated.Add(id);return id;}
        for(int column=0;column<4;column++){
            var center=camera+new Vector3D((column-1.5)*2.3,1.75,-8);
            Create(background,center,"base-"+column);
            uint a,b;
            if(column==1){b=Create(blue,center+new Vector3D(0,0,.01),"B-first");a=Create(red,center+new Vector3D(0,0,.02),"A-second");}
            else {a=Create(red,center+new Vector3D(0,0,.02),"A-first");b=Create(blue,center+new Vector3D(0,0,.01),"B-second");}
            if(column==2){MyRenderProxy.RemoveRenderObject(a,MyRenderProxy.ObjectType.Entity);meshDecalCreated.Remove(a);a=Create(red,center+new Vector3D(0,0,.02),"A-recreated");}
            manifest.Add(new XElement("Panel",new XAttribute("Id",a),new XAttribute("Row",1),new XAttribute("Column",column),new XAttribute("Technique",MyMeshDrawTechnique.DECAL_CUTOUT),new XAttribute("TextureRole",1),new XAttribute("OldRequested",true),new XAttribute("Case",column==1?"B-A":column==2?"A-B-A-recreated":"A-B"),new XAttribute("Model",red),new XAttribute("World",MatrixD.CreateScale(.9)*MatrixD.CreateTranslation(center))));
        }
        Volatile.Write(ref meshDecalIds,meshDecalCreated.ToArray());new XDocument(manifest).Save(Path.Combine(reports,"mesh-decals-assets.xml"));Camera(camera,camera+Vector3D.Forward);
    }
}
