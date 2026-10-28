using Microsoft.Win32;
using System.Diagnostics;

namespace OllamaTM
{
    internal static class OllamaLocator
    {
        public static string? FindExecutable()
        {
            // 1. Запущенный процесс (если Ollama работает)
            try
            {
                foreach (var p in Process.GetProcessesByName("ollama"))
                {
                    try
                    {
                        var path = p.MainModule?.FileName;
                        if (!string.IsNullOrEmpty(path) && File.Exists(path))
                            return path;
                    }
                    catch { /* нет прав на чужой процесс */ }
                }
            }
            catch { }

            var candidates = new List<string>();

            // 2. Стандартные папки установки
            var pf   = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var lad  = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            if (!string.IsNullOrEmpty(pf))
                candidates.Add(Path.Combine(pf, "Ollama", "ollama.exe"));
            if (!string.IsNullOrEmpty(pf86))
                candidates.Add(Path.Combine(pf86, "Ollama", "ollama.exe"));
            if (!string.IsNullOrEmpty(lad))
                candidates.Add(Path.Combine(lad, "Programs", "Ollama", "ollama.exe"));

            // 3. Реестр (HKLM и HKCU)
            foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                try
                {
                    using var key = root.OpenSubKey(@"SOFTWARE\Ollama");
                    if (key?.GetValue("InstallLocation") is string loc && !string.IsNullOrEmpty(loc))
                        candidates.Add(Path.Combine(loc, "ollama.exe"));
                }
                catch { }
            }

            // 4. Переменные окружения (включая OllamaDir)
            foreach (var name in new[] { "OllamaDir", "OLLAMA_DIR", "OLLAMA_HOME", "OLLAMA_PATH" })
            {
                var dir = Environment.GetEnvironmentVariable(name);
                if (!string.IsNullOrEmpty(dir))
                    candidates.Add(Path.Combine(dir, "ollama.exe"));
            }

            // 5. PATH системы
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try { candidates.Add(Path.Combine(dir.Trim(), "ollama.exe")); } catch { }
            }

            // Проверяем все найденные пути
            foreach (var c in candidates)
            {
                try
                {
                    if (!string.IsNullOrEmpty(c) && File.Exists(c))
                        return c;
                }
                catch { }
            }

            return null;
        }
    }
}
