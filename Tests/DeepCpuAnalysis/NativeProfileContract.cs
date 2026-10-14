using System.Reflection.PortableExecutable;
using Microsoft.Diagnostics.Symbols;
using System.Text.Json;
using System.Security.Cryptography;

static class NativeProfileContract
{
    public static void Check(string binary)
    {
        binary=Path.GetFullPath(binary);string pdb=Path.ChangeExtension(binary,"pdb");
        using var stream=File.OpenRead(binary);using var pe=new PEReader(stream);
        var records=pe.ReadDebugDirectory().Where(e=>e.Type==DebugDirectoryEntryType.CodeView).ToArray();
        if(records.Length!=1)throw new Exception("Native profiler DLL requires one CodeView signature; stale adjacent PDBs are insufficient.");
        var identity=pe.ReadCodeViewDebugDirectoryData(records[0]);
        using var symbols=new SymbolReader(TextWriter.Null,Path.GetDirectoryName(binary));
        symbols.SecurityCheck=p=>string.Equals(Path.GetFullPath(p),pdb,StringComparison.OrdinalIgnoreCase);
        var module=symbols.OpenSymbolFile(pdb);
        if(module is not NativeSymbolModule native||native.PdbGuid!=identity.Guid||native.PdbAge!=identity.Age)throw new Exception("Profiler PDB GUID/age does not match the DLL CodeView identity.");
        string Hash(string p)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)));
        Console.WriteLine(JsonSerializer.Serialize(new{DllSha256=Hash(binary),PdbSha256=Hash(pdb),PdbGuid=identity.Guid,PdbAge=identity.Age,Matched=true}));
    }
}
