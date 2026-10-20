using ClientPlugin.Rendering.Content;
using VRageRender.Import;

static partial class Program
{
    static void TransientIdentityChecks(Action<bool,string> check)
    {
        var pool=new StockOldResourceIndices();var materials=new StockOldMaterialIdentities();materials.RegisterCommonMaterials();var layouts=new StockOldVertexLayouts();layouts.RegisterBeforeBuiltinMeshes();
        var owner=new StockOldTransientIdentities(pool,materials,layouts);
        int file=pool.Allocate();var first=owner.CreateRuntime("runtime",2,true);var cell=owner.CreateVoxel();var retained=owner.CreateRuntime("static",1,false);
        check(file==0&&first.ResourceIndex==1&&cell.ResourceIndex==2&&retained.ResourceIndex==3,"file/runtime/voxel identity creation consumes one shared LOD sequence");
        check(first.LayoutIndex==0&&first.Material(0)==0&&first.Material(1)==0,"runtime creation retains source default layout/part identities until update");
        int material=materials.GetOrCreate(new StockOldMaterialIdentity("runtime-material","","","","",MyMeshDrawTechnique.MESH));int before=materials.Count;
        owner.UpdateRuntime(first,new[]{"runtime-material","missing"});check(first.ResourceIndex==1&&first.LayoutIndex==layouts.File(new(false,true,false))&&first.Material(0)==material&&first.Material(1)==0,"runtime update reuses LOD allocation and resolves section materials by existing name");
        check(materials.Count==before&&pool.Count==4,"runtime update does not allocate materials or another LOD identity");
        owner.UpdateRuntime(first,new[]{"runtime-material","runtime-material"});owner.UpdateRuntime(first,new[]{"missing"});check(first.Material(0)==0&&first.Material(1)==material,"short runtime section updates leave remaining part identities untouched");
        check(owner.FindRuntime("runtime")==first&&owner.FindRuntime("RUNTIME")==null,"runtime name lookup preserves ordinal stock spelling");
        var replacement=owner.CreateRuntime("runtime",0,true);check(replacement.ResourceIndex==4&&owner.FindRuntime("runtime")==replacement&&owner.RuntimeAllocations==3,"duplicate runtime creation replaces only the name alias and keeps the old allocation");
        owner.SessionEnd();check(owner.RuntimeAllocations==2&&owner.VoxelAllocations==0&&pool.Count==3,"session end retires named dynamic and voxel allocations while retaining static and overwritten runtime resources");
        check(owner.FindRuntime("static")==null&&owner.FindRuntime("runtime")==null,"session end clears runtime name aliases even for retained static resources");
        check(pool.Allocate()==2&&pool.Allocate()==4,"session retirement preserves dynamic-then-voxel LIFO reuse order");
        check(materials.Count==before&&layouts.Count>0,"session end preserves material and layout identity clocks");
        check(!owner.RemoveVoxel(cell)&&!owner.RemoveRuntime(replacement),"repeated transient removal consumes no resource identity");
        var sameName=owner.CreateRuntime("static",1,false);check(sameName!=retained&&sameName.ResourceIndex==5,"a later session can create a new allocation under a previously retained static name");
        check(owner.RemoveRuntime(retained)&&owner.FindRuntime("static")==null&&owner.RuntimeAllocations==2,"removing an older same-name runtime clears the current name alias like stock");
        check(pool.Allocate()==3,"explicit removal returns retained source LOD index to the shared pool");
    }
}
