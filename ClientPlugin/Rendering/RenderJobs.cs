using System;
using ParallelTasks;

namespace ClientPlugin.Rendering;

// Managed render fan-out on SE's ParallelTasks scheduler at VeryHigh, as DX11's
// MyRenderScheduler does: SE maps VeryHigh to its own Highest-priority worker
// array, so render jobs share workers with the game by priority instead of
// competing from the .NET thread pool. Waiting runs a job that has not started
// on the waiting thread. Without a game scheduler (standalone tests), or with
// SE_VULKAN_MANAGED_JOBS=0, it falls back to the .NET thread pool.
internal static class RenderJobs
{
    static readonly bool Disabled=Environment.GetEnvironmentVariable("SE_VULKAN_MANAGED_JOBS")=="0";
    static readonly WorkOptions Options=new WorkOptions{MaximumThreads=1,DebugName="Vulkan render job"};
    public static bool OnGameScheduler=>!Disabled&&Parallel.Scheduler!=null&&Parallel.Scheduler.GetType().Name!="FakeTaskScheduler";

    // One started job; Wait() joins it (running it here if no worker took it) and rethrows its failure.
    public readonly struct Job
    {
        readonly ParallelTasks.Task task;readonly System.Threading.Tasks.Task fallback;readonly bool game;
        internal Job(ParallelTasks.Task task){this.task=task;fallback=null;game=true;}
        internal Job(System.Threading.Tasks.Task fallback){task=default;this.fallback=fallback;game=false;}
        public void Wait(){if(game)task.WaitOrExecute();else fallback?.GetAwaiter().GetResult();}
        // Joins without rethrowing, for dropped work.
        public void Join(){try{Wait();}catch(Exception){}}
    }
    public static Job Start(Action action)=>OnGameScheduler?new Job(Parallel.Start(WorkPriority.VeryHigh,action,Options)):new Job(System.Threading.Tasks.Task.Run(action));
    // Parallel for over [0,count); the caller takes part and every index has run on return.
    public static void For(int count,Action<int> body)
    {
        if(OnGameScheduler)Parallel.For(0,count,body,WorkPriority.VeryHigh);
        else System.Threading.Tasks.Parallel.For(0,count,body);
    }
}
