internal static class RendererSystemClassifier {
    // These classify disjoint sample stacks, not inclusive function totals.
    public static string Classify(IReadOnlyList<string> names) {
        bool Has(params string[] tokens)=>names.Any(n=>tokens.Any(n.Contains));
        if(Has("VulkanScene.Draw(","VulkanScene.PublishNativePassScene(","Renderer::prepare_pass_frame","Renderer::pass_actor_input","native_pass::Scene::prepare","native_pass::Scene::publish","native_pass::Scene::update_actors","MyGeometryRenderer.Preprocess(","MyPreparePass`","MyPreparePass<","MyPrepareGroupPass","MyGeometryRendererOld.UpdateCullProxies(","MyGeometryRendererOld.ProcessCullProxies(","MyGeometryRendererOld.Prepare(","MyFrustumCullingWork","MyActorOcclusionRenderer","MyOcclusionTask"))return "Scene preparation/culling";
        if(Has("NativeBackend.QueueWorld("))return "Packet bridge and native queue";
        if(Has("Renderer::end_frame","Renderer::record_cascade_secondary","Renderer::record_world_selection","Renderer::render_spot_shadows","MyDepthRenderPass.DrawInstanceLodGroup","MyGBufferRenderPass.DrawInstanceLodGroup","MyForwardRenderPass.DrawInstanceLodGroup","MyRenderingWork.Render(","MyRenderingPass.RecordCommands"))return "Geometry recording/submission";
        if(Has("VulkanVoxels.Draw(","MyVoxelClipmap.Update(","MyClipmap.Update(","MyRenderVoxelActor"))return "Voxel preparation";
        if(Has("MyLightsRendering","MyShadowCascades.PrepareQueries","MyEnvironmentProbe","MyPostprocess"))return "Lighting/shadow/postprocess preparation";
        return "Other renderer/engine";
    }
}
