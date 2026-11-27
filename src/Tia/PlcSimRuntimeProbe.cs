using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace TiaOpennessMcp.Tia
{
    /// <summary>
    /// Diagnosis for the S7-PLCSIM Advanced runtime. This class must not reference any
    /// Siemens.Simatic.Simulation.Runtime type: it is the guard that turns a missing or unusable
    /// PLCSIM Advanced API into a clear result instead of a bare assembly-load error.
    /// </summary>
    public static class PlcSimRuntimeProbe
    {
        public const string ApiAssemblyName = "Siemens.Simatic.Simulation.Runtime.Api.x64";

        // The server is compiled against the PLCSIM Advanced API 8.0. Older API folders (5.0 is assembly
        // version 50.42.10.0) have different members and are never used.
        public static readonly Version MinimumApiVersion = new Version(8, 0);
        public static readonly Version PreferredApiVersion = new Version(8, 0);

        // Process names of standard S7-PLCSIM (V18 install; V21 names not verified). Used for hints only.
        public static readonly string[] StandardPlcSimProcessNames = { "S7-PLCSIM", "plcsim", "Siemens.Simatic.PlcSim.VplcHost" };

        public const string SupportNote =
            "Only S7-PLCSIM Advanced V8.0+ is supported (S7-1500 / ET 200SP virtual controllers). " +
            "Standard S7-PLCSIM (V18-V21), and therefore S7-1200 simulation, has no public API and is not supported.";

        public static string DefaultApiRoot
        {
            get
            {
                string commonX86 = Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86);
                if (string.IsNullOrEmpty(commonX86)) commonX86 = @"C:\Program Files (x86)\Common Files";
                return Path.Combine(commonX86, "Siemens", "PLCSIMADV", "API");
            }
        }

        /// <summary>
        /// Selects the PLCSIM Advanced API folder: API\8.0 when present, otherwise the highest numeric
        /// API\&lt;version&gt; folder at or above 8.0 that contains the API assembly; null when none does.
        /// </summary>
        public static PlcSimApiLocation? FindApiAssembly(string? apiRoot = null)
        {
            string root = apiRoot ?? DefaultApiRoot;
            string[] directories;
            try
            {
                if (!Directory.Exists(root)) return null;
                directories = Directory.GetDirectories(root);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                return null;
            }

            var candidates = new List<PlcSimApiLocation>();
            foreach (string directory in directories)
            {
                Version? version = ParseFolderVersion(Path.GetFileName(directory));
                if (version == null || version < MinimumApiVersion) continue;
                string dll = Path.Combine(directory, ApiAssemblyName + ".dll");
                if (!File.Exists(dll)) continue;
                candidates.Add(new PlcSimApiLocation { Directory = directory, Path = dll, FolderVersion = version.ToString() });
            }
            if (candidates.Count == 0) return null;

            return candidates.FirstOrDefault(c => new Version(c.FolderVersion) == PreferredApiVersion)
                ?? candidates.OrderByDescending(c => new Version(c.FolderVersion)).First();
        }

        private static Version? ParseFolderVersion(string name)
        {
            if (Version.TryParse(name, out Version? version)) return version;
            if (int.TryParse(name, out int major) && major >= 0) return new Version(major, 0);
            return null;
        }

        /// <summary>Running standard S7-PLCSIM processes (name and PID). Never throws.</summary>
        public static List<PlcSimProcessInfo> DetectStandardPlcSimProcesses()
        {
            var found = new List<PlcSimProcessInfo>();
            foreach (string name in StandardPlcSimProcessNames)
            {
                Process[] processes;
                try { processes = Process.GetProcessesByName(name); }
                catch { continue; }
                foreach (Process process in processes)
                {
                    try { found.Add(new PlcSimProcessInfo { Name = name, ProcessId = process.Id }); }
                    catch { }
                    finally { process.Dispose(); }
                }
            }
            return found;
        }

        /// <summary>The user-facing explanation for a plcsim_list_instances result.</summary>
        public static string BuildDiagnosis(bool runtimeAvailable, string? runtimeError, int instanceCount,
            IReadOnlyCollection<PlcSimProcessInfo> standardProcesses)
        {
            var parts = new List<string>();
            if (!runtimeAvailable)
            {
                parts.Add("S7-PLCSIM Advanced V8.0+ runtime not available" +
                    (string.IsNullOrWhiteSpace(runtimeError) ? "." : ": " + runtimeError!.Trim().TrimEnd('.') + "."));
                parts.Add(SupportNote);
            }
            else if (instanceCount == 0)
            {
                parts.Add("The S7-PLCSIM Advanced runtime is available, but no instances are registered. " +
                    "Start a virtual controller (S7-1500 / ET 200SP) in S7-PLCSIM Advanced.");
            }
            else
            {
                parts.Add($"Found {instanceCount} S7-PLCSIM Advanced instance(s).");
            }

            string? standard = DescribeStandardPlcSim(standardProcesses);
            if (standard != null)
            {
                if (runtimeAvailable && instanceCount == 0) parts.Add(SupportNote);
                parts.Add(standard);
            }
            return string.Join(" ", parts);
        }

        /// <summary>Text appended to the message of a failed plcsim connect.</summary>
        public static string BuildConnectHint(IReadOnlyCollection<PlcSimProcessInfo> standardProcesses)
        {
            string hint = SupportNote + " Call plcsim_list_instances to see the runtime state and the registered instances.";
            string? standard = DescribeStandardPlcSim(standardProcesses);
            return standard == null ? hint : hint + " " + standard;
        }

        private static string? DescribeStandardPlcSim(IReadOnlyCollection<PlcSimProcessInfo> standardProcesses)
        {
            if (standardProcesses == null || standardProcesses.Count == 0) return null;
            string list = string.Join(", ", standardProcesses.Select(p => $"{p.Name} (PID {p.ProcessId})"));
            return $"Standard S7-PLCSIM is running ({list}); it has no public API, so its instances cannot be listed or controlled. " +
                "If that S7-PLCSIM instance has TCP/IP communication enabled, plc_online_connect to its IP address may read tags " +
                "(read-only; not verified against standard S7-PLCSIM).";
        }

        /// <summary>plcsim_list_instances: never throws, also when the PLCSIM Advanced API cannot be loaded.</summary>
        public static PlcSimListResult ListInstancesSafe()
        {
            return BuildListResult(QueryManagerInstances, DetectStandardPlcSimProcesses());
        }

        /// <summary>Testable core of ListInstancesSafe: runs the query and fills the diagnosis.</summary>
        public static PlcSimListResult BuildListResult(Func<PlcSimListResult> query, List<PlcSimProcessInfo> standardProcesses)
        {
            PlcSimListResult result;
            try
            {
                result = query() ?? new PlcSimListResult { RuntimeAvailable = false, Error = "No result from the PLCSIM Advanced runtime." };
            }
            catch (Exception ex)
            {
                result = new PlcSimListResult { RuntimeAvailable = false, Error = DescribeException(ex) };
            }

            if (result.Instances == null) result.Instances = new List<PlcSimInstanceSummary>();
            result.StandardPlcSimProcesses = standardProcesses ?? new List<PlcSimProcessInfo>();

            Assembly? loaded = FindLoadedApiAssembly();
            if (loaded != null)
            {
                result.ApiPath = SafeLocation(loaded);
                result.ApiVersion = ApiFolderVersionOf(result.ApiPath);
                result.ApiAssemblyVersion = loaded.GetName().Version?.ToString();
            }
            else
            {
                PlcSimApiLocation? candidate = SafeFindApiAssembly();
                result.ApiPath = candidate?.Path;
                result.ApiVersion = candidate?.FolderVersion;
            }

            result.Message = BuildDiagnosis(result.RuntimeAvailable, result.Error, result.Instances.Count, result.StandardPlcSimProcesses);
            return result;
        }

        /// <summary>plcsim_connect: never throws; a failure carries the diagnosis.</summary>
        public static PlcSimConnectResult ConnectSafe(string instanceName)
        {
            try
            {
                return ConnectManager(instanceName);
            }
            catch (Exception ex)
            {
                return new PlcSimConnectResult
                {
                    Success = false,
                    InstanceName = instanceName,
                    Message = $"Failed to connect to PLCSIM Advanced instance '{instanceName}': {DescribeException(ex)} " +
                        BuildConnectHint(DetectStandardPlcSimProcesses())
                };
            }
        }

        /// <summary>
        /// The API folder version ("8.0") of an API assembly path inside a PLCSIMADV\API\&lt;version&gt; folder;
        /// null for any other location (for example a build reference copy).
        /// </summary>
        public static string? ApiFolderVersionOf(string? assemblyPath)
        {
            if (string.IsNullOrEmpty(assemblyPath)) return null;
            try
            {
                string? directory = Path.GetDirectoryName(assemblyPath);
                if (string.IsNullOrEmpty(directory)) return null;
                string? parent = Path.GetFileName(Path.GetDirectoryName(directory));
                if (!string.Equals(parent, "API", StringComparison.OrdinalIgnoreCase)) return null;
                return ParseFolderVersion(Path.GetFileName(directory))?.ToString();
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        /// <summary>Unwraps type-initialization and reflection wrappers and names a missing API assembly.</summary>
        public static string DescribeException(Exception ex)
        {
            Exception current = Unwrap(ex);
            if (IsApiLoadFailure(current))
            {
                return $"the S7-PLCSIM Advanced API assembly '{ApiAssemblyName}' (API 8.0 or newer) could not be loaded " +
                    $"({current.GetType().Name}: {current.Message.Trim()}). Install S7-PLCSIM Advanced V8.0 or newer";
            }
            return $"{current.GetType().Name}: {current.Message.Trim()}";
        }

        /// <summary>True when the exception (after unwrapping) is a failure to load the PLCSIM Advanced API assembly.</summary>
        public static bool IsApiLoadFailure(Exception ex)
        {
            Exception current = Unwrap(ex);
            if (!(current is FileNotFoundException || current is FileLoadException || current is BadImageFormatException)) return false;
            string? file = (current as FileNotFoundException)?.FileName ?? (current as FileLoadException)?.FileName
                ?? (current as BadImageFormatException)?.FileName;
            return file != null && file.IndexOf(ApiAssemblyName, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Error text for a failed plcsim_* tool call. A missing PLCSIM Advanced API gets the full diagnosis
        /// and the support hint; any other failure keeps its plain message.
        /// </summary>
        public static string DescribeToolFailure(Exception ex)
        {
            if (!IsApiLoadFailure(ex)) return ex.Message;
            return DescribeException(ex) + ". " + BuildConnectHint(DetectStandardPlcSimProcesses());
        }

        private static Exception Unwrap(Exception ex)
        {
            Exception current = ex;
            while ((current is TypeInitializationException || current is TargetInvocationException) && current.InnerException != null)
                current = current.InnerException;
            return current;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static PlcSimListResult QueryManagerInstances() => PlcSimManager.Instance.ListInstances();

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static PlcSimConnectResult ConnectManager(string instanceName) => PlcSimManager.Instance.Connect(instanceName);

        private static Assembly? FindLoadedApiAssembly()
        {
            try
            {
                return AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, ApiAssemblyName, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return null;
            }
        }

        private static string? SafeLocation(Assembly assembly)
        {
            try { return string.IsNullOrEmpty(assembly.Location) ? null : assembly.Location; }
            catch { return null; }
        }

        private static PlcSimApiLocation? SafeFindApiAssembly()
        {
            try { return FindApiAssembly(); }
            catch { return null; }
        }
    }

    public class PlcSimApiLocation
    {
        public string Directory { get; set; } = "";
        public string Path { get; set; } = "";
        public string FolderVersion { get; set; } = "";
    }

    public class PlcSimProcessInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("processId")]
        public int ProcessId { get; set; }
    }

    public class PlcSimListResult
    {
        [JsonPropertyName("runtimeAvailable")]
        public bool RuntimeAvailable { get; set; }

        [JsonPropertyName("apiPath")]
        public string? ApiPath { get; set; }

        /// <summary>Version of the PLCSIMADV\API folder ("8.0"); null when the API sits elsewhere or is not found.</summary>
        [JsonPropertyName("apiVersion")]
        public string? ApiVersion { get; set; }

        /// <summary>Assembly version of the loaded API ("8.0.0.0"); null until the API is loaded.</summary>
        [JsonPropertyName("apiAssemblyVersion")]
        public string? ApiAssemblyVersion { get; set; }

        [JsonPropertyName("runtimeManagerAvailable")]
        public bool? RuntimeManagerAvailable { get; set; }

        [JsonPropertyName("runtimeVersion")]
        public uint? RuntimeVersion { get; set; }

        [JsonPropertyName("instances")]
        public List<PlcSimInstanceSummary> Instances { get; set; } = new List<PlcSimInstanceSummary>();

        [JsonPropertyName("standardPlcSimProcesses")]
        public List<PlcSimProcessInfo> StandardPlcSimProcesses { get; set; } = new List<PlcSimProcessInfo>();

        [JsonPropertyName("message")]
        public string Message { get; set; } = "";

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }
}
