using System;
using System.Collections.Generic;

namespace AvgProfilerStreaming
{
    internal static class SessionStatistics
    {
        public static List<MetricSummary> Calculate(IList<ProfilerFrameSnapshot> samples, int ignoreHead, int ignoreTail)
        {
            List<ProfilerFrameSnapshot> validFrames = GetAnalysisFrames(samples, ignoreHead, ignoreTail);
            List<MetricSummary> result = new List<MetricSummary>();
            for (int i = 0; i < MetricIds.Definitions.Length; i++)
            {
                MetricDefinition definition = MetricIds.Definitions[i];
                result.Add(definition.Id == MetricIds.FpsDuration
                    ? CalculateDurationFps(validFrames)
                    : CalculateMetric(validFrames, definition.Id));
            }
            return result;
        }

        internal static List<ProfilerFrameSnapshot> GetAnalysisFrames(
            IList<ProfilerFrameSnapshot> samples,
            int ignoreHead,
            int ignoreTail)
        {
            List<ProfilerFrameSnapshot> valid = new List<ProfilerFrameSnapshot>();
            for (int i = 0; i < samples.Count; i++)
            {
                double frameTime;
                if (samples[i].TryGet(MetricIds.FrameTime, out frameTime) && frameTime > 0)
                    valid.Add(samples[i]);
            }
            int start = Math.Min(Math.Max(0, ignoreHead), valid.Count);
            int end = Math.Max(start, valid.Count - Math.Max(0, ignoreTail));
            return valid.GetRange(start, end - start);
        }

        internal static List<ProfilerFrameSnapshot> SelectProfilerFrameRange(
            IList<ProfilerFrameSnapshot> frames,
            int firstProfilerUiFrame,
            int lastProfilerUiFrame)
        {
            List<ProfilerFrameSnapshot> selected = new List<ProfilerFrameSnapshot>();
            for (int i = 0; i < frames.Count; i++)
            {
                int profilerUiFrame = frames[i].frameIndex + 1;
                if (profilerUiFrame >= firstProfilerUiFrame && profilerUiFrame <= lastProfilerUiFrame)
                    selected.Add(frames[i]);
            }
            return selected;
        }

        private static MetricSummary CalculateMetric(IList<ProfilerFrameSnapshot> frames, string metricId)
        {
            MetricSummary summary = new MetricSummary { metricId = metricId };
            double sum = 0;
            bool initialized = false;
            for (int i = 0; i < frames.Count; i++)
            {
                double value;
                if (!frames[i].TryGet(metricId, out value))
                    continue;
                if (!initialized || value < summary.minimum)
                    SetMinimum(summary, frames[i], value);
                if (!initialized || value > summary.maximum)
                    SetMaximum(summary, frames[i], value);
                initialized = true;
                sum += value;
                summary.sampleCount++;
            }
            if (summary.sampleCount > 0)
                summary.average = sum / summary.sampleCount;
            if (summary.sampleCount > 0 && (metricId == MetricIds.Logic || metricId == MetricIds.Rendering))
                ApplyRatios(summary, frames, metricId);
            return summary;
        }

        private static MetricSummary CalculateDurationFps(IList<ProfilerFrameSnapshot> frames)
        {
            MetricSummary summary = new MetricSummary { metricId = MetricIds.FpsDuration };
            double frameTimeSum = 0;
            bool initialized = false;
            for (int i = 0; i < frames.Count; i++)
            {
                double frameTime;
                if (!frames[i].TryGet(MetricIds.FrameTime, out frameTime) || frameTime <= 0)
                    continue;
                double fps;
                if (!frames[i].TryGet(MetricIds.FpsArithmetic, out fps) || fps <= 0)
                    fps = 1000.0 / frameTime;
                if (!initialized || fps < summary.minimum)
                    SetMinimum(summary, frames[i], fps);
                if (!initialized || fps > summary.maximum)
                    SetMaximum(summary, frames[i], fps);
                initialized = true;
                frameTimeSum += frameTime;
                summary.sampleCount++;
            }
            if (summary.sampleCount > 0 && frameTimeSum > 0)
                summary.average = 1000.0 * summary.sampleCount / frameTimeSum;
            return summary;
        }

        private static void ApplyRatios(MetricSummary summary, IList<ProfilerFrameSnapshot> frames, string metricId)
        {
            double metricSum = 0;
            double playerLoopSum = 0;
            for (int i = 0; i < frames.Count; i++)
            {
                double metric;
                double playerLoop;
                if (frames[i].TryGet(metricId, out metric) &&
                    frames[i].TryGet(MetricIds.PlayerLoop, out playerLoop))
                {
                    metricSum += metric;
                    playerLoopSum += playerLoop;
                }

                if (frames[i].frameIndex == summary.maximumFrame &&
                    frames[i].TryGet(MetricIds.PlayerLoop, out playerLoop) && playerLoop != 0)
                {
                    summary.extremePlayerLoopRatio = summary.maximum / playerLoop;
                    summary.hasRatioAnomaly = summary.maximum < 0 || summary.extremePlayerLoopRatio < 0 || summary.extremePlayerLoopRatio > 1;
                }
            }

            if (playerLoopSum == 0)
                return;
            summary.averageRatioValid = true;
            summary.averagePlayerLoopRatio = metricSum / playerLoopSum;
            summary.hasAverageRatioAnomaly = metricSum < 0 || summary.averagePlayerLoopRatio < 0 || summary.averagePlayerLoopRatio > 1;
        }

        private static void SetMinimum(MetricSummary summary, ProfilerFrameSnapshot frame, double value)
        {
            summary.minimum = value;
            summary.minimumFrame = frame.frameIndex;
            summary.minimumRelativeFrame = frame.relativeIndex;
        }

        private static void SetMaximum(MetricSummary summary, ProfilerFrameSnapshot frame, double value)
        {
            summary.maximum = value;
            summary.maximumFrame = frame.frameIndex;
            summary.maximumRelativeFrame = frame.relativeIndex;
        }
    }
}
