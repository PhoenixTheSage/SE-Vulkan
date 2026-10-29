using System;
using System.Runtime.InteropServices;

namespace ClientPlugin.Rendering.Interop;

[StructLayout(LayoutKind.Sequential,Pack=8)]public struct NativeCellPart {public ulong Recipe;public uint ViewFlags,Row0,Row1,Row2,AlphaOffset,MainPart,ConstantOffset,ConstantBytes;public NativeOldPartOrder OldOrder;}
[StructLayout(LayoutKind.Sequential,Pack=4)]public struct NativeCellLevel {public uint Model,PartOffset,PartCount,Reserved;}
[StructLayout(LayoutKind.Sequential,Pack=4)]public struct NativeCellLod {public int Current,Next;public float Transition,Start;public uint Initialized,Border;public int ShadowCurrent,ShadowNext;public float ShadowAlpha;public uint Reserved;}
[StructLayout(LayoutKind.Sequential,Pack=8)]public unsafe struct NativeCell
{
    public uint Size,Owner,Flags,InstanceCount;public ulong Group,InstanceOffset;
    public fixed double Bounds[6];public fixed float LocalMinimum[3];public fixed float LocalMaximum[3];
    public fixed double World[16];public fixed double Inverse[16];
    public uint LevelOffset,LevelCount,ThresholdOffset,ThresholdCount;public NativeCellLod Lod;
}
[StructLayout(LayoutKind.Sequential,Pack=8)]public unsafe struct NativeCellPose {public uint Size,Flags;public ulong Group;public fixed double Bounds[6];public fixed float LocalMinimum[3];public fixed float LocalMaximum[3];public fixed double World[16];public fixed double Inverse[16];}
[StructLayout(LayoutKind.Sequential,Pack=8)]public unsafe struct NativeCellCascade {public uint Active,SizeTest,Fitted,Reserved;public double SizeSquared;public fixed double Planes[24];}
[StructLayout(LayoutKind.Sequential,Pack=8)]public unsafe struct NativeCellFrame
{
    public uint Size,Flags;public fixed double Camera[3];public fixed double MainPlanes[24];public double ProbeDistance,ShadowDistance;
    public NativeCellCascade C0,C1,C2,C3,C4,C5,C6,C7;
    public float Coefficient,Delta,MaximumSeconds,DistanceAdd,DistanceMult;public int Selection,MinimumLod,Shift;
}
[StructLayout(LayoutKind.Sequential,Pack=8)]public struct NativeCellQuery {public ulong Group;public uint Representative,Cascades,Main,Reserved;}
[StructLayout(LayoutKind.Sequential,Pack=8)]public struct NativeCellResult {public ulong Group;public uint MainHidden,CascadesHidden;}
[StructLayout(LayoutKind.Sequential,Pack=8)]public struct NativeCellChange {public double X,Y,Z,Radius;}
[StructLayout(LayoutKind.Sequential,Pack=8)]public struct NativeCellStats {public uint Size,Live,Drawn,Draws;public ulong ObjectBytes,InstanceBytes;}

public sealed partial class NativeBackend
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int CellsCommitFn(IntPtr context,IntPtr cells,uint count,IntPtr handles,IntPtr output,IntPtr levels,uint levelCount,IntPtr parts,uint partCount,IntPtr thresholds,uint thresholdCount,IntPtr bytes,ulong byteCount);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int CellsPosesFn(IntPtr context,IntPtr handles,IntPtr poses,uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int CellsRemoveFn(IntPtr context,ulong handle,out NativeCellLod lod);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int CellsPrepareFn(IntPtr context,ref NativeCellFrame frame);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int CellsPreparationFn(IntPtr context,IntPtr queries,uint queryCapacity,out uint queryCount,IntPtr visible,uint visibleCapacity,out uint visibleCount,IntPtr changes,uint changeCapacity,out uint changeCount);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int CellsResultsFn(IntPtr context,IntPtr results,uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int CellsActionFn(IntPtr context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int CellsStatsFn(IntPtr context,ref NativeCellStats stats);
    CellsCommitFn cellsCommit;CellsPosesFn cellsPoses;CellsRemoveFn cellsRemove;CellsPrepareFn cellsPrepare;CellsPreparationFn cellsPreparation;CellsResultsFn cellsPublish;CellsActionFn cellsCancel,cellsEmit;CellsStatsFn cellsStats;
    // Set by a cell publication; QueueWorld appends the native cell draws once.
    internal bool CellEmissionPending;
    static void CheckCellAbi(){
        if(Marshal.SizeOf<NativeCellPart>()!=72||Marshal.SizeOf<NativeCellLevel>()!=16||Marshal.SizeOf<NativeCellLod>()!=40||Marshal.SizeOf<NativeCell>()!=416||Marshal.SizeOf<NativeCellCascade>()!=216||Marshal.SizeOf<NativeCellFrame>()!=2000||Marshal.SizeOf<NativeCellQuery>()!=24||Marshal.SizeOf<NativeCellResult>()!=16||Marshal.SizeOf<NativeCellChange>()!=32||Marshal.SizeOf<NativeCellStats>()!=32||Marshal.SizeOf<NativeCellPose>()!=344)
            throw new InvalidOperationException("Native OLD cell ABI mismatch.");
    }
    T CellSymbol<T>(ref T field,string name) where T:Delegate{if(field==null){CheckCellAbi();field=(T)Marshal.GetDelegateForFunctionPointer(GetSymbol(name),typeof(T));}return field;}
    public unsafe void CommitCells(NativeCell[] cells,int count,ulong[] handles,ulong[] output,NativeCellLevel[] levels,int levelCount,NativeCellPart[] parts,int partCount,float[] thresholds,int thresholdCount,byte[] bytes,int byteCount)
    {
        if(count==0)return;
        fixed(NativeCell* c=cells)fixed(ulong* h=handles)fixed(ulong* o=output)fixed(NativeCellLevel* l=levels)fixed(NativeCellPart* p=parts)fixed(float* t=thresholds)fixed(byte* b=bytes)
            Check(CellSymbol(ref cellsCommit,"sevk_cells_commit")(context,(IntPtr)c,(uint)count,(IntPtr)h,(IntPtr)o,(IntPtr)l,(uint)levelCount,(IntPtr)p,(uint)partCount,(IntPtr)t,(uint)thresholdCount,(IntPtr)b,(ulong)byteCount),context);
    }
    public unsafe void UpdateCellPoses(ulong[] handles,NativeCellPose[] poses,int count){fixed(ulong* h=handles)fixed(NativeCellPose* p=poses)Check(CellSymbol(ref cellsPoses,"sevk_cells_update_poses")(context,(IntPtr)h,(IntPtr)p,(uint)count),context);}
    public NativeCellLod RemoveCell(ulong handle){Check(CellSymbol(ref cellsRemove,"sevk_cells_remove")(context,handle,out var lod),context);return lod;}
    CellsPrepareFn cellsPrepareAsync;bool cellsPrepareAsyncMissing;
    // Plan 2 A2: the same preparation on a native background job; PrepareCells reuses it when its frame is identical.
    public bool PrepareCellsAsync(ref NativeCellFrame frame){
        if(cellsPrepareAsyncMissing)return false;
        try{CellSymbol(ref cellsPrepareAsync,"sevk_cells_prepare_async");}catch(Exception){cellsPrepareAsyncMissing=true;return false;}
        frame.Size=2000;Check(cellsPrepareAsync(context,ref frame),context);return true;
    }
    public void PrepareCells(ref NativeCellFrame frame){CellEmissionPending=false;frame.Size=2000;Check(CellSymbol(ref cellsPrepare,"sevk_cells_prepare")(context,ref frame),context);}
    NativeCellQuery[] cellQueries=Array.Empty<NativeCellQuery>();uint[] cellVisible=Array.Empty<uint>();NativeCellChange[] cellChanges=Array.Empty<NativeCellChange>();
    public unsafe (NativeCellQuery[] Queries,int QueryCount,uint[] Visible,int VisibleCount,NativeCellChange[] Changes,int ChangeCount) GetCellPreparation()
    {
        var api=CellSymbol(ref cellsPreparation,"sevk_cells_preparation");
        for(;;){
            uint queries,visible,changes;
            fixed(NativeCellQuery* q=cellQueries)fixed(uint* v=cellVisible)fixed(NativeCellChange* c=cellChanges)
                Check(api(context,(IntPtr)q,(uint)cellQueries.Length,out queries,(IntPtr)v,(uint)cellVisible.Length,out visible,(IntPtr)c,(uint)cellChanges.Length,out changes),context);
            if(queries<=cellQueries.Length&&visible<=cellVisible.Length&&changes<=cellChanges.Length)return (cellQueries,(int)queries,cellVisible,(int)visible,cellChanges,(int)changes);
            if(queries>cellQueries.Length)cellQueries=new NativeCellQuery[Math.Max(queries,(uint)cellQueries.Length*2)];
            if(visible>cellVisible.Length)cellVisible=new uint[Math.Max(visible,(uint)cellVisible.Length*2)];
            if(changes>cellChanges.Length)cellChanges=new NativeCellChange[Math.Max(changes,(uint)cellChanges.Length*2)];
        }
    }
    public unsafe void PublishCells(NativeCellResult[] results,int count){fixed(NativeCellResult* r=results)Check(CellSymbol(ref cellsPublish,"sevk_cells_publish")(context,(IntPtr)r,(uint)count),context);CellEmissionPending=true;}
    public void CancelCells(){CellEmissionPending=false;Check(CellSymbol(ref cellsCancel,"sevk_cells_cancel")(context),context);}
    void EmitCells(){if(!CellEmissionPending)return;CellEmissionPending=false;Check(CellSymbol(ref cellsEmit,"sevk_cells_emit")(context),context);}
    public NativeCellStats GetCellStats(){var stats=new NativeCellStats{Size=32};Check(CellSymbol(ref cellsStats,"sevk_cells_stats")(context,ref stats),context);return stats;}
}
