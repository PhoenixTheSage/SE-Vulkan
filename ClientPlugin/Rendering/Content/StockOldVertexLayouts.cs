using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using VRageRender;

namespace ClientPlugin.Rendering.Content;

internal readonly struct StockOldVertexLayout : IEquatable<StockOldVertexLayout>
{
    public readonly bool Animated,StandardStreams,GeometryArrays;
    public StockOldVertexLayout(bool animated,bool streams,bool arrays){Animated=animated;StandardStreams=streams;GeometryArrays=streams&&arrays;}
    public bool Equals(StockOldVertexLayout other)=>Animated==other.Animated&&StandardStreams==other.StandardStreams&&GeometryArrays==other.GeometryArrays;
    public override bool Equals(object obj)=>obj is StockOldVertexLayout other&&Equals(other);
    public override int GetHashCode()=>(Animated?1:0)|(StandardStreams?2:0)|(GeometryArrays?4:0);
}

internal readonly struct StockOldVertexComponent
{
    public readonly MyVertexInputComponentType Type;
    public readonly int Slot;
    public readonly bool PerInstance;
    public StockOldVertexComponent(MyVertexInputComponentType type,int slot=0,bool perInstance=false){Type=type;Slot=slot;PerInstance=perInstance;}
}

/// <summary>
/// CPU-owned MyVertexLayouts identity table. The installed component struct's
/// ValueType hash is runtime-specific, so hash a boxed metadata value of that
/// exact type; never call the stock layout allocator or initialize its managers.
/// Layouts persist across scene and device transitions in stock.
/// </summary>
internal sealed class StockOldVertexLayouts
{
    const BindingFlags fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static readonly Type componentType=typeof(MyRenderSettings1Extensions).Assembly.GetType("VRageRender.MyVertexInputComponent",true);
    static readonly FieldInfo typeField=componentType.GetField("Type",fields),slotField=componentType.GetField("Slot",fields),freqField=componentType.GetField("Freq",fields);
    readonly Dictionary<int,int> byHash=new(){{0,0}};
    readonly List<StockOldVertexComponent[]> components=new(){Array.Empty<StockOldVertexComponent>()};
    public int Count=>components.Count;
    public StockOldVertexComponent[] Components(int index)=>(StockOldVertexComponent[])components[index].Clone();
    public static int ComponentHash(StockOldVertexComponent value)
    {
        object boxed=Activator.CreateInstance(componentType);
        typeField.SetValue(boxed,value.Type);slotField.SetValue(boxed,value.Slot);
        freqField.SetValue(boxed,Enum.ToObject(freqField.FieldType,value.PerInstance?1:0));
        return boxed.GetHashCode();
    }
    public int Get(params StockOldVertexComponent[] value)
    {
        if(value==null||value.Length==0)return 0;
        int hash=0;foreach(var component in value)hash=unchecked((17*31+hash)*31+ComponentHash(component));
        if(byHash.TryGetValue(hash,out int existing))return existing;
        int index=components.Count;byHash.Add(hash,index);components.Add((StockOldVertexComponent[])value.Clone());return index;
    }
    public int Combine(int first,int second)
    {
        // Stock's empty entry has null Components; List/AddRange reject it.
        if(first==0||second==0)throw new ArgumentNullException("components");
        return Get(components[first].Concat(components[second]).ToArray());
    }
    public int File(StockOldVertexLayout layout)=>Get(FileComponents(layout));
    public static StockOldVertexComponent[] FileComponents(StockOldVertexLayout layout)
    {
        var result=new List<StockOldVertexComponent>{new(MyVertexInputComponentType.POSITION_PACKED)};
        if(layout.Animated){result.Add(new(MyVertexInputComponentType.BLEND_WEIGHTS));result.Add(new(MyVertexInputComponentType.BLEND_INDICES));}
        if(layout.StandardStreams){result.Add(new(MyVertexInputComponentType.NORMAL,1));result.Add(new(MyVertexInputComponentType.TANGENT_SIGN_OF_BITANGENT,1));result.Add(new(MyVertexInputComponentType.TEXCOORD0_H,1));if(layout.GeometryArrays)result.Add(new(MyVertexInputComponentType.TEXINDICES,1));}
        return result.ToArray();
    }
    // Source InitSubsystems/MyManagers registration order, before loading Cone.
    // MeshTable's standard layout is registered even without a file using it.
    public void RegisterBeforeBuiltinMeshes()
    {
        Get(new(MyVertexInputComponentType.POSITION3),new(MyVertexInputComponentType.COLOR4));
        // ModelFactory.OnDeviceInit loads NEW LoadingQuad before the occlusion
        // managers. Its GBuffer/depth/highlight bundles consume these three
        // shared layout identities, even when all visible actors are OLD.
        Get(new(MyVertexInputComponentType.POSITION_PACKED),new(MyVertexInputComponentType.NORMAL,1),new(MyVertexInputComponentType.TANGENT_SIGN_OF_BITANGENT,1),new(MyVertexInputComponentType.TEXCOORD0_H),new(MyVertexInputComponentType.SIMPLE_INSTANCE,2,true),new(MyVertexInputComponentType.SIMPLE_INSTANCE_COLORING,2,true));
        Get(new(MyVertexInputComponentType.POSITION_PACKED),new(MyVertexInputComponentType.TEXCOORD0_H),new(MyVertexInputComponentType.SIMPLE_INSTANCE,2,true));
        Get(new(MyVertexInputComponentType.POSITION_PACKED),new(MyVertexInputComponentType.NORMAL,1),new(MyVertexInputComponentType.TANGENT_SIGN_OF_BITANGENT,1),new(MyVertexInputComponentType.TEXCOORD0_H));
        Get(new(MyVertexInputComponentType.CUSTOM4_0,0,true),new(MyVertexInputComponentType.CUSTOM1_0,0,true));
        Get(new(MyVertexInputComponentType.CUSTOM3_0,0,true),new(MyVertexInputComponentType.CUSTOM3_0,0,true));
        Get(new(MyVertexInputComponentType.POSITION3),new(MyVertexInputComponentType.CUSTOM_UNORM4_0));
        Get(new(MyVertexInputComponentType.CUSTOM_HALF4_0,0,true),new(MyVertexInputComponentType.CUSTOM_HALF4_1,0,true),new(MyVertexInputComponentType.CUSTOM_HALF4_2,0,true),new(MyVertexInputComponentType.COLOR4,0,true));
        Get(new(MyVertexInputComponentType.POSITION3),new(MyVertexInputComponentType.TEXCOORD0_H));
        Get(new StockOldVertexComponent(MyVertexInputComponentType.POSITION3));
        Get(new(MyVertexInputComponentType.VOXEL_POSITION_MAT),new(MyVertexInputComponentType.VOXEL_NORMAL,1));
        File(new StockOldVertexLayout(false,true,false));
    }
    public void RegisterAfterBuiltinCone()
    {
        Get(new(MyVertexInputComponentType.POSITION3),new(MyVertexInputComponentType.TEXCOORD0_H));
        Get(new(MyVertexInputComponentType.POSITION2),new(MyVertexInputComponentType.TEXCOORD0));
    }
    public void RegisterAfterBuiltinSphere(bool stereo)
    {
        File(new StockOldVertexLayout(false,true,false));
        if(stereo)Get(new StockOldVertexComponent(MyVertexInputComponentType.POSITION2));
    }
}
