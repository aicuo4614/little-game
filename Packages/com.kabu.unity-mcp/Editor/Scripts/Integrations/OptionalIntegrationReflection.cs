using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using KabuUnityMcp.Editor.Handlers.GameObjects;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace KabuUnityMcp.Editor.Integrations
{
    /// <summary>Only adapter-owned member names are reflected; no arbitrary method execution is exposed.</summary>
    internal static class OptionalIntegrationReflection
    {
        public static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName, false);
                if (type != null)
                {
                    return type;
                }
            }
            return null;
        }

        public static object Get(object target, string name)
        {
            if (target == null)
            {
                return null;
            }
            var type = target.GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            return type.GetProperty(name, flags)?.GetValue(target) ?? type.GetField(name, flags)?.GetValue(target);
        }

        public static object Named(object target) => Get(target, "Name");

        public static IEnumerable<object> Items(object target)
        {
            if (target is IEnumerable enumerable)
            {
                var count = 0;
                foreach (var item in enumerable)
                {
                    if (count++ >= 10000)
                    {
                        throw new ArgumentException("Collection exceeds 10000 entries; query a smaller target.");
                    }
                    yield return item;
                }
            }
        }

        public static GameObject Target(JObject args)
        {
            var go = GameObjectLookup.Find(args["instanceId"]?.Value<int?>(), args["path"]?.Value<string>(), args["name"]?.Value<string>(), out var error);
            if (!go)
            {
                throw new ArgumentException(error);
            }
            return go;
        }

        public static int Limit(JObject args)
        {
            var limit = args["limit"]?.Value<int>() ?? 50;
            var offset = args["offset"]?.Value<int>() ?? 0;
            if (limit < 1 || limit > 200 || offset < 0 || offset > 10000)
            {
                throw new ArgumentException("limit must be 1..200 and offset 0..10000.");
            }
            return limit;
        }

        public static JObject TargetSchema(params string[] extra)
        {
            var properties = new JObject
            {
                ["instanceId"] = new JObject { ["type"] = "integer" },
                ["path"] = new JObject { ["type"] = "string" },
                ["name"] = new JObject { ["type"] = "string" },
                ["componentIndex"] = new JObject { ["type"] = "integer" },
                ["offset"] = new JObject { ["type"] = "integer" },
                ["limit"] = new JObject { ["type"] = "integer" }
            };
            foreach (var field in extra)
            {
                properties[field] = new JObject { ["type"] = "string" };
            }
            return new JObject { ["type"] = "object", ["properties"] = properties, ["additionalProperties"] = false };
        }
    }
}
