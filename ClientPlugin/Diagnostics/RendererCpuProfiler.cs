using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ClientPlugin.Diagnostics;

// Diagnostic scopes are inclusive. CPU counters name the owning render thread,
// including native calls on that thread, but exclude updater/recording workers.
// GetThreadTimes is quantized and aliases short phases. Use it only for the
// whole-frame aggregate; phase CPU attribution uses QueryThreadCycleTime.
// Fine actor scopes use only QPC: thousands of OS counter calls would distort
// the workload. Their CPU/allocation fields are explicitly unavailable.
public static class RendererCpuProfiler
{
    public enum Phase
    {
        Frame, Draw, Messages, Clipmaps, Particles, Scene, TextureLoads,
        TransformsBefore, ActorUpdater, TransformsAfter, SpatialIndex,
        LightsAndExclusions, ShadowPoses, Skybox, Occlusion, ActorTraversal,
        CandidateQuery, ActorAncestorVisibility, ActorViewGate, ActorSizeTest, ActorOcclusionTest, ActorLodSelection, CascadeMembership,
        NewViewPlan, NewViewEmission, PreparedActors, PreparedRebuild, PreparedReplay, LegacyRigidActors,
        LegacyInstancedActors, SkinnedActors, LegacyOpaqueParts, GlassParts,
        VoxelsAndBillboards, ShadowFit, LightingSetup, QueueWorld, BlobPack,
        NativeWorldQueue, TransparencyPack, NativeTransparencyQueue,
        NativeBillboardQueue, DecalQueue, HighlightQueue, ExclusionQueue,
        EndUploads, FrameSlot, Sprites, NativeRecord,
        NativePassCommits, NativePassPublication, NativePassFrameInputs,
        NativePassPrepare, NativePassResources, NativePassQueries,
        NativePassPublish, NativePassFallbacks, NativePassEndVisibility,
        GpuFrameInputs, GpuTopologyPublication, GpuResidentQueue, GpuRootQueries,
        GpuVisibilityPublication,
        MainOwnerDeclare, NewViewSpots, NewViewSelect, DrawWorldSetup, PreparedAppend, CandidatePlan, CellCommits, CellPublish, OffscreenTargets, OffscreenNative, VoxelCull, VoxelMaterials, VoxelEmit, Flares, Billboards, ParticleDraw, DecalDraw, Count
    }
    public static bool Enabled {get;}=Environment.GetEnvironmentVariable("SE_VULKAN_CPU_DETAIL")=="1";
    static readonly Counter[] Current=new Counter[(int)Phase.Count];
    static readonly Counter[] Completed=new Counter[(int)Phase.Count];
    static readonly Func<long> Allocated=GetAllocationCounter();
    static Scope frame;
    static bool active;
    static uint owner,completedOwner;
    static long sequence,completedSequence,readTicks,completedReadTicks;
    static int gen0,gen1,gen2,completedGen0,completedGen1,completedGen2;
    static Func<long> GetAllocationCounter()
    {
        var method=typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread",Type.EmptyTypes);
        return method==null?null:(Func<long>)Delegate.CreateDelegate(typeof(Func<long>),method);
    }
    // Exclusive wall ticks per render message type (MyRenderMessageEnum value); a DrawCommands
    // container keeps only its own time, its children count under their own types.
    const int MessageTypes=512,MessageNesting=8;
    static readonly long[] currentMessageTicks=new long[MessageTypes],completedMessageTicks=new long[MessageTypes];
    static readonly int[] currentMessageCalls=new int[MessageTypes],completedMessageCalls=new int[MessageTypes];
    static readonly long[] messageStart=new long[MessageNesting],messageChild=new long[MessageNesting];
    static int messageDepth;
    public static bool BeginMessage(){if(!Enabled||!active||messageDepth>=MessageNesting)return false;messageStart[messageDepth]=Stopwatch.GetTimestamp();messageChild[messageDepth]=0;messageDepth++;return true;}
    public static void EndMessage(int type){
        int d=--messageDepth;long total=Stopwatch.GetTimestamp()-messageStart[d];if(d>0)messageChild[d-1]+=total;
        if((uint)type<MessageTypes){currentMessageTicks[type]+=total-messageChild[d];currentMessageCalls[type]++;}
    }
    public static void BeginFrame()
    {
        if(!Enabled)return;
        Array.Clear(Current,0,Current.Length);readTicks=0;owner=GetCurrentThreadId();active=true;sequence++;
        Array.Clear(currentMessageTicks,0,MessageTypes);Array.Clear(currentMessageCalls,0,MessageTypes);messageDepth=0;
        gen0=GC.CollectionCount(0);gen1=GC.CollectionCount(1);gen2=GC.CollectionCount(2);
        frame=Measure(Phase.Frame);
    }
    public static void EndFrame()
    {
        if(!Enabled||!active)return;
        frame.Dispose();active=false;
        Array.Copy(Current,Completed,Current.Length);completedOwner=owner;completedSequence=sequence;completedReadTicks=readTicks;
        Array.Copy(currentMessageTicks,completedMessageTicks,MessageTypes);Array.Copy(currentMessageCalls,completedMessageCalls,MessageTypes);
        completedGen0=GC.CollectionCount(0)-gen0;completedGen1=GC.CollectionCount(1)-gen1;completedGen2=GC.CollectionCount(2)-gen2;
    }
    public static Scope Measure(Phase phase,bool counters=true)=>Enabled&&active?new Scope(phase,counters):default;
    public static Snapshot Capture()
        =>Describe(Completed,1,completedSequence,completedOwner,completedReadTicks);
    static Snapshot Describe(Counter[] values,long frames,long lastSequence,uint thread,long overhead)
    {
        var rows=new Row[values.Length];
        for(int i=0;i<rows.Length;i++){
            var c=values[i];double scale=1.0/Math.Max(1,frames);rows[i]=new Row{Phase=((Phase)i).ToString(),Calls=c.Calls*scale,
                WallMs=c.Wall*1000.0/Stopwatch.Frequency*scale,CpuMs=i==(int)Phase.Frame&&c.CpuCalls==c.Calls&&c.Calls!=0?c.Cpu*.0001*scale:(double?)null,
                CpuCycles=c.Cycles*scale,AllocatedBytes=c.Allocated*scale,CpuAvailable=c.CpuCalls==c.Calls&&c.Calls!=0,
                CyclesAvailable=c.CycleCalls==c.Calls&&c.Calls!=0,
                AllocationAvailable=Allocated!=null&&c.CycleCalls==c.Calls&&c.Calls!=0};
        }
        return new Snapshot{Enabled=Enabled,Frames=frames,Sequence=lastSequence,RenderThreadId=thread,
            CounterReadWallMs=overhead*1000.0/Stopwatch.Frequency/Math.Max(1,frames),Rows=rows};
    }
    // The harness calls this only on accepted completed frames, under its gate.
    // No per-frame snapshot arrays or diagnostic object allocations are needed.
    public sealed class Window
    {
        readonly Counter[] totals=new Counter[(int)Phase.Count];
        // Bounded diagnostic storage, allocated only when the profiler is enabled.
        // No frame snapshot, dictionary or phase array is allocated while sampling.
        const int Capacity=32768;
        readonly double[] walls=Enabled?new double[Capacity*(int)Phase.Count]:null;
        readonly FrameTail[] samples=Enabled?new FrameTail[Capacity]:null;
        int sampleCount;long dropped;
        long frames,lastSequence,overhead;uint thread;
        public void AddCompleted()=>AddCompleted(double.NaN);
        public void AddCompleted(double completedDrawMs)
        {
            if(!Enabled||completedSequence==lastSequence)return;
            if(frames==0)thread=completedOwner;else if(thread!=completedOwner)thread=0;
            for(int i=0;i<totals.Length;i++){ref var t=ref totals[i];var c=Completed[i];t.Calls+=c.Calls;t.CpuCalls+=c.CpuCalls;t.CycleCalls+=c.CycleCalls;t.Wall+=c.Wall;t.Cpu+=c.Cpu;t.Cycles+=c.Cycles;t.Allocated+=c.Allocated;}
            if(sampleCount<Capacity){
                var c=Completed[(int)Phase.Frame];samples[sampleCount]=new FrameTail{Sequence=completedSequence,CompletedDrawMs=completedDrawMs,
                    FrameWallMs=c.Wall*1000.0/Stopwatch.Frequency,FrameCpuMs=c.CpuCalls==c.Calls?c.Cpu*.0001:double.NaN,
                    AllocatedBytes=c.Allocated,Gen0Collections=completedGen0,Gen1Collections=completedGen1,Gen2Collections=completedGen2};
                for(int i=0;i<totals.Length;i++)walls[sampleCount*totals.Length+i]=Completed[i].Wall*1000.0/Stopwatch.Frequency;
                sampleCount++;
            }else dropped++;
            frames++;lastSequence=completedSequence;overhead+=completedReadTicks;
            bool spike=Completed[(int)Phase.Messages].Wall*1000.0/Stopwatch.Frequency>=MessageSpikeMs;if(spike)messageSpikeFrames++;
            for(int i=0;i<MessageTypes;i++){if(completedMessageCalls[i]==0)continue;messageTicks[i]+=completedMessageTicks[i];messageCalls[i]+=completedMessageCalls[i];if(spike)messageSpikeTicks[i]+=completedMessageTicks[i];}
        }
        // A frame whose Messages phase reaches this wall time is a spike frame.
        public const double MessageSpikeMs=2.0;
        readonly long[] messageTicks=Enabled?new long[MessageTypes]:null,messageCalls=Enabled?new long[MessageTypes]:null,messageSpikeTicks=Enabled?new long[MessageTypes]:null;
        long messageSpikeFrames;
        public MessageSnapshot CaptureMessages()
        {
            if(!Enabled||frames==0)return new MessageSnapshot{Enabled=Enabled,Rows=Array.Empty<MessageRow>()};
            double ms=1000.0/Stopwatch.Frequency;var rows=new System.Collections.Generic.List<MessageRow>();
            for(int i=0;i<MessageTypes;i++)if(messageCalls[i]!=0)rows.Add(new MessageRow{Type=((VRageRender.Messages.MyRenderMessageEnum)i).ToString(),CallsPerFrame=(double)messageCalls[i]/frames,MsPerFrame=messageTicks[i]*ms/frames,
                MsPerCall=messageTicks[i]*ms/messageCalls[i],MsPerSpikeFrame=messageSpikeFrames==0?0:messageSpikeTicks[i]*ms/messageSpikeFrames});
            rows.Sort((a,b)=>b.MsPerFrame.CompareTo(a.MsPerFrame));
            return new MessageSnapshot{Enabled=true,Frames=frames,SpikeFrames=messageSpikeFrames,SpikeThresholdMs=MessageSpikeMs,Rows=rows.ToArray()};
        }
        public Snapshot Capture()=>Describe(totals,frames,lastSequence,thread,overhead);
        public TailSnapshot CaptureTails()
        {
            if(!Enabled||sampleCount==0)return new TailSnapshot{Enabled=Enabled,Frames=0,DroppedSamples=dropped,Rows=Array.Empty<TailRow>(),Slowest=Array.Empty<SlowFrame>()};
            var order=new int[sampleCount];for(int i=0;i<order.Length;i++)order[i]=i;
            Array.Sort(order,(a,b)=>samples[a].FrameWallMs.CompareTo(samples[b].FrameWallMs));
            int tailStart=sampleCount-Math.Max(1,(int)Math.Ceiling(sampleCount*.05)),p95=Math.Max(0,(int)Math.Ceiling(sampleCount*.95)-1),gcTagged=0;
            for(int i=0;i<sampleCount;i++)if(samples[i].Gen0Collections+samples[i].Gen1Collections+samples[i].Gen2Collections>0)gcTagged++;
            var rows=new TailRow[totals.Length];var sorted=new double[sampleCount];
            for(int phase=0;phase<rows.Length;phase++){
                double all=0,tail=0;for(int i=0;i<sampleCount;i++){double value=walls[i*totals.Length+phase];all+=value;sorted[i]=value;}
                for(int i=tailStart;i<sampleCount;i++)tail+=walls[order[i]*totals.Length+phase];Array.Sort(sorted);
                rows[phase]=new TailRow{Phase=((Phase)phase).ToString(),MeanMs=all/sampleCount,P95Ms=sorted[p95],P99Ms=sorted[Math.Max(0,(int)Math.Ceiling(sampleCount*.99)-1)],MeanOnSlowest5PercentMs=tail/(sampleCount-tailStart)};
            }
            var slowest=new SlowFrame[Math.Min(16,sampleCount)];for(int i=0;i<slowest.Length;i++){
                int at=order[sampleCount-1-i];var phaseMs=new double[totals.Length];Array.Copy(walls,at*totals.Length,phaseMs,0,phaseMs.Length);
                slowest[i]=new SlowFrame{Frame=samples[at],PhaseWallMs=phaseMs};
            }
            return new TailSnapshot{Enabled=true,Frames=sampleCount,DroppedSamples=dropped,GcTaggedFrames=gcTagged,Rows=rows,Slowest=slowest};
        }
        public void Reset(){Array.Clear(totals,0,totals.Length);frames=lastSequence=overhead=dropped=0;sampleCount=0;thread=0;
            if(Enabled){Array.Clear(messageTicks,0,MessageTypes);Array.Clear(messageCalls,0,MessageTypes);Array.Clear(messageSpikeTicks,0,MessageTypes);}messageSpikeFrames=0;}
    }
    struct Counter {public long Calls,CpuCalls,CycleCalls,Wall,Cpu,Allocated;public ulong Cycles;}
    public struct Scope:IDisposable
    {
        readonly Phase phase;
        readonly bool live,counters,cpuOk,cyclesOk;
        readonly long wall,cpu,allocated;
        readonly ulong cycles;
        internal Scope(Phase phase,bool counters)
        {
            this.phase=phase;this.counters=counters;live=true;
            long start=Stopwatch.GetTimestamp();cpu=0;cycles=0;allocated=0;cpuOk=cyclesOk=false;
            if(counters){if(phase==Phase.Frame)cpuOk=ReadCpu(out cpu);cyclesOk=QueryThreadCycleTime(new IntPtr(-2),out cycles);allocated=Allocated?.Invoke()??0;}
            wall=Stopwatch.GetTimestamp();readTicks+=wall-start;
        }
        public void Dispose()
        {
            if(!live)return;
            long now=Stopwatch.GetTimestamp();ref var c=ref Current[(int)phase];c.Calls++;c.Wall+=now-wall;
            if(counters){
                long allocationEnd=Allocated?.Invoke()??0;
                if(cpuOk&&ReadCpu(out long cpuEnd)){c.Cpu+=Math.Max(0,cpuEnd-cpu);c.CpuCalls++;}
                if(cyclesOk&&QueryThreadCycleTime(new IntPtr(-2),out ulong cycleEnd)&&cycleEnd>=cycles){c.Cycles+=cycleEnd-cycles;c.CycleCalls++;}
                c.Allocated+=Math.Max(0,allocationEnd-allocated);
            }
            readTicks+=Stopwatch.GetTimestamp()-now;
        }
    }
    static bool ReadCpu(out long value)
    {
        bool ok=GetThreadTimes(new IntPtr(-2),out _,out _,out long kernel,out long user);value=kernel+user;return ok;
    }
    [DllImport("kernel32.dll")]static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll")][return:MarshalAs(UnmanagedType.Bool)]static extern bool GetThreadTimes(IntPtr thread,out long creation,out long exit,out long kernel,out long user);
    [DllImport("kernel32.dll")][return:MarshalAs(UnmanagedType.Bool)]static extern bool QueryThreadCycleTime(IntPtr thread,out ulong cycles);
    public sealed class Snapshot {public bool Enabled;public long Frames,Sequence;public uint RenderThreadId;public double CounterReadWallMs;public Row[] Rows;}
    public sealed class Row {public string Phase;public double Calls,WallMs,CpuCycles,AllocatedBytes;public double? CpuMs;public bool CpuAvailable,CyclesAvailable,AllocationAvailable;}
    public struct FrameTail {public long Sequence,AllocatedBytes;public double CompletedDrawMs,FrameWallMs,FrameCpuMs;public int Gen0Collections,Gen1Collections,Gen2Collections;}
    public sealed class SlowFrame {public FrameTail Frame;public double[] PhaseWallMs;}
    public sealed class TailRow {public string Phase;public double MeanMs,P95Ms,P99Ms,MeanOnSlowest5PercentMs;}
    public sealed class MessageRow {public string Type;public double CallsPerFrame,MsPerFrame,MsPerCall,MsPerSpikeFrame;}
    public sealed class MessageSnapshot {public bool Enabled;public long Frames,SpikeFrames;public double SpikeThresholdMs;public MessageRow[] Rows;}
    public sealed class TailSnapshot {public bool Enabled;public int Frames,GcTaggedFrames;public long DroppedSamples;public TailRow[] Rows;public SlowFrame[] Slowest;}
}
