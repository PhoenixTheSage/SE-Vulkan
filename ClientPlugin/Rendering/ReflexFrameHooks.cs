using System;
using System.Threading;
using ClientPlugin.Rendering.Interop;

namespace ClientPlugin.Rendering;

// Anomaly owns admission and the actual game queue. This boundary only brackets
// the renderer's Apply and transports an immutable, device-free output tuple.
internal static class ReflexFrameHooks
{
    internal sealed class Callbacks
    {
        internal readonly Func<object, bool, ulong[]> Begin;
        internal readonly Action<bool, int> End;
        internal readonly Func<int, bool> Validate;
        internal Callbacks(Func<object, bool, ulong[]> begin, Action<bool, int> end, Func<int, bool> validate) { Begin = begin; End = end; Validate = validate; }
    }
    static Callbacks current;
    internal static bool Connected => Volatile.Read(ref current) != null;
    internal static string Status { get; private set; } = "Reflex frame source not connected";
    internal static bool Set(Func<object, bool, ulong[]> begin, Action<bool, int> end, Func<int, bool> validate = null)
    {
        if ((begin == null) != (end == null)) return false;
        Interlocked.Exchange(ref current, begin == null ? null : new Callbacks(begin, end, validate));
        Status = begin == null ? "Reflex frame source not connected" : "Reflex frame source connected";
        return true;
    }

    internal static FrameScope Begin(NativeBackend backend, object shared, bool draw, Action joinPrevious)
    {
        var callbacks = Volatile.Read(ref current);
        if (callbacks == null) return null; // Inactive path keeps existing overlap.
        // Before Apply drains textures, before any resource/guest or game queue
        // lock. Join CPU Build/Launch only: no wait for GPU or physical present.
        // Native ordinary resource calls already wait any recording in flight.
        joinPrevious();
        var scope = new FrameScope(callbacks);
        scope.Start(backend, shared, draw);
        return scope;
    }

    internal sealed class FrameScope : IDisposable
    {
        readonly Callbacks callbacks;
        NativeBackend.ReflexOutputScope native, suppression;
        NativeBackend backend;
        bool validated, suppressed;
        bool entered, produced, closed;
        readonly int thread = Thread.CurrentThread.ManagedThreadId;
        int result;
        internal FrameScope(Callbacks owner) { callbacks = owner; }
        internal void Start(NativeBackend backend, object shared, bool draw)
        {
            this.backend = backend;
            try
            {
                var packet = callbacks.Begin(shared, draw);
                NativeBackend.ReflexOutputToken token = default;
                if (packet != null)
                {
                    if (packet.Length != 4) throw new ArgumentException("Reflex output tuple requires four unsigned values");
                    token = new NativeBackend.ReflexOutputToken(packet[0], packet[1], packet[2], packet[3]);
                }
                if (!draw && token.Tracked) throw new InvalidOperationException("Draw(false) cannot own a Reflex output");
                native = backend.BindReflexOutput(token); entered = true;
                if (token.Tracked && !native.Bound) result = native.Result != 0 ? native.Result : -1000001004;
            }
            catch (Exception error)
            {
                result = -3;
                Status = "Reflex frame start failed: " + error.GetBaseException().Message;
                Dispose(); // Let Anomaly cancel a partially opened admission now.
            }
        }
        // Called after the actual queue and billboard reads, before FramePacket
        // captures attribution. Apply may already have submitted provisional
        // uploads; cancellation cannot retract those submissions.
        internal bool Validate()
        {
            if (Thread.CurrentThread.ManagedThreadId != thread) throw new InvalidOperationException("Reflex validation must run on its entering thread");
            if (closed) return false;
            if (validated) return !suppressed && result == 0;
            validated = true;
            bool accepted = result == 0;
            try { if (callbacks.Validate != null) accepted = callbacks.Validate(result) && accepted; }
            catch (Exception error) { accepted = false;Status = "Reflex frame validation failed: " + error.GetBaseException().Message; }
            if (!accepted)
            {
                if (result == 0) result = -1000001004;
                // Explicitly clear attribution even if host retirement failed.
                // Keep this nested scope alive until the original scope exits.
                suppression = backend.BindReflexOutput(default);suppressed = true;
            }
            return accepted;
        }
        internal void Produced()
        {
            if (closed) return;
            if (Thread.CurrentThread.ManagedThreadId != thread) throw new InvalidOperationException("Reflex Apply completion must run on its entering thread");
            produced = true;
        }
        public void Dispose()
        {
            if (closed) return;
            if (Thread.CurrentThread.ManagedThreadId != thread) throw new InvalidOperationException("Reflex Apply cleanup must run on its entering thread");
            closed = true;
            try
            {
                if (suppressed) suppression.Dispose();
                if (entered)
                {
                    native.Dispose();
                    if (result == 0) result = native.RestoreResult;
                }
            }
            catch (Exception error) { result = -3; Status = "Reflex frame cleanup failed: " + error.GetBaseException().Message; }
            finally
            {
                // Pair with the callback captured at entry, even if the host
                // replaced/unregistered it while this Apply was in progress.
                try { callbacks.End(produced && result == 0, result); }
                catch (Exception error) { Status = "Reflex frame end failed: " + error.GetBaseException().Message; }
            }
        }
    }
}
