using System;
using System.Globalization;
using System.Reflection;
using KabuUnityMcp.Editor.Handlers;
using KabuUnityMcp.Editor.Handlers.GameObjects;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KabuUnityMcp.Editor.Handlers.Components
{
    /// <summary>组件增删改业务逻辑（无 WS）。</summary>
    internal static class ComponentOps
    {
        public static HandlerResult Add(int? instanceId, string path, string name, string componentType)
        {
            var go = GameObjectLookup.Find(instanceId, path, name, out var error);
            if (error != null)
                return HandlerResult.Fail(error);

            var type = ComponentTypeResolver.Resolve(componentType, out var typeError);
            if (typeError != null)
                return HandlerResult.Fail(typeError, -32602);

            if (go.GetComponent(type) != null)
                return HandlerResult.Fail($"物体已有组件 {type.Name}。");

            Undo.AddComponent(go, type);
            MarkDirty(go);
            return HandlerResult.Success(new
            {
                componentType = type.FullName,
                shortName = type.Name,
                gameObject = GameObjectLookup.ToInfo(go, includeComponents: true),
                message = $"已添加组件 {type.Name}"
            });
        }

        public static HandlerResult Remove(int? instanceId, string path, string name, string componentType)
        {
            var go = GameObjectLookup.Find(instanceId, path, name, out var error);
            if (error != null)
                return HandlerResult.Fail(error);

            var type = ComponentTypeResolver.Resolve(componentType, out var typeError);
            if (typeError != null)
                return HandlerResult.Fail(typeError, -32602);

            if (type == typeof(Transform))
                return HandlerResult.Fail("不能移除 Transform。", -32602);

            var component = go.GetComponent(type);
            if (component == null)
                return HandlerResult.Fail($"物体上没有组件 {type.Name}。");

            Undo.DestroyObjectImmediate(component);
            MarkDirty(go);
            return HandlerResult.Success(new
            {
                removed = type.Name,
                gameObject = GameObjectLookup.ToInfo(go, includeComponents: true),
                message = $"已移除组件 {type.Name}"
            });
        }

        public static HandlerResult SetField(
            int? instanceId,
            string path,
            string name,
            string componentType,
            string fieldName,
            string value,
            string valueType)
        {
            var go = GameObjectLookup.Find(instanceId, path, name, out var error);
            if (error != null)
                return HandlerResult.Fail(error);

            if (string.IsNullOrWhiteSpace(fieldName))
                return HandlerResult.Fail("fieldName 不能为空。", -32602);

            var type = ComponentTypeResolver.Resolve(componentType, out var typeError);
            if (typeError != null)
                return HandlerResult.Fail(typeError, -32602);

            var component = go.GetComponent(type);
            if (component == null)
                return HandlerResult.Fail($"物体上没有组件 {type.Name}。");

            fieldName = fieldName.Trim();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            var field = type.GetField(fieldName, flags);
            var property = type.GetProperty(fieldName, flags);

            try
            {
                if (field != null)
                {
                    var converted = ConvertValue(value, valueType, field.FieldType);
                    Undo.RecordObject(component, $"MCP Set {type.Name}.{fieldName}");
                    field.SetValue(component, converted);
                }
                else if (property != null && property.CanWrite)
                {
                    var converted = ConvertValue(value, valueType, property.PropertyType);
                    Undo.RecordObject(component, $"MCP Set {type.Name}.{fieldName}");
                    property.SetValue(component, converted);
                }
                else
                {
                    return HandlerResult.Fail(
                        $"在 {type.Name} 上找不到可写的公共字段/属性「{fieldName}」。", -32602);
                }
            }
            catch (Exception ex)
            {
                return HandlerResult.Fail($"赋值失败：{ex.Message}", -32602);
            }

            EditorUtility.SetDirty(component);
            MarkDirty(go);
            return HandlerResult.Success(new
            {
                componentType = type.Name,
                fieldName,
                value,
                valueType = string.IsNullOrWhiteSpace(valueType) ? "auto" : valueType.Trim(),
                message = $"已设置 {type.Name}.{fieldName}"
            });
        }

        internal static object ConvertValue(string value, string valueType, Type targetType)
        {
            if (targetType == typeof(string))
                return value;

            var kind = string.IsNullOrWhiteSpace(valueType) ? "auto" : valueType.Trim().ToLowerInvariant();

            if (kind == "auto")
            {
                if (targetType == typeof(int) || targetType == typeof(int?))
                    kind = "int";
                else if (targetType == typeof(float) || targetType == typeof(float?))
                    kind = "float";
                else if (targetType == typeof(double) || targetType == typeof(double?))
                    kind = "float";
                else if (targetType == typeof(bool) || targetType == typeof(bool?))
                    kind = "bool";
                else if (targetType == typeof(Vector3))
                    kind = "vector3";
                else if (targetType == typeof(Vector2))
                    kind = "vector2";
                else if (targetType == typeof(Color))
                    kind = "color";
                else if (typeof(UnityEngine.Object).IsAssignableFrom(targetType))
                    kind = "asset";
                else
                    kind = "string";
            }

            object converted = kind switch
            {
                "int" => int.Parse(value, CultureInfo.InvariantCulture),
                "float" => float.Parse(value, CultureInfo.InvariantCulture),
                "bool" => bool.Parse(value),
                "vector2" => ParseVector2(value),
                "vector3" => ParseVector3(value),
                "color" => ParseColor(value),
                "asset" => LoadAsset(value, targetType),
                "gameobject" => FindGameObject(value),
                _ => value
            };

            if (converted != null && !targetType.IsInstanceOfType(converted))
            {
                if (targetType.IsEnum && converted is string s)
                    return Enum.Parse(targetType, s, true);
                throw new InvalidCastException(
                    $"无法将 {converted.GetType().Name} 赋给 {targetType.Name}（valueType={kind}）。");
            }

            return converted;
        }

        private static Vector2 ParseVector2(string value)
        {
            var parts = SplitNums(value, 2);
            return new Vector2(parts[0], parts[1]);
        }

        private static Vector3 ParseVector3(string value)
        {
            var parts = SplitNums(value, 3);
            return new Vector3(parts[0], parts[1], parts[2]);
        }

        private static Color ParseColor(string value)
        {
            if (ColorUtility.TryParseHtmlString(value.Trim(), out var c))
                return c;
            var parts = SplitNums(value, 3);
            var a = value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries).Length >= 4
                ? SplitNums(value, 4)[3]
                : 1f;
            return new Color(parts[0], parts[1], parts[2], a);
        }

        private static float[] SplitNums(string value, int count)
        {
            var parts = value.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < count)
                throw new FormatException($"需要 {count} 个数字，收到：{value}");
            var nums = new float[count];
            for (var i = 0; i < count; i++)
                nums[i] = float.Parse(parts[i], CultureInfo.InvariantCulture);
            return nums;
        }

        private static UnityEngine.Object LoadAsset(string path, Type targetType)
        {
            path = path?.Trim().Replace('\\', '/');
            if (string.IsNullOrEmpty(path))
                return null;
            var asset = AssetDatabase.LoadAssetAtPath(path, targetType);
            if (asset == null)
                throw new InvalidOperationException($"加载资源失败：{path}（期望类型 {targetType.Name}）");
            return asset;
        }

        private static GameObject FindGameObject(string id)
        {
            if (int.TryParse(id, out var instanceId))
            {
                var go = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
                if (go != null)
                    return go;
            }

            return GameObjectLookup.Find(null, id.Contains("/") ? id : null,
                id.Contains("/") ? null : id, out _);
        }

        private static void MarkDirty(GameObject go)
        {
            EditorUtility.SetDirty(go);
            if (go.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(go.scene);
        }
    }
}
