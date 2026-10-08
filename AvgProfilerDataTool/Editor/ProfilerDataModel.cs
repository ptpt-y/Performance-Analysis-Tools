using System;
using System.Collections.Generic;

namespace AvgProfilerStreaming
{
    internal static class MetricIds
    {
        public const string FpsArithmetic = "fps.arithmetic";
        public const string FpsDuration = "fps.duration";
        public const string FrameTime = "frame.cpu_time_ms";
        public const string GpuFrameTime = "frame.gpu_time_ms";
        public const string PlayerLoop = "main.player_loop_ms";
        public const string WaitForTargetFps = "main.wait_for_target_fps_ms";
        public const string WaitForPresent = "main.wait_for_present_ms";
        public const string PipelineScripts = "main.pipeline_scripts_ms";
        public const string VSyncWait = "split.vsync_wait_ms";
        public const string Scripts = "split.scripts_ms";
        public const string Logic = "split.logic_ms";
        public const string Rendering = "split.rendering_ms";
        public const string PresentFrame = "render.present_frame_ms";
        public const string WaitForGfxCommands = "render.wait_for_gfx_commands_ms";
        public const string RenderThreadLogic = "render.logic_ms";
        public const string GpuCpuEstimate = "render.gpu_cpu_estimate_ms";
        public const string Batches = "rendering.batches";
        public const string SetPass = "rendering.set_pass";
        public const string Triangles = "rendering.triangles";
        public const string Vertices = "rendering.vertices";
        public const string TotalUsedMemory = "memory.total_used_bytes";
        public const string TextureMemory = "memory.texture_bytes";
        public const string MeshMemory = "memory.mesh_bytes";
        public const string MaterialCount = "memory.material_count";
        public const string ObjectCount = "memory.object_count";

        public static readonly MetricDefinition[] Definitions =
        {
            new MetricDefinition(FpsArithmetic, "逐帧 FPS 算术平均", "基础", true, "FPS"),
            new MetricDefinition(FpsDuration, "总时长平均 FPS", "基础", true, "FPS"),
            new MetricDefinition(FrameTime, "Frame CPU Time", "基础", false, "ms"),
            new MetricDefinition(GpuFrameTime, "GPU Frame Time", "GPU", false, "ms"),
            new MetricDefinition(PlayerLoop, "Player Loop", "Main Thread", false, "ms"),
            new MetricDefinition(WaitForTargetFps, "WaitForTargetFPS", "Main Thread", false, "ms"),
            new MetricDefinition(WaitForPresent, "WaitForPresentOnGfxThread", "Main Thread", false, "ms"),
            new MetricDefinition(PipelineScripts, "Pipeline Scripts", "Main Thread", false, "ms"),
            new MetricDefinition(VSyncWait, "VSync Wait", "逻辑/渲染", false, "ms"),
            new MetricDefinition(Scripts, "Scripts", "逻辑/渲染", false, "ms"),
            new MetricDefinition(Logic, "Logic", "逻辑/渲染", false, "ms"),
            new MetricDefinition(Rendering, "Rendering", "逻辑/渲染", false, "ms"),
            new MetricDefinition(PresentFrame, "Gfx.PresentFrame", "Render Thread", false, "ms"),
            new MetricDefinition(WaitForGfxCommands, "WaitForGfxCommandsFromMainThread", "Render Thread", false, "ms"),
            new MetricDefinition(RenderThreadLogic, "渲染线程逻辑", "Render Thread", false, "ms"),
            new MetricDefinition(GpuCpuEstimate, "GPU总耗时（CPU Marker估算）", "GPU", false, "ms"),
            new MetricDefinition(Batches, "Batches", "Rendering", false, ""),
            new MetricDefinition(SetPass, "SetPass Calls", "Rendering", false, ""),
            new MetricDefinition(Triangles, "Triangles", "Rendering", false, ""),
            new MetricDefinition(Vertices, "Vertices", "Rendering", false, ""),
            new MetricDefinition(TotalUsedMemory, "Total Used Memory", "Memory", false, "bytes"),
            new MetricDefinition(TextureMemory, "Texture Memory", "Memory", false, "bytes"),
            new MetricDefinition(MeshMemory, "Mesh Memory", "Memory", false, "bytes"),
            new MetricDefinition(MaterialCount, "Material Count", "Memory", false, ""),
            new MetricDefinition(ObjectCount, "Object Count", "Memory", false, "")
        };

        public static readonly string[] OverviewMetrics =
        {
            FpsArithmetic, FpsDuration, FrameTime
        };

        public static readonly string[] MainThreadMetrics =
        {
            PlayerLoop, WaitForTargetFps, WaitForPresent, PipelineScripts
        };

        public static readonly string[] RenderThreadMetrics =
        {
            PresentFrame, WaitForGfxCommands, RenderThreadLogic
        };

        public static readonly string[] PlayerLoopBreakdownMetrics =
        {
            VSyncWait, Scripts, Logic, Rendering
        };

        public static readonly string[] GpuMetrics =
        {
            GpuFrameTime, GpuCpuEstimate
        };

        public static readonly string[] RenderingMetrics =
        {
            Batches, SetPass, Triangles, Vertices
        };

        public static readonly string[] MemoryMetrics =
        {
            TotalUsedMemory, TextureMemory, MeshMemory, MaterialCount, ObjectCount
        };

        public static MetricDefinition Find(string id)
        {
            for (int i = 0; i < Definitions.Length; i++)
            {
                if (Definitions[i].Id == id)
                    return Definitions[i];
            }
            return new MetricDefinition(id, id, "Other", false, "");
        }

        public static string GetDescription(string metricId)
        {
            switch (metricId)
            {
                case FpsArithmetic:
                    return "逐帧 FPS 的算术平均，用于与旧工具对照；高 FPS 帧会提高结果。";
                case FpsDuration:
                    return "1000 × 有效帧数 / 总帧耗时，是跨会话比较的主要 FPS 口径。";
                case FrameTime:
                    return "Profiler 记录的整帧 CPU 时间；峰值为最大耗时帧。";
                case GpuFrameTime:
                    return "设备提供的真实 GPU 帧时间；平台不提供时显示 N/A。";
                case PlayerLoop:
                    return "Main Thread 一帧内 PlayerLoop 的总耗时，包含工作与等待。";
                case WaitForTargetFps:
                    return "Main Thread 为目标帧率或垂直同步等待下一帧的时间。";
                case WaitForPresent:
                    return "Main Thread 等待渲染线程、图形提交或 Present 进度的时间。";
                case PipelineScripts:
                    return "Main Thread 上 RenderPipelineManager.DoRenderLoop_Internal() 的耗时，主要是 SRP/URP 渲染组织与命令提交。";
                case VSyncWait:
                    return "当前口径等于 WaitForTargetFPS，用于表示 PlayerLoop 中的帧节奏等待。";
                case Scripts:
                    return "PlayerLoop 扣除渲染等待和帧节奏等待后的 Main Thread 工作；不只包含业务脚本。";
                case Logic:
                    return "Scripts 再扣除 Pipeline Scripts 后的剩余 Main Thread 工作；包含业务与其他引擎系统，不是纯 gameplay 时间。";
                case Rendering:
                    return "WaitForPresentOnGfxThread + Pipeline Scripts，是 Main Thread 渲染相关归因，不包含 Render Thread 或 GPU。";
                case PresentFrame:
                    return "Render Thread 提交或等待 Present 的 CPU Marker，可能受 GPU、VSync 和驱动影响。";
                case WaitForGfxCommands:
                    return "Render Thread 等待 Main Thread 产生图形命令的时间。";
                case RenderThreadLogic:
                    return "Render Thread 上匹配 RenderSingleCameraInternal* 的 Marker；部分平台可能不提供。";
                case GpuCpuEstimate:
                    return "渲染线程逻辑 + Gfx.PresentFrame 的 CPU Marker 估算，不是真实 GPU 时间。";
                case Batches:
                    return "每帧提交的绘制批次数。";
                case SetPass:
                    return "每帧切换 Shader Pass 或渲染状态的次数。";
                case Triangles:
                    return "每帧提交给渲染管线的三角形数量。";
                case Vertices:
                    return "每帧提交给渲染管线的顶点数量。";
                case TotalUsedMemory:
                    return "Unity Profiler 统计的总已用内存。";
                case TextureMemory:
                    return "Unity Profiler 统计的纹理内存。";
                case MeshMemory:
                    return "Unity Profiler 统计的网格内存。";
                case MaterialCount:
                    return "Unity Profiler 统计的 Material 数量。";
                case ObjectCount:
                    return "Unity Profiler 统计的 Unity Object 数量。";
                default:
                    return string.Empty;
            }
        }
    }

    internal sealed class MetricDefinition
    {
        public readonly string Id;
        public readonly string DisplayName;
        public readonly string Group;
        public readonly bool HigherIsBetter;
        public readonly string Unit;

        public MetricDefinition(string id, string displayName, string group, bool higherIsBetter, string unit)
        {
            Id = id;
            DisplayName = displayName;
            Group = group;
            HigherIsBetter = higherIsBetter;
            Unit = unit;
        }
    }

    [Serializable]
    internal sealed class MetricSample
    {
        public string metricId;
        public bool valid;
        public double value;

        public MetricSample() { }
        public MetricSample(string metricId, bool valid, double value)
        {
            this.metricId = metricId;
            this.valid = valid;
            this.value = value;
        }
    }

    [Serializable]
    internal sealed class ProfilerFrameSnapshot
    {
        public int frameIndex;
        public int relativeIndex;
        public double startTimeMs;
        public List<MetricSample> metrics = new List<MetricSample>();

        public void Add(string metricId, double value) { metrics.Add(new MetricSample(metricId, true, value)); }
        public void AddInvalid(string metricId) { metrics.Add(new MetricSample(metricId, false, 0)); }

        public bool TryGet(string metricId, out double value)
        {
            for (int i = 0; i < metrics.Count; i++)
            {
                MetricSample metric = metrics[i];
                if (metric.metricId == metricId && metric.valid)
                {
                    value = metric.value;
                    return true;
                }
            }
            value = 0;
            return false;
        }
    }

    [Serializable]
    internal sealed class MetricSummary
    {
        public string metricId;
        public int sampleCount;
        public double average;
        public double minimum;
        public int minimumFrame;
        public int minimumRelativeFrame;
        public double maximum;
        public int maximumFrame;
        public int maximumRelativeFrame;
        public bool hasRatioAnomaly;
        public double extremePlayerLoopRatio;
        public bool averageRatioValid;
        public double averagePlayerLoopRatio;
        public bool hasAverageRatioAnomaly;
    }

    [Serializable]
    internal sealed class SessionMetadata
    {
        public string sessionName;
        public string createdAt;
        public string unityVersion;
        public string platform;
        public string targetName;
        public string connectionName;
        public int firstFrame;
        public int lastFrame;
        public int ignoreHeadFrames;
        public int ignoreTailFrames;
    }

    [Serializable]
    internal sealed class SessionQuality
    {
        public string completeness = "Complete";
        public int expectedFrames;
        public int capturedFrames;
        public int validFrames;
        public int invalidFrames;
        public int lostFrames;
        public string reason;
    }

    [Serializable]
    internal sealed class ProfilerSessionFile
    {
        public const int CurrentSchemaVersion = 1;
        public int schemaVersion = CurrentSchemaVersion;
        public SessionMetadata metadata = new SessionMetadata();
        public SessionQuality quality = new SessionQuality();
        public List<ProfilerFrameSnapshot> samples = new List<ProfilerFrameSnapshot>();
        public List<MetricSummary> summaries = new List<MetricSummary>();
    }
}
