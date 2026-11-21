using System;
using System.Collections.Generic;
using KabuUnityMcp.Editor.Handlers.GameObjects;
using UnityEditor;
using UnityEngine;

namespace KabuUnityMcp.Editor.Handlers.Components
{
    /// <summary>Reads serialized state without evaluating arbitrary component property getters.</summary>
    internal static class ComponentReadOps
    {
        public static HandlerResult Read(int? instanceId, string path, string name, string componentType,
            int componentIndex, string propertyPath, int offset, int limit, int maxDepth)
        {
            var go = GameObjectLookup.Find(instanceId, path, name, out var error);
            if (!go)
            {
                return HandlerResult.Fail(error, -32602);
            }
            var type = ComponentTypeResolver.Resolve(componentType, out error);
            if (type == null)
            {
                return HandlerResult.Fail(error, -32602);
            }
            var components = go.GetComponents(type);
            if (componentIndex < 0 || componentIndex >= components.Length || offset < 0 || limit < 1 || limit > 200 || maxDepth < 0 || maxDepth > 8)
            {
                return HandlerResult.Fail("Invalid componentIndex/offset/limit(1..200)/maxDepth(0..8).", -32602);
            }
            var component = components[componentIndex];
            using (var serialized = new SerializedObject(component))
            {
                serialized.UpdateIfRequiredOrScript();
                var iterator = string.IsNullOrEmpty(propertyPath) ? serialized.GetIterator() : serialized.FindProperty(propertyPath);
                if (iterator == null)
                {
                    return HandlerResult.Fail("Serialized property not found: " + propertyPath, -32602);
                }
                var items = new List<object>();
                var matched = 0;
                var visited = 0;
                var selected = !string.IsNullOrEmpty(propertyPath);
                var rootDepth = selected ? iterator.depth : -1;
                var hasCurrent = selected || iterator.Next(true);
                while (hasCurrent && visited++ < 10000)
                {
                    if (selected && iterator.depth < rootDepth)
                    {
                        break;
                    }
                    if (selected && iterator.propertyPath != propertyPath && !iterator.propertyPath.StartsWith(propertyPath + ".", StringComparison.Ordinal))
                    {
                        break;
                    }
                    if (matched++ >= offset)
                    {
                        items.Add(new { path = iterator.propertyPath, type = iterator.type, depth = iterator.depth,
                            isArray = iterator.isArray, arraySize = iterator.isArray ? iterator.arraySize : (int?)null,
                            valueTruncated = iterator.propertyType == SerializedPropertyType.String && iterator.stringValue.Length > 4096,
                            value = Value(iterator) });
                        if (items.Count > limit)
                        {
                            break;
                        }
                    }
                    hasCurrent = iterator.Next(iterator.depth - rootDepth < maxDepth);
                }
                var more = items.Count > limit;
                if (more)
                {
                    items.RemoveAt(items.Count - 1);
                }
                return HandlerResult.Success(new { schema = "kabu.component.read/v1", instanceId = go.GetInstanceID(),
                    componentType = type.FullName, componentIndex, offset, limit, properties = items,
                    nextOffset = more ? offset + items.Count : (int?)null, scanLimitReached = visited >= 10000 });
            }
        }

        private static object Value(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer: return property.longValue;
                case SerializedPropertyType.Boolean: return property.boolValue;
                case SerializedPropertyType.Float: return property.doubleValue;
                case SerializedPropertyType.String: return property.stringValue.Substring(0, Math.Min(property.stringValue.Length, 4096));
                case SerializedPropertyType.Enum: return new { index = property.enumValueIndex, names = property.enumNames };
                case SerializedPropertyType.Vector2: return new { x = property.vector2Value.x, y = property.vector2Value.y };
                case SerializedPropertyType.Vector3: return new { x = property.vector3Value.x, y = property.vector3Value.y, z = property.vector3Value.z };
                case SerializedPropertyType.Vector4: return new { x = property.vector4Value.x, y = property.vector4Value.y, z = property.vector4Value.z, w = property.vector4Value.w };
                case SerializedPropertyType.Quaternion: return new { x = property.quaternionValue.x, y = property.quaternionValue.y, z = property.quaternionValue.z, w = property.quaternionValue.w };
                case SerializedPropertyType.Color: return new { r = property.colorValue.r, g = property.colorValue.g, b = property.colorValue.b, a = property.colorValue.a };
                case SerializedPropertyType.ObjectReference:
                    var reference = property.objectReferenceValue;
                    return reference ? new { name = reference.name, instanceId = reference.GetInstanceID(), type = reference.GetType().FullName,
                        assetPath = AssetDatabase.GetAssetPath(reference) } : (object)null;
                default: return null; // Containers are represented by their child paths; unsupported leaves remain explicitly typed.
            }
        }
    }
}
