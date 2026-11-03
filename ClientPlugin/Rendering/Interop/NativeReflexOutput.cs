using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace ClientPlugin.Rendering.Interop;

public sealed partial class NativeBackend
{
    internal readonly struct ReflexOutputToken
    {
        internal readonly ulong Epoch, Parent, Ordinal, Generation;
        internal bool Tracked => Parent != 0;
        internal ReflexOutputToken(ulong epoch, ulong parent, ulong ordinal, ulong generation)
        {
            if (parent == 0 ? epoch != 0 || ordinal != 0 || generation != 0 : epoch == 0 || generation == 0)
                throw new ArgumentException("A Reflex output requires its complete epoch, parent and generation");
            Epoch = epoch; Parent = parent; Ordinal = ordinal; Generation = generation;
        }
    }
    [StructLayout(LayoutKind.Sequential)]
    struct ReflexOutputPacket
    {
        internal uint Size, Version;
        internal ulong Epoch, Parent, Ordinal, Generation;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int ReflexBindOutputFn(IntPtr context, ref ReflexOutputPacket packet);
    ReflexBindOutputFn reflexBindOutput;
    ReflexSleepFn reflexRetire;
    bool reflexBindMissing, reflexRetireMissing;
    long retiredReflexEpoch;
    internal int LastReflexOutputResult { get; private set; }
    internal bool ReflexLifecycleReady
    {
        get
        {
            reflexLife.EnterReadLock();
            try
            {
                if (context == IntPtr.Zero || LoadReflexControls() == null) return false;
                lock (reflexExportsGate)
                {
                    if (reflexBindMissing || reflexRetireMissing) return false;
                    try
                    {
                        if (reflexBindOutput == null) reflexBindOutput = Marshal.GetDelegateForFunctionPointer<ReflexBindOutputFn>(GetSymbol("sevk_reflex_bind_output"));
                        if (reflexRetire == null) reflexRetire = Marshal.GetDelegateForFunctionPointer<ReflexSleepFn>(GetSymbol("sevk_reflex_retire"));
                        return true;
                    }
                    catch (EntryPointNotFoundException) { return false; }
                }
            }
            finally { reflexLife.ExitReadLock(); }
        }
    }
    [ThreadStatic] static OutputLease reflexOutputLease;

    bool Retired(ReflexOutputToken token) => token.Tracked && token.Epoch <= unchecked((ulong)Volatile.Read(ref retiredReflexEpoch));
    internal ReflexOutputToken CurrentReflexOutput
    {
        get
        {
            var lease = reflexOutputLease;
            return lease != null && ReferenceEquals(lease.Owner, this) && lease.Valid && !Retired(lease.Token) ? lease.Token : default;
        }
    }

    int SetReflexOutput(ReflexOutputToken token)
    {
        reflexLife.EnterReadLock();
        try
        {
            if (context == IntPtr.Zero) return -4;
            lock (reflexExportsGate)
            {
                if (reflexBindMissing) return -7;
                if (reflexBindOutput == null)
                {
                    try { reflexBindOutput = Marshal.GetDelegateForFunctionPointer<ReflexBindOutputFn>(GetSymbol("sevk_reflex_bind_output")); }
                    catch (EntryPointNotFoundException) { reflexBindMissing = true; return -7; }
                }
            }
            var packet = new ReflexOutputPacket { Size = 40, Version = AbiVersion, Epoch = token.Epoch,
                Parent = token.Parent, Ordinal = token.Ordinal, Generation = token.Generation };
            return reflexBindOutput(context, ref packet);
        }
        finally { reflexLife.ExitReadLock(); }
    }

    internal int ReflexRetire(ulong epoch)
    {
        if (epoch == 0) return -1000001004;
        reflexLife.EnterReadLock();
        try
        {
            if (context == IntPtr.Zero) return -4;
            lock (reflexExportsGate)
            {
                if (reflexRetireMissing) return -7;
                if (reflexRetire == null)
                {
                    try { reflexRetire = Marshal.GetDelegateForFunctionPointer<ReflexSleepFn>(GetSymbol("sevk_reflex_retire")); }
                    catch (EntryPointNotFoundException) { reflexRetireMissing = true; return -7; }
                }
            }
            int result = reflexRetire(context, epoch);
            if (result == 0)
            {
                long observed;
                do { observed = Volatile.Read(ref retiredReflexEpoch); if (unchecked((ulong)observed) >= epoch) break; }
                while (Interlocked.CompareExchange(ref retiredReflexEpoch, unchecked((long)epoch), observed) != observed);
            }
            return result;
        }
        finally { reflexLife.ExitReadLock(); }
    }

    internal readonly struct ReflexOutputScope : IDisposable
    {
        readonly OutputLease lease;
        internal ReflexOutputScope(OutputLease value) { lease = value; }
        internal bool Bound => lease != null && lease.Active && lease.Valid && !lease.Owner.Retired(lease.Token);
        internal int Result => lease?.Result ?? 0;
        internal int RestoreResult => lease?.RestoreResult ?? 0;
        public void Dispose() { lease?.Dispose(); }
    }
    internal sealed class OutputLease : IDisposable
    {
        internal readonly NativeBackend Owner;
        internal readonly ReflexOutputToken Token;
        internal readonly OutputLease Previous;
        internal bool Valid;
        internal readonly int Result;
        internal int RestoreResult;
        readonly int thread;
        bool disposed;
        internal bool Active => !disposed;
        internal OutputLease(NativeBackend owner, ReflexOutputToken token)
        {
            Owner = owner; Token = token; Previous = reflexOutputLease; thread = Thread.CurrentThread.ManagedThreadId;
            int result = owner.SetReflexOutput(token);
            Result = result;
            owner.LastReflexOutputResult = result;
            Valid = result == 0;
            reflexOutputLease = this;
        }
        public void Dispose()
        {
            if (disposed) return;
            if (Thread.CurrentThread.ManagedThreadId != thread || !ReferenceEquals(reflexOutputLease, this))
                throw new InvalidOperationException("Reflex output scopes must close on their entering thread in reverse order");
            disposed = true;
            var previous = Previous;
            var restoreOwner = previous?.Owner ?? Owner;
            var restore = previous != null && previous.Valid && !restoreOwner.Retired(previous.Token) ? previous.Token : default;
            int result = restoreOwner.SetReflexOutput(restore);
            RestoreResult = result;
            restoreOwner.LastReflexOutputResult = result;
            if (previous != null && result != 0) previous.Valid = false;
            reflexOutputLease = previous;
        }
    }
    internal ReflexOutputScope BindReflexOutput(ReflexOutputToken token) =>
        !token.Tracked && reflexOutputLease == null ? default : new ReflexOutputScope(new OutputLease(this, token));
}
