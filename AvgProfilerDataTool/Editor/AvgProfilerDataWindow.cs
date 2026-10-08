using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AvgProfilerStreaming
{
    internal sealed class AvgProfilerDataWindow : EditorWindow
    {
        private static readonly string[] ToolTabs = { "Profiler数据录制", "Profiler数据导入" };
        private static readonly string[] Tabs = { "采集", "结果", "对比" };
        private readonly List<string> _comparisonPaths = new List<string>();
        private readonly List<ProfilerSessionFile> _comparisonSessions = new List<ProfilerSessionFile>();
        private readonly LegacyAvgProfilerDataPanel _legacyPanel = new LegacyAvgProfilerDataPanel();
        private readonly List<ProfilerFrameSnapshot> _resultAnalysisFrames = new List<ProfilerFrameSnapshot>();
        private Vector2 _scroll;
        private Vector2 _legacyScroll;
        private int _toolTab;
        private int _tab;
        private int _baselineIndex;
        private bool _comparePeak;
        private string _groupFilter = "全部";
        private double _nextRepaintTime;
        private ProfilerSessionFile _resultRangeSource;
        private ProfilerSessionFile _rangedResult;
        private int _resultFirstFrame;
        private int _resultLastFrame;
        private int _defaultResultFirstFrame;
        private int _defaultResultLastFrame;
        private int _cachedResultFirstFrame;
        private int _cachedResultLastFrame;

        public static void Open()
        {
            AvgProfilerDataWindow window = GetWindow<AvgProfilerDataWindow>();
            window.titleContent = new GUIContent("Avg Profiler Data");
            window.minSize = new Vector2(720, 420);
            window.Show();
        }

        private void Update()
        {
            if (EditorApplication.timeSinceStartup < _nextRepaintTime)
                return;
            _nextRepaintTime = EditorApplication.timeSinceStartup + 0.25;
            Repaint();
        }

        private void OnGUI()
        {
            _toolTab = GUILayout.Toolbar(_toolTab, ToolTabs);
            EditorGUILayout.Space(6);
            if (_toolTab == 1)
            {
                _legacyScroll = EditorGUILayout.BeginScrollView(_legacyScroll);
                _legacyPanel.Draw();
                EditorGUILayout.EndScrollView();
                return;
            }

            _tab = GUILayout.Toolbar(_tab, Tabs);
            EditorGUILayout.Space(6);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            if (_tab == 0)
                DrawCapture();
            else if (_tab == 1)
                DrawResults();
            else
                DrawComparison();
            EditorGUILayout.EndScrollView();
        }

        private static void DrawCapture()
        {
            EditorGUILayout.LabelField("连接与状态", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Profiler 目标", CaptureSessionController.ConnectionName);
            EditorGUILayout.LabelField("Profiler Record", CaptureSessionController.State == CaptureState.Capturing ? "Recording" : "Idle");
            EditorGUILayout.LabelField("工具状态", CaptureSessionController.State.ToString());

            bool follow = EditorGUILayout.Toggle("跟随 Profiler Record", CaptureSessionController.FollowRecord);
            if (follow != CaptureSessionController.FollowRecord)
                CaptureSessionController.FollowRecord = follow;

            EditorGUI.BeginDisabledGroup(CaptureSessionController.State == CaptureState.Capturing);
            string sessionName = EditorGUILayout.TextField("会话名", CaptureSessionController.SessionName);
            if (sessionName != CaptureSessionController.SessionName)
                CaptureSessionController.SessionName = sessionName;

            EditorGUILayout.BeginHorizontal();
            string directory = EditorGUILayout.TextField("保存目录", CaptureSessionController.SaveDirectory);
            if (directory != CaptureSessionController.SaveDirectory)
                CaptureSessionController.SaveDirectory = directory;
            if (GUILayout.Button("选择", GUILayout.Width(60)))
            {
                string selected = EditorUtility.OpenFolderPanel("选择 Profiler 会话目录", CaptureSessionController.SaveDirectory, string.Empty);
                if (!string.IsNullOrEmpty(selected))
                    CaptureSessionController.SaveDirectory = selected;
            }
            EditorGUILayout.EndHorizontal();

            int ignoreHead = EditorGUILayout.IntField("忽略开头有效帧", CaptureSessionController.IgnoreHeadFrames);
            int ignoreTail = EditorGUILayout.IntField("忽略结尾有效帧", CaptureSessionController.IgnoreTailFrames);
            CaptureSessionController.IgnoreHeadFrames = ignoreHead;
            CaptureSessionController.IgnoreTailFrames = ignoreTail;
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(8);
            DrawCaptureQuality(CaptureSessionController.CurrentSession);
            int backlog = CaptureSessionController.BacklogFrames;
            EditorGUILayout.LabelField("积压帧", backlog.ToString());
            if (backlog >= 128)
                EditorGUILayout.HelpBox("采集积压较高，继续增长可能导致 Profiler 缓冲覆盖和丢帧。", MessageType.Error);
            else if (backlog > 0)
                EditorGUILayout.HelpBox("正在消费积压帧。", MessageType.Warning);

            if (!string.IsNullOrEmpty(CaptureSessionController.LastSavedPath))
                EditorGUILayout.LabelField("最近保存", CaptureSessionController.LastSavedPath);
            if (!string.IsNullOrEmpty(CaptureSessionController.LastError))
                EditorGUILayout.HelpBox("保存失败，结果仍保留在内存中：" + CaptureSessionController.LastError, MessageType.Error);
        }

        private static void DrawCaptureQuality(ProfilerSessionFile session)
        {
            if (session == null)
            {
                EditorGUILayout.LabelField("采集帧", "0");
                EditorGUILayout.LabelField("丢失帧", "0");
                return;
            }
            EditorGUILayout.LabelField("采集帧", session.quality.capturedFrames.ToString());
            EditorGUILayout.LabelField("有效 / 无效", session.quality.validFrames + " / " + session.quality.invalidFrames);
            EditorGUILayout.LabelField("丢失帧", session.quality.lostFrames.ToString());
        }

        private void DrawResults()
        {
            ProfilerSessionFile session = _loadedResult ?? CaptureSessionController.LastSession;
            EnsureResultFrameRange(session);
            ProfilerSessionFile displayedSession = GetDisplayedResult(session);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("加载会话", GUILayout.Width(90)))
            {
                string path = EditorUtility.OpenFilePanel("加载 Profiler 会话", CaptureSessionController.SaveDirectory, "json");
                if (!string.IsNullOrEmpty(path))
                    TryLoadResult(path);
            }
            EditorGUI.BeginDisabledGroup(session == null);
            if (GUILayout.Button("另存为", GUILayout.Width(90)))
                SaveResultAs(session);
            if (GUILayout.Button("导出表格", GUILayout.Width(90)))
                ExportResultXlsx(displayedSession);
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            if (session == null)
            {
                EditorGUILayout.HelpBox("尚无已完成会话。启动 Unity Profiler Record 后，工具会自动采集。", MessageType.Info);
                return;
            }

            DrawSessionHeader(session);
            DrawResultFrameRange();
            displayedSession = GetDisplayedResult(session);
            DrawMetricSection(displayedSession, "性能概览",
                "总时长平均 FPS 是跨会话比较主口径；逐帧 FPS 算术平均用于与旧工具对照。",
                MetricIds.OverviewMetrics);

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("CPU 线程", EditorStyles.boldLabel);
            DrawNote("以下为线程上的原始 Marker。Pipeline Scripts 在 Main Thread 执行渲染组织与命令提交，不代表 Render Thread 耗时。");
            DrawMetricSubsection(displayedSession, "Main Thread",
                MetricIds.MainThreadMetrics);
            DrawMetricSubsection(displayedSession, "Render Thread",
                MetricIds.RenderThreadMetrics);

            DrawMetricSection(displayedSession, "Main Thread PlayerLoop 归因（估算）",
                "Scripts = PlayerLoop - 渲染等待 - 帧节奏等待；Logic = Scripts - Pipeline Scripts；Rendering = 渲染等待 + Pipeline Scripts。该分区不代表 Render Thread 或 GPU 耗时。",
                MetricIds.PlayerLoopBreakdownMetrics);

            DrawMetricSection(displayedSession, "GPU",
                "GPU Frame Time 是设备提供的真实 GPU 时间；GPU总耗时（CPU Marker估算）仅为 CPU Marker 估算，两者不可互相替代。",
                MetricIds.GpuMetrics);

            DrawMetricSection(displayedSession, "Rendering Counters", null,
                MetricIds.RenderingMetrics);
            DrawMetricSection(displayedSession, "Memory", null,
                MetricIds.MemoryMetrics);
        }

        private ProfilerSessionFile _loadedResult;

        private void EnsureResultFrameRange(ProfilerSessionFile session)
        {
            if (ReferenceEquals(_resultRangeSource, session))
                return;

            _resultRangeSource = session;
            _rangedResult = null;
            _resultAnalysisFrames.Clear();
            if (session == null)
                return;

            _resultAnalysisFrames.AddRange(SessionStatistics.GetAnalysisFrames(
                session.samples,
                session.metadata.ignoreHeadFrames,
                session.metadata.ignoreTailFrames));
            if (_resultAnalysisFrames.Count == 0)
                return;

            _defaultResultFirstFrame = _resultAnalysisFrames[0].frameIndex + 1;
            _defaultResultLastFrame = _resultAnalysisFrames[_resultAnalysisFrames.Count - 1].frameIndex + 1;
            _resultFirstFrame = _defaultResultFirstFrame;
            _resultLastFrame = _defaultResultLastFrame;
            _cachedResultFirstFrame = int.MinValue;
            _cachedResultLastFrame = int.MinValue;
        }

        private void DrawResultFrameRange()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("分析数据帧", EditorStyles.boldLabel);
            if (_resultAnalysisFrames.Count == 0)
            {
                EditorGUILayout.HelpBox("当前会话没有可分析的有效帧。", MessageType.Info);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            _resultFirstFrame = EditorGUILayout.IntField("起始数据帧", _resultFirstFrame);
            _resultLastFrame = EditorGUILayout.IntField("结束数据帧", _resultLastFrame);
            if (GUILayout.Button("覆盖全部", GUILayout.Width(80)))
            {
                _resultFirstFrame = _defaultResultFirstFrame;
                _resultLastFrame = _defaultResultLastFrame;
            }
            EditorGUILayout.EndHorizontal();

            _resultFirstFrame = Mathf.Clamp(_resultFirstFrame, _defaultResultFirstFrame, _defaultResultLastFrame);
            _resultLastFrame = Mathf.Clamp(_resultLastFrame, _defaultResultFirstFrame, _defaultResultLastFrame);
            if (_resultFirstFrame > _resultLastFrame)
                _resultLastFrame = _resultFirstFrame;

            int selectedCount = CountSelectedResultFrames();
            EditorGUILayout.LabelField(
                "当前范围",
                _resultFirstFrame + " - " + _resultLastFrame + "（有效样本帧 " + selectedCount + "）");
        }

        private ProfilerSessionFile GetDisplayedResult(ProfilerSessionFile source)
        {
            if (source == null || _resultAnalysisFrames.Count == 0)
                return source;
            if (_resultFirstFrame == _defaultResultFirstFrame && _resultLastFrame == _defaultResultLastFrame)
                return source;
            if (_rangedResult != null &&
                _cachedResultFirstFrame == _resultFirstFrame &&
                _cachedResultLastFrame == _resultLastFrame)
                return _rangedResult;

            List<ProfilerFrameSnapshot> selectedFrames = SessionStatistics.SelectProfilerFrameRange(
                _resultAnalysisFrames,
                _resultFirstFrame,
                _resultLastFrame);

            _rangedResult = CreateRangedResult(source, selectedFrames);
            _cachedResultFirstFrame = _resultFirstFrame;
            _cachedResultLastFrame = _resultLastFrame;
            return _rangedResult;
        }

        private int CountSelectedResultFrames()
        {
            int count = 0;
            for (int i = 0; i < _resultAnalysisFrames.Count; i++)
            {
                int profilerUiFrame = _resultAnalysisFrames[i].frameIndex + 1;
                if (profilerUiFrame >= _resultFirstFrame && profilerUiFrame <= _resultLastFrame)
                    count++;
            }
            return count;
        }

        private ProfilerSessionFile CreateRangedResult(
            ProfilerSessionFile source,
            List<ProfilerFrameSnapshot> selectedFrames)
        {
            ProfilerSessionFile result = new ProfilerSessionFile();
            result.metadata.sessionName = source.metadata.sessionName;
            result.metadata.createdAt = source.metadata.createdAt;
            result.metadata.unityVersion = source.metadata.unityVersion;
            result.metadata.platform = source.metadata.platform;
            result.metadata.targetName = source.metadata.targetName;
            result.metadata.connectionName = source.metadata.connectionName;
            result.metadata.firstFrame = _resultFirstFrame - 1;
            result.metadata.lastFrame = _resultLastFrame - 1;
            result.metadata.ignoreHeadFrames = 0;
            result.metadata.ignoreTailFrames = 0;
            result.quality.completeness = source.quality.completeness;
            result.quality.expectedFrames = selectedFrames.Count;
            result.quality.capturedFrames = selectedFrames.Count;
            result.quality.validFrames = selectedFrames.Count;
            result.quality.invalidFrames = 0;
            result.quality.lostFrames = 0;
            result.quality.reason = source.quality.reason;
            result.samples = selectedFrames;
            result.summaries = SessionStatistics.Calculate(selectedFrames, 0, 0);
            return result;
        }

        private static void DrawSessionHeader(ProfilerSessionFile session)
        {
            SessionQuality quality = session.quality;
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField(session.metadata.sessionName, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("创建时间", session.metadata.createdAt);
            EditorGUILayout.LabelField("目标", session.metadata.connectionName);
            EditorGUILayout.LabelField("完整度", quality.completeness);
            EditorGUILayout.LabelField("期望 / 采集 / 有效 / 无效 / 丢失",
                quality.expectedFrames + " / " + quality.capturedFrames + " / " + quality.validFrames + " / " + quality.invalidFrames + " / " + quality.lostFrames);
            if (!string.IsNullOrEmpty(quality.reason))
                EditorGUILayout.HelpBox(quality.reason, MessageType.Warning);
        }

        private static void DrawMetricHeader()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("指标", EditorStyles.miniBoldLabel, GUILayout.Width(260));
            GUILayout.Label("平均", EditorStyles.miniBoldLabel, GUILayout.Width(110));
            GUILayout.Label("极值", EditorStyles.miniBoldLabel, GUILayout.Width(110));
            GUILayout.Label("Profiler 帧 / 相对帧", EditorStyles.miniBoldLabel, GUILayout.Width(150));
            GUILayout.Label("样本率", EditorStyles.miniBoldLabel, GUILayout.Width(80));
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawMetricSection(ProfilerSessionFile session, string title, string note, params string[] metricIds)
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            DrawNote(note);
            DrawMetricHeader();
            for (int i = 0; i < metricIds.Length; i++)
                DrawMetricRow(session, MetricIds.Find(metricIds[i]));
        }

        private static void DrawMetricSubsection(ProfilerSessionFile session, string title, params string[] metricIds)
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);
            DrawMetricHeader();
            for (int i = 0; i < metricIds.Length; i++)
                DrawMetricRow(session, MetricIds.Find(metricIds[i]));
        }

        private static void DrawNote(string note)
        {
            if (!string.IsNullOrEmpty(note))
                EditorGUILayout.LabelField(note, EditorStyles.wordWrappedMiniLabel);
        }

        private static void DrawMetricRow(ProfilerSessionFile session, MetricDefinition definition)
        {
            MetricSummary summary = SessionComparison.Find(session, definition.Id);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent(definition.DisplayName, MetricIds.GetDescription(definition.Id)), GUILayout.Width(260));
            if (summary == null || summary.sampleCount == 0)
            {
                GUILayout.Label("N/A", GUILayout.Width(110));
                GUILayout.Label("N/A", GUILayout.Width(110));
                GUILayout.Label("-", GUILayout.Width(150));
                GUILayout.Label("0%", GUILayout.Width(80));
            }
            else
            {
                double extreme = definition.HigherIsBetter ? summary.minimum : summary.maximum;
                int frame = definition.HigherIsBetter ? summary.minimumFrame : summary.maximumFrame;
                int relative = definition.HigherIsBetter ? summary.minimumRelativeFrame : summary.maximumRelativeFrame;
                GUILayout.Label(FormatValue(summary.average, definition.Unit), GUILayout.Width(110));
                GUILayout.Label(FormatValue(extreme, definition.Unit), GUILayout.Width(110));
                GUILayout.Label(frame + " / " + relative, GUILayout.Width(150));
                int denominator = Math.Max(1, session.quality.validFrames - session.metadata.ignoreHeadFrames - session.metadata.ignoreTailFrames);
                GUILayout.Label((100.0 * summary.sampleCount / denominator).ToString("F1") + "%", GUILayout.Width(80));
            }
            EditorGUILayout.EndHorizontal();
            if (summary != null && summary.averageRatioValid)
                EditorGUILayout.LabelField("平均占比", (summary.averagePlayerLoopRatio * 100).ToString("F1") + "%");
            if (summary != null && summary.hasAverageRatioAnomaly)
                EditorGUILayout.HelpBox(definition.DisplayName + " 平均占比口径异常（未截断）。", MessageType.Warning);
            if (summary != null && summary.hasRatioAnomaly)
                EditorGUILayout.HelpBox(definition.DisplayName + " 峰值相对 PlayerLoop 为 " + (summary.extremePlayerLoopRatio * 100).ToString("F1") + "%（口径异常，未截断）", MessageType.Warning);
        }


        private void DrawComparison()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("添加会话", GUILayout.Width(90)))
            {
                string path = EditorUtility.OpenFilePanel("添加 Profiler 会话", CaptureSessionController.SaveDirectory, "json");
                if (!string.IsNullOrEmpty(path))
                    TryAddComparison(path);
            }
            EditorGUI.BeginDisabledGroup(_comparisonSessions.Count == 0);
            if (GUILayout.Button("清空", GUILayout.Width(70)))
            {
                _comparisonPaths.Clear();
                _comparisonSessions.Clear();
                _baselineIndex = 0;
            }
            if (GUILayout.Button("导出 CSV", GUILayout.Width(90)))
                ExportComparisonCsv();
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            if (_comparisonSessions.Count == 0)
            {
                EditorGUILayout.HelpBox("添加至少两个 .avgprofiler.json 会话以进行横向对比。", MessageType.Info);
                return;
            }

            string[] names = new string[_comparisonSessions.Count];
            for (int i = 0; i < names.Length; i++)
                names[i] = _comparisonSessions[i].metadata.sessionName;
            _baselineIndex = EditorGUILayout.Popup("基准会话", Mathf.Clamp(_baselineIndex, 0, names.Length - 1), names);
            _comparePeak = GUILayout.Toolbar(_comparePeak ? 1 : 0, new[] { "平均值", "峰值/最低值" }) == 1;
            _groupFilter = DrawGroupFilter(_groupFilter);

            for (int i = 0; i < _comparisonPaths.Count; i++)
                EditorGUILayout.LabelField((i == _baselineIndex ? "[基准] " : string.Empty) + names[i], _comparisonPaths[i]);

            EditorGUILayout.Space(8);
            DrawComparisonTable();
        }

        private void DrawComparisonTable()
        {
            ProfilerSessionFile baseline = _comparisonSessions[_baselineIndex];
            for (int metricIndex = 0; metricIndex < MetricIds.Definitions.Length; metricIndex++)
            {
                MetricDefinition definition = MetricIds.Definitions[metricIndex];
                if (_groupFilter != "全部" && definition.Group != _groupFilter)
                    continue;
                MetricSummary baselineSummary = SessionComparison.Find(baseline, definition.Id);
                EditorGUILayout.LabelField(definition.DisplayName, EditorStyles.boldLabel);
                for (int sessionIndex = 0; sessionIndex < _comparisonSessions.Count; sessionIndex++)
                {
                    MetricSummary summary = SessionComparison.Find(_comparisonSessions[sessionIndex], definition.Id);
                    string valueText = "N/A";
                    string differenceText = string.Empty;
                    if (summary != null && summary.sampleCount > 0)
                    {
                        double value = SessionComparison.SelectValue(summary, definition, _comparePeak);
                        valueText = FormatValue(value, definition.Unit);
                        if (sessionIndex != _baselineIndex && baselineSummary != null && baselineSummary.sampleCount > 0)
                        {
                            double baselineValue = SessionComparison.SelectValue(baselineSummary, definition, _comparePeak);
                            double difference = value - baselineValue;
                            differenceText = "  Δ " + FormatValue(difference, definition.Unit);
                            if (baselineValue != 0)
                                differenceText += "  (" + (difference / baselineValue * 100).ToString("+0.0;-0.0;0.0") + "%)";
                        }
                    }
                    EditorGUILayout.LabelField(_comparisonSessions[sessionIndex].metadata.sessionName, valueText + differenceText);
                }
                EditorGUILayout.Space(4);
            }
        }

        private static string DrawGroupFilter(string selected)
        {
            List<string> groups = new List<string> { "全部" };
            for (int i = 0; i < MetricIds.Definitions.Length; i++)
            {
                if (!groups.Contains(MetricIds.Definitions[i].Group))
                    groups.Add(MetricIds.Definitions[i].Group);
            }
            int index = Math.Max(0, groups.IndexOf(selected));
            return groups[EditorGUILayout.Popup("分组", index, groups.ToArray())];
        }

        private void TryLoadResult(string path)
        {
            try
            {
                _loadedResult = SessionFileStore.Load(path);
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog("加载失败", exception.Message, "确定");
            }
        }

        private void TryAddComparison(string path)
        {
            if (_comparisonPaths.Contains(path))
                return;
            try
            {
                _comparisonSessions.Add(SessionFileStore.Load(path));
                _comparisonPaths.Add(path);
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog("加载失败", exception.Message, "确定");
            }
        }

        private static void SaveResultAs(ProfilerSessionFile session)
        {
            string directory = EditorUtility.OpenFolderPanel("另存 Profiler 会话", CaptureSessionController.SaveDirectory, string.Empty);
            if (string.IsNullOrEmpty(directory))
                return;
            try
            {
                SessionFileStore.Save(session, directory, session.quality.completeness != "Complete");
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog("保存失败", exception.Message, "确定");
            }
        }

        private static void ExportResultXlsx(ProfilerSessionFile session)
        {
            string path = EditorUtility.SaveFilePanel(
                "导出 Profiler 结果表格",
                CaptureSessionController.SaveDirectory,
                SessionXlsxExporter.CreateSuggestedFileName(session),
                "xlsx");
            if (string.IsNullOrEmpty(path))
                return;
            try
            {
                SessionXlsxExporter.Export(session, path);
                EditorUtility.DisplayDialog("导出完成", "Profiler 结果已导出到：\n" + path, "确定");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("导出失败", exception.Message, "确定");
            }
        }

        private void ExportComparisonCsv()
        {
            string path = EditorUtility.SaveFilePanel("导出对比 CSV", CaptureSessionController.SaveDirectory, "AvgProfilerComparison", "csv");
            if (string.IsNullOrEmpty(path))
                return;
            try
            {
                SessionComparison.ExportCsv(_comparisonSessions, path, _comparePeak);
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog("导出失败", exception.Message, "确定");
            }
        }

        private static string FormatValue(double value, string unit)
        {
            if (unit == "bytes")
                return value >= 1024 * 1024 * 1024
                    ? (value / (1024 * 1024 * 1024)).ToString("F2") + " GB"
                    : (value / (1024 * 1024)).ToString("F2") + " MB";
            if (string.IsNullOrEmpty(unit))
                return value.ToString("N0");
            return value.ToString("F2") + " " + unit;
        }
    }
}
