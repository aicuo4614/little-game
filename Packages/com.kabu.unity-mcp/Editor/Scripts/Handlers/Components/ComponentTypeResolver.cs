using System;
using System.Collections.Generic;
using UnityEngine;

namespace KabuUnityMcp.Editor.Handlers.Components
{
    /// <summary>按类型名解析 Component 类型（内置 / 自定义脚本）。</summary>
    internal static class ComponentTypeResolver
    {
        private static readonly Dictionary<string, Type> Cache = new Dictionary<string, Type>(StringComparer.Ordinal);

        public static Type Resolve(string typeName, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(typeName))
            {
                error = "componentType 不能为空。";
                return null;
            }

            typeName = typeName.Trim();
            if (Cache.TryGetValue(typeName, out var cached))
            {
                if (cached == null)
                    error = $"未找到组件类型：{typeName}";
                return cached;
            }

            Type found = null;

            // Common short names: Rigidbody, BoxCollider, …
            if (!typeName.Contains("."))
                found = Type.GetType($"UnityEngine.{typeName}, UnityEngine.CoreModule")
                        ?? Type.GetType($"UnityEngine.{typeName}, UnityEngine")
                        ?? Type.GetType($"UnityEngine.{typeName}, UnityEngine.PhysicsModule")
                        ?? Type.GetType($"UnityEngine.{typeName}, UnityEngine.UIModule")
                        ?? Type.GetType($"UnityEngine.UI.{typeName}, UnityEngine.UI");

            if (found == null)
                found = Type.GetType(typeName);

            if (found == null)
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        found = assembly.GetType(typeName);
                        if (found != null)
                            break;

                        if (!typeName.Contains("."))
                        {
                            foreach (var t in assembly.GetTypes())
                            {
                                if (t.Name == typeName && typeof(Component).IsAssignableFrom(t))
                                {
                                    found = t;
                                    break;
                                }
                            }
                            if (found != null)
                                break;
                        }
                    }
                    catch
                    {
                        // Dynamic / reflection-only assemblies
                    }
                }
            }

            if (found == null || !typeof(Component).IsAssignableFrom(found))
            {
                Cache[typeName] = null;
                error = $"未找到组件类型：{typeName}（需为 Component 子类，可用短名如 Rigidbody 或完整名）。";
                return null;
            }

            Cache[typeName] = found;
            return found;
        }
    }
}
