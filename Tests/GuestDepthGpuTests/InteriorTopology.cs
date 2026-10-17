using System.Reflection;
using VRage.Game.ModAPI;
using VRageMath;

partial class Program
{
    static int InteriorTopology(string bin)
    {
        int checks=0;void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        var core=Assembly.LoadFrom(Path.GetFullPath("../Anomaly/ClientPlugin/bin/Release/net10.0/Anomaly.dll"));
        var trackedType=core.GetType("ClientPlugin.ShaderFramework.VolumetricInteriorSnapshot+Tracked",true);
        object Track(GasStub gas)=>Activator.CreateInstance(trackedType,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{gas},null);
        void Request(object tracked)=>trackedType.GetMethod("RequestInitialTopology",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(tracked,null);
        long Generation(object tracked)=>(long)trackedType.GetField("Generation",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(tracked);
        var gas=new SupportedGasStub();int previousStarts=0;gas.OnProcessingDataStart=()=>previousStarts++;
        var lease=Track(gas);Check(gas.ForcePressurize,"actual snapshot requests topology independently of vent/oxygen presence");
        Request(lease);Check(gas.Starts==1 && gas.IsProcessingData && previousStarts==1 && Generation(lease)==1,"initial request starts generation and preserves previous callback");
        Request(lease);Check(gas.Starts==1,"pending topology generation is not started twice");
        gas.Complete();Check(Generation(lease)==2,"completion invalidates the captured topology generation");
        Request(lease);Check(gas.Starts==1,"initial generation remains a one-shot after completion");
        var dirty=trackedType.GetMethod("TopologyChanged",BindingFlags.Instance|BindingFlags.NonPublic);
        dirty.Invoke(lease,null);long dirtyGeneration=Generation(lease);
        Check(dirtyGeneration==3 && trackedType.GetProperty("NeedsTopologyRefresh",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(lease) is true,"grid or door edits immediately invalidate prior room provenance");
        dirty.Invoke(lease,null);Request(lease);Request(lease);
        Check(gas.Starts==2 && gas.IsProcessingData,"multiple topology edits coalesce into one full regeneration");
        gas.Complete();Check(Generation(lease)==6,"replacement topology completion advances generation after edits");
        ((IDisposable)lease).Dispose();Check(!gas.ForcePressurize,"snapshot release restores its previous force flag");
        long disposedGeneration=Generation(lease);gas.Start();Check(Generation(lease)==disposedGeneration && previousStarts==3,"release unsubscribes only its own callbacks");
        var preexisting=new SupportedGasStub{ForcePressurize=true};((IDisposable)Track(preexisting)).Dispose();Check(preexisting.ForcePressurize,"pre-existing topology demand survives snapshot release");
        var unsupported=new GasStub();object missing=Track(unsupported);Request(missing);Check(!unsupported.IsProcessingData,"unsupported generation hook cannot claim processing or completed topology");((IDisposable)missing).Dispose();Check(!unsupported.ForcePressurize,"unsupported hook still releases its topology demand");
        Console.WriteLine("PASS "+checks+" actual-core interior topology lifecycle checks");return 0;
    }
    class GasStub:IMyGridGasSystem
    {
        public bool ForcePressurize{get;set;}public bool IsProcessingData{get;protected set;}
        public Action OnProcessingDataStart{get;set;}public Action OnProcessingDataComplete{get;set;}
        public bool GetRooms(List<IMyOxygenRoom> list)=>false;
        public IMyOxygenRoom GetOxygenRoomForCubeGridPosition(ref Vector3I position)=>null;
        public IMyOxygenBlock GetOxygenBlock(Vector3D position)=>null;
        public int Starts;
        public void Start(){Starts++;IsProcessingData=true;OnProcessingDataStart?.Invoke();}
        public void Complete(){IsProcessingData=false;OnProcessingDataComplete?.Invoke();}
    }
    sealed class SupportedGasStub:GasStub {void StartGenerateAirtightData()=>Start();}
}
