using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace XrealCaption
{
    /// <summary>
    /// 操作と認識のログ（persistentDataPath/Logs/caption_YYYYMMDD_HHMMSS.csv）。
    /// 列：timestamp,event,objectId,name,score,second,detail
    /// event：start / shown（キャプションを出した）/ hidden（消した）/ register / sample / edit / delete / reset / setting / key_press
    /// </summary>
    public class CaptionLogger : IDisposable
    {
        StreamWriter m_Writer;
        public string Path { get; }

        public CaptionLogger()
        {
            try
            {
                var dir = System.IO.Path.Combine(Application.persistentDataPath, "Logs");
                Directory.CreateDirectory(dir);
                Path = System.IO.Path.Combine(dir, $"caption_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                m_Writer = new StreamWriter(Path, false, new System.Text.UTF8Encoding(true)) { AutoFlush = true };
                m_Writer.WriteLine("timestamp,event,objectId,name,score,second,detail");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CaptionApp] ログを作れませんでした: {e.Message}");
            }
        }

        public void Log(string ev, RegisteredObject obj = null, float score = float.NaN, float second = float.NaN, string detail = "")
        {
            if (m_Writer == null) return;
            string F(float v) => float.IsNaN(v) ? "" : v.ToString("0.000", CultureInfo.InvariantCulture);
            m_Writer.WriteLine(string.Join(",",
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                ev, Csv(obj?.id), Csv(obj?.name), F(score), F(second), Csv(detail)));
        }

        static string Csv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\n", " ");
            return s.IndexOfAny(new[] { ',', '"' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }

        public void Dispose()
        {
            m_Writer?.Dispose();
            m_Writer = null;
        }
    }
}
