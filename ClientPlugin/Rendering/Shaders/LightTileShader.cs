using System;
using System.IO;
using System.Text.RegularExpressions;

namespace ClientPlugin.Rendering.Shaders;

public static class LightTileShader
{
    public const string Variant="f1bcea60cd6e8a9656e38f518e5aa7fb00b1ada6ffbb87dd8ccdb1feb4f7b753";
    public const string Contract="StockDepthTiles-Ordered64-1";
    public static string DeriveSource(string source)
    {
        string Replace(string text,string pattern,string value)
        {
            var regex=new Regex(pattern,RegexOptions.CultureInvariant);
            if(regex.Matches(text).Count!=1)throw new InvalidDataException("Stock tile source contract changed: "+pattern);
            return regex.Replace(text,value);
        }
        source=Replace(source,@"groupshared uint sTileNumLights;","groupshared uint sTileNumLights;\ngroupshared uint sLightMask[2];");
        source=Replace(source,@"sTileNumLights = 0;","sTileNumLights = 0;\n        sLightMask[0] = sLightMask[1] = 0;");
        source=Replace(source,@"uint listIndex;\s*InterlockedAdd\(sTileNumLights, 1, listIndex\);\s*TileIndices\[frame_\.Light\.tiles_num \+ tileIndex \* frame_\.Light\.maxTileLights \+ listIndex\] = index;",
            "InterlockedOr(sLightMask[index / 32], 1u << (index % 32));");
        source=Replace(source,@"TileIndices\[tileIndex\] = sTileNumLights;",
            "for (uint index = 0; index < pointlights_num; ++index)\n"+
            "            if ((sLightMask[index / 32] & (1u << (index % 32))) != 0)\n"+
            "                TileIndices[frame_.Light.tiles_num + tileIndex * frame_.Light.maxTileLights + sTileNumLights++] = index;\n"+
            "        TileIndices[tileIndex] = sTileNumLights;");
        const string pixel="frame_.Screen.offset + dispatchThreadID.xy";
        if(source.Split(new[]{pixel},StringSplitOptions.None).Length!=3)throw new InvalidDataException("Stock tile depth load contract changed.");
        source=source.Replace(pixel,"frame_.Screen.offset + min(dispatchThreadID.xy, frame_.Screen.resolution - 1)");
        return source;
    }
}
