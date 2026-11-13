using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;

namespace TiaOpennessMcp.Protocol
{
    public static class McpResourceCatalog
    {
        private static readonly Dictionary<string, ResourceEntry> Entries =
            new Dictionary<string, ResourceEntry>(StringComparer.Ordinal)
            {
                ["tia://docs/tools-reference"] = new ResourceEntry(
                    "tools-reference",
                    "Detailed MCP tool argument and return-value reference.",
                    "TiaOpennessMcp.Resources.ToolsReference.md",
                    "text/markdown"),
                ["tia://docs/skills-and-tools"] = new ResourceEntry(
                    "skills-and-tools",
                    "Overview of the TIA MCP server, its tools, and agent skills.",
                    "TiaOpennessMcp.Resources.SkillsAndTools.md",
                    "text/markdown"),
                ["tia://docs/agent-prompting-and-system-functions"] = new ResourceEntry(
                    "agent-prompting-and-system-functions",
                    "Prompting guidance and WinCC Unified runtime system-function reference.",
                    "TiaOpennessMcp.Resources.AgentPromptingAndSystemFunctions.md",
                    "text/markdown"),
                ["tia://docs/hmi-javascript-type-definitions"] = new ResourceEntry(
                    "hmi-javascript-type-definitions",
                    "Conservative TypeScript declarations for runtime APIs documented by this server; not the complete Siemens V21 API.",
                    "TiaOpennessMcp.Resources.HmiJavaScriptTypeDefinitions.d.ts",
                    "text/typescript")
            };

        public static ResourcesListResult List()
        {
            var resources = new List<ResourceDefinition>();
            foreach (KeyValuePair<string, ResourceEntry> entry in Entries)
            {
                resources.Add(new ResourceDefinition
                {
                    Uri = entry.Key,
                    Name = entry.Value.Name,
                    Description = entry.Value.Description,
                    MimeType = entry.Value.MimeType
                });
            }

            return new ResourcesListResult { Resources = resources };
        }

        public static ReadResourceResult Read(string uri)
        {
            if (!Entries.TryGetValue(uri, out ResourceEntry? entry))
                throw new KeyNotFoundException($"Unknown MCP resource URI '{uri}'.");

            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(entry.ManifestName)
                ?? throw new InvalidOperationException(
                    $"Embedded MCP resource '{entry.ManifestName}' was not found."))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                return new ReadResourceResult
                {
                    Contents = new List<ResourceContent>
                    {
                        new ResourceContent
                        {
                            Uri = uri,
                            MimeType = entry.MimeType,
                            Text = reader.ReadToEnd()
                        }
                    }
                };
            }
        }

        private sealed class ResourceEntry
        {
            public ResourceEntry(string name, string description, string manifestName, string mimeType)
            {
                Name = name;
                Description = description;
                ManifestName = manifestName;
                MimeType = mimeType;
            }

            public string Name { get; }
            public string Description { get; }
            public string ManifestName { get; }
            public string MimeType { get; }
        }
    }

    public sealed class ResourcesListResult
    {
        [JsonPropertyName("resources")]
        public List<ResourceDefinition> Resources { get; set; } = new List<ResourceDefinition>();
    }

    public sealed class ResourceDefinition
    {
        [JsonPropertyName("uri")]
        public string Uri { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("mimeType")]
        public string MimeType { get; set; } = "";
    }

    public sealed class ReadResourceResult
    {
        [JsonPropertyName("contents")]
        public List<ResourceContent> Contents { get; set; } = new List<ResourceContent>();
    }

    public sealed class ResourceContent
    {
        [JsonPropertyName("uri")]
        public string Uri { get; set; } = "";

        [JsonPropertyName("mimeType")]
        public string MimeType { get; set; } = "";

        [JsonPropertyName("text")]
        public string Text { get; set; } = "";
    }
}
