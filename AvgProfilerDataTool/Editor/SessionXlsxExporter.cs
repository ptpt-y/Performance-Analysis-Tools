using System;
using System.Drawing;
using System.IO;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace AvgProfilerStreaming
{
    internal static class SessionXlsxExporter
    {
        private const int ConclusionTitleRow = 11;
        private const int ConclusionContentRow = 12;
        private const int FirstSectionRow = 13;
        private const int ColumnCount = 13;

        private static readonly Color TitleColor = Color.FromArgb(31, 78, 121);
        private static readonly Color SectionColor = Color.FromArgb(31, 78, 121);
        private static readonly Color SubsectionColor = Color.FromArgb(221, 230, 242);
        private static readonly Color HeaderColor = Color.FromArgb(68, 84, 106);
        private static readonly Color LabelColor = Color.FromArgb(221, 230, 242);
        private static readonly Color FirstRowColor = Color.FromArgb(248, 250, 253);
        private static readonly Color SecondRowColor = Color.White;
        private static readonly Color NoteColor = Color.FromArgb(242, 246, 252);
        private static readonly Color WarningColor = Color.FromArgb(255, 242, 204);
        private static readonly Color ErrorColor = Color.FromArgb(252, 228, 214);
        private static readonly Color BorderColor = Color.FromArgb(191, 191, 191);
        private static readonly Color MutedTextColor = Color.FromArgb(89, 89, 89);

        public static string CreateSuggestedFileName(ProfilerSessionFile session)
        {
            string name = session == null || session.metadata == null ? "Profiler结果" : session.metadata.sessionName;
            if (string.IsNullOrWhiteSpace(name))
                name = "Profiler结果_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            char[] invalidCharacters = Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalidCharacters.Length; i++)
                name = name.Replace(invalidCharacters[i], '_');
            return name + "_Profiler结果";
        }

        public static void Export(ProfilerSessionFile session, string path)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("导出路径为空。", nameof(path));

            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;
            using (ExcelPackage package = new ExcelPackage())
            {
                ExcelWorksheet worksheet = package.Workbook.Worksheets.Add("Profiler结果");
                WriteSessionSummary(worksheet, session);
                WriteDataConclusion(worksheet, session);
                int lastRow = WriteMetricTable(worksheet, session);
                ApplyWorksheetLayout(worksheet, lastRow);
                package.SaveAs(new FileInfo(path));
            }
        }

        private static void WriteSessionSummary(ExcelWorksheet worksheet, ProfilerSessionFile session)
        {
            worksheet.Cells[2, 1].Value = "Profiler 性能统计结果";
            worksheet.Cells[2, 1].Style.Font.Name = "Arial";
            worksheet.Cells[2, 1].Style.Font.Size = 15;
            worksheet.Cells[2, 1].Style.Font.Bold = true;
            worksheet.Cells[2, 1].Style.Font.Color.SetColor(TitleColor);

            WriteSummaryPair(worksheet, 4, 1, "会话名", session.metadata.sessionName);
            WriteSummaryPair(worksheet, 5, 1, "创建时间", session.metadata.createdAt);
            WriteSummaryPair(worksheet, 6, 1, "目标", session.metadata.targetName);
            WriteSummaryPair(worksheet, 7, 1, "连接", session.metadata.connectionName);
            WriteSummaryPair(worksheet, 8, 1, "平台", session.metadata.platform);
            WriteSummaryPair(worksheet, 9, 1, "Unity", session.metadata.unityVersion);

            WriteSummaryPair(worksheet, 4, 4, "完整度", session.quality.completeness);
            WriteSummaryPair(worksheet, 5, 4, "期望帧", session.quality.expectedFrames);
            WriteSummaryPair(worksheet, 6, 4, "采集帧", session.quality.capturedFrames);
            WriteSummaryPair(worksheet, 7, 4, "有效 / 无效帧", session.quality.validFrames + " / " + session.quality.invalidFrames);
            WriteSummaryPair(worksheet, 8, 4, "丢失帧", session.quality.lostFrames);
            WriteSummaryPair(worksheet, 9, 4, "过滤", "开头 " + session.metadata.ignoreHeadFrames + " / 结尾 " + session.metadata.ignoreTailFrames);
            WriteSummaryPair(worksheet, 10, 4, "原因", session.quality.reason);

            ExcelRange summaryRange = worksheet.Cells[4, 1, 10, 5];
            summaryRange.Style.Font.Name = "Arial";
            summaryRange.Style.Font.Size = 10;
            summaryRange.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
            worksheet.Cells[4, 5].Style.Font.Bold = true;
            if (!string.Equals(session.quality.completeness, "Complete", StringComparison.Ordinal))
            {
                worksheet.Cells[4, 5].Style.Fill.PatternType = ExcelFillStyle.Solid;
                worksheet.Cells[4, 5].Style.Fill.BackgroundColor.SetColor(ErrorColor);
            }
        }

        private static void WriteSummaryPair(ExcelWorksheet worksheet, int row, int column, string label, object value)
        {
            ExcelRange labelCell = worksheet.Cells[row, column];
            labelCell.Value = label;
            labelCell.Style.Font.Bold = true;
            labelCell.Style.Fill.PatternType = ExcelFillStyle.Solid;
            labelCell.Style.Fill.BackgroundColor.SetColor(LabelColor);
            labelCell.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
            labelCell.Style.Border.Bottom.Color.SetColor(BorderColor);

            ExcelRange valueCell = worksheet.Cells[row, column + 1];
            valueCell.Value = value;
            valueCell.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
            valueCell.Style.Border.Bottom.Color.SetColor(BorderColor);
        }

        private static void WriteDataConclusion(ExcelWorksheet worksheet, ProfilerSessionFile session)
        {
            WriteSectionTitle(worksheet, ConclusionTitleRow, "数据结论");

            ExcelRange contentRange = worksheet.Cells[ConclusionContentRow, 1, ConclusionContentRow, ColumnCount];
            contentRange.Merge = true;
            contentRange.Value = BuildDataConclusion(session);
            contentRange.Style.Fill.PatternType = ExcelFillStyle.Solid;
            contentRange.Style.Fill.BackgroundColor.SetColor(NoteColor);
            contentRange.Style.Font.Color.SetColor(MutedTextColor);
            contentRange.Style.WrapText = true;
            contentRange.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
            worksheet.Row(ConclusionContentRow).Height = 34;
        }

        private static string BuildDataConclusion(ProfilerSessionFile session)
        {
            MetricSummary durationFps = SessionComparison.Find(session, MetricIds.FpsDuration);
            MetricSummary frameTime = SessionComparison.Find(session, MetricIds.FrameTime);
            MetricSummary playerLoop = SessionComparison.Find(session, MetricIds.PlayerLoop);
            MetricSummary logic = SessionComparison.Find(session, MetricIds.Logic);
            MetricSummary rendering = SessionComparison.Find(session, MetricIds.Rendering);
            MetricSummary gpuFrameTime = SessionComparison.Find(session, MetricIds.GpuFrameTime);
            int effectiveFrameCount = Math.Max(0,
                session.quality.validFrames - session.metadata.ignoreHeadFrames - session.metadata.ignoreTailFrames);

            string framePeak = FormatMetricValue(frameTime, false, "ms");
            string framePeakLocation = frameTime == null || frameTime.sampleCount == 0
                ? string.Empty
                : "（Profiler 界面帧 " + (frameTime.maximumFrame + 1) + "）";
            return "整体：总时长平均 FPS " + FormatMetricValue(durationFps, true, "FPS")
                   + "，Frame CPU Time 平均 " + FormatMetricValue(frameTime, true, "ms")
                   + " / 峰值 " + framePeak + framePeakLocation
                   + "。CPU：Player Loop 平均 " + FormatMetricValue(playerLoop, true, "ms")
                   + "；Logic / Rendering 平均 " + FormatMetricValue(logic, true, "ms")
                   + " / " + FormatMetricValue(rendering, true, "ms")
                   + "。GPU：GPU Frame Time 平均 " + FormatMetricValue(gpuFrameTime, true, "ms")
                   + "。数据：" + session.quality.completeness + "，有效分析帧 " + effectiveFrameCount + "。";
        }

        private static string FormatMetricValue(MetricSummary summary, bool useAverage, string unit)
        {
            if (summary == null || summary.sampleCount == 0)
                return "N/A";
            double value = useAverage ? summary.average : summary.maximum;
            return value.ToString("F2") + " " + unit;
        }

        private static int WriteMetricTable(ExcelWorksheet worksheet, ProfilerSessionFile session)
        {
            int effectiveFrameCount = Math.Max(0,
                session.quality.validFrames - session.metadata.ignoreHeadFrames - session.metadata.ignoreTailFrames);
            int row = FirstSectionRow;

            WriteSection(worksheet, session, ref row, "性能概览",
                "先看帧率与单帧耗时，快速判断整体流畅度与异常帧规模。",
                MetricIds.OverviewMetrics, effectiveFrameCount);
            WriteCpuSection(worksheet, session, ref row, effectiveFrameCount);
            WriteSection(worksheet, session, ref row, "Main Thread PlayerLoop 归因（估算）",
                "按主线程 PlayerLoop 拆分等待、脚本、逻辑与渲染归因；各项为估算口径，可能存在交叠。",
                MetricIds.PlayerLoopBreakdownMetrics, effectiveFrameCount);
            WriteSection(worksheet, session, ref row, "GPU",
                "用于判断 GPU 帧耗时及 CPU 侧可观测的 GPU 等待估算。",
                MetricIds.GpuMetrics, effectiveFrameCount);
            WriteSection(worksheet, session, ref row, "Rendering Counters",
                "渲染提交规模与几何复杂度计数，用于横向比较场景压力。",
                MetricIds.RenderingMetrics, effectiveFrameCount);
            WriteSection(worksheet, session, ref row, "Memory",
                "内存占用与资源对象规模，适合结合不同会话做趋势对比。",
                MetricIds.MemoryMetrics, effectiveFrameCount);

            return row - 2;
        }

        private static void WriteCpuSection(ExcelWorksheet worksheet, ProfilerSessionFile session, ref int row,
            int effectiveFrameCount)
        {
            WriteSectionTitle(worksheet, row++, "CPU 线程");
            WriteSectionNote(worksheet, row++, "分别观察主线程与渲染线程；等待类指标保留在线程内，便于判断真正的执行瓶颈。");
            WriteSubsection(worksheet, session, ref row, "Main Thread", MetricIds.MainThreadMetrics, effectiveFrameCount);
            WriteSubsection(worksheet, session, ref row, "Render Thread", MetricIds.RenderThreadMetrics, effectiveFrameCount);
            row++;
        }

        private static void WriteSection(ExcelWorksheet worksheet, ProfilerSessionFile session, ref int row,
            string title, string note, string[] metricIds, int effectiveFrameCount)
        {
            WriteSectionTitle(worksheet, row++, title);
            WriteSectionNote(worksheet, row++, note);
            WriteMetricHeader(worksheet, row++);
            WriteMetricRows(worksheet, session, ref row, metricIds, effectiveFrameCount);
            row++;
        }

        private static void WriteSubsection(ExcelWorksheet worksheet, ProfilerSessionFile session, ref int row,
            string title, string[] metricIds, int effectiveFrameCount)
        {
            ExcelRange titleRange = worksheet.Cells[row, 1, row, ColumnCount];
            titleRange.Merge = true;
            titleRange.Value = title;
            titleRange.Style.Fill.PatternType = ExcelFillStyle.Solid;
            titleRange.Style.Fill.BackgroundColor.SetColor(SubsectionColor);
            titleRange.Style.Font.Bold = true;
            titleRange.Style.Font.Color.SetColor(TitleColor);
            titleRange.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
            titleRange.Style.Border.Bottom.Color.SetColor(BorderColor);
            row++;
            WriteMetricHeader(worksheet, row++);
            WriteMetricRows(worksheet, session, ref row, metricIds, effectiveFrameCount);
        }

        private static void WriteSectionTitle(ExcelWorksheet worksheet, int row, string title)
        {
            ExcelRange range = worksheet.Cells[row, 1, row, ColumnCount];
            range.Merge = true;
            range.Value = title;
            range.Style.Fill.PatternType = ExcelFillStyle.Solid;
            range.Style.Fill.BackgroundColor.SetColor(SectionColor);
            range.Style.Font.Bold = true;
            range.Style.Font.Color.SetColor(Color.White);
            range.Style.Font.Size = 11;
        }

        private static void WriteSectionNote(ExcelWorksheet worksheet, int row, string note)
        {
            ExcelRange range = worksheet.Cells[row, 1, row, ColumnCount];
            range.Merge = true;
            range.Value = note;
            range.Style.Fill.PatternType = ExcelFillStyle.Solid;
            range.Style.Fill.BackgroundColor.SetColor(NoteColor);
            range.Style.Font.Italic = true;
            range.Style.Font.Color.SetColor(MutedTextColor);
        }

        private static void WriteMetricHeader(ExcelWorksheet worksheet, int row)
        {
            string[] headers =
            {
                "指标", "平均值", "极值类型", "极值", "Profiler 帧索引", "Profiler 界面帧",
                "录制相对帧", "有效样本", "样本率", "平均占比", "极值帧占比", "单位", "说明"
            };
            for (int column = 0; column < headers.Length; column++)
                worksheet.Cells[row, column + 1].Value = headers[column];
            ApplyHeaderStyle(worksheet.Cells[row, 1, row, ColumnCount]);
            worksheet.Row(row).Height = 32;
        }

        private static void WriteMetricRows(ExcelWorksheet worksheet, ProfilerSessionFile session, ref int row,
            string[] metricIds, int effectiveFrameCount)
        {
            for (int metricIndex = 0; metricIndex < metricIds.Length; metricIndex++)
            {
                MetricDefinition definition = MetricIds.Find(metricIds[metricIndex]);
                MetricSummary summary = SessionComparison.Find(session, definition.Id);
                WriteMetricRow(worksheet, row, definition, summary, effectiveFrameCount);
                ApplyMetricRowStyle(worksheet, row, metricIndex, summary);
                row++;
            }
        }

        private static void WriteMetricRow(ExcelWorksheet worksheet, int row, MetricDefinition definition,
            MetricSummary summary, int effectiveFrameCount)
        {
            worksheet.Cells[row, 1].Value = definition.DisplayName;
            worksheet.Cells[row, 12].Value = definition.Unit;
            worksheet.Cells[row, 13].Value = MetricIds.GetDescription(definition.Id);
            worksheet.Cells[row, 9, row, 11].Style.Numberformat.Format = "0.0%";

            if (summary == null || summary.sampleCount == 0)
            {
                worksheet.Cells[row, 2].Value = "N/A";
                worksheet.Cells[row, 3].Value = definition.HigherIsBetter ? "最低" : "峰值";
                worksheet.Cells[row, 4].Value = "N/A";
                worksheet.Cells[row, 9].Value = 0d;
                return;
            }

            bool useMinimum = definition.HigherIsBetter;
            worksheet.Cells[row, 2].Value = summary.average;
            worksheet.Cells[row, 3].Value = useMinimum ? "最低" : "峰值";
            worksheet.Cells[row, 4].Value = useMinimum ? summary.minimum : summary.maximum;
            string valueFormat = definition.Unit == "ms" || definition.Unit == "FPS" ? "#,##0.00" : "#,##0";
            worksheet.Cells[row, 2].Style.Numberformat.Format = valueFormat;
            worksheet.Cells[row, 4].Style.Numberformat.Format = valueFormat;
            int profilerFrame = useMinimum ? summary.minimumFrame : summary.maximumFrame;
            worksheet.Cells[row, 5].Value = profilerFrame;
            worksheet.Cells[row, 6].Value = profilerFrame + 1;
            worksheet.Cells[row, 7].Value = useMinimum ? summary.minimumRelativeFrame : summary.maximumRelativeFrame;
            worksheet.Cells[row, 8].Value = summary.sampleCount;
            worksheet.Cells[row, 9].Value = effectiveFrameCount > 0 ? (double)summary.sampleCount / effectiveFrameCount : 0d;
            if (summary.averageRatioValid)
                worksheet.Cells[row, 10].Value = summary.averagePlayerLoopRatio;
            if (summary.averageRatioValid)
                worksheet.Cells[row, 11].Value = summary.extremePlayerLoopRatio;
        }

        private static void ApplyMetricRowStyle(ExcelWorksheet worksheet, int row, int metricIndex, MetricSummary summary)
        {
            ExcelRange range = worksheet.Cells[row, 1, row, ColumnCount];
            range.Style.Fill.PatternType = ExcelFillStyle.Solid;
            range.Style.Fill.BackgroundColor.SetColor(metricIndex % 2 == 0 ? FirstRowColor : SecondRowColor);
            range.Style.Border.Bottom.Style = ExcelBorderStyle.Hair;
            range.Style.Border.Bottom.Color.SetColor(BorderColor);
            range.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
            worksheet.Cells[row, 1].Style.Font.Bold = true;
            worksheet.Cells[row, 1].Style.Indent = 1;
            worksheet.Cells[row, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
            worksheet.Cells[row, 2, row, 11].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
            worksheet.Cells[row, 3].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            worksheet.Cells[row, 12].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            worksheet.Cells[row, 13].Style.Font.Color.SetColor(MutedTextColor);

            if (summary == null || summary.sampleCount == 0)
                worksheet.Cells[row, 2, row, 4].Style.Font.Color.SetColor(MutedTextColor);
            else if (summary.hasRatioAnomaly || summary.hasAverageRatioAnomaly)
                range.Style.Fill.BackgroundColor.SetColor(WarningColor);
        }

        private static void ApplyHeaderStyle(ExcelRange range)
        {
            range.Style.Fill.PatternType = ExcelFillStyle.Solid;
            range.Style.Fill.BackgroundColor.SetColor(HeaderColor);
            range.Style.Font.Name = "Arial";
            range.Style.Font.Size = 10;
            range.Style.Font.Bold = true;
            range.Style.Font.Color.SetColor(Color.White);
            range.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            range.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
            range.Style.WrapText = true;
        }

        private static void ApplyWorksheetLayout(ExcelWorksheet worksheet, int lastRow)
        {
            worksheet.View.ShowGridLines = false;
            worksheet.View.FreezePanes(4, 2);
            ExcelRange populatedRange = worksheet.Cells[2, 1, lastRow, ColumnCount];
            populatedRange.Style.Font.Name = "Arial";
            populatedRange.Style.VerticalAlignment = ExcelVerticalAlignment.Center;

            worksheet.Column(1).Width = 38;
            worksheet.Column(2).Width = 13;
            worksheet.Column(3).Width = 11;
            worksheet.Column(4).Width = 13;
            worksheet.Column(5).Width = 17;
            worksheet.Column(6).Width = 17;
            worksheet.Column(7).Width = 15;
            worksheet.Column(8).Width = 12;
            worksheet.Column(9).Width = 11;
            worksheet.Column(10).Width = 12;
            worksheet.Column(11).Width = 13;
            worksheet.Column(12).Width = 10;
            worksheet.Column(13).Width = 72;

            worksheet.Column(13).Style.WrapText = false;
            worksheet.Row(2).Height = 24;
            for (int row = FirstSectionRow; row <= lastRow; row++)
            {
                if (worksheet.Row(row).Height <= 15)
                    worksheet.Row(row).Height = 21;
            }
        }
    }
}
