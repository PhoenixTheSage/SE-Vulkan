using System.Reflection;
using ClientPlugin.Rendering.Interop;

partial class Program
{
    static int VolumeResizeRegression(Assembly core,NativeBackend backend,Func<bool> frame,Action<int,int> resize,Func<float> interval)
    {
        int checks=0;void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        const BindingFlags stat=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        object Product(string name)=>core.GetType("ClientPlugin.Buffers.BufferCatalog",true).GetMethod("Active",stat).Invoke(null,new object[]{name});
        int Dimension(object image,string name)=>(int)image.GetType().GetProperty(name).GetValue(image);
        ulong Id(object image)=>(ulong)image.GetType().GetProperty("ImageId").GetValue(image);
        bool Available(object image)=>image!=null && image.GetType().GetProperty("IsAvailable").GetValue(image) is true;
        var read=typeof(NativeBackend).GetMethod("GuestReadback",BindingFlags.Instance|BindingFlags.NonPublic);
        double coverage=1-Math.Exp(-2),expected=Math.Max(coverage,2*Math.Min(coverage,1-coverage));
        foreach(var test in new (int Width,int Height,float Fraction)[]{(64,64,.5f),(64,64,.75f),(64,64,1),(95,73,1),(95,73,.5f)}) {
            object old=Product("volumeReactive");resize(test.Width,test.Height);backend.SetRenderFraction(test.Fraction);
            var size=backend.ResolveRenderResolution(test.Width,test.Height);
            bool active=frame();
            if(!active) {
                Check(interval()==0 && Product("volumeReactive")==null,"resize transition retains fallback without a stale reject product");
                active=frame();
            }
            string label=test.Width+"x"+test.Height+" @ "+test.Fraction+" -> "+size.X+"x"+size.Y;
            Check(active && interval()==100,"shared volume owner resumes at "+label);
            object reject=Product("volumeReactive");
            Check(Available(reject) && Dimension(reject,"Width")==size.X && Dimension(reject,"Height")==size.Y && !Available(old),"resized reject dimensions and expired previous wrapper at "+label);
            var pixels=new byte[size.X*size.Y*4];read.Invoke(backend,new object[]{Id(reject),pixels});
            Check(Enumerable.Range(0,size.X*size.Y).All(i=>Math.Abs(BitConverter.ToSingle(pixels,i*4)-expected)<.015),"resize automatically rejects old froxel history across every output pixel at "+label);
            object representative=Product("volumeRepresentativeDepth");
            Check(Dimension(representative,"Width")== (size.X+11)/12 && Dimension(representative,"Height")== (size.Y+11)/12,"froxel grid rounds partial columns at "+label);
            foreach(string name in new[]{"playerDepth","litMips"}) {
                object image=Product(name);Check(Available(image) && Dimension(image,"Width")==size.X && Dimension(image,"Height")==size.Y,"resized "+name+" matches render dimensions at "+label);
            }
            Check(frame(),"consecutive resized shared frame commits at "+label);
            read.Invoke(backend,new object[]{Id(Product("volumeReactive")),pixels});
            Check(BitConverter.ToSingle(pixels,((size.Y/2)*size.X+size.X/2)*4)<expected-.2,"new-size froxel history recovers at "+label);
        }
        resize(64,64);backend.SetRenderFraction(1);bool restored=frame();if(!restored)restored=frame();
        Check(restored,"shared volume returns to the original output and render dimensions");
        return checks;
    }
}
