using System;
using System.Collections.Generic;
using VRage.Import;
using VRageMath;
using VRageMath.PackedVector;
using VRageRender.Import;
namespace ClientPlugin.Rendering.Content;
internal sealed partial class VulkanModelCache
{
    // CPU metadata only: MyModel.IsModelSuitable/MyMwmData stream requirements.
    private static bool StockNewModelDataSuitable(Dictionary<string,object> tags){
        if(!tags.TryGetValue("Vertices",out var p)||p is not HalfVector4[] positions||positions.Length==0)return false;
        if(tags.TryGetValue("BlendIndices",out var indices)&&indices is Vector4I[] bones&&bones.Length!=0)return false;
        if(tags.TryGetValue("BlendWeights",out var weights)&&weights is Vector4[] blend&&blend.Length!=0)return false;
        if(!tags.TryGetValue("Normals",out var normals)||normals is not Byte4[] n||n.Length!=positions.Length || !tags.TryGetValue("Tangents",out var tangents)||tangents is not Byte4[] t||t.Length!=positions.Length || !tags.TryGetValue("Binormals",out var bitangents)||bitangents is not Byte4[] b||b.Length!=positions.Length || !tags.TryGetValue("TexCoords0",out var uv)||uv is not HalfVector2[] u||u.Length!=positions.Length)return false;
        if(!tags.TryGetValue("MeshParts",out var parts)||parts is not List<MyMeshPartInfo> list)return false;
        foreach(var part in list){if(part.m_MaterialDesc!=null && part.m_MaterialDesc.Facing!=MyFacingEnum.None)return false;
            switch(part.Technique){case MyMeshDrawTechnique.MESH:case MyMeshDrawTechnique.ALPHA_MASKED:case MyMeshDrawTechnique.ALPHA_MASKED_SINGLE_SIDED:case MyMeshDrawTechnique.DECAL:case MyMeshDrawTechnique.DECAL_NOPREMULT:case MyMeshDrawTechnique.DECAL_CUTOUT:case MyMeshDrawTechnique.HOLO:case MyMeshDrawTechnique.GLASS:case MyMeshDrawTechnique.SHIELD:case MyMeshDrawTechnique.SHIELD_LIT:break;default:return false;}}
        return true;
    }
    public bool IsStockNewModel(VulkanModel root){
        if(root==null)return false;if(root.NewPipelineEligible.HasValue)return root.NewPipelineEligible.Value;
        bool eligible=root.NewPipelineDataEligible;var bounds=root.AuthoredBounds??root.Bounds;
        foreach(var descriptor in root.LodDescriptors){if(!eligible)break;
            try{string path=descriptor.GetModelAbsoluteFilePath(VulkanTextureCache.ResolvePath(root.Name));if(path==null){eligible=false;break;}
                var importer=new MyModelImporter();importer.ImportData(path,Tags);var tags=importer.GetTagData();
                BoundingBox? authoredBox=tags.TryGetValue("BoundingBox",out var box)?(BoundingBox)box:null;
                if(tags.TryGetValue("GeometryDataAsset",out var geometry)){string geometryPath=new MyLODDescriptor{Model=(string)geometry}.GetModelAbsoluteFilePath(VulkanTextureCache.ResolvePath(path));importer.ImportData(geometryPath,Tags);tags=importer.GetTagData();}
                eligible=StockNewModelDataSuitable(tags);
                if(eligible&&authoredBox.HasValue){var lodBox=authoredBox.Value;bounds.Min=Vector3.Min(bounds.Min,lodBox.Min*root.Scale);bounds.Max=Vector3.Max(bounds.Max,lodBox.Max*root.Scale);}
                if(eligible)foreach(var part in (List<MyMeshPartInfo>)tags["MeshParts"])root.StockMaterialNames.Add(part.GetMaterialName()??string.Empty);
            }catch{eligible=false;}
        }
        root.StockNewBounds=eligible?bounds:null;root.NewPipelineEligible=eligible;return eligible;
    }
    private BoundingBox? stockLoadingBounds;
    public BoundingBox StockNewCullingBounds(VulkanModel root){
        if(!IsStockNewModel(root))return root.Bounds;
        if(!stockLoadingBounds.HasValue){var importer=new MyModelImporter();importer.ImportData(VulkanTextureCache.ResolvePath("Models/Debug/LoadingQuad.mwm"),new[]{"BoundingBox"});stockLoadingBounds=(BoundingBox)importer.GetTagData()["BoundingBox"];}
        // MyInstanceComponent initially adds the asynchronous loading model;
        // MyInstance.SetModel adds the final union without replacing it.
        return (root.StockNewBounds??root.Bounds).Include(stockLoadingBounds.Value);
    }
    public bool IsStockMaterialUsed(VulkanModel root,string material)=>IsStockNewModel(root)&&root.StockMaterialNames.Contains(material??string.Empty);
}
