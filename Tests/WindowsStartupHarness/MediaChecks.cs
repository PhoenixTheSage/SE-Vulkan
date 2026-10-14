using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ClientPlugin.Diagnostics;
using VRageMath;
using VRageRender;
using Sandbox.Game.Gui;
using Sandbox.Graphics.GUI;
using System.Reflection;

namespace WindowsStartupHarness;

// Observes the stock intro/menu rather than substituting a decoder test screen.
internal sealed class MediaChecks
{
    private sealed class Sample { internal double Time; internal long Bytes, Textures, Frames; }
    private readonly List<Sample> samples = new List<Sample>();
    private readonly string directory;
    private double next;
    private long previousFrames, previousId = -1, progressed;
    private int audioSamples;
    private long maxAvOffset;
    private bool earlyCapture, menuCapture;
    private readonly Dictionary<string, double> sourceRates = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    private long previousPosition;
    private double expectedFrames, deliveredFrames;
    private readonly bool testSkip = Environment.GetEnvironmentVariable("SE_VULKAN_STARTUP_MEDIA_SKIP") == "1";
    private bool skipRequested, skipPassed;
    private uint skippedId;
    private bool introObserved, introClosed, introCancelled, introFallback;
    internal MediaChecks(string directory)
    {
        this.directory = directory;
        string rates = Path.Combine(directory, "media-video-rates.tsv");
        if (File.Exists(rates)) foreach (string row in File.ReadAllLines(rates))
        { var parts = row.Split('\t');if (parts.Length == 2) { double rate=double.Parse(parts[1], CultureInfo.InvariantCulture);if(double.IsNaN(rate)||double.IsInfinity(rate)||rate<=0)throw new InvalidOperationException("Invalid source cadence: "+row);sourceRates[parts[0]] = rate; } }
    }
    internal void Update(double time)
    {
        if (time < next) return;next = time + 1;
        string status = VulkanDiagnostics.Report();
        var match = Regex.Match(status, @"Native media: Active .*?Video (\d+): (\d+)x(\d+); frames=(\d+);.*?position us=(\d+); A/V offset us=(unavailable|-?\d+); audio=(\w+)");
        long frames = 0, position = -1, avOffset = long.MinValue, idSample = -1;
        string sourceName = "";
        long dropped = -1;
        var startupScreen=MyScreenManager.GetFirstScreenOfType<MyGuiScreenIntroVideo>();
        if(!introObserved && !introFallback && time>10 && startupScreen!=null && (bool)typeof(MyGuiScreenIntroVideo).GetField("m_loop",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(startupScreen)){
            introFallback=true;
            // Some launchers override ENABLE_LOGOS after plugin initialization.
            // Re-enter the game's original intro chain, once, in this isolated
            // profile rather than claiming menu footage proves KSH playback.
            MyGuiSandbox.BackToIntroLogos(()=>MyGuiSandbox.BackToMainMenu());
        }
        if (match.Success)
        {
            long id = long.Parse(match.Groups[1].Value), current = long.Parse(match.Groups[4].Value);
            idSample = id;position = long.Parse(match.Groups[5].Value);
            var dropMatch = Regex.Match(status, @"Native media: Active .*?dropped=(\d+)");if(dropMatch.Success)dropped=long.Parse(dropMatch.Groups[1].Value);
            var screen = MyScreenManager.GetFirstScreenOfType<MyGuiScreenIntroVideo>();
            if(screen != null)sourceName=Path.GetFileName((string)typeof(MyGuiScreenIntroVideo).GetField("m_currentVideo",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(screen));
            if(screen != null && sourceName.Equals("KSH.wmv",StringComparison.OrdinalIgnoreCase) && !introObserved){
                introObserved=true;
                screen.Closed+=(closed,isUnloading)=>{introClosed=true;introCancelled=closed.Cancelled;File.WriteAllText(Path.Combine(directory,"media-intro-closed.txt"),"Stock KSH screen closed; Cancelled="+introCancelled);};
                File.WriteAllText(Path.Combine(directory,"media-intro-observed.txt"),"Stock KSH screen; ID="+id+"; position us="+position);
                MyRenderProxy.TakeScreenshot(Vector2.One,Path.Combine(directory,"media-intro-frame.png"),false,false,false);
            }
            if(testSkip && !skipRequested && screen != null && sourceName.Equals("KSH.wmv",StringComparison.OrdinalIgnoreCase) && position>2000000){
                skipRequested=true;skippedId=(uint)id;
                // This is the stock action invoked by the intro's Space/Enter/
                // mouse handler. Input-device delivery is a separate check.
                typeof(MyGuiScreenBase).GetMethod("Canceling",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(screen,null);
                File.WriteAllText(Path.Combine(directory,"media-skip-requested.txt"),"Stock Canceling action; ID="+id+"; position us="+position);
            }
            if(skipRequested && !skipPassed && id!=(long)skippedId && !MyRenderProxy.IsVideoValid(skippedId)){
                skipPassed=true;File.WriteAllText(Path.Combine(directory,"media-skip-passed.txt"),"Skipped intro ID is invalid; menu video is progressing under a new ID.");
            }
            if(id==previousId && current>=previousFrames && position>previousPosition && previousPosition>2000000 && sourceRates.TryGetValue(sourceName,out double fps))
            {expectedFrames+=(position-previousPosition)*fps/1000000;deliveredFrames+=current-previousFrames;}
            if (match.Groups[6].Value != "unavailable") avOffset = long.Parse(match.Groups[6].Value);
            if (id == previousId && current >= previousFrames) progressed += current - previousFrames;
            previousId = id;previousFrames = current;frames = progressed;
            previousPosition=position;
            if (long.Parse(match.Groups[5].Value) > 1000000 && match.Groups[6].Value != "unavailable" && match.Groups[7].Value == "device")
            { audioSamples++;maxAvOffset = Math.Max(maxAvOffset, Math.Abs(long.Parse(match.Groups[6].Value))); }
            if (!earlyCapture) { earlyCapture = true;MyRenderProxy.TakeScreenshot(Vector2.One, Path.Combine(directory, "media-first-frame.png"), false, false, false); }
            if (!menuCapture && time > 30) { menuCapture = true;MyRenderProxy.TakeScreenshot(Vector2.One, Path.Combine(directory, "media-menu-frame.png"), false, false, false); }
        }
        var resource = Regex.Match(status, @"Resources: textures=(\d+)");
        using (var process = Process.GetCurrentProcess())
        {
            var sample = new Sample { Time = time, Bytes = process.PrivateMemorySize64, Textures = resource.Success ? long.Parse(resource.Groups[1].Value) : -1, Frames = frames };
            samples.Add(sample);
            if (samples.Count == 1) File.WriteAllText(Path.Combine(directory, "media-samples.csv"), "elapsed_seconds,private_bytes,textures,video_frames,video_id,position_us,av_offset_us,dropped,source\n");
            File.AppendAllText(Path.Combine(directory, "media-samples.csv"), time.ToString("F3", CultureInfo.InvariantCulture) + "," + sample.Bytes + "," + sample.Textures + "," + sample.Frames + "," + idSample + "," + position + "," + (avOffset == long.MinValue ? "" : avOffset.ToString()) + ","+dropped+",\""+sourceName.Replace("\"","\"\"")+"\"\n");
        }
    }
    internal void Complete(double time)
    {
        var steady = samples.Where(s => s.Time >= 120).ToArray();
        double cadence = progressed / Math.Max(1, time);
        long memoryDelta = steady.Length > 1 ? steady.Last().Bytes - steady.First().Bytes : 0;
        long textureRange = steady.Length > 1 ? steady.Max(s => s.Textures) - steady.Min(s => s.Textures) : 0;
        double cadenceRatio=expectedFrames>0?deliveredFrames/expectedFrames:0;
        string result = "Duration=" + time.ToString("F1", CultureInfo.InvariantCulture) + "; observed video frames=" + progressed + "; observed frames/sec=" + cadence.ToString("F2", CultureInfo.InvariantCulture) + "; steady source-cadence ratio="+cadenceRatio.ToString("F4",CultureInfo.InvariantCulture)+"; steady private-byte delta=" + memoryDelta + "; texture range=" + textureRange + "; real audio samples=" + audioSamples + "; max observed A/V offset us=" + maxAvOffset;
        File.WriteAllText(Path.Combine(directory, "media-result.txt"), result);
        if(!introObserved || !introClosed || introCancelled!=testSkip)throw new InvalidOperationException("Stock KSH completion/skip observation is incomplete: "+result);
        if(testSkip && !skipPassed)throw new InvalidOperationException("Stock intro skip action did not complete: "+result);
        if (progressed < 100 || !menuCapture) throw new InvalidOperationException("Stock intro/menu media did not produce moving frames: " + result);
        if (audioSamples > 0 && maxAvOffset > 100000) throw new InvalidOperationException("Observed media A/V offset exceeds 100ms: " + result);
        if(sourceRates.Count>0 && expectedFrames<=100)throw new InvalidOperationException("Source cadence observation is incomplete: "+result);
        if(sourceRates.Count>0 && cadenceRatio<.98)throw new InvalidOperationException("Media does not sustain source cadence within 2%: "+result);
        if (time >= 600 && (memoryDelta > 32L * 1024 * 1024 || textureRange > 4)) throw new InvalidOperationException("Media soak shows continuing resource growth: " + result);
        File.WriteAllText(Path.Combine(directory, "media-passed.txt"), result + "\nAudio without an observed stream and matched DX11 pixels require separate evidence.");
    }
}
