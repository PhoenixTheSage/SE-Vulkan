using System;
using System.Linq;
using System.Runtime.InteropServices;
using ClientPlugin.Rendering.Shaders;

namespace ClientPlugin.Rendering.Interop;

[StructLayout(LayoutKind.Sequential, Pack=8)]
public struct NativeStageBindingState
{
    public uint Size, AbiVersion, Flags, Rows, FrameIndex, Reserved;
    public ulong Resolved, Missing, Mismatched;
}

public sealed partial class NativeBackend
{
    [StructLayout(LayoutKind.Sequential, Pack=8)]
    struct StageBindingRow
    {
        public uint Size, AbiVersion, Stage, FrameIndex, Requested, Fresh, Attachments, Reserved;
        public ulong Velocity;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst=10)] public ulong[] Images;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int SetStageBindingsFn(IntPtr context,[In] StageBindingRow[] rows,uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int StageBindingStateFn(IntPtr context,ref NativeStageBindingState state);
    SetStageBindingsFn stageBindings;
    StageBindingStateFn bindingState;
    internal bool SetStageBindings(uint frame,uint[] ids,ulong[] images,uint[] requested,uint[] fresh,uint[] attachments,ulong velocity)
    {
        if((GuestFeatures()&4194304)==0)throw new NotSupportedException("Update SE-Vulkan: native stage bindings require bit22.");
        if(ids==null||images==null||requested==null||fresh==null||attachments==null||ids.Length>82||
            images.Length!=ids.Length*10||requested.Length!=ids.Length||fresh.Length!=ids.Length||attachments.Length!=ids.Length)
            throw new ArgumentException("Native stage binding row lengths differ.");
        var rows=new StageBindingRow[ids.Length];
        for(int i=0;i<rows.Length;i++){
            var rowImages=new ulong[10];Array.Copy(images,i*10,rowImages,0,10);
            rows[i]=new StageBindingRow{Size=120,AbiVersion=AbiVersion,Stage=ids[i],FrameIndex=frame,Requested=requested[i],Fresh=fresh[i],Attachments=attachments[i],Velocity=velocity,Images=rowImages};
        }
        stageBindings??=PostFunction<SetStageBindingsFn>("sevk_set_stage_bindings");
        Check(stageBindings(context,rows,(uint)rows.Length),context);return true;
    }
    public NativeStageBindingState GetStageBindingState()
    {
        if((GuestFeatures()&4194304)==0)throw new NotSupportedException("Update SE-Vulkan: native stage bindings require bit22.");
        var state=new NativeStageBindingState{Size=48,AbiVersion=AbiVersion};
        bindingState??=PostFunction<StageBindingStateFn>("sevk_get_stage_binding_state");Check(bindingState(context,ref state),context);return state;
    }
    public object[] StageBindingState()
    {
        var state=GetStageBindingState();return new object[]{state.Flags,state.Rows,state.FrameIndex,state.Resolved,state.Missing,state.Mismatched};
    }
    static bool UsesStageBindings(ShaderMetadata layout)=>layout.Descriptors.Any(d=>d.Set==0&&(d.RegisterClass=='b'&&d.RegisterIndex==6||d.RegisterClass=='t'&&d.RegisterIndex>=5&&d.RegisterIndex<=9));
    public object[][] StageBindingOwners()
    {
        if(ActivePostprocessGeneration==null||postprocessLibrary==null)return Array.Empty<object[]>();
        return ActivePostprocessGeneration.Layouts.Where(p=>ActivePostprocessGeneration.Fingerprints.ContainsKey(p.Key)&&UsesStageBindings(p.Value)).Select(p=>{
            uint mask=0;foreach(var d in p.Value.Descriptors)if(d.Set==0&&d.RegisterClass=='t'&&d.RegisterIndex>=5&&d.RegisterIndex<=9)mask|=1u<<(int)d.RegisterIndex;
            bool extras=p.Value.Descriptors.Any(d=>d.Set==0&&d.RegisterClass=='b'&&d.RegisterIndex==6);
            return new object[]{p.Key,postprocessLibrary.Load(NativeOwnerVariant(p.Key)).Source,mask,extras};
        }).ToArray();
    }
}
