using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.World;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;

namespace CreativeFixtureHarness;

public sealed partial class Plugin
{
    MyCubeGrid interiorParityGrid;
    bool interiorParityComplete;
    int interiorParityPhase;
    static readonly Vector3I InteriorWall=new Vector3I(0,0,-1);
    const BindingFlags InteriorFields=BindingFlags.Instance|BindingFlags.NonPublic;

    void BeginInteriorParity()
    {
        long owner=MySession.Static.LocalHumanPlayer?.Identity.IdentityId??0;
        var shell=Grid("Anomaly ventless airtight parity room",MyCubeSize.Large,new Vector3D(25,0,0),true);
        for(int x=-1;x<=1;x++)for(int y=-1;y<=1;y++)for(int z=-1;z<=1;z++)
            if(x!=0 || y!=0 || z!=0)shell.CubeBlocks.Add(Block<MyObjectBuilder_CubeBlock>("LargeBlockArmorBlock",new Vector3I(x,y,z),owner));
        interiorParityGrid=(MyCubeGrid)MyEntities.CreateFromObjectBuilderAndAdd(shell,false);
        if(interiorParityGrid==null)throw new InvalidOperationException("Ventless parity room creation failed.");
        // This temporary test entity belongs only to the marked private session.
        interiorParityGrid.Save=false;
    }

    void ExerciseInteriorParity()
    {
        if(interiorParityComplete || interiorParityGrid==null)return;
        var owner=expectedBackend.ToString()=="Dx11"?AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(a=>a.GetName().Name=="Anomaly.Dx11"):anomalyAssembly;
        var snapshotType=owner?.GetType("ClientPlugin.ShaderFramework.VolumetricInteriorSnapshot");
        var snapshot=snapshotType?.GetMethod("Capture",BindingFlags.Static|BindingFlags.NonPublic)?.Invoke(null,null);
        if(snapshot==null || snapshot.GetType().GetField("Complete",InteriorFields)?.GetValue(snapshot) is not true)return;
        object grid=null;
        foreach(object candidate in (IEnumerable)snapshot.GetType().GetField("Grids",InteriorFields).GetValue(snapshot))
            if((long)candidate.GetType().GetField("Id",InteriorFields).GetValue(candidate)==interiorParityGrid.EntityId){grid=candidate;break;}
        if(grid==null || grid.GetType().GetProperty("IsCurrent",InteriorFields)?.GetValue(grid) is not true)return;
        var cells=(Vector3I[])grid.GetType().GetField("Cells",InteriorFields).GetValue(grid);
        bool sealedCenter=cells.Contains(Vector3I.Zero);
        var gas=((IMyCubeGrid)interiorParityGrid).GasSystem;
        var rooms=new List<IMyOxygenRoom>();
        if(gas==null || gas.IsProcessingData || !gas.GetRooms(rooms))return;
        bool engineSealed=rooms.Any(r=>r.IsAirtight && !r.IsDirty && r.Blocks.Contains(Vector3I.Zero));
        if(engineSealed!=sealedCenter)return;
        if(interiorParityPhase==0 && sealedCenter) {
            if(rooms.Where(r=>r.IsAirtight).Any(r=>r.OxygenAmount!=0))throw new InvalidOperationException("Ventless room unexpectedly contains oxygen.");
            File.AppendAllText(Path.Combine(reports,"anomaly-interior-history.txt"),"Sealed zero-oxygen center is excluded by the actual shared snapshot.\n");
            interiorParityGrid.RemoveBlock(interiorParityGrid.GetCubeBlock(InteriorWall),true);
            interiorParityPhase=1;
        } else if(interiorParityPhase==1 && !sealedCenter) {
            File.AppendAllText(Path.Combine(reports,"anomaly-interior-history.txt"),"Removed real wall: center is no longer excluded after engine topology completion.\n");
            var wall=Block<MyObjectBuilder_CubeBlock>("LargeBlockArmorBlock",InteriorWall,MySession.Static.LocalHumanPlayer?.Identity.IdentityId??0);
            if(((IMyCubeGrid)interiorParityGrid).AddBlock(wall,false)==null)throw new InvalidOperationException("Parity wall could not be rebuilt.");
            interiorParityPhase=2;
        } else if(interiorParityPhase==2 && sealedCenter) {
            interiorParityComplete=true;
            File.WriteAllText(Path.Combine(reports,"anomaly-interior-passed.txt"),"Actual engine gas topology and shared snapshot agree: sealed zero-oxygen room -> removed wall/open -> rebuilt wall/sealed.\n");
        }
    }
}
