using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;

namespace AvgProfilerStreaming
{
    internal interface IProfilerFrameSource
    {
        bool IsRecording { get; }
        int FirstFrameIndex { get; }
        int LastFrameIndex { get; }
        string ConnectionName { get; }
        bool TryReadFrame(int frameIndex, int relativeIndex, out ProfilerFrameSnapshot snapshot);
        void ResetCaches();
    }

    internal sealed class UnityProfilerFrameSource : IProfilerFrameSource
    {
        private readonly FrameMetricExtractor _extractor = new FrameMetricExtractor();

        public bool IsRecording { get { return ProfilerDriver.enabled; } }
        public int FirstFrameIndex { get { return ProfilerDriver.firstFrameIndex; } }
        public int LastFrameIndex { get { return ProfilerDriver.lastFrameIndex; } }
        public string ConnectionName { get { return ReadConnectionName(); } }

        public bool TryReadFrame(int frameIndex, int relativeIndex, out ProfilerFrameSnapshot snapshot)
        {
            return _extractor.TryExtract(frameIndex, relativeIndex, out snapshot);
        }

        public void ResetCaches()
        {
            _extractor.ResetCaches();
        }

        private static string ReadConnectionName()
        {
            const BindingFlags Flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            try
            {
                PropertyInfo connectedProperty = typeof(ProfilerDriver).GetProperty("connectedProfiler", Flags);
                object connected = connectedProperty == null ? null : connectedProperty.GetValue(null, null);
                if (connected == null)
                    return "Profiler";

                MethodInfo identifierMethod = typeof(ProfilerDriver).GetMethod("GetConnectionIdentifier", Flags);
                if (identifierMethod != null)
                {
                    object identifier = identifierMethod.Invoke(null, new[] { connected });
                    if (identifier != null && !string.IsNullOrEmpty(identifier.ToString()))
                        return identifier.ToString();
                }
                return "Profiler " + connected;
            }
            catch (Exception)
            {
                return "Profiler";
            }
        }
    }

    internal sealed class FrameMetricExtractor
    {
        private const string MainThreadName = "Main Thread";
        private const string RenderThreadName = "Render Thread";

        private readonly List<int> _parents = new List<int>();
        private readonly List<int> _children = new List<int>();
        private readonly Dictionary<string, int> _counterMarkerIds = new Dictionary<string, int>();
        private int _cachedMainThreadIndex = -1;
        private int _cachedRenderThreadIndex = -1;

        public void ResetCaches()
        {
            _cachedMainThreadIndex = -1;
            _cachedRenderThreadIndex = -1;
            _counterMarkerIds.Clear();
        }

        public bool TryExtract(int frameIndex, int relativeIndex, out ProfilerFrameSnapshot snapshot)
        {
            snapshot = null;
            int mainThreadIndex = FindThread(frameIndex, MainThreadName, _cachedMainThreadIndex);
            if (mainThreadIndex < 0)
                return false;
            _cachedMainThreadIndex = mainThreadIndex;

            int renderThreadIndex = FindThread(frameIndex, RenderThreadName, _cachedRenderThreadIndex);
            _cachedRenderThreadIndex = renderThreadIndex;

            using (RawFrameDataView rawMain = ProfilerDriver.GetRawFrameDataView(frameIndex, mainThreadIndex))
            {
                if (!rawMain.valid || rawMain.frameTimeMs <= 0)
                    return false;

                snapshot = new ProfilerFrameSnapshot
                {
                    frameIndex = frameIndex,
                    relativeIndex = relativeIndex,
                    startTimeMs = rawMain.frameStartTimeMs
                };
                snapshot.Add(MetricIds.FrameTime, rawMain.frameTimeMs);
                AddCounter(rawMain, snapshot, MetricIds.Batches, "Batches Count");
                AddCounter(rawMain, snapshot, MetricIds.SetPass, "SetPass Calls Count");
                AddCounter(rawMain, snapshot, MetricIds.Triangles, "Triangles Count");
                AddCounter(rawMain, snapshot, MetricIds.Vertices, "Vertices Count");
                AddCounter(rawMain, snapshot, MetricIds.TotalUsedMemory, "Total Used Memory");
                AddCounter(rawMain, snapshot, MetricIds.TextureMemory, "Texture Memory");
                AddCounter(rawMain, snapshot, MetricIds.MeshMemory, "Mesh Memory");
                AddCounter(rawMain, snapshot, MetricIds.MaterialCount, "Material Count");
                AddCounter(rawMain, snapshot, MetricIds.ObjectCount, "Object Count");
            }

            ExtractMainHierarchy(frameIndex, mainThreadIndex, snapshot);
            ExtractRenderHierarchy(frameIndex, renderThreadIndex, snapshot);
            return true;
        }

        private void ExtractMainHierarchy(int frameIndex, int threadIndex, ProfilerFrameSnapshot snapshot)
        {
            double playerLoop = 0;
            double waitForTarget = 0;
            double waitForPresent = 0;
            double lastPresentationWait = 0;
            double pipelineScripts = 0;
            bool playerLoopFound = false;
            bool waitForTargetFound = false;
            bool waitForPresentFound = false;
            bool lastPresentationFound = false;
            bool pipelineFound = false;

            using (HierarchyFrameDataView view = ProfilerDriver.GetHierarchyFrameDataView(
                       frameIndex, threadIndex, HierarchyFrameDataView.ViewModes.Default,
                       HierarchyFrameDataView.columnTotalTime, false))
            {
                if (!view.valid)
                {
                    AddInvalidMainMetrics(snapshot);
                    return;
                }

                double fps = view.frameFps > 0 ? view.frameFps : 1000.0 / view.frameTimeMs;
                snapshot.Add(MetricIds.FpsArithmetic, fps);
                if (view.frameGpuTimeMs > 0)
                    snapshot.Add(MetricIds.GpuFrameTime, view.frameGpuTimeMs);
                else
                    snapshot.AddInvalid(MetricIds.GpuFrameTime);

                VisitItems(view, delegate(string name, double duration)
                {
                    if (name == "PlayerLoop")
                    {
                        playerLoop += duration;
                        playerLoopFound = true;
                    }
                    else if (name == "WaitForTargetFPS")
                    {
                        waitForTarget += duration;
                        waitForTargetFound = true;
                    }
                    else if (name == "Gfx.WaitForPresentOnGfxThread")
                    {
                        waitForPresent += duration;
                        waitForPresentFound = true;
                    }
                    else if (name == "TimeUpdate.WaitForLastPresentationAndUpdateTime")
                    {
                        lastPresentationWait += duration;
                        lastPresentationFound = true;
                    }
                    else if (name.Contains("RenderPipelineManager.DoRenderLoop_Internal()"))
                    {
                        pipelineScripts += duration;
                        pipelineFound = true;
                    }
                });
            }

            AddOptional(snapshot, MetricIds.PlayerLoop, playerLoopFound, playerLoop);
            AddOptional(snapshot, MetricIds.WaitForTargetFps, waitForTargetFound, waitForTarget);
            AddOptional(snapshot, MetricIds.WaitForPresent, waitForPresentFound, waitForPresent);
            AddOptional(snapshot, MetricIds.PipelineScripts, pipelineFound, pipelineScripts);

            if (!playerLoopFound)
            {
                snapshot.AddInvalid(MetricIds.VSyncWait);
                snapshot.AddInvalid(MetricIds.Scripts);
                snapshot.AddInvalid(MetricIds.Logic);
                snapshot.AddInvalid(MetricIds.Rendering);
                return;
            }

            double presentationWait = waitForTarget > 0 ? waitForTarget : (lastPresentationFound ? lastPresentationWait : 0);
            double scripts = playerLoop - waitForPresent - presentationWait;
            snapshot.Add(MetricIds.VSyncWait, waitForTarget);
            snapshot.Add(MetricIds.Scripts, scripts);
            snapshot.Add(MetricIds.Logic, scripts - pipelineScripts);
            snapshot.Add(MetricIds.Rendering, waitForPresent + pipelineScripts);
        }

        private void ExtractRenderHierarchy(int frameIndex, int threadIndex, ProfilerFrameSnapshot snapshot)
        {
            if (threadIndex < 0)
            {
                AddInvalidRenderMetrics(snapshot);
                return;
            }

            double present = 0;
            double wait = 0;
            double renderLogic = 0;
            bool presentFound = false;
            bool waitFound = false;
            bool renderLogicFound = false;
            using (HierarchyFrameDataView view = ProfilerDriver.GetHierarchyFrameDataView(
                       frameIndex, threadIndex, HierarchyFrameDataView.ViewModes.Default,
                       HierarchyFrameDataView.columnTotalTime, false))
            {
                if (!view.valid)
                {
                    AddInvalidRenderMetrics(snapshot);
                    return;
                }
                VisitItems(view, delegate(string name, double duration)
                {
                    if (name == "Gfx.PresentFrame")
                    {
                        present += duration;
                        presentFound = true;
                    }
                    else if (name == "Gfx.WaitForGfxCommandsFromMainThread")
                    {
                        wait += duration;
                        waitFound = true;
                    }
                    else if (name.StartsWith("UniversalRenderPipeline.RenderSingleCameraInternal", StringComparison.Ordinal))
                    {
                        renderLogic += duration;
                        renderLogicFound = true;
                    }
                });
            }

            AddOptional(snapshot, MetricIds.PresentFrame, presentFound, present);
            AddOptional(snapshot, MetricIds.WaitForGfxCommands, waitFound, wait);
            AddOptional(snapshot, MetricIds.RenderThreadLogic, renderLogicFound, renderLogic);
            AddOptional(snapshot, MetricIds.GpuCpuEstimate, presentFound && renderLogicFound, present + renderLogic);
        }

        private int FindThread(int frameIndex, string expectedName, int cachedIndex)
        {
            using (ProfilerFrameDataIterator iterator = new ProfilerFrameDataIterator())
            {
                int count = iterator.GetThreadCount(frameIndex);
                if (cachedIndex >= 0 && cachedIndex < count)
                {
                    iterator.SetRoot(frameIndex, cachedIndex);
                    if (iterator.GetThreadName() == expectedName)
                        return cachedIndex;
                }
                for (int i = 0; i < count; i++)
                {
                    iterator.SetRoot(frameIndex, i);
                    if (iterator.GetThreadName() == expectedName)
                        return i;
                }
            }
            return -1;
        }

        private void VisitItems(HierarchyFrameDataView view, Action<string, double> visitor)
        {
            int root = view.GetRootItemID();
            _parents.Clear();
            view.GetItemDescendantsThatHaveChildren(root, _parents);
            for (int parentIndex = 0; parentIndex < _parents.Count; parentIndex++)
            {
                _children.Clear();
                view.GetItemChildren(_parents[parentIndex], _children);
                for (int childIndex = 0; childIndex < _children.Count; childIndex++)
                {
                    int id = _children[childIndex];
                    visitor(view.GetItemName(id), view.GetItemColumnDataAsSingle(id, HierarchyFrameDataView.columnTotalTime));
                }
            }
        }

        private void AddCounter(RawFrameDataView view, ProfilerFrameSnapshot snapshot, string metricId, string markerName)
        {
            int markerId;
            if (!_counterMarkerIds.TryGetValue(markerName, out markerId))
            {
                markerId = view.GetMarkerId(markerName);
                if (markerId >= 0)
                    _counterMarkerIds.Add(markerName, markerId);
            }
            if (markerId < 0)
            {
                snapshot.AddInvalid(metricId);
                return;
            }
            try
            {
                snapshot.Add(metricId, view.GetCounterValueAsLong(markerId));
            }
            catch (InvalidOperationException)
            {
                snapshot.AddInvalid(metricId);
            }
            catch (ArgumentException)
            {
                snapshot.AddInvalid(metricId);
            }
        }

        private static void AddOptional(ProfilerFrameSnapshot snapshot, string metricId, bool valid, double value)
        {
            if (valid)
                snapshot.Add(metricId, value);
            else
                snapshot.AddInvalid(metricId);
        }

        private static void AddInvalidMainMetrics(ProfilerFrameSnapshot snapshot)
        {
            snapshot.AddInvalid(MetricIds.FpsArithmetic);
            snapshot.AddInvalid(MetricIds.GpuFrameTime);
            snapshot.AddInvalid(MetricIds.PlayerLoop);
            snapshot.AddInvalid(MetricIds.WaitForTargetFps);
            snapshot.AddInvalid(MetricIds.WaitForPresent);
            snapshot.AddInvalid(MetricIds.PipelineScripts);
            snapshot.AddInvalid(MetricIds.VSyncWait);
            snapshot.AddInvalid(MetricIds.Scripts);
            snapshot.AddInvalid(MetricIds.Logic);
            snapshot.AddInvalid(MetricIds.Rendering);
        }

        private static void AddInvalidRenderMetrics(ProfilerFrameSnapshot snapshot)
        {
            snapshot.AddInvalid(MetricIds.PresentFrame);
            snapshot.AddInvalid(MetricIds.WaitForGfxCommands);
            snapshot.AddInvalid(MetricIds.RenderThreadLogic);
            snapshot.AddInvalid(MetricIds.GpuCpuEstimate);
        }
    }
}
