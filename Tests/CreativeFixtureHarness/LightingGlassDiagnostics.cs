using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using ClientPlugin.Rendering.Interop;
using VRageRender;

namespace CreativeFixtureHarness;

public sealed partial class Plugin
{
    static void SaveLightingGlass(NativeBackend backend)
    {
        var root=new XElement("LightingGlass",new XAttribute("Schema",2),new XAttribute("Backend",lightingOwner.expectedBackend),
            new XAttribute("Scope","Registered material definitions plus current Vulkan glass packets/cache identities; stock draw CB bytes are not captured."));
        foreach(var material in MyTransparentMaterials.Materials){
            // Definitions include particle materials; retain named fields so a
            // submitted glass material can be identified without guessing.
            var row=new XElement("Material",new XAttribute("Name",material.Id.ToString()));
            foreach(string field in new[]{"Texture","GlossTexture","Color","ColorAdd","ShadowMultiplier","LightMultiplier","Reflectivity","Fresnel","ReflectionShadow","Gloss","GlossTextureAdd","SpecularColorFactor","TriangleFaceCulling"}){
                object value=FlareValue(material,field);row.Add(new XAttribute(field,Convert.ToString(value,CultureInfo.InvariantCulture)??string.Empty));
            }
            root.Add(row);
        }
        if(backend!=null){
            if(lightingScene==null)throw new InvalidOperationException("Glass diagnostic has no observed scene.");
            var cache=FlareValue(lightingScene,"TextureCache");var entries=(IDictionary)FlareValue(cache,"entries");
            string Texture(ulong handle){var names=new System.Collections.Generic.List<string>();foreach(DictionaryEntry entry in entries)if((ulong)FlareValue(entry.Value,"Handle")==handle)names.Add(entry.Key.ToString());return string.Join(";",names);}
            var depthFlags=(uint[])typeof(NativeBackend).GetMethod("GlassDecalDepthFlags",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(backend,null);int drawIndex=0;
            foreach(object tuple in (IEnumerable)FlareValue(backend,"glassPackets")){
                var geometry=FlareValue(tuple,"Item1");byte[] bytes=(byte[])FlareValue(tuple,"Item2");
                var row=new XElement("Draw",new XAttribute("Mesh",FlareValue(geometry,"Mesh")),new XAttribute("Pipeline",FlareValue(geometry,"Pipeline")),new XAttribute("Flags",FlareValue(geometry,"Flags")),
                    new XAttribute("ColorTexture",Texture((ulong)FlareValue(geometry,"Color"))),new XAttribute("NormalTexture",Texture((ulong)FlareValue(geometry,"Normal"))));
                uint flags=(uint)FlareValue(geometry,"Flags");row.Add(new XAttribute("SourcePipeline",(flags&2)!=0?"OLD":"NEW"),new XAttribute("QueuedDepthFlags",flags|depthFlags[drawIndex++]));
                var distance=FlareValue(tuple,"Item3");if(distance!=null)row.Add(new XAttribute("OldDecalDistanceSquared",Convert.ToString(distance,CultureInfo.InvariantCulture)));
                row.Add(new XElement("MaterialFloats",string.Join(" ",Enumerable.Range(0,bytes.Length/4).Select(i=>BitConverter.ToSingle(bytes,i*4).ToString("R",CultureInfo.InvariantCulture)))));
                root.Add(row);
            }
        }
        string folder=Path.Combine(lightingOwner.reports,"lighting-buffers");Directory.CreateDirectory(folder);root.Save(Path.Combine(folder,"glass.xml"));
    }
}
