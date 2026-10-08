using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace AvgProfilerStreaming
{
    internal static class AvgProfilerDataTool
    {
        [MenuItem("Tools/TA/AvgProfilerDataTool")]
        private static void Open()
        {
            AvgProfilerDataWindow.Open();
        }
    }

    internal sealed class LegacyAvgProfilerDataPanel
    {
        private int _uiInputBeginFrame;
        private int _uiInputEndFrame;

        private bool _cpuDataFoldout = true;
        private bool _memoryDataFoldout = true;
        private bool _renderingDataFoldout = true;

        private float _boxLengthZoom = 10;
        private readonly Color _vsyncWaitTimeColor = new Color(158 / 255f, 132 / 255f, 15 / 255f);
        private readonly Color _renderingWaitTimeColor = new Color(118 / 255f, 150 / 255f, 7 / 255f);
        private readonly Color _scriptsUseTimeColor = new Color(49 / 255f, 112 / 255f, 135 / 255f);

        private double _frameFps;
        private double _frameCpuTimeMs;
        private double _frameGpuTimeMs;
        private double _playerLoopTime;
        private double _waitForTargetFpsTime;
        private double _waitForPresentOnGfxThreadTime;
        private double _pipelineScriptsUseTime;

        private double _vsyncWaitTime;
        private double _scriptsUseTime;
        private double _renderingWaitTime;

        private double _gfxPresentFrameTime;
        private double _waitForGfxCommandsFromMainThreadTime;
        private double _renderThreadLogicTime;
        private double _gpuTotalTime;

        private double _totalUsedMemory;
        private double _textureMemory;
        private double _meshMemory;
        private double _materialCount;
        private double _objectCount;

        private double _batchesCount;
        private double _setPassCallsCount;
        private double _trianglesCount;
        private double _verticesCount;

        private readonly List<int> _parentsCache = new List<int>();
        private readonly List<int> _childrenCache = new List<int>();

        public void Draw()
        {
            EditorGUILayout.LabelField("Profiler平均CPU,GPU参数Debug工具");
            EditorGUILayout.LabelField("----------------------------------------------");

            GUIStyle boldStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            _uiInputBeginFrame = EditorGUILayout.IntField("起始数据帧", _uiInputBeginFrame);
            _uiInputEndFrame = EditorGUILayout.IntField("结束数据帧", _uiInputEndFrame);

            if (GUILayout.Button("计算平均参数"))
                CalculateSelectedFrames();

            EditorGUILayout.Space();
            _cpuDataFoldout = EditorGUILayout.Foldout(_cpuDataFoldout, "CPU相关--------------------------------------");
            if (_cpuDataFoldout)
                DrawCpuData(boldStyle);

            EditorGUILayout.Space();
            _renderingDataFoldout = EditorGUILayout.Foldout(_renderingDataFoldout, "渲染相关--------------------------------------");
            if (_renderingDataFoldout)
            {
                EditorGUILayout.LabelField($"Batches Count：{_batchesCount:N0}");
                EditorGUILayout.LabelField($"SetPass Calls Count：{_setPassCallsCount:N0}");
                EditorGUILayout.LabelField($"Triangles Count：{_trianglesCount / 1000f:F2}K");
                EditorGUILayout.LabelField($"Vertices Count：{_verticesCount / 1000f:F2}K");
            }

            EditorGUILayout.Space();
            _memoryDataFoldout = EditorGUILayout.Foldout(_memoryDataFoldout, "内存相关--------------------------------------");
            if (_memoryDataFoldout)
            {
                EditorGUILayout.LabelField($"Total Used Memory：{_totalUsedMemory / 1024f / 1024f / 1024f:F2}G");
                EditorGUILayout.LabelField($"Texture Memory：{_textureMemory / 1024f / 1024f:F2}M");
                EditorGUILayout.LabelField($"Mesh Memory：{_meshMemory / 1024f / 1024f:F2}M");
                EditorGUILayout.LabelField($"Material Count：{_materialCount:N0}");
                EditorGUILayout.LabelField($"Object Count：{_objectCount:N0}");
            }
        }

        private void CalculateSelectedFrames()
        {
            int realBeginFrame = _uiInputBeginFrame - 1;
            int realEndFrame = _uiInputEndFrame - 1;
            int firstFrameIndex = ProfilerDriver.firstFrameIndex;
            int lastFrameIndex = ProfilerDriver.lastFrameIndex;

            if (realBeginFrame < firstFrameIndex)
            {
                Debug.LogError($"指定起始数据位置小于数据起点，最小值为：{firstFrameIndex + 1}");
            }
            else if (realEndFrame > lastFrameIndex)
            {
                Debug.LogError($"指定结束数据位置大于数据末尾，最大值为：{lastFrameIndex + 1}");
            }
            else if (realBeginFrame > realEndFrame)
            {
                Debug.LogError("起始帧必须小于等于结束帧");
            }
            else
            {
                CalculateAverageFrameDataCpu(realBeginFrame, realEndFrame);
                CalculateAverageFrameDataGpu(realBeginFrame, realEndFrame);
                CalculateAverageFrameDataMemory(realBeginFrame, realEndFrame);
                CalculateAverageFrameDataRendering(realBeginFrame, realEndFrame);
            }
        }

        private void DrawCpuData(GUIStyle boldStyle)
        {
            EditorGUILayout.LabelField("Main Thread-----------", boldStyle);
            EditorGUILayout.LabelField($"Frame FPS：{_frameFps:F2}");
            EditorGUILayout.LabelField($"Frame CPU Time：{_frameCpuTimeMs:F2}ms");
            EditorGUILayout.LabelField($"Frame GPU Time：{_frameGpuTimeMs:F2}ms");
            EditorGUILayout.LabelField($"Player Loop Time：{_playerLoopTime:F2}ms");
            EditorGUILayout.LabelField($"WaitForTargetFpsTime：{_waitForTargetFpsTime:F2}ms");
            EditorGUILayout.LabelField($"Gfx.WaitForPresentOnGfxThread Time：{_waitForPresentOnGfxThreadTime:F2}ms");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Render Thread-----------", boldStyle);
            EditorGUILayout.LabelField($"Gfx.PresentFrame Time：{_gfxPresentFrameTime:F2}ms");
            EditorGUILayout.LabelField($"Gfx.WaitForGfxCommandsFromMainThread Time：{_waitForGfxCommandsFromMainThreadTime:F2}ms");
            EditorGUILayout.LabelField($"渲染线程逻辑耗时：{_renderThreadLogicTime:F2}ms");
            EditorGUILayout.LabelField($"GPU总耗时：{_gpuTotalTime:F2}ms");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("渲染/逻辑占比情况-----------", boldStyle);
            _boxLengthZoom = Mathf.Clamp(EditorGUILayout.FloatField("显示缩放", _boxLengthZoom), 1, 30);
            ShowUiBox($"VSync Wait Time:({_vsyncWaitTime:F2}ms)", _vsyncWaitTime, _vsyncWaitTimeColor);
            ShowUiBox($"Rendering Wait Time:({_renderingWaitTime:F2}ms)", _renderingWaitTime, _renderingWaitTimeColor);
            ShowUiBox($"Total Scripts Use Time:({_scriptsUseTime:F2}ms)", _scriptsUseTime, _scriptsUseTimeColor);
            ShowUiBox($"Pipeline Scripts Use Time:({_pipelineScriptsUseTime:F2}ms)", _pipelineScriptsUseTime, _scriptsUseTimeColor);
            EditorGUILayout.LabelField("注意：Total Scripts包含了Pipeline Scripts耗时");

            double logicTime = _scriptsUseTime - _pipelineScriptsUseTime;
            double renderingTime = _renderingWaitTime + _pipelineScriptsUseTime;
            ShowUiDoubleBox("Logic/Rendering:", logicTime, renderingTime,
                _scriptsUseTimeColor, _renderingWaitTimeColor);
        }

        private void ShowUiBox(string title, double value, Color color)
        {
            float percent = _playerLoopTime == 0 ? 0 : (float)(value / _playerLoopTime);
            float length = Mathf.Max(0, (float)(value * _boxLengthZoom));
            Rect boxRect = EditorGUILayout.GetControlRect(GUILayout.Width(length), GUILayout.Height(20));
            Rect backgroundRect = new Rect(boxRect.x, boxRect.y, 1000, boxRect.height);
            Rect labelRect = new Rect(boxRect.x, boxRect.y, 400, boxRect.height);

            EditorGUILayout.Space(0.05f);
            EditorGUI.DrawRect(backgroundRect, new Color(0.16f, 0.16f, 0.16f));
            EditorGUI.DrawRect(boxRect, color);
            GUI.Label(labelRect, $"{title}({percent * 100:F2}%)");
        }

        private void ShowUiDoubleBox(string title, double value1, double value2, Color color1, Color color2)
        {
            float percent1 = _playerLoopTime == 0 ? 0 : (float)(value1 / _playerLoopTime);
            float percent2 = _playerLoopTime == 0 ? 0 : (float)(value2 / _playerLoopTime);
            float length1 = Mathf.Max(0, (float)(value1 * _boxLengthZoom));
            float length2 = Mathf.Max(0, (float)(value2 * _boxLengthZoom));
            Rect boxRect1 = EditorGUILayout.GetControlRect(GUILayout.Width(length1), GUILayout.Height(20));
            Rect boxRect2 = new Rect(boxRect1.x + boxRect1.width, boxRect1.y, length2, boxRect1.height);
            Rect backgroundRect = new Rect(boxRect1.x, boxRect1.y, 1000, boxRect1.height);
            Rect labelRect = new Rect(boxRect1.x, boxRect1.y, 400, boxRect1.height);

            EditorGUILayout.Space(0.05f);
            EditorGUI.DrawRect(backgroundRect, new Color(0.16f, 0.16f, 0.16f));
            EditorGUI.DrawRect(boxRect1, color1);
            EditorGUI.DrawRect(boxRect2, color2);
            GUI.Label(labelRect,
                $"{title}({value1:F1}ms{percent1 * 100:F1}%/{value2:F1}ms{percent2 * 100:F1}%)");
        }

        private void CalculateAverageFrameDataCpu(int beginFrame, int endFrame)
        {
            _frameFps = 0;
            _frameCpuTimeMs = 0;
            _frameGpuTimeMs = 0;
            _playerLoopTime = 0;
            _waitForTargetFpsTime = 0;
            _waitForPresentOnGfxThreadTime = 0;
            _pipelineScriptsUseTime = 0;
            _vsyncWaitTime = 0;
            _scriptsUseTime = 0;
            _renderingWaitTime = 0;

            int frameCount = endFrame - beginFrame + 1;
            for (int i = 0; i < frameCount; i++)
            {
                double playerLoopTime = 0;
                double waitForTargetFpsTime = 0;
                double waitForPresentTime = 0;
                double waitForLastPresentationTime = 0;
                using (HierarchyFrameDataView frameData = ProfilerDriver.GetHierarchyFrameDataView(
                           beginFrame + i, 0, HierarchyFrameDataView.ViewModes.Default,
                           HierarchyFrameDataView.columnGcMemory, false))
                {
                    _frameFps += frameData.frameFps;
                    _frameCpuTimeMs += frameData.frameTimeMs;
                    _frameGpuTimeMs += frameData.frameGpuTimeMs;
                    int rootId = frameData.GetRootItemID();
                    frameData.GetItemDescendantsThatHaveChildren(rootId, _parentsCache);
                    foreach (int parentId in _parentsCache)
                    {
                        frameData.GetItemChildren(parentId, _childrenCache);
                        foreach (int id in _childrenCache)
                        {
                            string markerName = frameData.GetItemName(id);
                            if (markerName == "PlayerLoop")
                            {
                                playerLoopTime = frameData.GetItemColumnDataAsSingle(id,
                                    HierarchyFrameDataView.columnTotalTime);
                                _playerLoopTime += playerLoopTime;
                            }
                            else if (markerName == "WaitForTargetFPS")
                            {
                                waitForTargetFpsTime = frameData.GetItemColumnDataAsSingle(id,
                                    HierarchyFrameDataView.columnTotalTime);
                                _waitForTargetFpsTime += waitForTargetFpsTime;
                            }
                            else if (markerName == "Gfx.WaitForPresentOnGfxThread")
                            {
                                waitForPresentTime = frameData.GetItemColumnDataAsSingle(id,
                                    HierarchyFrameDataView.columnTotalTime);
                                _waitForPresentOnGfxThreadTime += waitForPresentTime;
                            }
                            else if (markerName == "TimeUpdate.WaitForLastPresentationAndUpdateTime")
                            {
                                waitForLastPresentationTime = frameData.GetItemColumnDataAsSingle(id,
                                    HierarchyFrameDataView.columnTotalTime);
                            }
                            else if (markerName.Contains("RenderPipelineManager.DoRenderLoop_Internal()"))
                            {
                                _pipelineScriptsUseTime += frameData.GetItemColumnDataAsSingle(id,
                                    HierarchyFrameDataView.columnTotalTime);
                            }
                        }
                    }
                }

                _vsyncWaitTime += waitForTargetFpsTime;
                _renderingWaitTime += waitForPresentTime;
                _scriptsUseTime += playerLoopTime - waitForPresentTime;
                _scriptsUseTime -= waitForTargetFpsTime > 0
                    ? waitForTargetFpsTime
                    : waitForLastPresentationTime;
            }

            _frameFps /= frameCount;
            _frameCpuTimeMs /= frameCount;
            _frameGpuTimeMs /= frameCount;
            _playerLoopTime /= frameCount;
            _waitForTargetFpsTime /= frameCount;
            _waitForPresentOnGfxThreadTime /= frameCount;
            _pipelineScriptsUseTime /= frameCount;
            _vsyncWaitTime /= frameCount;
            _scriptsUseTime /= frameCount;
            _renderingWaitTime /= frameCount;
        }

        private void CalculateAverageFrameDataGpu(int beginFrame, int endFrame)
        {
            _gfxPresentFrameTime = 0;
            _waitForGfxCommandsFromMainThreadTime = 0;
            _renderThreadLogicTime = 0;
            int frameCount = endFrame - beginFrame + 1;
            for (int i = 0; i < frameCount; i++)
            {
                using (HierarchyFrameDataView frameData = ProfilerDriver.GetHierarchyFrameDataView(
                           beginFrame + i, 1, HierarchyFrameDataView.ViewModes.Default,
                           HierarchyFrameDataView.columnGcMemory, false))
                {
                    int rootId = frameData.GetRootItemID();
                    frameData.GetItemDescendantsThatHaveChildren(rootId, _parentsCache);
                    foreach (int parentId in _parentsCache)
                    {
                        frameData.GetItemChildren(parentId, _childrenCache);
                        foreach (int id in _childrenCache)
                        {
                            string markerName = frameData.GetItemName(id);
                            if (markerName == "Gfx.PresentFrame")
                                _gfxPresentFrameTime += frameData.GetItemColumnDataAsSingle(id,
                                    HierarchyFrameDataView.columnTotalTime);
                            else if (markerName == "Gfx.WaitForGfxCommandsFromMainThread")
                                _waitForGfxCommandsFromMainThreadTime += frameData.GetItemColumnDataAsSingle(id,
                                    HierarchyFrameDataView.columnTotalTime);
                            else if (markerName.StartsWith("UniversalRenderPipeline.RenderSingleCameraInternal"))
                                _renderThreadLogicTime += frameData.GetItemColumnDataAsSingle(id,
                                    HierarchyFrameDataView.columnTotalTime);
                        }
                    }
                }
            }

            _gfxPresentFrameTime /= frameCount;
            _waitForGfxCommandsFromMainThreadTime /= frameCount;
            _renderThreadLogicTime /= frameCount;
            _gpuTotalTime = _renderThreadLogicTime + _gfxPresentFrameTime;
        }

        private void CalculateAverageFrameDataRendering(int beginFrame, int endFrame)
        {
            _batchesCount = 0;
            _setPassCallsCount = 0;
            _trianglesCount = 0;
            _verticesCount = 0;
            int frameCount = endFrame - beginFrame + 1;
            for (int i = 0; i < frameCount; i++)
            {
                using (RawFrameDataView frameData = ProfilerDriver.GetRawFrameDataView(beginFrame + i, 0))
                {
                    _batchesCount += frameData.GetCounterValueAsLong(frameData.GetMarkerId("Batches Count"));
                    _setPassCallsCount += frameData.GetCounterValueAsLong(frameData.GetMarkerId("SetPass Calls Count"));
                    _trianglesCount += frameData.GetCounterValueAsLong(frameData.GetMarkerId("Triangles Count"));
                    _verticesCount += frameData.GetCounterValueAsLong(frameData.GetMarkerId("Vertices Count"));
                }
            }

            _batchesCount /= frameCount;
            _setPassCallsCount /= frameCount;
            _trianglesCount /= frameCount;
            _verticesCount /= frameCount;
        }

        private void CalculateAverageFrameDataMemory(int beginFrame, int endFrame)
        {
            _totalUsedMemory = 0;
            _textureMemory = 0;
            _meshMemory = 0;
            _materialCount = 0;
            _objectCount = 0;
            int frameCount = endFrame - beginFrame + 1;
            for (int i = 0; i < frameCount; i++)
            {
                using (RawFrameDataView frameData = ProfilerDriver.GetRawFrameDataView(beginFrame + i, 0))
                {
                    _totalUsedMemory += frameData.GetCounterValueAsLong(frameData.GetMarkerId("Total Used Memory"));
                    _textureMemory += frameData.GetCounterValueAsLong(frameData.GetMarkerId("Texture Memory"));
                    _meshMemory += frameData.GetCounterValueAsLong(frameData.GetMarkerId("Mesh Memory"));
                    _materialCount += frameData.GetCounterValueAsLong(frameData.GetMarkerId("Material Count"));
                    _objectCount += frameData.GetCounterValueAsLong(frameData.GetMarkerId("Object Count"));
                }
            }

            // Preserve the original tool's divisor so legacy results remain directly comparable.
            frameCount += 1;
            _totalUsedMemory /= frameCount;
            _textureMemory /= frameCount;
            _meshMemory /= frameCount;
            _materialCount /= frameCount;
            _objectCount /= frameCount;
        }
    }
}
