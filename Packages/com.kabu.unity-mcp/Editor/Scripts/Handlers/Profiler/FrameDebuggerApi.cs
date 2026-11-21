using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace KabuUnityMcp.Editor.Handlers.Profiler
{
    /// <summary>
    /// Reflection binding for Unity internal Frame Debugger APIs (2022.3 / Unity 6).
    /// </summary>
    internal static class FrameDebuggerApi
    {
        private static readonly BindingFlags StaticFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private static readonly BindingFlags InstanceFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private static bool discovered;
        private static Type utilType;
        private static Type eventDataType;
        private static Type windowType;
        private static PropertyInfo countProp;
        private static PropertyInfo limitProp;
        private static MethodInfo setEnabledMethod;
        private static MethodInfo getFrameEventsMethod;
        private static MethodInfo getFrameEventDataMethod;
        private static MethodInfo getFrameEventInfoNameMethod;
        private static MethodInfo getFrameEventObjectMethod;
        private static MethodInfo getBatchBreakCauseStringsMethod;
        private static MethodInfo windowOpenMethod;
        private static MethodInfo windowEnableMethod;
        private static MethodInfo windowDisableMethod;
        private static MethodInfo repaintOnLimitChangeMethod;
        private static FieldInfo eventTypeField;
        private static FieldInfo eventObjField;
        private static int getEventDataParamCount = -1;
        private static object[] eventDataArgs;
        private static object eventDataScratch;
        private static readonly object[] intArg1 = new object[1];
        private static readonly Dictionary<(Type, string), FieldInfo> fieldCache =
            new Dictionary<(Type, string), FieldInfo>();
        private static string[] batchBreakCauseStrings;

        public static bool Available
        {
            get
            {
                EnsureDiscovered();
                return utilType != null && countProp != null && setEnabledMethod != null;
            }
        }

        public static string UnavailableReason
        {
            get
            {
                EnsureDiscovered();
                if (utilType == null)
                {
                    return "未找到 FrameDebuggerUtility（当前 Unity 版本可能不兼容）。";
                }

                if (countProp == null)
                {
                    return "FrameDebuggerUtility.count 不可用。";
                }

                if (setEnabledMethod == null)
                {
                    return "FrameDebuggerUtility.SetEnabled 不可用。";
                }

                return null;
            }
        }

        public static void EnsureDiscovered()
        {
            if (discovered)
            {
                return;
            }

            discovered = true;

            utilType = Type.GetType("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerUtility,UnityEditor")
                       ?? Type.GetType("UnityEditorInternal.FrameDebuggerUtility,UnityEditor");
            eventDataType = Type.GetType("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerEventData,UnityEditor")
                            ?? Type.GetType("UnityEditorInternal.FrameDebuggerEventData,UnityEditor");
            windowType = typeof(EditorWindow).Assembly.GetType("UnityEditor.FrameDebuggerWindow");

            if (utilType == null)
            {
                return;
            }

            countProp = utilType.GetProperty("count", StaticFlags);
            limitProp = utilType.GetProperty("limit", StaticFlags);
            setEnabledMethod = utilType.GetMethod("SetEnabled", StaticFlags, null, new[] { typeof(bool), typeof(int) }, null)
                               ?? utilType.GetMethod("SetEnabled", StaticFlags);
            getFrameEventsMethod = utilType.GetMethod("GetFrameEvents", StaticFlags);
            getFrameEventInfoNameMethod = utilType.GetMethod("GetFrameEventInfoName", StaticFlags);
            getFrameEventObjectMethod = utilType.GetMethod("GetFrameEventObject", StaticFlags);
            getBatchBreakCauseStringsMethod = utilType.GetMethod("GetBatchBreakCauseStrings", StaticFlags);

            if (eventDataType != null)
            {
                getFrameEventDataMethod = utilType.GetMethod(
                    "GetFrameEventData", StaticFlags, null, new[] { typeof(int), eventDataType }, null);
            }

            if (getFrameEventDataMethod == null)
            {
                getFrameEventDataMethod = utilType.GetMethod("GetFrameEventData", StaticFlags);
            }

            if (getFrameEventDataMethod != null)
            {
                getEventDataParamCount = getFrameEventDataMethod.GetParameters().Length;
                if (getEventDataParamCount >= 2 && eventDataType != null)
                {
                    eventDataScratch = Activator.CreateInstance(eventDataType);
                    eventDataArgs = new object[] { 0, eventDataScratch };
                }
            }

            if (windowType != null)
            {
                windowOpenMethod = windowType.GetMethod("OpenWindow", StaticFlags, null, Type.EmptyTypes, null);
                windowEnableMethod = windowType.GetMethod("EnableFrameDebugger", InstanceFlags);
                windowDisableMethod = windowType.GetMethod("DisableFrameDebugger", InstanceFlags);
                repaintOnLimitChangeMethod = windowType.GetMethod("RepaintOnLimitChange", InstanceFlags);
            }

            var eventType = Type.GetType("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerEvent,UnityEditor")
                            ?? Type.GetType("UnityEditorInternal.FrameDebuggerEvent,UnityEditor");
            if (eventType != null)
            {
                eventTypeField = eventType.GetField("m_Type", InstanceFlags)
                                 ?? eventType.GetField("type", InstanceFlags);
                eventObjField = eventType.GetField("m_Obj", InstanceFlags)
                                ?? eventType.GetField("obj", InstanceFlags);
            }

            try
            {
                if (getBatchBreakCauseStringsMethod != null)
                {
                    batchBreakCauseStrings = getBatchBreakCauseStringsMethod.Invoke(null, null) as string[];
                }
            }
            catch
            {
                batchBreakCauseStrings = null;
            }
        }

        public static int GetCount()
        {
            EnsureDiscovered();
            try
            {
                return countProp != null ? (int)countProp.GetValue(null) : 0;
            }
            catch
            {
                return 0;
            }
        }

        public static int GetLimit()
        {
            EnsureDiscovered();
            try
            {
                return limitProp != null ? (int)limitProp.GetValue(null) : 0;
            }
            catch
            {
                return 0;
            }
        }

        public static bool SetLimit(int value)
        {
            EnsureDiscovered();
            if (limitProp == null || !limitProp.CanWrite)
            {
                return false;
            }

            try
            {
                limitProp.SetValue(null, value);
                return GetLimit() == value;
            }
            catch
            {
                return false;
            }
        }

        public static void SetEnabled(bool enabled)
        {
            EnsureDiscovered();
            if (setEnabledMethod == null)
            {
                throw new InvalidOperationException("SetEnabled 不可用。");
            }

            var n = setEnabledMethod.GetParameters().Length;
            if (n == 2)
            {
                setEnabledMethod.Invoke(null, new object[] { enabled, 0 });
            }
            else if (n == 1)
            {
                setEnabledMethod.Invoke(null, new object[] { enabled });
            }
            else
            {
                throw new InvalidOperationException($"SetEnabled 参数个数异常: {n}");
            }
        }

        public static EditorWindow OpenOrGetWindow()
        {
            EnsureDiscovered();
            if (windowOpenMethod != null)
            {
                try
                {
                    var wnd = windowOpenMethod.Invoke(null, null) as EditorWindow;
                    if (wnd != null)
                    {
                        return wnd;
                    }
                }
                catch
                {
                    /* fall through */
                }
            }

            EditorApplication.ExecuteMenuItem("Window/Analysis/Frame Debugger");
            if (windowType == null)
            {
                return null;
            }

            var windows = Resources.FindObjectsOfTypeAll(windowType);
            if (windows == null || windows.Length == 0)
            {
                return null;
            }

            return windows[0] as EditorWindow;
        }

        public static bool TryEnableViaWindow(out string error)
        {
            error = null;
            EnsureDiscovered();
            var window = OpenOrGetWindow();
            if (window == null)
            {
                try
                {
                    SetEnabled(true);
                    return true;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
            }

            try
            {
                if (windowEnableMethod != null)
                {
                    windowEnableMethod.Invoke(window, null);
                }
                else
                {
                    SetEnabled(true);
                }

                window.Repaint();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool TryDisableViaWindow(out string error)
        {
            error = null;
            EnsureDiscovered();
            try
            {
                if (windowType != null)
                {
                    var windows = Resources.FindObjectsOfTypeAll(windowType);
                    if (windows != null)
                    {
                        for (var i = 0; i < windows.Length; i++)
                        {
                            var w = windows[i];
                            if (w == null)
                            {
                                continue;
                            }

                            if (windowDisableMethod != null)
                            {
                                windowDisableMethod.Invoke(w, null);
                            }
                        }
                    }
                }

                SetEnabled(false);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static Array GetFrameEvents()
        {
            EnsureDiscovered();
            if (getFrameEventsMethod == null)
            {
                return null;
            }

            try
            {
                return getFrameEventsMethod.Invoke(null, null) as Array;
            }
            catch
            {
                return null;
            }
        }

        public static string GetEventInfoName(int index)
        {
            EnsureDiscovered();
            if (getFrameEventInfoNameMethod == null)
            {
                return null;
            }

            try
            {
                intArg1[0] = index;
                return getFrameEventInfoNameMethod.Invoke(null, intArg1) as string;
            }
            catch
            {
                return null;
            }
        }

        public static UnityEngine.Object GetEventObject(int index)
        {
            EnsureDiscovered();
            if (getFrameEventObjectMethod == null)
            {
                return null;
            }

            try
            {
                intArg1[0] = index;
                return getFrameEventObjectMethod.Invoke(null, intArg1) as UnityEngine.Object;
            }
            catch
            {
                return null;
            }
        }

        public static string GetEventTypeName(object frameEvent)
        {
            if (frameEvent == null || eventTypeField == null)
            {
                return null;
            }

            try
            {
                var v = eventTypeField.GetValue(frameEvent);
                return v != null ? v.ToString() : null;
            }
            catch
            {
                return null;
            }
        }

        public static UnityEngine.Object GetEventBoundObject(object frameEvent)
        {
            if (frameEvent == null || eventObjField == null)
            {
                return null;
            }

            try
            {
                return eventObjField.GetValue(frameEvent) as UnityEngine.Object;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Move GPU replay cursor to event index and force view repaint.
        /// Detail must be read on a later editor frame via <see cref="TryGetEventData"/>.
        /// </summary>
        public static bool SeekToEvent(int index)
        {
            EnsureDiscovered();
            if (!SetLimit(index + 1))
            {
                return false;
            }

            try
            {
                if (windowType != null && repaintOnLimitChangeMethod != null)
                {
                    var windows = Resources.FindObjectsOfTypeAll(windowType);
                    if (windows != null)
                    {
                        for (var i = 0; i < windows.Length; i++)
                        {
                            var w = windows[i];
                            if (w == null)
                            {
                                continue;
                            }

                            repaintOnLimitChangeMethod.Invoke(w, null);
                        }
                    }
                }
            }
            catch
            {
                /* ignore */
            }

            try
            {
                EditorApplication.QueuePlayerLoopUpdate();
            }
            catch
            {
                /* ignore */
            }

            InternalEditorUtility.RepaintAllViews();
            return true;
        }

        public static object TryGetEventData(int index)
        {
            EnsureDiscovered();
            if (getFrameEventDataMethod == null)
            {
                return null;
            }

            try
            {
                if (getEventDataParamCount >= 2 && eventDataArgs != null)
                {
                    eventDataArgs[0] = index;
                    eventDataArgs[1] = eventDataScratch;
                    var ok = getFrameEventDataMethod.Invoke(null, eventDataArgs);
                    if (ok is bool b && !b)
                    {
                        return null;
                    }

                    return eventDataArgs[1];
                }

                intArg1[0] = index;
                return getFrameEventDataMethod.Invoke(null, intArg1);
            }
            catch
            {
                return null;
            }
        }

        public static string ResolveBatchBreakCause(int cause)
        {
            if (cause <= 0 || batchBreakCauseStrings == null || cause >= batchBreakCauseStrings.Length)
            {
                return null;
            }

            return batchBreakCauseStrings[cause];
        }

        public static T GetField<T>(object obj, params string[] names)
        {
            if (obj == null || names == null)
            {
                return default;
            }

            var type = obj.GetType();
            for (var i = 0; i < names.Length; i++)
            {
                var fi = GetFieldInfo(type, names[i]);
                if (fi == null)
                {
                    continue;
                }

                try
                {
                    var val = fi.GetValue(obj);
                    if (val == null)
                    {
                        return default;
                    }

                    if (val is T t)
                    {
                        return t;
                    }

                    return (T)Convert.ChangeType(val, typeof(T));
                }
                catch
                {
                    /* try next name */
                }
            }

            return default;
        }

        public static object GetFieldObject(object obj, params string[] names)
        {
            if (obj == null || names == null)
            {
                return null;
            }

            var type = obj.GetType();
            for (var i = 0; i < names.Length; i++)
            {
                var fi = GetFieldInfo(type, names[i]);
                if (fi == null)
                {
                    continue;
                }

                try
                {
                    return fi.GetValue(obj);
                }
                catch
                {
                    /* next */
                }
            }

            return null;
        }

        private static FieldInfo GetFieldInfo(Type type, string name)
        {
            var key = (type, name);
            if (fieldCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var fi = type.GetField(name, InstanceFlags);
            fieldCache[key] = fi;
            return fi;
        }
    }
}
