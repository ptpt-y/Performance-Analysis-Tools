#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;

namespace AvgProfilerStreaming.Tests
{
    internal sealed class FixtureProfilerFrameSource : IProfilerFrameSource
    {
        private readonly Dictionary<int, ProfilerFrameSnapshot> _frames = new Dictionary<int, ProfilerFrameSnapshot>();

        public bool IsRecording { get; set; }
        public int FirstFrameIndex { get; set; }
        public int LastFrameIndex { get; set; }
        public string ConnectionName { get; set; } = "Fixture";

        public void AddFrame(int frameIndex, double frameTimeMs, double fps)
        {
            ProfilerFrameSnapshot frame = new ProfilerFrameSnapshot { frameIndex = frameIndex };
            frame.Add(MetricIds.FrameTime, frameTimeMs);
            frame.Add(MetricIds.FpsArithmetic, fps);
            _frames[frameIndex] = frame;
        }

        public bool TryReadFrame(int frameIndex, int relativeIndex, out ProfilerFrameSnapshot snapshot)
        {
            if (!_frames.TryGetValue(frameIndex, out snapshot))
                return false;
            snapshot.relativeIndex = relativeIndex;
            return true;
        }

        public void ResetCaches() { }
    }
}
#endif
