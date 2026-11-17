using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TiaOpennessMcp.Config
{
    public class ServerConfig
    {
        public ServerSettings Server { get; set; } = new ServerSettings();
        public TiaSettings Tia { get; set; } = new TiaSettings();
        public DocsSettings Docs { get; set; } = new DocsSettings();

        public static ServerConfig Load(string? configPath = null)
        {
            string path = ResolveConfigPath(configPath);
            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    var cfg = JsonSerializer.Deserialize<ServerConfig>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        ReadCommentHandling = JsonCommentHandling.Skip
                    });
                    if (cfg != null)
                    {
                        Console.Error.WriteLine($"[ServerConfig] Loaded configuration from '{path}'.");
                        return cfg;
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[ServerConfig] Warning: Failed to parse '{path}': {ex.Message}. Using defaults.");
                }
            }

            var defaultCfg = new ServerConfig();
            try
            {
                // Write default config file for easy editing
                string json = JsonSerializer.Serialize(defaultCfg, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
                Console.Error.WriteLine($"[ServerConfig] Created default configuration file at '{path}'.");
            }
            catch { }

            return defaultCfg;
        }

        private static string ResolveConfigPath(string? explicitPath)
        {
            if (!string.IsNullOrEmpty(explicitPath) && File.Exists(explicitPath))
                return Path.GetFullPath(explicitPath);

            string localDir = AppDomain.CurrentDomain.BaseDirectory;
            string localConfig = Path.Combine(localDir, "config.json");
            if (File.Exists(localConfig))
                return localConfig;

            string cwdConfig = Path.Combine(Directory.GetCurrentDirectory(), "config.json");
            if (File.Exists(cwdConfig))
                return cwdConfig;

            return localConfig;
        }
    }

    public class ServerSettings
    {
        public string Host { get; set; } = "http://localhost:5001/";
        public int Port { get; set; } = 5001;
        public string Mode { get; set; } = "HttpAndStdio"; // Http, Stdio, or HttpAndStdio
        public bool EnableCors { get; set; } = true;
        public bool RequireBearerToken { get; set; } = true;
        public bool LogHttpRequestDetails { get; set; } = false;
    }

    public class TiaSettings
    {
        /// <summary>Obsolete and ignored: the server only attaches through tia_connect with an explicit process_id.</summary>
        public bool AutoConnect { get; set; } = false;
        /// <summary>Obsolete and ignored: pass process_id to tia_connect instead.</summary>
        public int? PreferredProcessId { get; set; } = null;
        public bool KeepAliveConnection { get; set; } = true;
        public string ApiPath { get; set; } = @"C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48";
    }

    /// <summary>Settings for the docs_* tools that read the online Siemens documentation.</summary>
    public class DocsSettings
    {
        public static readonly string[] DefaultHosts = { "docs.tia.siemens.cloud", "docs.industrial-operations-x.siemens.cloud" };

        /// <summary>Extra Fluid Topics hosts, added after the two built-in Siemens hosts.</summary>
        public List<string> ExtraHosts { get; set; } = new List<string>();
        /// <summary>Default locale for search and publication listings.</summary>
        public string Locale { get; set; } = "en-US";
        /// <summary>Minimum seconds between requests to one host.</summary>
        public double MinIntervalSeconds { get; set; } = 0.3;

        /// <summary>The built-in hosts first, then the extra ones, lower-cased and without duplicates.</summary>
        public List<string> AllHosts()
        {
            var result = new List<string>();
            foreach (var h in DefaultHosts.Concat(ExtraHosts ?? new List<string>()))
            {
                string host = (h ?? "").Trim().ToLowerInvariant();
                if (host.Length > 0 && !result.Contains(host)) result.Add(host);
            }
            return result;
        }

        /// <summary>
        /// A copy with SIEMENS_DOCS_HOSTS (comma-separated extra hosts), SIEMENS_DOCS_LOCALE and
        /// SIEMENS_DOCS_MIN_INTERVAL applied on top, the same variables the Python siemens-docs-mcp reads.
        /// </summary>
        public DocsSettings WithEnvironment(Func<string, string?> getEnv)
        {
            var copy = new DocsSettings
            {
                ExtraHosts = new List<string>(ExtraHosts ?? new List<string>()),
                Locale = string.IsNullOrWhiteSpace(Locale) ? "en-US" : Locale,
                MinIntervalSeconds = MinIntervalSeconds
            };
            string? hosts = getEnv("SIEMENS_DOCS_HOSTS");
            if (!string.IsNullOrWhiteSpace(hosts)) copy.ExtraHosts.AddRange(hosts!.Split(','));
            string? locale = getEnv("SIEMENS_DOCS_LOCALE");
            if (!string.IsNullOrWhiteSpace(locale)) copy.Locale = locale!.Trim();
            string? interval = getEnv("SIEMENS_DOCS_MIN_INTERVAL");
            if (!string.IsNullOrWhiteSpace(interval))
            {
                if (!double.TryParse(interval, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value)
                    || double.IsNaN(value) || double.IsInfinity(value) || value < 0)
                    throw new ArgumentException($"SIEMENS_DOCS_MIN_INTERVAL must be a number >= 0, got '{interval}'");
                copy.MinIntervalSeconds = value;
            }
            if (copy.MinIntervalSeconds < 0 || double.IsNaN(copy.MinIntervalSeconds)) copy.MinIntervalSeconds = 0.3;
            return copy;
        }
    }
}
