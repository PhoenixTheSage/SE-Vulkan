using System;
using System.Reflection;
using System.Collections.Concurrent;
using VRage;
using VRage.Render.Particles;
using VRage.Render.Scene;
using VRageMath;
using VRageRender;
using VRageRender.Messages;
namespace ClientPlugin.Rendering.Scene;
internal static class StockParticles
{
    internal static readonly Assembly Assembly=System.Reflection.Assembly.Load("VRage.Render11");
    internal static Type Type(string name)=>Assembly.GetType(name,true);
    internal static readonly Type Emitters=Type("VRageRender.MyGPUEmitters"),Render=Type("VRageRender.MyRender11"),Common=Type("VRageRender.MyCommon"),Managers=Type("VRage.Render11.Common.MyManagers"),Light=Type("VRage.Render11.Particles.MyParticleLight");
    internal static object Get(object target,string name)=>MemberGet(target.GetType(),target,name);
    internal static object StaticGet(Type type,string name)=>MemberGet(type,null,name);
    private static readonly ConcurrentDictionary<(Type,string,bool),MemberInfo> members=new ConcurrentDictionary<(Type,string,bool),MemberInfo>();
    private static readonly ConcurrentDictionary<(Type,string,bool),MethodInfo> methods=new ConcurrentDictionary<(Type,string,bool),MethodInfo>();
    private static MemberInfo Member(Type type,string name,bool isStatic)=>members.GetOrAdd((type,name,isStatic),key=>{var flags=BindingFlags.Public|BindingFlags.NonPublic|(isStatic?BindingFlags.Static:BindingFlags.Instance);return (MemberInfo)type.GetField(name,flags)??type.GetProperty(name,flags)??throw new MissingMemberException(type.FullName,name);});
    private static object MemberGet(Type type,object target,string name){var m=Member(type,name,target==null);return m is FieldInfo f?f.GetValue(target):((PropertyInfo)m).GetValue(target,null);}
    internal static void Set(object target,string name,object value)=>MemberSet(target.GetType(),target,name,value);
    internal static void StaticSet(Type type,string name,object value)=>MemberSet(type,null,name,value);
    private static void MemberSet(Type type,object target,string name,object value){var m=Member(type,name,target==null);if(m is FieldInfo f)f.SetValue(target,value);else ((PropertyInfo)m).SetValue(target,value,null);}
    internal static object Call(object target,string name,params object[] args)=>Invoke(target.GetType(),target,name,args);
    internal static object StaticCall(Type type,string name,params object[] args)=>Invoke(type,null,name,args);
    private static object Invoke(Type type,object target,string name,object[] args){try{return methods.GetOrAdd((type,name,target==null),key=>type.GetMethod(name,BindingFlags.Public|BindingFlags.NonPublic|(target==null?BindingFlags.Static:BindingFlags.Instance))??throw new MissingMethodException(type.FullName,name)).Invoke(target,args);}catch(TargetInvocationException e){throw e.InnerException??e;}}
    internal static object Manager=>StaticGet(Managers,"ParticleEffectsManager");
}
internal sealed class StockEffect
{
    private readonly object value;internal StockEffect(object value){this.value=value;}
    public float GetElapsedTime()=>(float)StockParticles.Call(value,"GetElapsedTime");public float GetLoopTime()=>(float)StockParticles.Call(value,"GetLoopTime");public float GetScale()=>(float)StockParticles.Call(value,"GetScale");
    public float UserBirthMultiplier=>(float)StockParticles.Get(value,"UserBirthMultiplier");public bool IsStopped=>(bool)StockParticles.Get(value,"IsStopped");public uint ParentID=>(uint)StockParticles.Get(value,"ParentID");public MatrixD WorldMatrix=>(MatrixD)StockParticles.Get(value,"WorldMatrix");public Vector3 Gravity=>(Vector3)StockParticles.Get(value,"Gravity");
}
internal sealed class StockLight
{
    private readonly object value;internal StockLight(object value){this.value=value;}
    private T G<T>(string n)=>(T)StockParticles.Get(value,n);private void S<T>(string n,T v)=>StockParticles.Set(value,n,v);
    public MyParticleLightData m_data=>G<MyParticleLightData>("m_data");public StockEffect m_effect=>new StockEffect(StockParticles.Get(value,"m_effect"));
    public MyActor m_light{get=>G<MyActor>("m_light");set=>S("m_light",value);}public bool Enabled=>G<bool>("Enabled");public string Name=>G<string>("Name");
public Vector3D m_position{get=>G<Vector3D>("m_position");set=>S("m_position",value);}
public Vector4 m_color{get=>G<Vector4>("m_color");set=>S("m_color",value);}
public float m_range{get=>G<float>("m_range");set=>S("m_range",value);}
public float m_intensity{get=>G<float>("m_intensity");set=>S("m_intensity",value);}
public float m_falloff{get=>G<float>("m_falloff");set=>S("m_falloff",value);}
public Vector3 m_localPositionVarRnd{get=>G<Vector3>("m_localPositionVarRnd");set=>S("m_localPositionVarRnd",value);}
public float m_colorVarRnd{get=>G<float>("m_colorVarRnd");set=>S("m_colorVarRnd",value);}
public float m_lastVarianceTime{get=>G<float>("m_lastVarianceTime");set=>S("m_lastVarianceTime",value);}
public float m_rangeVarRnd{get=>G<float>("m_rangeVarRnd");set=>S("m_rangeVarRnd",value);}
public float m_intensityRnd{get=>G<float>("m_intensityRnd");set=>S("m_intensityRnd",value);}
public uint m_parentID{get=>G<uint>("m_parentID");set=>S("m_parentID",value);}
public UpdateRenderLightData m_renderLightData{get=>G<UpdateRenderLightData>("m_renderLightData");set=>S("m_renderLightData",value);}
}
