using Newtonsoft.Json.Linq;

namespace KabuUnityMcp.Editor.Extensions
{
    /// <summary>Optional integrations and project adapters implement this Editor-only contract.</summary>
    public interface IMcpExtension
    {
        string Id { get; }
        string Version { get; }
        string Description { get; }
        string UnavailableReason { get; }
        McpExtensionOperation[] Operations { get; }
        McpHostResult Execute(string operation, JObject arguments);
    }

    public sealed class McpExtensionOperation
    {
        public string Name { get; }
        public string Description { get; }
        public bool ReadOnly { get; }
        public JObject InputSchema { get; }

        public McpExtensionOperation(string name, string description, bool readOnly, JObject inputSchema)
        {
            Name = name;
            Description = description;
            ReadOnly = readOnly;
            InputSchema = (JObject)inputSchema.DeepClone();
        }
    }
}
