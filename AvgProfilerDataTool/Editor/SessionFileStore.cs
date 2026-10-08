using System;
using System.IO;
using UnityEngine;

namespace AvgProfilerStreaming
{
    internal static class SessionFileStore
    {
        public static string DefaultDirectory
        {
            get { return Path.GetFullPath(Path.Combine(Application.dataPath, "../ProfilerCaptures/AvgProfilerData")); }
        }

        public static string Save(ProfilerSessionFile session, string directory, bool partial)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));
            Directory.CreateDirectory(directory);
            string sessionName = SanitizeFileName(session.metadata.sessionName);
            if (string.IsNullOrEmpty(sessionName))
                sessionName = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string path = GetUniquePath(directory, sessionName + (partial ? "_partial" : string.Empty), ".avgprofiler.json");
            string temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, JsonUtility.ToJson(session, true));
            Validate(JsonUtility.FromJson<ProfilerSessionFile>(File.ReadAllText(temporaryPath)));
            File.Move(temporaryPath, path);
            return path;
        }

        public static ProfilerSessionFile Load(string path)
        {
            ProfilerSessionFile session = JsonUtility.FromJson<ProfilerSessionFile>(File.ReadAllText(path));
            Validate(session);
            return session;
        }

        private static void Validate(ProfilerSessionFile session)
        {
            if (session == null)
                throw new InvalidDataException("会话文件不是有效 JSON。");
            if (session.schemaVersion != ProfilerSessionFile.CurrentSchemaVersion)
                throw new InvalidDataException("不支持的会话版本: " + session.schemaVersion);
            if (session.metadata == null || session.quality == null || session.samples == null || session.summaries == null)
                throw new InvalidDataException("会话文件缺少必要字段。");
        }

        private static string GetUniquePath(string directory, string baseName, string extension)
        {
            string path = Path.Combine(directory, baseName + extension);
            int suffix = 1;
            while (File.Exists(path) || File.Exists(path + ".tmp"))
                path = Path.Combine(directory, baseName + "_" + suffix++ + extension);
            return path;
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalid.Length; i++)
                value = value.Replace(invalid[i], '_');
            return value.Trim();
        }
    }
}
