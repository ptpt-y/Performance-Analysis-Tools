#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace AvgProfilerStreaming.Tests
{
    internal sealed class SessionStatisticsTests
    {
        [Test]
        public void Calculate_UsesBothFpsDefinitionsAndSkipsMissingMetrics()
        {
            List<ProfilerFrameSnapshot> frames = new List<ProfilerFrameSnapshot>
            {
                Frame(10, 1, 10, 100, 4),
                Frame(11, 2, 20, 50, null)
            };

            List<MetricSummary> summaries = SessionStatistics.Calculate(frames, 0, 0);

            Assert.That(Find(summaries, MetricIds.FpsArithmetic).average, Is.EqualTo(75).Within(0.001));
            Assert.That(Find(summaries, MetricIds.FpsDuration).average, Is.EqualTo(2000.0 / 30.0).Within(0.001));
            Assert.That(Find(summaries, MetricIds.FpsArithmetic).minimumFrame, Is.EqualTo(11));
            Assert.That(Find(summaries, MetricIds.PlayerLoop).sampleCount, Is.EqualTo(1));
            Assert.That(Find(summaries, MetricIds.PlayerLoop).average, Is.EqualTo(4));
        }

        [Test]
        public void Calculate_AppliesValidFrameHeadAndTailFilters()
        {
            List<ProfilerFrameSnapshot> frames = new List<ProfilerFrameSnapshot>
            {
                Frame(1, 1, 10, 100, null),
                Frame(2, 2, 20, 50, null),
                Frame(3, 3, 40, 25, null)
            };

            MetricSummary summary = Find(SessionStatistics.Calculate(frames, 1, 1), MetricIds.FrameTime);

            Assert.That(summary.sampleCount, Is.EqualTo(1));
            Assert.That(summary.average, Is.EqualTo(20));
            Assert.That(summary.maximumFrame, Is.EqualTo(2));
        }

        [Test]
        public void SelectProfilerFrameRange_UsesInclusiveProfilerUiFrameNumbers()
        {
            List<ProfilerFrameSnapshot> frames = new List<ProfilerFrameSnapshot>
            {
                Frame(10, 1, 10, 100, null),
                Frame(11, 2, 20, 50, null),
                Frame(12, 3, 40, 25, null)
            };

            List<ProfilerFrameSnapshot> selected = SessionStatistics.SelectProfilerFrameRange(frames, 12, 13);
            MetricSummary summary = Find(SessionStatistics.Calculate(selected, 0, 0), MetricIds.FrameTime);

            Assert.That(selected.Count, Is.EqualTo(2));
            Assert.That(selected[0].frameIndex, Is.EqualTo(11));
            Assert.That(selected[1].frameIndex, Is.EqualTo(12));
            Assert.That(summary.average, Is.EqualTo(30));
        }

        [Test]
        public void Calculate_UsesSumRatioForLogicAndKeepsAnomalies()
        {
            ProfilerFrameSnapshot first = Frame(1, 1, 10, 100, 10);
            first.Add(MetricIds.Logic, 4);
            ProfilerFrameSnapshot second = Frame(2, 2, 20, 50, 20);
            second.Add(MetricIds.Logic, 24);

            MetricSummary summary = Find(SessionStatistics.Calculate(
                new List<ProfilerFrameSnapshot> { first, second }, 0, 0), MetricIds.Logic);

            Assert.That(summary.averagePlayerLoopRatio, Is.EqualTo(28.0 / 30.0).Within(0.001));
            Assert.That(summary.extremePlayerLoopRatio, Is.EqualTo(1.2).Within(0.001));
            Assert.That(summary.hasRatioAnomaly, Is.True);
        }

        [Test]
        public void Compare_ReportsAbsoluteAndPercentageDifference()
        {
            ProfilerSessionFile baseline = SessionWithSummary(MetricIds.FrameTime, 10);
            ProfilerSessionFile candidate = SessionWithSummary(MetricIds.FrameTime, 12);

            ComparisonValue value = SessionComparison.Compare(baseline, candidate, false)
                .Find(item => item.MetricId == MetricIds.FrameTime);

            Assert.That(value.Valid, Is.True);
            Assert.That(value.AbsoluteDifference, Is.EqualTo(2));
            Assert.That(value.PercentageDifference, Is.EqualTo(20));
        }

        private static ProfilerFrameSnapshot Frame(int frameIndex, int relativeIndex, double frameTime, double fps, double? playerLoop)
        {
            ProfilerFrameSnapshot frame = new ProfilerFrameSnapshot { frameIndex = frameIndex, relativeIndex = relativeIndex };
            frame.Add(MetricIds.FrameTime, frameTime);
            frame.Add(MetricIds.FpsArithmetic, fps);
            if (playerLoop.HasValue)
                frame.Add(MetricIds.PlayerLoop, playerLoop.Value);
            else
                frame.AddInvalid(MetricIds.PlayerLoop);
            return frame;
        }

        private static MetricSummary Find(List<MetricSummary> summaries, string id)
        {
            return summaries.Find(summary => summary.metricId == id);
        }

        private static ProfilerSessionFile SessionWithSummary(string id, double average)
        {
            ProfilerSessionFile session = new ProfilerSessionFile();
            session.summaries.Add(new MetricSummary { metricId = id, sampleCount = 1, average = average, maximum = average, minimum = average });
            return session;
        }
    }

    internal sealed class CaptureSessionControllerTests
    {
        private string _oldDirectory;
        private string _testDirectory;

        [SetUp]
        public void SetUp()
        {
            _oldDirectory = CaptureSessionController.SaveDirectory;
            _testDirectory = Path.Combine(Path.GetTempPath(), "AvgProfilerDataToolTests");
            CaptureSessionController.SaveDirectory = _testDirectory;
            CaptureSessionController.FollowRecord = true;
        }

        [TearDown]
        public void TearDown()
        {
            CaptureSessionController.SetFrameSource(new UnityProfilerFrameSource());
            CaptureSessionController.SaveDirectory = _oldDirectory;
        }

        [Test]
        public void Tick_CapturesCompletedFramesAndDrainsTailOnStop()
        {
            FixtureProfilerFrameSource source = new FixtureProfilerFrameSource { FirstFrameIndex = 0, LastFrameIndex = 0 };
            source.AddFrame(1, 16, 62.5);
            source.AddFrame(2, 18, 55.5);
            CaptureSessionController.SetFrameSource(source);
            CaptureSessionController.Tick();

            source.IsRecording = true;
            source.LastFrameIndex = 2;
            CaptureSessionController.Tick();
            Assert.That(CaptureSessionController.CurrentSession.quality.capturedFrames, Is.EqualTo(1));

            source.IsRecording = false;
            CaptureSessionController.Tick();
            Assert.That(CaptureSessionController.LastSession.quality.capturedFrames, Is.EqualTo(2));
            Assert.That(CaptureSessionController.LastSession.quality.completeness, Is.EqualTo("Complete"));
        }

        [Test]
        public void Tick_MarksFramesLostWhenBufferOverwritesBacklog()
        {
            FixtureProfilerFrameSource source = new FixtureProfilerFrameSource { FirstFrameIndex = 0, LastFrameIndex = 0 };
            source.AddFrame(1, 16, 62.5);
            CaptureSessionController.SetFrameSource(source);
            CaptureSessionController.Tick();
            source.IsRecording = true;
            source.LastFrameIndex = 2;
            CaptureSessionController.Tick();
            source.FirstFrameIndex = 5;
            source.LastFrameIndex = 6;
            source.AddFrame(5, 16, 62.5);
            CaptureSessionController.Tick();

            Assert.That(CaptureSessionController.CurrentSession.quality.lostFrames, Is.EqualTo(3));
            Assert.That(CaptureSessionController.CurrentSession.quality.completeness, Is.EqualTo("Partial"));
        }
    }
}
#endif
