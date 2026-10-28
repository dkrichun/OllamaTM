using System;
using System.IO;
using System.Text;

namespace OllamaTM
{
    internal static class Logger
    {
        private static readonly object _lock = new object();
        private static readonly string _logPath = Path.Combine(AppContext.BaseDirectory, "OllamaTM.log");
        private const long MaxSizeBytes = 5 * 1024 * 1024;

        public static void Info(string message) => Write("INFO", message);
        public static void Warn(string message) => Write("WARN", message);

        public static void Error(string message, Exception? ex = null)
        {
            string msg = ex == null ? message : $"{message}\nИсключение: {ex.Message}\nСтек: {ex.StackTrace}";
            Write("ERROR", msg);
        }

        private static void Write(string level, string message)
        {
            try
            {
                lock (_lock)
                {
                    if (File.Exists(_logPath))
                    {
                        var fi = new FileInfo(_logPath);
                        if (fi.Length > MaxSizeBytes)
                        {
                            string backup = _logPath + ".old";
                            if (File.Exists(backup)) File.Delete(backup);
                            File.Move(_logPath, backup);
                        }
                    }

                    string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}{Environment.NewLine}";
                    File.AppendAllText(_logPath, line, Encoding.UTF8);
                }
            }
            catch { }
        }
    }
}
