using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using ClientPlugin.Rendering.Shaders;
using VRage.Import;
using VRageMath;
using VRageRender;
using VRageRender.Messages;

partial class Program
{
    // Audit P7: DebugDrawLine3D replay with stock MyLinesRenderer semantics (after the scene, before sprites;
    // DepthRead tests scene depth, others ignore it) and MyRender11's persistent-message queueing.
    static int DebugLinesRegression(NativeBackend backend,MatrixD projection,MyEnvironmentLightData light)
    {
        int checks=0;void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        const int size=64;
        NativeWorldVertex V(float x,float y){uint n=VF_Packer.PackNormal(Vector3.Backward);return new(){Material=new(){X=x,Y=y,Z=-3,Scale=1,NormalX=n&65535,NormalY=n>>16,TangentX=1,TangentY=.5f,TangentZ=.5f,TangentW=1,U=(x+1)/2,V=(1-y)/2},WeightX=1};}
        backend.UploadWorldMesh(948001,new[]{V(-2,-2),V(2,-2),V(0,2)},new uint[]{0,1,2,2,1,0});backend.UploadTexture(948002,1,1,0,new byte[]{40,40,200,0},true);backend.UploadTexture(948003,1,1,0,new byte[]{128,128,255,100},false);backend.UploadTexture(948004,1,1,0,new byte[]{255,1,0,0},false);
        var post=new VulkanPostprocess();post.Hbao.Enabled=false;post.Ssao.Enabled=false;post.Settings.BloomEnabled=false;post.Settings.EnableEyeAdaptation=false;
        byte[] Frame(bool world,Action draw){
            backend.BeginPacketCollection();
            if(world){
                post.Configure(backend,size,size,false,view:MatrixD.Identity,projection:projection);
                backend.AddWorldPacket(0,948001,948002,948003,948004,0,WorldConstants.Object(backend.WorldLayouts[0],MatrixD.Identity,Vector3.One,Vector3.Zero,1,0),null,1,512|16384,0);
                backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,size,size,light,postprocess:post.Settings),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            }
            Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"debug line frame begins");
            draw();backend.RequestReadback();backend.EndFrame();return backend.Readback(size,size);
        }
        NativeLineVertex[] Line(float z,Color color){
            var a=new Vector3(-1,0,z);var b=new Vector3(1,0,z);
            return new[]{new NativeLineVertex{X=a.X,Y=a.Y,Z=a.Z,Color=color.PackedValue},new NativeLineVertex{X=b.X,Y=b.Y,Z=b.Z,Color=color.PackedValue}};
        }
        // The horizontal line through the view centre lands on row 31 or 32 depending on the rasterizer's diamond rule.
        (byte r,byte g,byte b) Centre(byte[] p){foreach(int row in new[]{31,32}){int i=(row*size+32)*4;if(p[i]>40||p[i+1]>40||p[i+2]>40)return (p[i],p[i+1],p[i+2]);}int c=(32*size+32)*4;return (p[c],p[c+1],p[c+2]);}
        bool Red(byte[] p){var c=Centre(p);return c.r>200&&c.g<40&&c.b<40;}
        bool Green(byte[] p){var c=Centre(p);return c.g>200&&c.r<40&&c.b<40;}
        bool Off(byte[] p){int i=(10*size+32)*4;return p[i]<10&&p[i+1]<10&&p[i+2]<10;}

        var plain=Frame(false,()=>{});Check(!Red(plain)&&!Green(plain),"no lines leave the cleared output untouched");
        var overlay=Frame(false,()=>backend.DrawLines(Line(-3,new Color(255,0,0)),2,false));
        Console.WriteLine("DEBUG overlay centre="+Centre(overlay));
        Check(Red(overlay)&&Off(overlay),"an IgnoreDepth line draws its sRGB colour across the view centre only");
        Check(Green(Frame(false,()=>backend.DrawLines(Line(-3,new Color(0,255,0)),2,true))),"a DepthRead line passes against cleared depth");

        var scene=Frame(true,()=>{});var sceneCentre=Centre(scene);Console.WriteLine("DEBUG scene centre="+sceneCentre);
        Check(!Red(scene)&&!Green(scene),"the scene triangle covers the view centre");
        var hidden=Frame(true,()=>backend.DrawLines(Line(-6,new Color(0,255,0)),2,true));
        Check(Centre(hidden)==sceneCentre,"a DepthRead line behind scene geometry is hidden by scene depth");
        Check(Green(Frame(true,()=>backend.DrawLines(Line(-2,new Color(0,255,0)),2,true))),"a DepthRead line in front of scene geometry draws");
        Check(Red(Frame(true,()=>backend.DrawLines(Line(-6,new Color(255,0,0)),2,false))),"an IgnoreDepth line behind scene geometry still draws");
        var half=Frame(false,()=>backend.DrawLines(Line(-3,new Color(255,0,0,128)),2,false));var hc=Centre(half);
        Console.WriteLine("DEBUG half-alpha centre="+hc);Check(hc.r>150&&hc.r<230,"line alpha blends premultiplied over the output");

        // Collector: camera-relative in double precision, persistent queueing and DebugClearPersistentMessages.
        var lines=new VulkanDebugLines();var far=new Vector3D(1e7,-2e6,3e6);
        MyRenderMessageDebugDrawLine3D Message(bool persistent)=>new(){PointFrom=far+new Vector3D(-1,0,-3),PointTo=far+new Vector3D(1,0,-3),ColorFrom=new Color(255,0,0),ColorTo=new Color(255,0,0),DepthRead=false,Persistent=persistent};
        lines.Add(Message(false));Check(Red(Frame(false,()=>lines.Submit(backend,far)))&&lines.FrameCount==0,"a frame line far from the origin draws camera-relative and is consumed");
        Check(!Red(Frame(false,()=>lines.Submit(backend,far))),"a non-persistent line draws for one frame only");
        lines.Add(Message(true));Frame(false,()=>lines.Submit(backend,far));Check(lines.PersistentCount==1,"a persistent line moves to the persistent queue after drawing");
        Check(Red(Frame(false,()=>lines.Submit(backend,far))),"a persistent line keeps drawing in later frames");
        lines.ClearPersistent();Check(Red(Frame(false,()=>lines.Submit(backend,far)))&&lines.PersistentCount==0,"DebugClearPersistentMessages clears after that frame draws, as on DX11");
        Check(!Red(Frame(false,()=>lines.Submit(backend,far))),"cleared persistent lines stop drawing");
        string rejected=null;Frame(false,()=>{try{backend.DrawLines(Line(-3,Color.Red),1,false);}catch(Exception e){rejected=e.Message;}});
        Check(rejected!=null&&rejected.Contains("invalid line batch"),"odd line vertex counts are rejected");
        Check(backend.GetStats().ValidationEnabled!=0&&backend.GetStats().ValidationErrors==0,"debug line replay raises zero core/synchronization errors");
        return checks;
    }
}
