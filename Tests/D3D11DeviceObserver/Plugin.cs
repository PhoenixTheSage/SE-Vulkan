using VRage.Plugins;

namespace D3D11DeviceObserver
{
    // Test-only local plugin. No renderer or SharpDX assembly references.
    public sealed class Plugin : IPlugin
    {
        public void Init(object gameInstance) => Observer.Milestone("plugin-init");
        public void Update() => Observer.Refresh();
        public void Dispose() => Observer.Milestone("plugin-dispose");
    }
}
