using System.Collections.Generic;
using VRage;
using VRageMath;
using VRageRender;
using VRageRender.Messages;

namespace ClientPlugin.Rendering.Scene;

// Phase 4 frame split, milestone 2, slice 4 (Docs/planning/PLAN-2026-10-25-MEET-DX11.md, item 0): scene state that
// messages own and Build reads. Messages write the Apply copies below; LatchFrame folds them into the fields Build
// already reads (FoldSceneState): colour-mask HSV, bloom emissiveness, the skybox, the new-LOD settings, highlights,
// instance buffers (a snapshot per changed buffer: generic arrays are written in place by range updates, cube arrays
// are replaced per update) and the frame counter Build's occlusion groups compare against.
public sealed partial class VulkanScene
{
    Vector3 applyColorizeHsv=new Vector3(MyTextureDebugMultipliers.Defaults.ColorizeHSV);
    float applyBloomEmissiveness=1;
    (string Path,Quaternion Rotation)? pendingSkybox;
    readonly MyNewLoddingSettings applyLodSettings=new();bool lodSettingsChanged;
    readonly Dictionary<uint,List<Highlight>> applyHighlights=new();readonly HashSet<uint> applyOverlappingHighlights=new();bool highlightsChanged;
    readonly Dictionary<uint,InstanceBuffer> applyInstances=new();readonly HashSet<uint> instancesChanged=new();
    // The scene frame counter (static on MyScene, incremented by the scene update) as of the latched frame.
    long BuildFrameCounter;
    void MarkInstances(uint id)=>instancesChanged.Add(id);
    void FoldSceneState()
    {
        colorizeHsv=applyColorizeHsv;bloomEmissiveness=applyBloomEmissiveness;
        if(pendingSkybox is {} sky){skybox.Set(sky.Path,sky.Rotation);pendingSkybox=null;}
        if(lodSettingsChanged){actorLodSettings.CopyFrom(applyLodSettings);lodSettingsChanged=false;}
        if(highlightsChanged)
        {
            highlights.Clear();foreach(var item in applyHighlights)highlights.Add(item.Key,new List<Highlight>(item.Value));
            overlappingHighlights.Clear();overlappingHighlights.UnionWith(applyOverlappingHighlights);highlightsChanged=false;
        }
        foreach(uint id in instancesChanged){if(applyInstances.TryGetValue(id,out var buffer))instances[id]=SnapshotInstances(buffer);else instances.Remove(id);}
        instancesChanged.Clear();
        BuildFrameCounter=FrameCounter;
    }
    static InstanceBuffer SnapshotInstances(InstanceBuffer source)=>new InstanceBuffer{Generic=(MyInstanceData[])source.Generic?.Clone(),Cubes=source.Cubes,EnabledSkinning=source.EnabledSkinning,
        Parent=source.Parent,Revision=source.Revision,OldLayout=source.OldLayout,OldCube=source.OldCube};
    // Verify mode: after the latch Build's copies equal the Apply state.
    string SceneStateMismatch()
    {
        if(colorizeHsv!=applyColorizeHsv||bloomEmissiveness!=applyBloomEmissiveness)return "Environment";
        if(highlights.Count!=applyHighlights.Count||overlappingHighlights.Count!=applyOverlappingHighlights.Count)return "Highlights";
        if(instances.Count!=applyInstances.Count)return "Instances";
        foreach(var item in applyInstances)if(!instances.TryGetValue(item.Key,out var copy)||copy.Revision!=item.Value.Revision||!ReferenceEquals(copy.Cubes,item.Value.Cubes)||(copy.Generic?.Length??-1)!=(item.Value.Generic?.Length??-1))return "Instances";
        if(BuildFrameCounter!=FrameCounter)return "FrameCounter";
        return null;
    }
}
