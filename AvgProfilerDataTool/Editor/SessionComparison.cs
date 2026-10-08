using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace AvgProfilerStreaming
{
    internal sealed class ComparisonValue
    {
        public string MetricId;
        public double Value;
        public bool Valid;
        public double AbsoluteDifference;
        public double PercentageDifference;
        public bool HasPercentageDifference;
    }

    internal static class SessionComparison
    {
        public static List<ComparisonValue> Compare(ProfilerSessionFile baseline, ProfilerSessionFile candidate, bool usePeak)
        {
            List<ComparisonValue> result = new List<ComparisonValue>();
            for (int i = 0; i < MetricIds.Definitions.Length; i++)
            {
                MetricDefinition definition = MetricIds.Definitions[i];
                MetricSummary baselineSummary = Find(baseline, definition.Id);
                MetricSummary candidateSummary = Find(candidate, definition.Id);
                ComparisonValue comparison = new ComparisonValue { MetricId = definition.Id };
                if (baselineSummary != null && candidateSummary != null && baselineSummary.sampleCount > 0 && candidateSummary.sampleCount > 0)
                {
                    double baselineValue = SelectValue(baselineSummary, definition, usePeak);
                    comparison.Value = SelectValue(candidateSummary, definition, usePeak);
                    comparison.AbsoluteDifference = comparison.Value - baselineValue;
                    comparison.Valid = true;
                    if (baselineValue != 0)
                    {
                        comparison.PercentageDifference = comparison.AbsoluteDifference / baselineValue * 100.0;
                        comparison.HasPercentageDifference = true;
                    }
                }
                result.Add(comparison);
            }
            return result;
        }

        public static void ExportCsv(IList<ProfilerSessionFile> sessions, string path, bool usePeak)
        {
            if (sessions.Count == 0)
                throw new InvalidOperationException("没有可导出的会话。");
            StringBuilder csv = new StringBuilder();
            csv.Append("Metric");
            for (int i = 0; i < sessions.Count; i++)
                csv.Append(',').Append(Escape(sessions[i].metadata.sessionName));
            csv.AppendLine();
            for (int metricIndex = 0; metricIndex < MetricIds.Definitions.Length; metricIndex++)
            {
                MetricDefinition definition = MetricIds.Definitions[metricIndex];
                csv.Append(Escape(definition.DisplayName));
                for (int sessionIndex = 0; sessionIndex < sessions.Count; sessionIndex++)
                {
                    MetricSummary summary = Find(sessions[sessionIndex], definition.Id);
                    csv.Append(',');
                    if (summary != null && summary.sampleCount > 0)
                        csv.Append(SelectValue(summary, definition, usePeak).ToString("G17", CultureInfo.InvariantCulture));
                }
                csv.AppendLine();
            }
            File.WriteAllText(path, csv.ToString(), new UTF8Encoding(false));
        }

        internal static MetricSummary Find(ProfilerSessionFile session, string metricId)
        {
            if (session == null || session.summaries == null)
                return null;
            for (int i = 0; i < session.summaries.Count; i++)
            {
                if (session.summaries[i].metricId == metricId)
                    return session.summaries[i];
            }
            return null;
        }

        internal static double SelectValue(MetricSummary summary, MetricDefinition definition, bool usePeak)
        {
            if (!usePeak)
                return summary.average;
            return definition.HigherIsBetter ? summary.minimum : summary.maximum;
        }

        private static string Escape(string value)
        {
            value = value ?? string.Empty;
            return '"' + value.Replace("\"", "\"\"") + '"';
        }
    }
}
