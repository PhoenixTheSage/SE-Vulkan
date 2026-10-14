using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Reflection;
using System.Text.Json;
using Mono.Cecil;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using VRageMath;
using VRageRender.Messages;
class Program {
static int Main(string[] args){string bin=Path.GetFullPath(args[0]);AssemblyLoadContext.Default.Resolving+=(_,n)=>{string p=Path.Combine(bin,n.Name+".dll");return File.Exists(p)?AssemblyLoadContext.Default.LoadFromAssemblyPath(p):null;};return Run(bin);}
[MethodImpl(MethodImplOptions.NoInlining)]static int Run(string bin){int checks=0;void Check(bool b,string t){if(!b)throw new Exception(t);checks++;Console.WriteLine("PASS "+t);}
var contract=typeof(VulkanScene).Assembly.GetType("ClientPlugin.Rendering.Scene.ParticleCpuContract");void Validate(string path){try{contract.GetMethod("Check",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new[]{path});}catch(TargetInvocationException e){throw e.InnerException;}}
var exports=typeof(NativeBackend).GetMethod("ValidateRequiredSymbols",BindingFlags.NonPublic|BindingFlags.Static);exports.Invoke(null,new object[]{new Func<string,IntPtr>(_=>new IntPtr(1))});Check(true,"complete native ABI export inventory is accepted before activation");bool missingExport=false;try{exports.Invoke(null,new object[]{new Func<string,IntPtr>(name=>name=="sevk_init_particles"?IntPtr.Zero:new IntPtr(1))});}catch(TargetInvocationException e) when(e.InnerException is EntryPointNotFoundException){missingExport=true;}Check(missingExport,"missing particle export is rejected before renderer activation");
string source=Path.Combine(bin,"VRage.Render11.dll");Validate(source);Check(true,"all three installed CPU particle hook fingerprints match");
string output=Path.GetFullPath("TestResults/ParticleBridgeTests");Directory.CreateDirectory(output);string broken=Path.Combine(output,"changed-renderer.dll");using(var asm=AssemblyDefinition.ReadAssembly(source)){var method=asm.MainModule.GetType("VRage.Render11.Particles.MyParticleLight").Methods.Single(m=>m.Name=="Update");method.Body.Instructions.Insert(0,Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Nop));asm.Write(broken);}bool rejected=false;try{Validate(broken);}catch(InvalidDataException){rejected=true;}Check(rejected,"changed stock CPU hook IL is rejected before activation");
Check(VulkanScene.FlareCurve(null,0)==1&&VulkanScene.FlareCurve(null,1)<0,"stock default flare curve responds to occlusion");var curve=new[]{new MySubGlare.KeyPoint{Occlusion=0,Intensity=2},new MySubGlare.KeyPoint{Occlusion=1,Intensity=0}};Check(VulkanScene.FlareCurve(curve,.25f)==1.5f&&VulkanScene.FlareCurve(curve,2)==0,"custom flare curves interpolate and clamp");
VRage.FileSystem.MyFileSystem.Init(Path.Combine(bin,"../Content"),Path.Combine(output,"UserData"));using var backend=new NativeBackend(null);using var scene=new VulkanScene(backend);
var light=scene.ActorFactory.CreateLight("CPU particle light");Check(light.Owner.GetComponent<VRage.Render.Scene.Components.MyLightComponent>()==light,"particle actor exposes API-neutral light component");light.Owner.Destruct();Check(scene.LightCount==0,"particle light destruction unregisters scene ownership");
var decalsType=typeof(VulkanScene).Assembly.GetType("ClientPlugin.Rendering.Scene.VulkanDecals");var decals=Activator.CreateInstance(decalsType,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,null,new object[]{backend,scene},null);object Call(string name,params object[] a)=>decalsType.GetMethod(name).Invoke(decals,a);
var parents=new uint[]{123};var topo=new MyDecalTopoData{MatrixCurrent=Matrix.Identity,MatrixBinding=Matrix.Identity};Call("Process",new MyRenderMessageCreateScreenDecal{ID=7,ParentIDs=parents,TopoData=topo,Material="CPU",RenderSqDistance=100,TimeUntilLive=int.MaxValue});parents[0]=999;
Call("Observe",new MyRenderMessageRemoveRenderObject{ID=999});Check((int)decalsType.GetProperty("Count").GetValue(decals)==1,"decal parent IDs are copied before pooled message reuse");Call("Observe",new MyRenderMessageRemoveRenderObject{ID=123});Check((int)decalsType.GetProperty("Count").GetValue(decals)==0,"parent removal discards attached copied decals");
File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{Checks=checks,Scope="Hook IL rejection, flare curves, API-neutral light and copied decal ownership"}));return 0;}}
