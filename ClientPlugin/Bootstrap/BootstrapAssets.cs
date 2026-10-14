using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;

namespace ClientPlugin.Bootstrap;

public static class BootstrapAssets
{
    // Native dependencies must be resolvable before Plugin.Init or LoadAssets.
    // -Sources compiles into Pulsar's DevFolder, which does not receive <AssetFolder>, so the
    // registered repository in Sources/sources.xml is the asset root for that launch.
    public static string FindDirectory(string requiredRelativePath) => FindDirectory(requiredRelativePath, null);
    internal static string FindDirectory(string requiredRelativePath, string extraStart)
    {
        if (string.IsNullOrEmpty(requiredRelativePath) || Path.IsPathRooted(requiredRelativePath)) throw new ArgumentException("A relative asset path is required.", nameof(requiredRelativePath));
        var candidates = new List<string>();
        string explicitDirectory = Environment.GetEnvironmentVariable("SE_VULKAN_ASSET_DIR");
        if (!string.IsNullOrWhiteSpace(explicitDirectory)) candidates.Add(explicitDirectory);
        string location = typeof(BootstrapAssets).Assembly.Location;
        if (!string.IsNullOrEmpty(location)) AddNearby(candidates, Path.GetDirectoryName(location));
        AddNearby(candidates, AppDomain.CurrentDomain.BaseDirectory);
        if (!string.IsNullOrEmpty(extraStart)) AddNearby(candidates, extraStart);
        foreach (string candidate in candidates)
        {
            string directory;
            try { directory = Path.GetFullPath(candidate); }
            catch (Exception) { continue; }
            if (File.Exists(Path.Combine(directory, requiredRelativePath))) return directory;
        }
        return null;
    }
    private static void AddNearby(List<string> candidates, string start)
    {
        Add(candidates, start);
        if (string.IsNullOrEmpty(start)) return;
        int depth = 0;
        for (DirectoryInfo dir = new DirectoryInfo(start); dir != null && depth < 8; dir = dir.Parent, depth++)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Vulkan.xml"))) Add(candidates, dir.FullName);
            AddSources(candidates, Path.Combine(dir.FullName, "Sources", "sources.xml"));
            AddSources(candidates, Path.Combine(dir.FullName, "Interim", "Sources", "sources.xml"));
            AddSources(candidates, Path.Combine(dir.FullName, "Legacy", "Sources", "sources.xml"));
            AddSources(candidates, Path.Combine(dir.FullName, "Pulsar", "Interim", "Sources", "sources.xml"));
            AddSources(candidates, Path.Combine(dir.FullName, "Pulsar", "Legacy", "Sources", "sources.xml"));
        }
    }
    private static void AddSources(List<string> candidates, string manifest)
    {
        if (!File.Exists(manifest)) return;
        try
        {
            foreach (XElement folder in XDocument.Load(manifest).Descendants("Folder"))
            {
                string path = folder.Value?.Trim();
                if (!string.IsNullOrEmpty(path)) Add(candidates, path);
            }
        }
        catch (Exception) { }
    }
    private static void Add(List<string> candidates, string directory)
    {
        if (string.IsNullOrEmpty(directory)) return;
        candidates.Add(directory); candidates.Add(Path.Combine(directory, "Native")); candidates.Add(Path.Combine(directory, "Assets"));
    }
}
