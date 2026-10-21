using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
namespace ClientPlugin.Rendering.Interop;
public sealed partial class NativeBackend
{
    public bool WorldRecipesEnabled {get;set;}=Environment.GetEnvironmentVariable("SE_VULKAN_WORLD_RECIPES")!="0";
    public sealed class WorldRecipe
    {
        internal NativeBackend Owner;internal bool Alive;internal ulong Handle;internal RecipeKey Key;
    }
    internal struct RecipeKey:IEquatable<RecipeKey>
    {
        public uint Pipeline,Bytes;public ulong Mesh,Color,Normal,Extensions,Alpha;
        public bool Equals(RecipeKey b)=>Pipeline==b.Pipeline&&Bytes==b.Bytes&&Mesh==b.Mesh&&Color==b.Color&&Normal==b.Normal&&Extensions==b.Extensions&&Alpha==b.Alpha;
        public override bool Equals(object obj)=>obj is RecipeKey key&&Equals(key);
        public override int GetHashCode(){unchecked{int h=(int)Pipeline*397^(int)Bytes;h=h*397^Mesh.GetHashCode();h=h*397^Color.GetHashCode();h=h*397^Normal.GetHashCode();h=h*397^Extensions.GetHashCode();h=h*397^Alpha.GetHashCode();return h;}}
    }
    [StructLayout(LayoutKind.Sequential,Pack=8)] private struct RecipeDefinition{public uint Size,Pipeline,Bytes,Reserved;public ulong Mesh,Color,Normal,Extensions,Alpha;}
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int RegisterRecipeFn(IntPtr context,ref RecipeDefinition recipe,out ulong handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int QueueRecipesFn(IntPtr context,byte[] frame,uint bytes,float[] projection,float[] inverse,IntPtr draws,uint count,IntPtr objects,uint objectBytes,IntPtr instances,uint instanceBytes);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int RecipeStatsFn(IntPtr context,ref NativeRecipeStats stats);
    private RegisterRecipeFn registerRecipe;private QueueRecipesFn queueRecipes;private RecipeStatsFn recipeStatsGet;
    private readonly Dictionary<RecipeKey,WorldRecipe> worldRecipes=new();
    private NativeRecipeDraw[] recipeScratch=Array.Empty<NativeRecipeDraw>();
    public WorldRecipe EnsureWorldRecipe(ref WorldRecipe hint,uint pipeline,ulong mesh,ulong color,ulong normal,ulong extensions,ulong alpha,int bytes)
    {
        var key=new RecipeKey{Pipeline=pipeline,Bytes=checked((uint)bytes),Mesh=mesh,Color=color,Normal=normal,Extensions=extensions,Alpha=alpha};
        if(hint!=null&&hint.Alive&&ReferenceEquals(hint.Owner,this)&&hint.Key.Equals(key))return hint;
        if(worldRecipes.TryGetValue(key,out hint)&&hint.Alive)return hint;
        if(registerRecipe==null)registerRecipe=(RegisterRecipeFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_register_world_recipe"),typeof(RegisterRecipeFn));
        var definition=new RecipeDefinition{Size=56,Pipeline=pipeline,Bytes=key.Bytes,Mesh=mesh,Color=color,Normal=normal,Extensions=extensions,Alpha=alpha};
        Check(registerRecipe(context,ref definition,out ulong handle),context);
        hint=new WorldRecipe{Owner=this,Alive=true,Handle=handle,Key=key};worldRecipes[key]=hint;return hint;
    }
    private WorldRecipe PacketRecipe(in WorldPacket packet){var recipe=packet.Recipe;return EnsureWorldRecipe(ref recipe,packet.Pipeline,packet.Mesh,packet.Color,packet.Normal,packet.Extensions,packet.Alpha,packet.Constants.Length);}
    private void ResetWorldRecipes(){ResetNativeModels();foreach(var recipe in worldRecipes.Values)recipe.Alive=false;worldRecipes.Clear();}
    private void DropWorldRecipes(uint kind,ulong id){
        if(kind!=3 || worldRecipes.Count==0)return;nativeModelResourceRevision++;
        var removed=new List<RecipeKey>();foreach(var pair in worldRecipes)if(pair.Key.Mesh==id){pair.Value.Alive=false;removed.Add(pair.Key);}
        foreach(var key in removed)worldRecipes.Remove(key);
    }
    public NativeRecipeStats GetRecipeStats(){if(recipeStatsGet==null)recipeStatsGet=(RecipeStatsFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_get_recipe_stats"),typeof(RecipeStatsFn));var stats=new NativeRecipeStats{Size=40,AbiVersion=AbiVersion};Check(recipeStatsGet(context,ref stats),context);return stats;}
}
[StructLayout(LayoutKind.Sequential,Pack=8)] internal struct NativeRecipeDraw {public ulong Recipe;public uint Count,Flags,Cascades,ObjectOffset,InstanceOffset,Reserved;public float OriginX,OriginY,OriginZ,Radius;}
[StructLayout(LayoutKind.Sequential,Pack=4)] public struct NativeRecipeStats {public uint Size,AbiVersion,Enabled,Draws,Registered,Revalidated,MaterialsReused,GeometryReused,Live,Retired;}
