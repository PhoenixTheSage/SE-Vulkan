using System.Diagnostics;
using ClientPlugin.Rendering.Scene;
using VRageMath;

#if NET8_0_OR_GREATER
if(args.Length>=2&&args[0]=="--replay")
{
    ReplayGpuVisible(args[1]);return;
}
void ReplayGpuVisible(string path)
{
    using var document=System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
    var last=document.RootElement.GetProperty("Last");var matrix=last.GetProperty("Matrix");
    BoundingFrustumD frustum=null;
    if(matrix.ValueKind!=System.Text.Json.JsonValueKind.Null){var m=matrix.EnumerateArray().Select(x=>x.GetDouble()).ToArray();frustum=new BoundingFrustumD(new MatrixD(m[0],m[1],m[2],m[3],m[4],m[5],m[6],m[7],m[8],m[9],m[10],m[11],m[12],m[13],m[14],m[15]));}
    uint mask=last.GetProperty("SkipMainMask").GetUInt32();
    var rows=last.GetProperty("Rows").EnumerateArray().Select(r=>{var b=r.GetProperty("Bounds").EnumerateArray().Select(x=>x.GetDouble()).ToArray();return (Bounds:new BoundingBoxD(new Vector3D(b[0],b[1],b[2]),new Vector3D(b[3],b[4],b[5])),Id:r.GetProperty("Id").GetUInt32(),Flags:r.GetProperty("Flags").GetUInt32(),Destroyed:r.GetProperty("Destroyed").GetBoolean(),Owned:r.GetProperty("Owned").GetBoolean());}).ToArray();
    var prepared=new FrustumVisibility(frustum);var actual=new List<uint>();var reference=new List<uint>();
    for(int i=0;i<rows.Length;i++){ref var row=ref rows[i];if(row.Destroyed||!row.Owned||(row.Flags&mask)!=0)continue;if(prepared.Intersects(in row.Bounds))actual.Add(row.Id);if(frustum==null||frustum.Contains(row.Bounds)!=ContainmentType.Disjoint)reference.Add(row.Id);}
    var expected=last.GetProperty("Visible").EnumerateArray().Select(r=>r.GetUInt32()).ToArray();
    if(!actual.SequenceEqual(reference)||!actual.SequenceEqual(expected))throw new Exception("Actual-input replay differs from game oracle or published IDs");
    Console.WriteLine($"PASS actual-input replay: {rows.Length} actors, {expected.Length} visible IDs, exact game-oracle and publication agreement");
    var output=new uint[rows.Length];var set=new HashSet<uint>(rows.Length);
    int Predicate(bool fast){int n=0;for(int i=0;i<rows.Length;i++){ref var row=ref rows[i];if(!row.Destroyed&&row.Owned&&(row.Flags&mask)==0&&(fast?prepared.Intersects(in row.Bounds):frustum==null||frustum.Contains(row.Bounds)!=ContainmentType.Disjoint))output[n++]=row.Id;}return n;}
    Predicate(false);Predicate(true);
    for(int round=0;round<4;round++)
    {
        const int repeats=256;var timer=Stopwatch.StartNew();for(int i=0;i<repeats;i++)Predicate(false);double oldMs=timer.Elapsed.TotalMilliseconds/repeats;
        timer.Restart();for(int i=0;i<repeats;i++)Predicate(true);double fastMs=timer.Elapsed.TotalMilliseconds/repeats;
        timer.Restart();for(int i=0;i<repeats;i++){set.Clear();for(int j=0;j<actual.Count;j++)set.Add(output[j]);}double addMs=timer.Elapsed.TotalMilliseconds/repeats;
        Console.WriteLine($"ACTUAL INPUT MICROBENCH round={round} reference={oldMs:F4}ms packed={fastMs:F4}ms publicationAndClear={addMs:F4}ms");
    }
}
#endif

int checks=0;
var random=new Random(8916306);
double Rand(double lo,double hi)=>lo+(hi-lo)*random.NextDouble();
double Next(double value,bool up)
{
    if(value==0)return up?double.Epsilon:-double.Epsilon;
    long bits=BitConverter.DoubleToInt64Bits(value);
    return BitConverter.Int64BitsToDouble(bits+((value>0)==up?1:-1));
}
void Check(BoundingFrustumD frustum,in BoundingBoxD box,in FrustumVisibility prepared)
{
    bool expected=frustum==null||frustum.Contains(box)!=ContainmentType.Disjoint;
    if(prepared.Intersects(in box)!=expected)throw new Exception("Visibility mismatch at check "+checks+" box "+box+" frustum "+frustum);
    checks++;
}
for(int cameraIndex=0;cameraIndex<240;cameraIndex++)
{
    double scale=cameraIndex%3==0?1e9:cameraIndex%3==1?1e5:1;
    var camera=new Vector3D(Rand(-scale,scale),Rand(-scale,scale),Rand(-scale,scale));
    var direction=Vector3D.Normalize(new Vector3D(Rand(-1,1),Rand(-.8,.8),Rand(-1,1)));
    var view=MatrixD.CreateLookAt(camera,camera+direction,Vector3D.Up);
    double near=Rand(.01,10),far=Rand(1000,50000);
    var projection=cameraIndex%4==0?MatrixD.CreateOrthographic(Rand(1,10000),Rand(1,10000),near,far):MatrixD.CreatePerspectiveFieldOfView(Rand(.1,2.8),Rand(.3,3),near,far);
    var frustum=new BoundingFrustumD(view*projection);
    var prepared=new FrustumVisibility(frustum);
    for(int i=0;i<2000;i++)
    {
        var center=camera+new Vector3D(Rand(-far,far),Rand(-far,far),Rand(-far,far));
        var half=new Vector3D(Rand(0,200),Rand(0,200),Rand(0,200));
        var box=new BoundingBoxD(center-half,center+half);
        Check(frustum,in box,in prepared);
    }
    // Frustum corner points, zero-size boxes, and the adjacent representable
    // values on either side of each coordinate exercise strict >0 boundaries.
    foreach(var corner in frustum.GetCorners())
    for(int axis=0;axis<3;axis++)
    for(int step=-1;step<=1;step++)
    {
        var point=corner;
        if(step!=0){if(axis==0)point.X=Next(point.X,step>0);else if(axis==1)point.Y=Next(point.Y,step>0);else point.Z=Next(point.Z,step>0);}
        var box=new BoundingBoxD(point,point);
        Check(frustum,in box,in prepared);
    }
    // Preserve the game's conservative NaN behavior and invalid/empty bounds.
    foreach(var box in new[]{BoundingBoxD.CreateInvalid(),new BoundingBoxD(new Vector3D(double.NaN),new Vector3D(double.NaN)),new BoundingBoxD(new Vector3D(double.NegativeInfinity),new Vector3D(double.PositiveInfinity))})
        Check(frustum,in box,in prepared);
}
var noFrustum=new FrustumVisibility(null);var origin=new BoundingBoxD(Vector3D.Zero,Vector3D.One);Check(null,in origin,in noFrustum);
Console.WriteLine("PASS "+checks+" exact game-oracle comparisons (perspective/orthographic, 1e9 world coordinates, plane boundaries, null/invalid/nonfinite bounds)");
if(args.Contains("--benchmark"))
{
    var frustum=new BoundingFrustumD(MatrixD.CreatePerspectiveFieldOfView(1.2,16.0/9,.1,20000));var prepared=new FrustumVisibility(frustum);
    var boxes=new BoundingBoxD[6306];for(int i=0;i<boxes.Length;i++){var p=new Vector3D(Rand(-100,100),Rand(-100,100),Rand(-300,-5));boxes[i]=new BoundingBoxD(p-Vector3D.One,p+Vector3D.One);}
    int Baseline(){int result=0;for(int repeat=0;repeat<200;repeat++)for(int i=0;i<boxes.Length;i++)if(frustum.Contains(boxes[i])!=ContainmentType.Disjoint)result++;return result;}
    int Treatment(){int result=0;for(int repeat=0;repeat<200;repeat++)for(int i=0;i<boxes.Length;i++)if(prepared.Intersects(in boxes[i]))result++;return result;}
    Baseline();Treatment();
    for(int round=0;round<4;round++){var timer=Stopwatch.StartNew();int a=Baseline();double oldMs=timer.Elapsed.TotalMilliseconds;timer.Restart();int b=Treatment();double newMs=timer.Elapsed.TotalMilliseconds;if(a!=b)throw new Exception("Benchmark membership mismatch");Console.WriteLine($"MICROBENCH round={round} reference={oldMs/200:F4} ms/6306 prepared={newMs/200:F4} ms/6306 visible={a/200}");}
}
