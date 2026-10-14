using System.Runtime.Loader;
using System.Text.Json;
using VRage.FileSystem;
using VRageRender.Import;

class Program
{
    static int Main(string[] args){string bin=Path.GetFullPath(args[0]);AssemblyLoadContext.Default.Resolving+=(_,name)=>{string path=Path.Combine(bin,name.Name+".dll");return File.Exists(path)?AssemblyLoadContext.Default.LoadFromAssemblyPath(path):null;};return Run(bin,Path.GetFullPath(args[1]));}
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static int Run(string bin,string workload)
    {
        string content=Path.GetFullPath(Path.Combine(bin,"../Content"));MyFileSystem.Init(content,Path.GetDirectoryName(workload));
        var source=JsonDocument.Parse(File.ReadAllText(workload));var used=source.RootElement.GetProperty("Packets").EnumerateArray().Where(p=>p.GetProperty("Pipeline").GetInt32()<20&&p.GetProperty("ModelKey").ValueKind==JsonValueKind.String).Select(p=>p.GetProperty("ModelKey").GetString()).ToHashSet();
        string Resolve(string file)=>Path.IsPathRooted(file)?file:Path.Combine(content,file);
        object Geometry(string file){
            file=Resolve(file);if(!File.Exists(file))throw new FileNotFoundException("Stock asset path unavailable",file);
            var importer=new MyModelImporter();importer.ImportData(file,new[]{"MeshParts","GeometryDataAsset"});var tags=importer.GetTagData();
            if(tags.TryGetValue("GeometryDataAsset",out object redirect)){file=new MyLODDescriptor{Model=(string)redirect}.GetModelAbsoluteFilePath(file);importer.ImportData(file,new[]{"MeshParts"});tags=importer.GetTagData();}
            var parts=(List<MyMeshPartInfo>)tags["MeshParts"];var rows=parts.Select(p=>new{Material=p.m_MaterialDesc?.MaterialName,Technique=p.Technique.ToString(),IndexCount=p.m_indices.Count}).ToArray();
            return new{File=file,IndexCount=rows.Sum(p=>(long)p.IndexCount),OpaqueIndexCount=rows.Where(p=>p.Technique!="GLASS"&&p.Technique!="HOLO").Sum(p=>(long)p.IndexCount),Parts=rows};
        }
        var result=new List<object>();int failures=0,withLods=0;
        foreach(var model in source.RootElement.GetProperty("Models").EnumerateArray()){
            string key=model.GetProperty("Key").GetString();if(!used.Contains(key)||!model.TryGetProperty("File",out var value))continue;
            string file=Resolve(value.GetString());try{
                var importer=new MyModelImporter();importer.ImportData(file,new[]{"LODs","GeometryDataAsset"});var tags=importer.GetTagData();var descriptors=tags.TryGetValue("LODs",out object lods)?(MyLODDescriptor[])lods:Array.Empty<MyLODDescriptor>();
                var levels=new List<object>{new{Lod=0,Distance=0f,Geometry=Geometry(file)}};
                for(int i=0;i<descriptors.Length;i++){var lod=descriptors[i];levels.Add(new{Lod=i+1,Distance=lod.Distance,Geometry=Geometry(lod.GetModelAbsoluteFilePath(file))});}
                if(descriptors.Length>0)withLods++;result.Add(new{Key=key,File=file,Levels=levels});
            }catch(Exception error){failures++;result.Add(new{Key=key,File=file,Error=error.ToString()});}
        }
        string output=Path.Combine(Path.GetDirectoryName(workload),"model-lod-assets.json");File.WriteAllText(output,JsonSerializer.Serialize(new{Source=workload,Models=result,Failures=failures,ModelsWithLods=withLods,Scope="Read-only stock model LOD metadata and geometry counts for mapped Vulkan packet models. Not a selected-LOD prediction, reduced-quality policy or benchmark."},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"Audited {result.Count} models, {withLods} with LODs, {failures} unavailable: {output}");return 0;
    }
}
