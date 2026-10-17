using System;
using System.Globalization;
using System.IO;
using System.Text;
using ClientPlugin.Diagnostics;
using VRage.Stats;
using VRageMath;
using VRageRender;
using VRageRender.Utils;

namespace ClientPlugin.Rendering;

/// <summary>Draws Keen's Shift-F11 statistics without invoking MyRender11.</summary>
internal sealed class VulkanRenderStats
{
    private readonly StringBuilder text = new(4096);
    private bool rendererPage;

    public void Draw(VulkanSprites sprites, int width)
    {
        var state = MyRenderProxy.DrawRenderStats;
        if (state == MyRenderProxy.MyStatsState.NoDraw || state == MyRenderProxy.MyStatsState.Last)
        {
            rendererPage = false;
            return;
        }
        // Developer mode cycles from complex timing into backend statistics.
        // Supply one Vulkan page, then let MyDX9Gui close its timing screen.
        if (state == MyRenderProxy.MyStatsState.MoveNext)
        {
            MyRenderProxy.DrawRenderStats = rendererPage ? MyRenderProxy.MyStatsState.Last : MyRenderProxy.MyStatsState.Draw;
            rendererPage = !rendererPage;
            if (!rendererPage) return;
            state = MyRenderProxy.MyStatsState.Draw;
        }
        if (state == MyRenderProxy.MyStatsState.Draw)
        {
            rendererPage = true;
            text.Clear().AppendLine("Vulkan renderer");
            AppendVulkanTiming(text, FrameProfiler.Capture(true), true);
            sprites.DrawDebugText(text.ToString(), new Vector2(10, 10));
            return;
        }
        rendererPage = false;
        var frame = FrameProfiler.Capture(true);
        if (state == MyRenderProxy.MyStatsState.SimpleTimingStats)
        {
            text.Clear();
            Stats.Timing.WriteTo(text);
            sprites.DrawDebugText(ReplaceGpuTiming(text.ToString(), frame, true), new Vector2(10, 10));
            return;
        }
        foreach (var column in MyRenderStats.m_stats)
        {
            text.Clear();
            foreach (var stats in column.Value)
            {
                stats.WriteTo(text);
                text.AppendLine();
            }
            string rendered = ReplaceGpuTiming(text.ToString(), frame, column.Key == MyRenderStats.ColumnEnum.Left);
            bool right = column.Key == MyRenderStats.ColumnEnum.Right;
            sprites.DrawDebugText(rendered, new Vector2(right ? width - 10 : 10, 10), rightAligned: right);
        }
    }

    internal static string ReplaceGpuTiming(string stock, FrameProfiler.View frame, bool append)
    {
        // The stock GPU row reads fields produced only by MyRender11. Filter
        // the rendered copy, leaving the game's shared collectors untouched.
        MyStatKeys.GetNameAndPriority(MyStatKeys.StatKeysEnum.RenderGpuLoad, out string format, out _);
        int argument = format.IndexOf('{');
        string prefix = argument < 0 ? format : format.Substring(0, argument);
        var result = new StringBuilder(stock.Length + 128);
        using (var lines = new StringReader(stock))
        {
            string line;
            while ((line = lines.ReadLine()) != null)
                if (!line.StartsWith(prefix, StringComparison.Ordinal)) result.AppendLine(line);
        }
        if (append) AppendVulkanTiming(result, frame, false);
        return result.ToString();
    }

    private static void AppendVulkanTiming(StringBuilder target, FrameProfiler.View frame, bool details)
    {
        target.Append("Vulkan GPU frame: ");
        if (frame.HaveFrame && frame.GpuSamples > 0 && frame.GpuMs >= 0 && !float.IsNaN(frame.GpuMs) && !float.IsInfinity(frame.GpuMs))
            target.Append(frame.GpuMs.ToString("0.00", CultureInfo.InvariantCulture)).AppendLine("ms (timestamps)");
        else target.AppendLine("unavailable (awaiting a world GPU sample)");
        if (!details || !frame.HaveFrame) return;
        target.Append("Render thread frame: ").Append(frame.Total.ToString("0.00", CultureInfo.InvariantCulture)).AppendLine("ms");
        target.Append("World draws: ").Append(frame.WorldDraws).AppendLine();
        for (int i = 0; i < FrameProfiler.SectionCount; ++i)
            target.Append(FrameProfiler.Names[i]).Append(": ").Append(frame.Milliseconds[i].ToString("0.00", CultureInfo.InvariantCulture)).AppendLine("ms CPU");
    }
}
