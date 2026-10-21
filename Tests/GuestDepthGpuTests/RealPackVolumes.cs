using System.Reflection;
using ClientPlugin.Rendering.Interop;

partial class Program
{
    // Real production provider sources and host transaction, with controlled
    // constant weather/shape volumes and complete empty-room provenance.
    static int RealPackVolumes(Assembly core,string root,string assets,NativeBackend backend,Func<bool> frame)
    {
        int checks=0;void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        const BindingFlags stat=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        object Call(string type,string method,params object[] args)=>core.GetType("ClientPlugin."+type,true).GetMethod(method,stat).Invoke(null,args);
        object Property(string type,string name)=>core.GetType("ClientPlugin."+type,true).GetProperty(name,stat).GetValue(null);
        const string fog="fixture.real.aFog",clouds="fixture.real.bCloud";
        string fogFile=Path.GetFullPath(Path.Combine(root,"../FinalFrontier/Assets/Celestial/Atmosphere/Fog.hlsli"));
        string cloudFile=Path.GetFullPath(Path.Combine(root,"../VolumetricClouds/Pack/Medium/Clouds.hlsli"));
        string[] names={"fixture.shape","fixture.detail","fixture.weather"};
        byte[][] channels={new byte[]{255,0,0,0},new byte[4],new byte[]{255,200,220,0}};
        object[] textures=new object[3];
        for(int t=0;t<3;t++) {
            byte[][] mips=new byte[3][];
            for(int level=0;level<3;level++){int dim=4>>level;mips[level]=Enumerable.Range(0,dim*dim*dim*4).Select(i=>channels[t][i%4]).ToArray();}
            textures[t]=Activator.CreateInstance(core.GetType("ClientPlugin.Buffers.PublishedBuffer",true));
            if(t==2)textures[t].GetType().GetMethod("PublishVolumeRgba8").Invoke(textures[t],new object[]{Enumerable.Range(0,64*64*2*4).Select(i=>channels[t][i%4]).ToArray(),64,64,2});
            else textures[t].GetType().GetMethod("PublishVolumeMips").Invoke(textures[t],new object[]{mips,4});
            Call("Buffers.BufferCatalog","Publish","fixture.real",names[t],textures[t]);
        }
        Check(Call("Shaders.VolumetricMediumRegistry","Register",fog,fogFile,Array.Empty<string>()) is true,"real FinalFrontier Fog provider registers");
        Check(Call("Shaders.VolumetricMediumRegistry","Register",clouds,cloudFile,names) is true,"real VolumetricClouds provider registers with mipmapped noise and its 64x64x2 weather layout");
        foreach(string id in new[]{fog,clouds}) {
            Call("Shaders.VolumetricMediumRegistry","SetOrigin",id,0d,-65000d,0d);
            Call("Shaders.VolumetricMediumRegistry","SetEnabled",id,true);Call("Shaders.VolumetricMediumRegistry","RequestRenderer",id,true);
        }
        float fogInterval=-1,cloudInterval=-1;
        Call("Shaders.VolumetricMediumRegistry","SetIntervalCallback",fog,(Action<float>)(v=>fogInterval=v));Call("Shaders.VolumetricMediumRegistry","SetIntervalCallback",clouds,(Action<float>)(v=>cloudInterval=v));
        Call("Shaders.VolumetricMediumRegistry","Configure",0,1000f,0);
        var f=new float[64];f[0]=60000;f[1]=70000;f[2]=1000000;f[4]=.6f;f[5]=.7f;f[6]=.8f;f[7]=.3f;f[8]=1000;f[15]=1;f[17]=10000;
        var c=new float[64];c[3]=60000;c[7]=70000;c[8]=c[9]=c[10]=.9f;c[11]=1.15f;c[17]=1;c[20]=5000;c[22]=60000;c[23]=70000;c[24]=c[25]=1;c[37]=1;c[40]=10000;c[42]=60000;c[43]=1;c[52]=60000;c[53]=100000;c[63]=40;
        foreach(int index in new[]{31,32,33,34,35,44,45,46,47,48})c[index]=3;
        double[] bounds={-70000,-135000,-70000,70000,5000,70000};
        void Parameters(float fogDensity,float cloudDensity) {
            f[3]=fogDensity;c[15]=cloudDensity;
            Call("Shaders.VolumetricMediumRegistry","SetParameters",fog,f,bounds,new float[3]);Call("Shaders.VolumetricMediumRegistry","SetParameters",clouds,c,bounds,new float[3]);
            Call("Shaders.VolumetricMediumRegistry","InvalidateHistory",fog);
        }
        Parameters(.001f,0);Call("Backend.GuestVolumes","PreparePrograms",assets);Call("Backend.GuestCompiler","CompilePending",assets);
        var failed=((System.Collections.IEnumerable)Call("Backend.GuestPassList","Snapshot")).Cast<object>().Where(p=>((string)p.GetType().GetField("Id").GetValue(p)).StartsWith("anomaly.owned.Volumes.")&&(ulong)p.GetType().GetField("Pipeline").GetValue(p)==0).Select(p=>p.GetType().GetField("Id").GetValue(p)+": "+p.GetType().GetField("Error").GetValue(p)).ToArray();
        Check(failed.Length==0,"real provider shader pipelines ready: "+string.Join("; ",failed));
        var read=typeof(NativeBackend).GetMethod("GuestReadback",BindingFlags.Instance|BindingFlags.NonPublic);
        float[] Coefficients() {
            object image=core.GetType("ClientPlugin.Backend.GuestVolumes",true).GetField("oldCoefficients",stat).GetValue(null);
            ulong id=(ulong)image.GetType().GetField("Id",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(image);var bytes=new byte[6*6*64*8];read.Invoke(backend,new object[]{id,bytes});
            return Enumerable.Range(0,6*6*64).Select(i=>(float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(bytes,i*8+6))).ToArray();
        }
        Check(frame() && fogInterval==1000 && cloudInterval==1000,"real Fog/Clouds shared transaction claims both near intervals: "+Property("Shaders.VolumetricMediumRegistry","StatusLine"));
        var fogOnly=Coefficients();Check(fogOnly.All(v=>v>.00098f && v<.00101f),"real Fog extinction matches its configured exponential atmosphere across every froxel");
        Parameters(0,1);Check(frame(),"real cloud-only density frame commits");var cloudOnly=Coefficients();
        Check(cloudOnly.All(float.IsFinite) && cloudOnly.Any(v=>v>.0001f),"real cloud shader samples the uploaded shape/detail/weather volumes and produces nonzero extinction");
        Parameters(.001f,1);Check(frame(),"combined real Fog and Clouds frame commits");var combined=Coefficients();
        Check(Enumerable.Range(0,combined.Length).All(i=>Math.Abs(combined[i]-fogOnly[i]-cloudOnly[i])<Math.Max(.00002f,(fogOnly[i]+cloudOnly[i])*.005f)),"combined production provider extinction equals the sum of separate contributions across every froxel");
        var programs=(System.Collections.IEnumerable)Call("Backend.GuestPassList","Snapshot");
        Check(programs.Cast<object>().Where(p=>new[]{fog,clouds,fog+".present",clouds+".present"}.Contains((string)p.GetType().GetField("Id").GetValue(p))).All(p=>p.GetType().GetField("SubmittedThisFrame").GetValue(p) is false),"committed production providers suppress their legacy march and presentation packets");
        Call("Buffers.BufferCatalog","Unpublish","fixture.real",names[2]);
        Check(!frame() && fogInterval==0 && cloudInterval==0 && Call("Buffers.BufferCatalog","Active","volumeReactive")==null,"missing real cloud weather revokes the entire shared transaction and both intervals");
        Call("Buffers.BufferCatalog","Publish","fixture.real",names[2],textures[2]);Call("Shaders.VolumetricMediumRegistry","InvalidateHistory",clouds);
        Check(frame() && fogInterval==1000 && cloudInterval==1000,"real mixed-provider transaction recovers after weather restoration");
        ulong weatherId=(ulong)textures[2].GetType().GetProperty("ImageId").GetValue(textures[2]);
        textures[2].GetType().GetMethod("PublishVolumeRgba8").Invoke(textures[2],new object[]{new byte[64*64*2*4],64,64,2});
        Check((ulong)textures[2].GetType().GetProperty("ImageId").GetValue(textures[2])==weatherId,"live weather updates retain their native allocation ID");
        Call("Shaders.VolumetricMediumRegistry","InvalidateHistory",clouds);Check(frame(),"updated empty weather frame commits");var cleared=Coefficients();
        Check(Enumerable.Range(0,cleared.Length).All(i=>Math.Abs(cleared[i]-fogOnly[i])<.00002f),"actual updated weather GPU bytes remove clouds while preserving real fog extinction");
        foreach(string id in new[]{fog,clouds}){Call("Shaders.VolumetricMediumRegistry","RequestRenderer",id,false);Call("Shaders.VolumetricMediumRegistry","SetEnabled",id,false);}
        return checks;
    }
}
