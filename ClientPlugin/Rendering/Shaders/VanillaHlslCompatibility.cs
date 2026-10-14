using System;

namespace ClientPlugin.Rendering.Shaders;

// Repairs are confined to exact installed SE 1.210.014 source hashes. Updated
// game files or content mods pass through unchanged and receive DXC diagnostics.
internal static class VanillaHlslCompatibility
{
    public static string Apply(string sourceHash, string source, out string description)
    {
        description = null;
        switch (sourceHash)
        {
            case "af05f711f4ef54f6a7178a9076819876c2aca9aa4f021be7d04fb7ac99878b68":
                description = "AlphaMasked array sampling uses the supplied material texture index.";
                return source.Replace("alphamaskTexIndex", "pixel.custom.texIndices.w");
            case "007b5be745eb541a5b6dd719f6cb97813ae543451f3302c9293cbc936828d3cd":
                description = "AlphaMasked texture-index semantic avoids the instancing TEXCOORD10 collision.";
                return source.Replace("texIndices : TEXCOORD10", "texIndices : TEXCOORD1");
            case "7d9953942b84d44cfbdd0682d465b6017f1059dfe253f632095b33059cccc133":
                description = "GBuffer array decal sampling references the declared array resource.";
                return source.Replace("AlphamaskTexture.Sample(TextureSampler, float3(", "AlphamaskArrayTexture.Sample(TextureSampler, float3(");
        }
        return source;
    }
}
