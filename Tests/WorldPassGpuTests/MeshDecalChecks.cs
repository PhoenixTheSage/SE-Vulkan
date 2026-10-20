using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Shaders;
using VRageMath;
using VRageRender;
using VRageRender.Import;
using VRage;

partial class Program
{
    static void MeshDecalChecks(NativeBackend backend,MatrixD projection,MyEnvironmentLightData light,NativeWorldVertex[] vertices,Action<bool,string> check)
    {
        backend.UploadTexture(13,1,1,0,new byte[]{0,0,0,0},false);
        backend.UploadTexture(14,1,1,0,new byte[]{255,0,0,255},true);
        backend.UploadWorldMesh(2,vertices,new uint[]{0,2,1});bool reverse=false;
        uint kind=0,role=7,shaderRole=0;bool metal=false,prepared=false;float[] bones=null;byte[] stream=null;ulong normal=11,extensions=12;NativeBackend.PreparedActor actor=null;
        byte[] Frame(bool overlay,MyMeshDrawTechnique technique=MyMeshDrawTechnique.DECAL_NOPREMULT)
        {
            backend.BeginPacketCollection();
            backend.AddWorldPacket(0,1,10,11,12,13,WorldConstants.Object(backend.WorldLayouts[0],MatrixD.Identity,Vector3.One,Vector3.Zero,1,0),flags:512);
            if(overlay){
                uint pipeline=WorldShaderVariants.Pipeline(technique,kind,false,metal,role,shaderRole);
                var pose=MatrixD.CreateTranslation(0,0,.02);
                if(prepared){actor??=new NativeBackend.PreparedActor(new[]{new NativeBackend.PreparedPart(pipeline,reverse?2UL:1UL,14,normal,extensions,13,Vector3.One,Vector3.Zero,1,0,backend.WorldLayouts[pipeline])});backend.SubmitPreparedActor(actor,pose,stream,1,512,255,Vector3.Zero,-1);}
                else backend.AddWorldPacket(pipeline,reverse?2UL:1UL,14,normal,extensions,13,WorldConstants.Object(backend.WorldLayouts[pipeline],pose,Vector3.One,Vector3.Zero,1,0,bones),stream,flags:512);
            }
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            if(!backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black))throw new Exception("Mesh decal frame missing");
            backend.EndFrame();backend.Readback(64,64);return backend.ReadbackGbufferColor(64,64);
        }
        var background=Frame(false);int center=(32*64+32)*4;
        byte[][] Buffers()=>Enumerable.Range(0,4).Select(i=>backend.ReadbackWorldGbuffer((uint)i,64,64)).ToArray();
        var backgroundBuffers=Buffers();
        check(background[center]>100&&background[center]<220&&background[center+3]==0,"mesh-decal fixture center contains the authored gray background material and LOD");
        var transparent=Frame(true);
        Directory.CreateDirectory("TestResults/MeshDecalGpuTests");File.WriteAllBytes("TestResults/MeshDecalGpuTests/background.rgba8",background);File.WriteAllBytes("TestResults/MeshDecalGpuTests/zero-alpha.rgba8",transparent);
        Console.WriteLine("Mesh decal center background="+string.Join(",",background.Skip(center).Take(4))+"; zero-alpha="+string.Join(",",transparent.Skip(center).Take(4)));
        check(transparent.SequenceEqual(background),"zero-alpha non-premultiplied mesh decal preserves background GBuffer color and LOD alpha");
        backend.UploadTexture(13,1,1,0,new byte[]{255,255,255,255},false);var opaque=Frame(true);
        reverse=true;var opposite=Frame(true);Console.WriteLine("Opaque mesh decal center="+string.Join(",",opaque.Skip(center).Take(4))+"; reversed="+string.Join(",",opposite.Skip(center).Take(4)));
        bool front=opaque[center]>250&&opaque[center+1]<3;bool back=opposite[center]>250&&opposite[center+1]<3;
        check(front!=back,"stock default mesh decal rasterizer accepts exactly one winding");
        reverse=back;opaque=reverse?opposite:opaque;
        check(opaque[center]>250&&opaque[center+1]<3&&opaque[center+2]<3&&opaque[center+3]==background[center+3],"opaque-alpha mesh decal visibly covers the background without replacing destination LOD");
        backend.UploadTexture(13,1,1,0,new byte[]{0,0,0,0},false);check(Frame(true).SequenceEqual(background),"zero-alpha check also exercises the accepted front winding");
        backend.UploadTexture(13,1,1,0,new byte[]{128,128,128,128},false);var straight=Frame(true);var premultiplied=Frame(true,MyMeshDrawTechnique.DECAL);
        check(Math.Abs(straight[center]-221)<=2&&Math.Abs(straight[center+1]-131)<=2&&Math.Abs(straight[center+2]-131)<=2,"non-premultiplied mesh decal blends in linear GBuffer color space");
        check(premultiplied[center]>250&&Math.Abs(premultiplied[center+1]-131)<=2&&premultiplied[center+3]==background[center+3],"premultiplied mesh decal uses source-one RGB and preserves destination alpha");
        backend.UploadTexture(13,1,1,0,new byte[]{128,128,128,128},true);var srgbAlpha=Frame(true);
        check(Math.Abs(srgbAlpha[center]-199)<=2&&Math.Abs(srgbAlpha[center+1]-161)<=2&&Math.Abs(srgbAlpha[center+2]-161)<=2,"stock sRGB RGBA mask view produces about .216 alpha rather than .502");
        backend.UploadTexture(13,1,1,0,new byte[]{128,128,128,128},false); // Remaining raw-API permutations explicitly exercise a linear view.
        backend.UploadTexture(15,1,1,0,new byte[]{128,200,255,220},false);backend.UploadTexture(16,1,1,0,new byte[]{64,128,0,0},true);normal=15;extensions=16;
        foreach(uint bundle in new uint[]{1,2,3,7})foreach(bool paint in new[]{false,true})foreach(uint type in Enumerable.Range(0,5).Select(i=>(uint)i))foreach(var technique in new[]{MyMeshDrawTechnique.DECAL,MyMeshDrawTechnique.DECAL_NOPREMULT}){
            kind=type;role=bundle;metal=paint;actor=null;prepared=false;backend.WorldRecipesEnabled=false;
            bones=null;stream=null;
            if(type==1){bones=new float[960];for(int i=0;i<60;i++)Array.Copy(NativeBackend.MatrixValues(MatrixD.Identity),0,bones,i*16,16);}
            if(type==2){stream=new byte[64];Buffer.BlockCopy(new[]{1f,0,0,0,0,1f,0,0,0,0,1f,0,0,0,0,0},0,stream,0,64);}
            if(type>=3){var cube=new MyCubeInstanceData{LocalMatrix=Matrix.Identity};stream=new byte[64];for(int i=0;i<8;i++){var p=cube.GetPackedBone(i);stream[i*4]=p.X;stream[i*4+1]=p.Y;stream[i*4+2]=p.Z;stream[i*4+3]=p.W;}var t=cube.PackedOrthoMatrix;Buffer.BlockCopy(new[]{t.X,t.Y,t.Z,t.W,0f,0,0,0},0,stream,32,32);}
            Frame(true,technique);var reference=Buffers();string name=$"bundle={role} stream={type} paint={metal} technique={technique}";
            check(reference[3].SequenceEqual(backgroundBuffers[3]),"mesh decal leaves actual opaque depth unchanged "+name);
            check(reference[0][center+3]==background[center+3]&&reference[2][center+3]==backgroundBuffers[2][center+3],"mesh decal preserves destination alpha fields "+name);
            check((role&1)!=0?reference[0][center]!=background[center]:reference[0].SequenceEqual(background),"mesh decal writes only authored color bundle "+name);
            if((role&2)!=0)check(BitConverter.ToUInt32(reference[1],center)!=BitConverter.ToUInt32(backgroundBuffers[1],center)&&reference[2][center+1]!=backgroundBuffers[2][center+1],"authored normal and gloss visibly change GPU channels "+name);
            if((role&4)!=0)check(reference[2][center+2]!=backgroundBuffers[2][center+2],"authored extension emission visibly changes GPU material "+name);
            for(int channel=0;channel<3;channel++)if((role&(1u<<channel))==0)check(reference[2][center+channel]==backgroundBuffers[2][center+channel],"mesh decal preserves unauthored material channel "+channel+" "+name);
            backend.WorldRecipesEnabled=true;Frame(true,technique);var recipe=Buffers();check(recipe.Zip(reference,(a,b)=>a.SequenceEqual(b)).All(x=>x),"native mesh decal recipe preserves exact GBuffer/depth "+name);
            backend.SetWorldIndirect(true);Frame(true,technique);var indirect=Buffers();check(indirect.Zip(reference,(a,b)=>a.SequenceEqual(b)).All(x=>x),"indirect mesh decal phase preserves exact GBuffer/depth "+name);backend.SetWorldIndirect(false);
            if(type!=1){prepared=true;Frame(true,technique);var retained=Buffers();check(retained.Zip(reference,(a,b)=>a.SequenceEqual(b)).All(x=>x),"prepared mesh decal submission preserves exact GBuffer/depth "+name);}
        }
        // Authored blend masks and cached shader macros are independent.
        // Exercise every bank, stream, convention and metal choice through
        // the actual GPU and compare recipe replay to its direct reference.
        foreach(uint authored in new uint[]{1,2,3,7})foreach(uint effective in new uint[]{1,2,3,7})foreach(bool paint in new[]{false,true})foreach(uint type in Enumerable.Range(0,5).Select(x=>(uint)x))foreach(var technique in new[]{MyMeshDrawTechnique.DECAL,MyMeshDrawTechnique.DECAL_NOPREMULT}){
            role=authored;shaderRole=effective;kind=type;metal=paint;prepared=false;actor=null;
            bones=null;stream=null;
            if(type==1){bones=new float[960];for(int i=0;i<60;i++)Array.Copy(NativeBackend.MatrixValues(MatrixD.Identity),0,bones,i*16,16);}
            if(type==2){stream=new byte[64];Buffer.BlockCopy(new[]{1f,0,0,0,0,1f,0,0,0,0,1f,0,0,0,0,0},0,stream,0,64);}
            if(type>=3){var cube=new MyCubeInstanceData{LocalMatrix=Matrix.Identity};stream=new byte[64];for(int i=0;i<8;i++){var p=cube.GetPackedBone(i);stream[i*4]=p.X;stream[i*4+1]=p.Y;stream[i*4+2]=p.Z;stream[i*4+3]=p.W;}var t=cube.PackedOrthoMatrix;Buffer.BlockCopy(new[]{t.X,t.Y,t.Z,t.W,0f,0,0,0},0,stream,32,32);}

            backend.WorldRecipesEnabled=false;Frame(true,technique);var reference=Buffers();
            bool valid=reference[3].SequenceEqual(backgroundBuffers[3]);
            if((authored&2)==0 || (effective&2)==0)valid &= reference[1].SequenceEqual(backgroundBuffers[1]);
            for(int channel=0;channel<3;channel++)if((authored&(1u<<channel))==0)valid &= reference[2][center+channel]==backgroundBuffers[2][center+channel];
            check(valid,"independent mesh decal blend/macro GPU contract "+authored+"/"+effective+" stream="+type+" metal="+paint+" "+technique);
            backend.WorldRecipesEnabled=true;Frame(true,technique);check(Buffers().Zip(reference,(a,b)=>a.SequenceEqual(b)).All(x=>x),"independent mesh decal blend/macro recipe replay "+authored+"/"+effective+" stream="+type+" metal="+paint+" "+technique);
        }
        check(backend.GetStats().ValidationErrors==0,"mesh-decal GPU path has zero validation errors");
    }
}
