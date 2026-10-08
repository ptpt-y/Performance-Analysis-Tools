using System;
using UnityEditor;
using UnityEngine;

namespace AvgProfilerStreaming
{
    internal enum CaptureState
    {
        Disabled,
        Observing,
        Capturing,
        Finalizing,
        Interrupted
    }

    [InitializeOnLoad]
    internal static class CaptureSessionController
    {
        private const int MaxFramesPerUpdate = 64;
        private const string FollowKey = "AvgProfilerDataTool.FollowRecord";
        private const string DirectoryKey = "AvgProfilerDataTool.SaveDirectory";
        private const string SessionNameKey = "AvgProfilerDataTool.SessionName";
        private const string IgnoreHeadKey = "AvgProfilerDataTool.IgnoreHead";
        private const string IgnoreTailKey = "AvgProfilerDataTool.IgnoreTail";

        private static IProfilerFrameSource _source;
        private static bool _wasRecording;
        private static int _idleLastFrame;
        private static int _previousFirstFrame;
        private static int _previousLastFrame;
        private static int _nextFrame;
        private static int _relativeIndex;
        private static string _captureConnection;

        public static CaptureState State { get; private set; }
        public static ProfilerSessionFile CurrentSession { get; private set; }
        public static ProfilerSessionFile LastSession { get; private set; }
        public static string LastSavedPath { get; private set; }
        public static string LastError { get; private set; }
        public static int BacklogFrames { get; private set; }

        public static bool FollowRecord
        {
            get { return EditorPrefs.GetBool(FollowKey, true); }
            set
            {
                EditorPrefs.SetBool(FollowKey, value);
                if (!value && State == CaptureState.Capturing)
                    Interrupt("用户关闭跟随 Profiler Record");
                State = value ? CaptureState.Observing : CaptureState.Disabled;
            }
        }

        public static string SaveDirectory
        {
            get { return EditorPrefs.GetString(DirectoryKey, SessionFileStore.DefaultDirectory); }
            set { EditorPrefs.SetString(DirectoryKey, value); }
        }

        public static string SessionName
        {
            get { return EditorPrefs.GetString(SessionNameKey, string.Empty); }
            set { EditorPrefs.SetString(SessionNameKey, value); }
        }

        public static int IgnoreHeadFrames
        {
            get { return EditorPrefs.GetInt(IgnoreHeadKey, 0); }
            set { EditorPrefs.SetInt(IgnoreHeadKey, Math.Max(0, value)); }
        }

        public static int IgnoreTailFrames
        {
            get { return EditorPrefs.GetInt(IgnoreTailKey, 0); }
            set { EditorPrefs.SetInt(IgnoreTailKey, Math.Max(0, value)); }
        }

        public static string ConnectionName
        {
            get { return _source == null ? "N/A" : _source.ConnectionName; }
        }

        static CaptureSessionController()
        {
            SetFrameSource(new UnityProfilerFrameSource());
            State = FollowRecord ? CaptureState.Observing : CaptureState.Disabled;
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += SaveBeforeReload;
            EditorApplication.quitting += SaveBeforeExit;
        }

        internal static void SetFrameSource(IProfilerFrameSource source)
        {
            _source = source;
            CurrentSession = null;
            BacklogFrames = 0;
            _wasRecording = source != null && source.IsRecording;
            _idleLastFrame = source == null ? -1 : source.LastFrameIndex;
            _previousFirstFrame = source == null ? -1 : source.FirstFrameIndex;
            _previousLastFrame = _idleLastFrame;
            State = FollowRecord ? CaptureState.Observing : CaptureState.Disabled;
        }

        internal static void Tick()
        {
            if (_source == null)
                return;

            bool recording = _source.IsRecording;
            int first = _source.FirstFrameIndex;
            int last = _source.LastFrameIndex;

            if (!FollowRecord)
            {
                State = CaptureState.Disabled;
                _wasRecording = recording;
                _idleLastFrame = last;
                return;
            }

            if (!_wasRecording && recording)
                BeginCapture(first, last);

            if (State == CaptureState.Capturing)
            {
                if (last < _previousLastFrame || first < _previousFirstFrame || _source.ConnectionName != _captureConnection)
                {
                    Interrupt("Profiler Clear、数据重置或连接变化");
                }
                else
                {
                    Consume(first, recording ? last - 1 : last);
                    if (!recording)
                    {
                        while (_nextFrame <= last)
                            Consume(first, last);
                        FinalizeCapture(false, string.Empty);
                    }
                }
            }

            if (State == CaptureState.Observing)
                _idleLastFrame = last;

            _previousFirstFrame = first;
            _previousLastFrame = last;
            _wasRecording = recording;
        }

        private static void Update()
        {
            try
            {
                Tick();
            }
            catch (Exception exception)
            {
                if (CurrentSession != null)
                    Interrupt("采集异常: " + exception.Message);
                else
                    LastError = exception.Message;
                Debug.LogException(exception);
            }
        }

        private static void BeginCapture(int first, int last)
        {
            _source.ResetCaches();
            _captureConnection = _source.ConnectionName;
            _nextFrame = _idleLastFrame >= first && last >= _idleLastFrame ? _idleLastFrame + 1 : first;
            _relativeIndex = 1;
            BacklogFrames = 0;
            LastError = string.Empty;
            CurrentSession = new ProfilerSessionFile();
            DateTime now = DateTime.Now;
            CurrentSession.metadata.sessionName = string.IsNullOrWhiteSpace(SessionName)
                ? now.ToString("yyyyMMdd_HHmmss")
                : SessionName;
            CurrentSession.metadata.createdAt = now.ToString("o");
            CurrentSession.metadata.unityVersion = Application.unityVersion;
            CurrentSession.metadata.platform = EditorUserBuildSettings.activeBuildTarget.ToString();
            CurrentSession.metadata.targetName = _captureConnection;
            CurrentSession.metadata.connectionName = _captureConnection;
            CurrentSession.metadata.firstFrame = _nextFrame;
            CurrentSession.metadata.ignoreHeadFrames = IgnoreHeadFrames;
            CurrentSession.metadata.ignoreTailFrames = IgnoreTailFrames;
            State = CaptureState.Capturing;
        }

        private static void Consume(int firstAvailable, int lastComplete)
        {
            if (_nextFrame < firstAvailable)
            {
                int lost = firstAvailable - _nextFrame;
                CurrentSession.quality.lostFrames += lost;
                _relativeIndex += lost;
                _nextFrame = firstAvailable;
                CurrentSession.quality.completeness = "Partial";
            }

            BacklogFrames = Math.Max(0, lastComplete - _nextFrame + 1);
            int processed = 0;
            while (_nextFrame <= lastComplete && processed < MaxFramesPerUpdate)
            {
                ProfilerFrameSnapshot snapshot;
                if (_source.TryReadFrame(_nextFrame, _relativeIndex, out snapshot))
                {
                    CurrentSession.samples.Add(snapshot);
                    CurrentSession.quality.validFrames++;
                }
                else
                {
                    CurrentSession.quality.invalidFrames++;
                }
                CurrentSession.quality.capturedFrames++;
                CurrentSession.metadata.lastFrame = _nextFrame;
                _nextFrame++;
                _relativeIndex++;
                processed++;
            }
            CurrentSession.quality.expectedFrames = CurrentSession.quality.capturedFrames + CurrentSession.quality.lostFrames;
            BacklogFrames = Math.Max(0, lastComplete - _nextFrame + 1);
        }

        private static void Interrupt(string reason)
        {
            State = CaptureState.Interrupted;
            if (CurrentSession != null)
            {
                CurrentSession.quality.completeness = "Interrupted";
                CurrentSession.quality.reason = reason;
                FinalizeCapture(true, reason);
            }
            _source.ResetCaches();
        }

        private static void FinalizeCapture(bool partial, string reason)
        {
            State = CaptureState.Finalizing;
            if (CurrentSession == null)
            {
                State = FollowRecord ? CaptureState.Observing : CaptureState.Disabled;
                return;
            }

            if (partial && CurrentSession.quality.completeness == "Complete")
                CurrentSession.quality.completeness = "Partial";
            if (!string.IsNullOrEmpty(reason))
                CurrentSession.quality.reason = reason;
            CurrentSession.summaries = SessionStatistics.Calculate(
                CurrentSession.samples,
                CurrentSession.metadata.ignoreHeadFrames,
                CurrentSession.metadata.ignoreTailFrames);
            LastSession = CurrentSession;
            CurrentSession = null;
            try
            {
                LastSavedPath = SessionFileStore.Save(LastSession, SaveDirectory,
                    partial || LastSession.quality.completeness != "Complete");
                LastError = string.Empty;
            }
            catch (Exception exception)
            {
                LastError = exception.Message;
                Debug.LogException(exception);
            }
            BacklogFrames = 0;
            State = FollowRecord ? CaptureState.Observing : CaptureState.Disabled;
        }

        private static void SaveBeforeReload()
        {
            if (State == CaptureState.Capturing)
                Interrupt("Domain Reload");
        }

        private static void SaveBeforeExit()
        {
            if (State == CaptureState.Capturing)
                Interrupt("Editor 退出");
        }
    }
}
