using System;
using System.Collections.Generic;
using System.IO;
using ClientPlugin.Rendering.Interop;
using VRage.Import;
using VRageMath;
using VRageMath.PackedVector;
using VRageRender.Import;
using VRageRender.Messages;

namespace ClientPlugin.Rendering.Content
{
    internal sealed class VulkanModelPart
    {
        public ulong Mesh;
        public string MaterialName, ColorTexture, NormalTexture, ExtensionTexture, AlphaTexture;
        public uint[] Indices;
        public uint Flags;
        public uint IndexStart;public MyMeshDrawTechnique Technique;
        public int SourceIndex;
        public readonly List<VulkanSkinPart> SkinParts = new List<VulkanSkinPart>();
    }
    internal sealed class VulkanSkinPart { public ulong Mesh; public int[] Bones;public uint StartIndex,IndexCount; }

    internal sealed class VulkanModel
    {
        public string Name;
        public float Scale=1;
        public bool NewPipelineDataEligible;public bool? NewPipelineEligible;
        public BoundingBox? StockNewBounds;
        public BoundingBox? AuthoredBounds;
        public readonly HashSet<string> StockMaterialNames=new(StringComparer.Ordinal);
        public MyLODDescriptor[] LodDescriptors=Array.Empty<MyLODDescriptor>();
        public float[] LodDistances=Array.Empty<float>();
        public VulkanModel[] LodModels;
        public readonly Dictionary<string,List<MyMeshSectionMeshInfo>> Sections=new(StringComparer.Ordinal);
        public float[] Vertices;
        public Vector4I[] BlendIndices;
        public Vector4[] BlendWeights;
        public NativeWorldVertex[] WorldVertices;
        public long ExpandedVertexBytes,UploadedVertexBytes,IndexBytes,SkinVertexBytes,SkinIndexBytes;
        public BoundingBox Bounds;
        public readonly List<VulkanModelPart> Parts = new List<VulkanModelPart>();
        public List<VulkanModelPart> NewParts;
        public ulong SharedMesh;public readonly Dictionary<ulong,ulong> DepthRanges=new();
    }

    /// <summary>Imports game MWM through Keen's CPU importer, then owns Vulkan geometry.</summary>
    internal sealed partial class VulkanModelCache : IDisposable
    {
        private static readonly string[] Tags = { "Vertices", "Normals", "Tangents", "Bitangents", "Binormals", "TexCoords0", "MeshParts", "MeshSections", "BoundingBox", "BlendIndices", "BlendWeights", "PatternScale", "GeometryDataAsset", "LODs" };
        private readonly NativeBackend backend;
        private readonly Dictionary<string, VulkanModel> models = new Dictionary<string, VulkanModel>(StringComparer.OrdinalIgnoreCase);
        private ulong nextMesh = 0x100000000;
        public int Count => models.Count;
        private readonly bool compactGeometry=Environment.GetEnvironmentVariable("SE_VULKAN_EXPANDED_GEOMETRY")!="1";
        private long expandedVertexBytes,uploadedVertexBytes,indexBytes,skinVertexBytes,skinIndexBytes;
        public GeometryStorageStats StorageStats=>new GeometryStorageStats{Compacted=compactGeometry,ExpandedBaseVertexBytes=expandedVertexBytes,UploadedBaseVertexBytes=uploadedVertexBytes,BaseIndexBytes=indexBytes,SkinVertexBytes=skinVertexBytes,SkinIndexBytes=skinIndexBytes};
        public event Action<string, Exception> LoadFailed;

        public VulkanModelCache(NativeBackend backend) { this.backend = backend; }
        public ulong AllocateMesh() => nextMesh++;

        public VulkanModel Get(string path, float scale = 1)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            string key = path + "@" + scale.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            VulkanModel model = null;
            if (models.TryGetValue(key, out model)) return model;
            try
            {
                var importer = new MyModelImporter();
                importer.ImportData(path, Tags);
                var tags = importer.GetTagData();
                // DX11 keeps bounds authored on a geometry stub. Its geometry
                // asset supplies vertex data, not the stub's culling metadata.
                BoundingBox? authoredBounds=tags.TryGetValue("BoundingBox",out var authoredBox)?(BoundingBox)authoredBox:null;
                var lods=tags.TryGetValue("LODs",out object descriptors)?(MyLODDescriptor[])descriptors:Array.Empty<MyLODDescriptor>();
                object geometry;
                if (tags.TryGetValue("GeometryDataAsset", out geometry))
                {
                    string geometryPath = new MyLODDescriptor { Model = (string)geometry }.GetModelAbsoluteFilePath(VulkanTextureCache.ResolvePath(path));
                    importer.ImportData(geometryPath, Tags);
                    tags = importer.GetTagData();
                }
                if (!tags.ContainsKey("Vertices") || !tags.ContainsKey("MeshParts")) throw new InvalidDataException("MWM has no drawable geometry: " + path);
                var positions = (HalfVector4[])tags["Vertices"];
                var normals = tags.ContainsKey("Normals") ? (Byte4[])tags["Normals"] : null;
                var uv = tags.ContainsKey("TexCoords0") ? (HalfVector2[])tags["TexCoords0"] : null;
                var tangents = tags.ContainsKey("Tangents") ? (Byte4[])tags["Tangents"] : null;
                var bitangents = tags.ContainsKey("Binormals") ? (Byte4[])tags["Binormals"] : tags.ContainsKey("Bitangents") ? (Byte4[])tags["Bitangents"] : null;
                float patternScale = tags.ContainsKey("PatternScale") ? (float)tags["PatternScale"] : 1;
                if (patternScale <= 0) patternScale = 1;
                model = new VulkanModel { Name = path, Scale=scale,LodDescriptors=lods, Vertices = new float[checked(positions.Length * 8)], Bounds = tags.ContainsKey("BoundingBox") ? (BoundingBox)tags["BoundingBox"] : BoundingBox.CreateInvalid() };
                model.NewPipelineDataEligible=StockNewModelDataSuitable(tags);
                model.LodDistances=new float[lods.Length];for(int i=0;i<lods.Length;i++)model.LodDistances[i]=lods[i].Distance;
                if(tags.TryGetValue("MeshSections",out var sections))foreach(var section in (List<MyMeshSectionInfo>)sections)model.Sections[section.Name]=section.Meshes;
                model.Bounds = new BoundingBox(model.Bounds.Min * scale, model.Bounds.Max * scale);
                if(authoredBounds.HasValue)model.AuthoredBounds=new BoundingBox(authoredBounds.Value.Min*scale,authoredBounds.Value.Max*scale);
                model.BlendIndices = tags.ContainsKey("BlendIndices") ? (Vector4I[])tags["BlendIndices"] : null;
                model.BlendWeights = tags.ContainsKey("BlendWeights") ? (Vector4[])tags["BlendWeights"] : null;
                for (int i = 0; i < positions.Length; ++i)
                {
                    Vector3 p = VF_Packer.UnpackPosition(positions[i]) * scale;
                    Vector3 n = normals != null && i < normals.Length ? VF_Packer.UnpackNormal(normals[i]) : Vector3.Up;
                    Vector2 t = uv != null && i < uv.Length ? uv[i].ToVector2() / patternScale : Vector2.Zero;
                    // Both stock mesh pipelines store the scaled coordinates
                    // back into their half stream before vertex fetch.
                    if(patternScale!=1) t=new HalfVector2(t).ToVector2();
                    WriteVertex(model.Vertices, i, p, n, t);
                }
                var authoredParts=(List<MyMeshPartInfo>)tags["MeshParts"];
                var importedParts=new Dictionary<MyMeshPartInfo,VulkanModelPart>();
                int authoredPartIndex=0;foreach (var source in authoredParts)
                {
                    int sourceIndex=authoredPartIndex++;
                    model.StockMaterialNames.Add(source.GetMaterialName()??string.Empty);
                    if (source.m_indices.Count == 0) continue;
                    var part = new VulkanModelPart { SourceIndex=sourceIndex,Mesh = AllocateMesh(), MaterialName = source.GetMaterialName(),Technique=source.Technique, Indices = new uint[source.m_indices.Count] };
                    for (int i = 0; i < part.Indices.Length; ++i)
                    {
                        int index = source.m_indices[i];
                        if (index < 0 || index >= positions.Length) throw new InvalidDataException("MWM index outside vertex array.");
                        part.Indices[i] = (uint)index;
                    }
                    var material = source.m_MaterialDesc;
                    if (material != null)
                    {
                        part.ColorTexture = ResolveTexture(path, GetTexture(material, "ColorMetalTexture") ?? GetTexture(material, "DiffuseTexture"));
                        part.NormalTexture = ResolveTexture(path, GetTexture(material, "NormalGlossTexture") ?? GetTexture(material, "NormalTexture"));
                        part.ExtensionTexture = ResolveTexture(path, GetTexture(material, "AddMapsTexture"));
                        part.AlphaTexture = ResolveTexture(path, GetTexture(material, "AlphamaskTexture"));
                        if (source.Technique == MyMeshDrawTechnique.ALPHA_MASKED || source.Technique == MyMeshDrawTechnique.ALPHA_MASKED_SINGLE_SIDED || source.Technique == MyMeshDrawTechnique.MESH_INSTANCED_GENERIC_MASKED)
                            part.Flags |= 1;
                        if (source.Technique == MyMeshDrawTechnique.GLASS || source.Technique == MyMeshDrawTechnique.HOLO)
                            part.Flags |= 4; // Separate transparency path.
                        if (source.Technique == MyMeshDrawTechnique.HOLO) part.Flags |= 8;
                    }
                    model.Parts.Add(part);
                    importedParts.Add(source,part);
                }
                // MyMwmData sorts the complete source list by technique before
                // building NEW preprocessed parts. Keep the authored list for
                // OLD proxy/cache registration, including its first-use roles.
                var newSourceParts=new List<MyMeshPartInfo>(authoredParts);
                newSourceParts.Sort((a,b)=>((int)a.Technique).CompareTo((int)b.Technique));
                model.NewParts=new List<VulkanModelPart>();
                foreach(var source in newSourceParts)if(importedParts.TryGetValue(source,out var part))model.NewParts.Add(part);
                if (backend.WorldReady)
                {
                    BuildWorldVertices(model, tangents, bitangents, normals);
                    UploadWorldParts(model);
                }
                else foreach (var part in model.Parts) backend.UploadMesh(part.Mesh, model.Vertices, part.Indices);
                models.Add(key, model);
                return model;
            }
            catch (Exception error)
            {
                Release(model);
                models[key] = null;
                LoadFailed?.Invoke(path, error);
                return null;
            }
        }

        public VulkanModel PutRuntime(string name, MyModelData data)
        {
            string key = name + "@1";
            VulkanModel old;
            if (models.TryGetValue(key, out old)) Release(old);
            var model = new VulkanModel { Name = name, Bounds = data.AABB, Vertices = new float[checked(data.Positions.Count * 8)] };
            for (int i = 0; i < data.Positions.Count; ++i)
                WriteVertex(model.Vertices, i, data.Positions[i], i < data.Normals.Count ? data.Normals[i] : Vector3.Up, i < data.TexCoords.Count ? data.TexCoords[i] : Vector2.Zero);
            if (data.Sections.Count == 0)
                AddRuntimePart(model, data, 0, data.Indices.Count, string.Empty);
            else
                foreach (var section in data.Sections) AddRuntimePart(model, data, section.IndexStart, checked(section.TriCount * 3), section.MaterialName);
            if (backend.WorldReady)
            {
                BuildWorldVertices(model, null, null);
                UploadWorldParts(model);
            }
            models[key] = model;
            return model;
        }

        private void UploadWorldParts(VulkanModel model)
        {
            if(backend.SharedModelGeometryEnabled && model.Parts.Count>0){
                int count=0;foreach(var part in model.Parts){part.IndexStart=(uint)count;count=checked(count+part.Indices.Length);}
                var all=new uint[count];foreach(var part in model.Parts)Array.Copy(part.Indices,0,all,(int)part.IndexStart,part.Indices.Length);
                var vertices=model.WorldVertices;if(compactGeometry)WorldMeshGeometry.Compact(vertices,all,out vertices,out all);
                model.SharedMesh=AllocateMesh();backend.UploadWorldMesh(model.SharedMesh,vertices,all);
                foreach(var part in model.Parts)backend.RegisterWorldMeshRange(part.Mesh,model.SharedMesh,part.IndexStart,(uint)part.Indices.Length);
                model.ExpandedVertexBytes=model.WorldVertices.LongLength*80*model.Parts.Count;expandedVertexBytes+=model.ExpandedVertexBytes;
                model.UploadedVertexBytes=vertices.LongLength*80;uploadedVertexBytes+=model.UploadedVertexBytes;model.IndexBytes=all.LongLength*4;indexBytes+=model.IndexBytes;return;
            }
            foreach(var part in model.Parts)
            {
                var vertices=model.WorldVertices;var indices=part.Indices;
                if(compactGeometry)WorldMeshGeometry.Compact(vertices,indices,out vertices,out indices);
                backend.UploadWorldMesh(part.Mesh,vertices,indices);
                long expanded=model.WorldVertices.LongLength*80,uploaded=vertices.LongLength*80,index=indices.LongLength*4;
                model.ExpandedVertexBytes+=expanded;expandedVertexBytes+=expanded;
                model.UploadedVertexBytes+=uploaded;uploadedVertexBytes+=uploaded;model.IndexBytes+=index;indexBytes+=index;
            }
        }

        public VulkanModel GetLod(VulkanModel root,int lod)
        {
            if(lod==0 || root.LodDescriptors.Length==0)return root;
            if(lod<0 || lod>root.LodDescriptors.Length)throw new ArgumentOutOfRangeException(nameof(lod));
            if(root.LodModels==null){root.LodModels=new VulkanModel[root.LodDescriptors.Length+1];root.LodModels[0]=root;}
            // Missing mod LODs conservatively retain the authored base model.
            return root.LodModels[lod]??(root.LodModels[lod]=Get(root.LodDescriptors[lod-1].GetModelAbsoluteFilePath(VulkanTextureCache.ResolvePath(root.Name)),root.Scale)??root);
        }
        public ulong GetDepthRange(VulkanModel model,uint first,uint count){
            if(model.SharedMesh==0)throw new InvalidOperationException("Dedicated depth requires shared model geometry.");
            ulong key=((ulong)first<<32)|count;if(!model.DepthRanges.TryGetValue(key,out ulong mesh)){mesh=AllocateMesh();backend.RegisterWorldMeshRange(mesh,model.SharedMesh,first,count);model.DepthRanges.Add(key,mesh);}return mesh;
        }

        private void AddRuntimePart(VulkanModel model, MyModelData data, int start, int count, string material)
        {
            if (start < 0 || count < 0 || start > data.Indices.Count - count) throw new InvalidDataException("Runtime model section outside index array.");
            var part = new VulkanModelPart { SourceIndex=model.Parts.Count,Mesh = AllocateMesh(), MaterialName = material, Indices = new uint[count] };
            for (int i = 0; i < count; ++i)
            {
                int index = data.Indices[start + i];
                if (index < 0 || index >= data.Positions.Count) throw new InvalidDataException("Runtime model index outside vertex array.");
                part.Indices[i] = (uint)index;
            }
            if (!backend.WorldReady) backend.UploadMesh(part.Mesh, model.Vertices, part.Indices);
            model.Parts.Add(part);
        }

        private static void BuildWorldVertices(VulkanModel model, Byte4[] tangents, Byte4[] bitangents, Byte4[] normals = null)
        {
            int count = model.Vertices.Length / 8;
            var generated = new Vector3[count]; var generatedB = new Vector3[count];
            if (tangents == null || tangents.Length != count || bitangents == null || bitangents.Length != count)
                foreach (var part in model.Parts) for (int t = 0; t < part.Indices.Length; t += 3)
                {
                    int a=(int)part.Indices[t],b=(int)part.Indices[t+1],c=(int)part.Indices[t+2];
                    Vector3 P(int i)=>new Vector3(model.Vertices[i*8],model.Vertices[i*8+1],model.Vertices[i*8+2]);
                    Vector2 U(int i)=>new Vector2(model.Vertices[i*8+6],model.Vertices[i*8+7]);
                    Vector3 e=P(b)-P(a),f=P(c)-P(a);Vector2 u=U(b)-U(a),v=U(c)-U(a);float determinant=u.X*v.Y-u.Y*v.X;
                    if (Math.Abs(determinant)<1e-10f) continue;
                    Vector3 tangent=(e*v.Y-f*u.Y)/determinant,bitangent=(f*u.X-e*v.X)/determinant;
                    foreach(int i in new[]{a,b,c}){generated[i]+=tangent;generatedB[i]+=bitangent;}
                }
            model.WorldVertices = new NativeWorldVertex[count];
            for(int i=0;i<count;++i)
            {
                int o=i*8;Vector3 normal=new Vector3(model.Vertices[o+3],model.Vertices[o+4],model.Vertices[o+5]);
                Vector3 tangent=tangents!=null && tangents.Length==count?VF_Packer.UnpackNormal(tangents[i]):generated[i];
                Vector3 bitangent=bitangents!=null && bitangents.Length==count?VF_Packer.UnpackNormal(bitangents[i]):generatedB[i];
                bool authored=tangents!=null && tangents.Length==count && bitangents!=null && bitangents.Length==count;
                // Match stock authored packing. Orthogonalization/regeneration
                // applies only to assets that did not provide their full basis.
                if(!authored){tangent-=normal*Vector3.Dot(tangent,normal);
                    if(tangent.LengthSquared()<1e-10f)tangent=Vector3.Cross(normal,Math.Abs(normal.Y)<.9f?Vector3.Up:Vector3.Right);
                    tangent.Normalize();}
                Vector4 sign=new Vector4(tangent,Vector3.Dot(Vector3.Cross(tangent,normal),bitangent)<0?-1:1);
                // Stock mesh streams retain authored normal bytes. A float
                // unpack/repack can truncate a component by one packed unit.
                uint n=normals!=null && i<normals.Length?normals[i].PackedValue:VF_Packer.PackNormal(normal),packed=VF_Packer.PackTangentSign(ref sign);
                var vertex=new NativeWorldVertex{Material=new NativeGBufferVertex{X=model.Vertices[o],Y=model.Vertices[o+1],Z=model.Vertices[o+2],Scale=1,NormalX=n&65535,NormalY=n>>16,
                    TangentX=(packed&255)/255f,TangentY=((packed>>8)&255)/255f,TangentZ=((packed>>16)&255)/255f,TangentW=(packed>>24)/255f,U=model.Vertices[o+6],V=model.Vertices[o+7]}};
                if(model.BlendWeights!=null && model.BlendIndices!=null && i<model.BlendWeights.Length && i<model.BlendIndices.Length)
                {
                    Vector4 w=model.BlendWeights[i];Vector4I b=model.BlendIndices[i];vertex.WeightX=w.X;vertex.WeightY=w.Y;vertex.WeightZ=w.Z;vertex.WeightW=w.W;
                    vertex.BoneX=(uint)Math.Max(0,b.X);vertex.BoneY=(uint)Math.Max(0,b.Y);vertex.BoneZ=(uint)Math.Max(0,b.Z);vertex.BoneW=(uint)Math.Max(0,b.W);
                }
                model.WorldVertices[i]=vertex;
            }
        }

        // Immutable geometry is remapped once. Animated frames upload only the
        // stock 60-matrix palette; cube deformation keeps its original indices.
        public void EnsureSkinParts(VulkanModel model,VulkanModelPart part)
        {
            if(part.SkinParts.Count!=0)return;
            uint sourceStart=0;var triangles=new List<uint>();var map=new Dictionary<int,int>();
            void Flush()
            {
                if(triangles.Count==0)return;
                var vertices=new List<NativeWorldVertex>();var indices=new List<uint>();var remap=new Dictionary<uint,uint>();
                foreach(uint source in triangles)
                {
                    if(!remap.TryGetValue(source,out uint index))
                    {
                        var v=model.WorldVertices[source];uint Bone(uint b,float w)=>w>0?(uint)map[(int)b]:0;
                        v.BoneX=Bone(v.BoneX,v.WeightX);v.BoneY=Bone(v.BoneY,v.WeightY);v.BoneZ=Bone(v.BoneZ,v.WeightZ);v.BoneW=Bone(v.BoneW,v.WeightW);
                        index=(uint)vertices.Count;vertices.Add(v);remap.Add(source,index);
                    }
                    indices.Add(index);
                }
                var bones=new int[map.Count];foreach(var entry in map)bones[entry.Value]=entry.Key;
                ulong mesh=AllocateMesh();backend.UploadWorldMesh(mesh,vertices.ToArray(),indices.ToArray());part.SkinParts.Add(new VulkanSkinPart{Mesh=mesh,Bones=bones,StartIndex=sourceStart,IndexCount=(uint)indices.Count});
                long vb=vertices.Count*80L,ib=indices.Count*4L;model.SkinVertexBytes+=vb;skinVertexBytes+=vb;model.SkinIndexBytes+=ib;skinIndexBytes+=ib;
                sourceStart+=(uint)triangles.Count;triangles.Clear();map.Clear();
            }
            for(int t=0;t<part.Indices.Length;t+=3)
            {
                var needed=new HashSet<int>();for(int c=0;c<3;++c){var v=model.WorldVertices[part.Indices[t+c]];
                    if(v.WeightX>0)needed.Add((int)v.BoneX);if(v.WeightY>0)needed.Add((int)v.BoneY);if(v.WeightZ>0)needed.Add((int)v.BoneZ);if(v.WeightW>0)needed.Add((int)v.BoneW);}
                int additional=0;foreach(int bone in needed)if(!map.ContainsKey(bone))additional++;
                if(map.Count+additional>60)Flush();foreach(int bone in needed)if(!map.ContainsKey(bone))map.Add(bone,map.Count);
                triangles.Add(part.Indices[t]);triangles.Add(part.Indices[t+1]);triangles.Add(part.Indices[t+2]);
            }
            Flush();
        }

        internal static void WriteVertex(float[] output, int i, Vector3 p, Vector3 n, Vector2 t)
        {
            int o = i * 8;
            output[o] = p.X; output[o + 1] = p.Y; output[o + 2] = p.Z;
            output[o + 3] = n.X; output[o + 4] = n.Y; output[o + 5] = n.Z;
            output[o + 6] = t.X; output[o + 7] = t.Y;
        }

        private static string GetTexture(MyMaterialDescriptor material, string key)
        {
            string result; return material.Textures.TryGetValue(key, out result) ? result : null;
        }

        private static string ResolveTexture(string modelPath, string texture)
        {
            if (string.IsNullOrEmpty(texture)) return null;
            if (Path.IsPathRooted(texture)) return texture;
            // Content paths begin with Textures; model-relative paths are used by workshop assets.
            if (texture.StartsWith("Textures", StringComparison.OrdinalIgnoreCase))
            {
                if (!Path.IsPathRooted(modelPath)) return texture;
                string normalized = modelPath.Replace('\\', '/');
                int modelsIndex = normalized.IndexOf("/Models/", StringComparison.OrdinalIgnoreCase);
                return modelsIndex >= 0 ? Path.Combine(modelPath.Substring(0, modelsIndex), texture) : texture;
            }
            return Path.Combine(Path.GetDirectoryName(modelPath) ?? string.Empty, texture);
        }

        private void Release(VulkanModel model)
        {
            if (model == null) return;
            foreach (var part in model.Parts) { backend.RemoveResource(backend.WorldReady?3u:1u, part.Mesh); foreach(var skin in part.SkinParts)backend.RemoveResource(3,skin.Mesh); }
            foreach(var range in model.DepthRanges.Values)backend.RemoveResource(3,range);model.DepthRanges.Clear();if(model.SharedMesh!=0){backend.RemoveResource(3,model.SharedMesh);model.SharedMesh=0;}
            expandedVertexBytes-=model.ExpandedVertexBytes;uploadedVertexBytes-=model.UploadedVertexBytes;indexBytes-=model.IndexBytes;
            skinVertexBytes-=model.SkinVertexBytes;skinIndexBytes-=model.SkinIndexBytes;
            model.ExpandedVertexBytes=model.UploadedVertexBytes=model.IndexBytes=model.SkinVertexBytes=model.SkinIndexBytes=0;
        }

        public void Clear() { foreach (var model in models.Values) Release(model); models.Clear(); }
        public void Dispose() => Clear();
    }
}
