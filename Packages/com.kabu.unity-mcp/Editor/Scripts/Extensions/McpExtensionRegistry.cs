using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace KabuUnityMcp.Editor.Extensions
{
    /// <summary>Main-thread registry. Projects register on load and unregister before assembly reload.</summary>
    public static class McpExtensionRegistry
    {
        private static readonly Dictionary<string, IMcpExtension> Extensions = new Dictionary<string, IMcpExtension>(StringComparer.Ordinal);

        public static void Register(IMcpExtension extension)
        {
            if (extension == null || string.IsNullOrWhiteSpace(extension.Id))
            {
                throw new ArgumentException("Extension and stable Id are required.");
            }
            if (Extensions.TryGetValue(extension.Id, out var current) && !ReferenceEquals(current, extension))
            {
                throw new InvalidOperationException("Extension already registered: " + extension.Id);
            }
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var operation in extension.Operations)
            {
                if (string.IsNullOrWhiteSpace(operation.Name) || !names.Add(operation.Name))
                {
                    throw new ArgumentException("Operation names must be unique and nonempty.");
                }
            }
            Extensions[extension.Id] = extension;
        }

        public static void Unregister(IMcpExtension extension)
        {
            if (extension != null && Extensions.TryGetValue(extension.Id, out var current) && ReferenceEquals(current, extension))
            {
                Extensions.Remove(extension.Id);
            }
        }

        public static object Catalog(string id = null)
        {
            return Extensions.Values.Where(item => string.IsNullOrEmpty(id) || item.Id == id)
                .OrderBy(item => item.Id, StringComparer.Ordinal).Select(item => new
                {
                    id = item.Id, version = item.Version, description = item.Description,
                    available = string.IsNullOrEmpty(item.UnavailableReason), unavailableReason = item.UnavailableReason,
                    operations = item.Operations.Select(operation => new
                    {
                        name = operation.Name, description = operation.Description, readOnly = operation.ReadOnly,
                        inputSchema = operation.InputSchema.DeepClone()
                    }).ToArray()
                }).ToArray();
        }

        public static McpHostResult Execute(string id, string operation, JObject arguments, bool allowWrite)
        {
            if (id == null || !Extensions.TryGetValue(id, out var extension))
            {
                return McpHostResult.Fail("Extension not registered: " + id, -32602);
            }
            if (!string.IsNullOrEmpty(extension.UnavailableReason))
            {
                return McpHostResult.Fail(extension.UnavailableReason);
            }
            var descriptor = extension.Operations.SingleOrDefault(item => item.Name == operation);
            if (descriptor == null)
            {
                return McpHostResult.Fail("Unknown operation. Query unity_extension_catalog first.", -32602);
            }
            if (!descriptor.ReadOnly && !allowWrite)
            {
                return McpHostResult.Fail("This operation changes state; set allowWrite=true explicitly.", -32602);
            }
            try
            {
                arguments = arguments ?? new JObject();
                ValidateArguments(descriptor.InputSchema, arguments);
                return extension.Execute(operation, (JObject)arguments.DeepClone());
            }
            catch (ArgumentException exception)
            {
                return McpHostResult.Fail(exception.Message, -32602);
            }
            catch (Exception exception)
            {
                return McpHostResult.Fail(exception.GetBaseException().Message);
            }
        }

        // Transport validates top-level required/type/enum/additionalProperties. Adapters validate domain constraints.
        private static void ValidateArguments(JObject schema, JObject arguments)
        {
            foreach (var name in schema["required"]?.Values<string>() ?? Enumerable.Empty<string>())
            {
                if (arguments[name] == null || arguments[name].Type == JTokenType.Null)
                {
                    throw new ArgumentException("Required argument: " + name);
                }
            }
            var properties = schema["properties"] as JObject ?? new JObject();
            foreach (var property in arguments.Properties())
            {
                var field = properties[property.Name];
                if (field == null)
                {
                    if (schema["additionalProperties"]?.Value<bool>() == false)
                    {
                        throw new ArgumentException("Unknown argument: " + property.Name);
                    }
                    continue;
                }
                var type = field["type"]?.Value<string>();
                var actual = property.Value.Type;
                var valid = type == null || (type == "string" && actual == JTokenType.String)
                    || (type == "integer" && actual == JTokenType.Integer)
                    || (type == "number" && (actual == JTokenType.Integer || actual == JTokenType.Float))
                    || (type == "boolean" && actual == JTokenType.Boolean)
                    || (type == "object" && actual == JTokenType.Object)
                    || (type == "array" && actual == JTokenType.Array);
                if (!valid || (field["enum"] is JArray choices && !choices.Any(value => JToken.DeepEquals(value, property.Value))))
                {
                    throw new ArgumentException("Invalid argument: " + property.Name);
                }
            }
        }
    }
}
