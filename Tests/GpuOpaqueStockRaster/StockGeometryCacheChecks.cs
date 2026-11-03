using System.Text.Json;

partial class Program
{
    static void VerifyGeometryCacheCounters()
    {
        // Dispose writes recording telemetry, including the final rejected
        // validation attempt. "PerFrame" is normalized by completed frames,
        // while "Samples" counts attempts including the rejected frame.
        var path=Environment.GetEnvironmentVariable("SE_VULKAN_RECORD_TIMING");
        using var json=JsonDocument.Parse(File.ReadAllText(path));
        var root=json.RootElement;
        var row=root.GetProperty("Counters").EnumerateArray().Single(c=>
            c.GetProperty("Name").GetString()=="stock geometry validation reused");
        long attempts=row.GetProperty("Samples").GetInt64();
        long hits=(long)Math.Round(row.GetProperty("PerFrame").GetDouble()*root.GetProperty("Frames").GetInt64());
        long expected=Environment.GetEnvironmentVariable("SE_VULKAN_GPU_OPAQUE_GEOMETRY_CACHE")=="0"?0:3;
        if(attempts!=6||hits!=expected)throw new Exception($"Stock geometry validation: {hits} hits in {attempts} attempts; expected {expected} in 6.");
        Console.WriteLine($"PASS stock geometry validation: {hits} hits in {attempts} attempts");
    }
}
