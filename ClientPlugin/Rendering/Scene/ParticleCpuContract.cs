using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using HarmonyLib;
namespace ClientPlugin.Rendering.Scene;
internal static class ParticleCpuContract
{
    private static readonly (string Type,string Method,string Hash)[] Methods={
        ("VRageRender.MyGPUEmitters","UpdateTextureArray","f857cb6c1770dae4a4ca24919ba9a8bf5962de0544829830123529de9552f18d"),
        ("VRage.Render11.Particles.MyParticleLight","Update","9b3ede4662d2f2347f654907fbc04a2cb25d1ecfeee72eb02ecb250523b9296e"),
        ("VRage.Render11.Particles.MyParticleLight","CloseLight","f729c5f5b7ff7a3c67416c86c7a0ad72ed30e0b991873ba0519b98f00dd9d5ba")};
    // Compare installed, unrewritten IL. Interim rewrites its runtime assembly; its
    // live signatures and Harmony owners are checked separately before patching.
    internal static void Check(string assemblyPath)
    {
        using(var assembly=AssemblyDefinition.ReadAssembly(assemblyPath))foreach(var spec in Methods){var method=assembly.MainModule.GetType(spec.Type)?.Methods.SingleOrDefault(m=>m.Name==spec.Method&&m.Parameters.Count==0&&m.ReturnType.FullName=="System.Void");if(method==null||Preloader.Fingerprint(method,true)!=spec.Hash)throw new InvalidDataException("Stock particle CPU IL changed: "+spec.Type+"."+spec.Method+"; DX11 recovery remains available.");}
    }
    internal static void CheckRuntime()
    {
        foreach(var spec in Methods){var method=AccessTools.Method(StockParticles.Type(spec.Type),spec.Method,Type.EmptyTypes);if(method==null||method.ReturnType!=typeof(void))throw new MissingMethodException("Stock particle CPU runtime signature changed: "+spec.Method);var owners=Harmony.GetPatchInfo(method)?.Owners;if(owners!=null&&owners.Any(o=>o!="PhoenixTheSage.SE-Vulkan.ParticleCPU"))throw new InvalidOperationException("Enabled plugin already patches stock particle CPU hook: "+spec.Method+" ("+string.Join(", ",owners)+")");}
    }
}
