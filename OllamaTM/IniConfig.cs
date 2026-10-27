using System.Text;

namespace OllamaTM
{
    internal class IniConfig
    {
        private readonly Dictionary<string, Dictionary<string, string>> _data =
            new(StringComparer.OrdinalIgnoreCase);

        public static IniConfig Load(string path)
        {
            var cfg = new IniConfig();
            if (!File.Exists(path)) return cfg;

            string section = "";
            foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#"))
                    continue;

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    section = line[1..^1].Trim();
                    EnsureSection(cfg, section);
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                var key = line[..eq].Trim();
                var val = line[(eq + 1)..].Trim();
                EnsureSection(cfg, section)[key] = val;
            }
            return cfg;
        }

        private static Dictionary<string, string> EnsureSection(IniConfig cfg, string name)
        {
            if (!cfg._data.TryGetValue(name, out var s))
                cfg._data[name] = s = new(StringComparer.OrdinalIgnoreCase);
            return s;
        }

        public string? GetString(string section, string key, string? defaultValue)
            => _data.TryGetValue(section, out var s) && s.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)
                ? v : defaultValue;

        public int GetInt(string section, string key, int defaultValue)
            => int.TryParse(GetString(section, key, string.Empty), out var v) ? v : defaultValue;
    }
}