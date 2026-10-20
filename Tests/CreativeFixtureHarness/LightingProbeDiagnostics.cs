using System;
using System.IO;
using System.Xml.Linq;
using ClientPlugin.Rendering.Interop;

namespace CreativeFixtureHarness;

public sealed partial class Plugin
{
    static void SaveLightingProbes(NativeBackend backend,string folder)
    {
        int resolution=(int)backend.CurrentProbeConfig.Resolution,mips=0;
        var stats=backend.GetProbeStats();var schedule=backend.GetProbeScheduleStats();
        if(stats.Ready==0 || resolution<2)throw new InvalidOperationException("The captured lighting frame has no published probes.");
        for(int size=resolution;size>1;size>>=1)mips++;
        var root=new XElement("LightingProbes",new XAttribute("Schema",1),new XAttribute("Backend","Vulkan"),new XAttribute("Layout","FaceMajorMipChain"),new XAttribute("WorkingStage",schedule.Stage),new XAttribute("WorkingBusy",schedule.Busy!=0),new XAttribute("CompletedCycles",stats.Cycles));
        string[] names={"close-original","far-original","close-final","far-final"};uint[] kinds={0,1,4,5};
        root.SetAttributeValue("ConfigFlags",backend.CurrentProbeConfig.Flags);
        root.SetAttributeValue("BlendStrength",backend.CurrentProbeConfig.Blend);
        for(int cube=0;cube<names.Length;cube++){
            using(var file=File.Create(Path.Combine(folder,"probe-"+names[cube]+".r11g11b10")))
            for(uint face=0;face<6;face++)for(uint mip=0;mip<mips;mip++){
                byte[] half=backend.ReadbackProbe(kinds[cube],face,mip,resolution),packed=new byte[half.Length/2];
                for(int pixel=0;pixel<packed.Length/4;pixel++){
                    uint value=(uint)(BitConverter.ToUInt16(half,pixel*8)>>4)|((uint)(BitConverter.ToUInt16(half,pixel*8+2)>>4)<<11)|((uint)(BitConverter.ToUInt16(half,pixel*8+4)>>5)<<22);
                    int at=pixel*4;packed[at]=(byte)value;packed[at+1]=(byte)(value>>8);packed[at+2]=(byte)(value>>16);packed[at+3]=(byte)(value>>24);
                }
                file.Write(packed,0,packed.Length);
            }
            root.Add(new XElement("Cube",new XAttribute("Name",names[cube]),new XAttribute("Resolution",resolution),new XAttribute("MipLevels",mips),new XAttribute("Faces",6),new XAttribute("Format","R11G11B10_Float")));
        }
        root.Save(Path.Combine(folder,"probes.xml"));
    }
}
