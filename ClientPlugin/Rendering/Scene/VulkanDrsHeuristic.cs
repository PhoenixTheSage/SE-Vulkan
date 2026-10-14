using System;
using System.Runtime.CompilerServices;
using VRageMath;

using VRageRender;
namespace ClientPlugin.Rendering.Scene;

// Port of the installed stock DRS heuristic; renderer globals are explicit settings.
public class VulkanDrsHeuristic
{
	public MyDRSSettings Settings {get;set;}=new MyDRSSettings();
	public static readonly ulong kInvalidEntryId = ulong.MaxValue;

	private static readonly MyFrameHistoryEntry InvalidEntry = MyFrameHistoryEntry.CreateDefault();

	private MyFrameHistoryEntry[] History = Array.Empty<MyFrameHistoryEntry>();

	private int PreviousFrameIndex;

	private int HistorySize;

	private int NumberOfFramesSinceScreenPercentageChange;

	private int IgnoreFrameRemainingCount;

	private float CurrentFrameResolutionFraction = 1f;

	private ulong FrameCounter;

	/// Resets the proxy. 
	public void Reset_RenderThread()
	{
		ResetInternal();
	}

	/// Create a new previous frame and feeds its timings and returns history's unique id. 
	public ulong CreateNewPreviousFrameTimings(float GameThreadTimeMs, float RenderThreadTimeMs)
	{
		if (IgnoreFrameRemainingCount > 0)
		{
			IgnoreFrameRemainingCount--;
			return kInvalidEntryId;
		}
		ResizeHistoryIfNeeded();
		int num = (PreviousFrameIndex + 1) % History.Length;
		History[num] = MyFrameHistoryEntry.CreateDefault();
		History[num].ResolutionFraction = CurrentFrameResolutionFraction;
		History[num].GameThreadTimeMs = GameThreadTimeMs;
		History[num].RenderThreadTimeMs = RenderThreadTimeMs;
		PreviousFrameIndex = num;
		HistorySize = Math.Min(HistorySize + 1, History.Length);
		return ++FrameCounter;
	}

	/// Commit GPU busy times
	///
	/// 	@param	HistoryFrameId The history's unique id for the frame returned by CreateNewPreviousFrameTimings_RenderThread()
	/// 	@param	TotalFrameGPUBusyTimeMs Total GPU busy time for the frame.
	/// 	@param	drsGPUBusyTimeMs GPU time for sub parts of the frame that does dynamic resolution scaling.
	public void CommitPreviousFrameGPUTimings(ulong HistoryFrameId, float TotalFrameGPUBusyTimeMs, float drsGPUBusyTimeMs, bool bGPUTimingsHaveCPUBubbles)
	{
		if (HistoryFrameId != kInvalidEntryId && HistoryFrameId > (ulong)((long)FrameCounter - (long)HistorySize))
		{
			int num = (History.Length + PreviousFrameIndex - (int)(FrameCounter - HistoryFrameId)) % History.Length;
			History[num].TotalFrameGPUBusyTimeMs = TotalFrameGPUBusyTimeMs;
			History[num].GlobalDRSTimeMs = drsGPUBusyTimeMs;
			History[num].bGPUTimingsHaveCPUBubbles = bGPUTimingsHaveCPUBubbles;
		}
	}

	/// Refresh resolution fraction's from history. 
	public void RefreshCurentFrameResolutionFraction()
	{
		float frameTimeBudgetMs = Settings.FrameTimeBudgetMs;
		float num = frameTimeBudgetMs * (1f - Settings.TargetedGPUHeadRoomPercentage / 100f);
		float num2 = Settings.CPUBoundScreenPercentage / 100f;
		float num3 = CurrentFrameResolutionFraction;
		bool flag = false;
		if (HistorySize > 0)
		{
			float frameWeightExponent = Settings.FrameWeightExponent;
			float cPUTimeHeadRoom = Settings.CPUTimeHeadRoom;
			int num4 = Math.Max(Settings.MaxConsecutiveOverbudgetGPUFrameCount, 2);
			float num5 = frameTimeBudgetMs * Settings.OutlierThreshold;
			num3 = 0f;
			float num6 = 0f;
			float num7 = 1f;
			int num8 = 0;
			bool flag2 = true;
			int num9 = 0;
			for (int i = 0; i < HistorySize && flag2; i++)
			{
				MyFrameHistoryEntry previousFrameEntry = GetPreviousFrameEntry(i - 1);
				MyFrameHistoryEntry previousFrameEntry2 = GetPreviousFrameEntry(i);
				MyFrameHistoryEntry previousFrameEntry3 = GetPreviousFrameEntry(i + 1);
				if (previousFrameEntry2.TotalFrameGPUBusyTimeMs < 0f)
				{
					continue;
				}
				Math.Max(previousFrameEntry2.RenderThreadTimeMs + (previousFrameEntry2.bGPUTimingsHaveCPUBubbles ? cPUTimeHeadRoom : 0f), previousFrameEntry2.GameThreadTimeMs);
				bool num10 = previousFrameEntry2.GameThreadTimeMs > frameTimeBudgetMs;
				bool flag3 = previousFrameEntry2.RenderThreadTimeMs + (previousFrameEntry2.bGPUTimingsHaveCPUBubbles ? cPUTimeHeadRoom : 0f) > num;
				bool flag4 = num10 || flag3;
				bool flag5 = previousFrameEntry2.bGPUTimingsHaveCPUBubbles && flag3;
				int num11;
				int num12;
				if (!flag4)
				{
					num11 = ((previousFrameEntry2.TotalFrameGPUBusyTimeMs > frameTimeBudgetMs) ? 1 : 0);
					if (num11 != 0 && num8 == 0 && num5 > 0f && previousFrameEntry2.TotalFrameGPUBusyTimeMs > num5 && (!previousFrameEntry.HasGPUTimings() || (previousFrameEntry.TotalFrameGPUBusyTimeMs < num5 && previousFrameEntry.GameThreadTimeMs < num5 && previousFrameEntry.RenderThreadTimeMs < num5)))
					{
						num12 = ((!previousFrameEntry3.HasGPUTimings() || (previousFrameEntry3.TotalFrameGPUBusyTimeMs < num5 && previousFrameEntry3.GameThreadTimeMs < num5 && previousFrameEntry3.RenderThreadTimeMs < num5)) ? 1 : 0);
						goto IL_01c5;
					}
				}
				else
				{
					num11 = 0;
				}
				num12 = 0;
				goto IL_01c5;
				IL_01c5:
				bool flag6 = (byte)num12 != 0;
				if (num11 != 0)
				{
					num8++;
					if (num8 == num4)
					{
						flag2 = false;
					}
				}
				else
				{
					num8 = 0;
				}
				if (!flag6)
				{
					float num13 = 1f;
					num13 = ((!previousFrameEntry2.bGPUTimingsHaveCPUBubbles || (previousFrameEntry2.bGPUTimingsHaveCPUBubbles && !flag4 && !flag5)) ? ((float)(Math.Sqrt(num / previousFrameEntry2.TotalFrameGPUBusyTimeMs) * (double)previousFrameEntry2.ResolutionFraction)) : ((!(previousFrameEntry2.ResolutionFraction > num2)) ? previousFrameEntry2.ResolutionFraction : num2));
					num3 += num13 * num7;
					num6 += num7;
					num9++;
					num7 *= frameWeightExponent;
				}
			}
			if (!(num6 > 0)) return;
			num3 /= num6;
			flag = num9 > 0 && num8 == num9;
			if (flag)
			{
				HistorySize = 0;
			}
			else
			{
				if (Settings.ChangePercentageThreshold > 0f)
				{
					float num14 = CurrentFrameResolutionFraction * (1f + Settings.ChangePercentageThreshold / 100f);
					_ = CurrentFrameResolutionFraction;
					_ = Settings.ChangePercentageThreshold / 100f;
					if (num3 > CurrentFrameResolutionFraction && num3 < num14)
					{
						num3 = CurrentFrameResolutionFraction;
					}
				}
				if (num3 > CurrentFrameResolutionFraction)
				{
					num3 = MathHelper.Lerp(CurrentFrameResolutionFraction, num3, Settings.IncreaseAmortizationBlendFactor);
				}
			}
			num3 = MathHelper.Clamp(num3, Settings.MinScreenPercentage / 100f, GetResolutionFractionUpperBound());
		}
		if ((CurrentFrameResolutionFraction != num3 && NumberOfFramesSinceScreenPercentageChange >= Settings.MinResolutionChangePeriod) || flag)
		{
			NumberOfFramesSinceScreenPercentageChange = 0;
			CurrentFrameResolutionFraction = num3;
		}
		else
		{
			NumberOfFramesSinceScreenPercentageChange++;
		}
	}

	/// Returns the view fraction upper bound. 
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public float GetResolutionFractionUpperBound()
	{
		return Settings.MaxScreenPercentage / 100f;
	}

	/// Returns the view fraction that should be used for current frame. 
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public float QueryCurentFrameResolutionFraction()
	{
		return Math.Min(CurrentFrameResolutionFraction, GetResolutionFractionUpperBound());
	}

	/// Returns a non thread safe approximation of the current resolution fraction applied on render thread. 
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public float GetResolutionFractionApproximation()
	{
		return Math.Min(CurrentFrameResolutionFraction, GetResolutionFractionUpperBound());
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private MyFrameHistoryEntry GetPreviousFrameEntry(int BrowsingFrameId)
	{
		if (BrowsingFrameId < 0 || BrowsingFrameId >= HistorySize)
		{
			return InvalidEntry;
		}
		return History[(History.Length + PreviousFrameIndex - BrowsingFrameId) % History.Length];
	}

	private void ResetInternal()
	{
		PreviousFrameIndex = -1;
		HistorySize = 0;
		History = Array.Empty<MyFrameHistoryEntry>();
		NumberOfFramesSinceScreenPercentageChange = 0;
		CurrentFrameResolutionFraction = 1f;
		IgnoreFrameRemainingCount = 1;
	}

	private void ResizeHistoryIfNeeded()
	{
		uint num = (uint)Math.Max(1, Settings.HistorySize);
		if (History.Length != num)
		{
			MyFrameHistoryEntry[] array = new MyFrameHistoryEntry[num];
			int num2 = Math.Min(HistorySize, array.Length);
			int num3 = num2 - 1;
			for (int i = 0; i < num2; i++)
			{
				array[num3 - i] = History[(History.Length + PreviousFrameIndex - i) % History.Length];
			}
			History = array;
			HistorySize = num2;
			PreviousFrameIndex = num3;
		}
	}
}
