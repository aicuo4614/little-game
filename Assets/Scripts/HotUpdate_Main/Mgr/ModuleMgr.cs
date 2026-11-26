using System;
using System.Collections.Generic;
using System.Reflection;

/// <summary>
/// 功能模块开/关/销毁管理器（对齐 Demo ModuleMgr，底层改用 UGUI UiMgr Loading）。
/// 通过 <see cref="Instance"/> 访问。
/// </summary>
public sealed class ModuleMgr : MgrBase<ModuleMgr>
{
    private enum ModuleCacheMode
    {
        Hide = 0,
        Destroy = 1,
    }

    private enum ModuleState
    {
        Creating = 0,
        Opened = 1,
        Closed = 2,
        Destroyed = 3,
    }

    private sealed class ModuleConfig
    {
        public string ModuleName { get; }
        public string ModuleTypeName { get; }
        public ModuleCacheMode CacheMode { get; }

        public ModuleConfig(string moduleName, string moduleTypeName, ModuleCacheMode cacheMode)
        {
            ModuleName = moduleName;
            ModuleTypeName = moduleTypeName;
            CacheMode = cacheMode;
        }
    }

    private sealed class ModuleHandle
    {
        public ModuleConfig Config { get; }
        public ModuleBase Module { get; }
        public ModuleState State { get; set; }

        public ModuleHandle(ModuleConfig config, ModuleBase module)
        {
            Config = config;
            Module = module;
            State = ModuleState.Creating;
        }
    }

    private readonly Dictionary<string, ModuleHandle> openedModules =
        new Dictionary<string, ModuleHandle>(StringComparer.OrdinalIgnoreCase);

    private Dictionary<string, ModuleConfig> moduleConfigs;

    protected override void OnCreated()
    {
        moduleConfigs = new Dictionary<string, ModuleConfig>(StringComparer.OrdinalIgnoreCase);
        RegisterModule("Battle", "BattleModule", ModuleCacheMode.Destroy);
    }

    public void RegisterModule(string moduleName, string moduleTypeName, bool destroyOnClose = true)
    {
        RegisterModule(moduleName, moduleTypeName, destroyOnClose ? ModuleCacheMode.Destroy : ModuleCacheMode.Hide);
    }

    private void RegisterModule(string moduleName, string moduleTypeName, ModuleCacheMode cacheMode)
    {
        if (string.IsNullOrWhiteSpace(moduleName) || string.IsNullOrWhiteSpace(moduleTypeName))
        {
            return;
        }

        moduleConfigs[moduleName] = new ModuleConfig(moduleName, moduleTypeName, cacheMode);
    }

    public void OpenModule(string moduleName, Action completeCallback = null)
        => OpenModule(moduleName, null, completeCallback);

    public void OpenModule<TArgs>(string moduleName, TArgs args, Action completeCallback = null)
        => OpenModule(moduleName, (object)args, completeCallback);

    public void OpenModule(string moduleName, object args, Action completeCallback = null)
    {
        if (string.IsNullOrWhiteSpace(moduleName))
        {
            GameDebug.LogError("ModuleMgr", "OpenModule: moduleName 不能为空。");
            completeCallback?.Invoke();
            return;
        }

        if (openedModules.TryGetValue(moduleName, out var existedHandle))
        {
            EnsureCreated(existedHandle);
            OpenHandle(existedHandle, args, completeCallback);
            return;
        }

        if (!moduleConfigs.TryGetValue(moduleName, out var config))
        {
            GameDebug.LogError("ModuleMgr", $"OpenModule: 未注册模块 {moduleName}");
            completeCallback?.Invoke();
            return;
        }

        var module = CreateModule(config.ModuleTypeName, config.ModuleName);
        if (module == null)
        {
            completeCallback?.Invoke();
            return;
        }

        var handle = new ModuleHandle(config, module);
        openedModules[config.ModuleName] = handle;
        EnsureCreated(handle);
        OpenHandle(handle, args, completeCallback);
    }

    public void CloseModule(string moduleName)
    {
        if (!openedModules.TryGetValue(moduleName, out var handle))
        {
            return;
        }

        if (handle.Config.CacheMode == ModuleCacheMode.Destroy)
        {
            DestroyModule(moduleName);
            return;
        }

        handle.Module.Close();
        handle.State = ModuleState.Closed;
    }

    public override void Clear()
    {
        var names = new List<string>(openedModules.Keys);
        for (var i = 0; i < names.Count; i++)
        {
            DestroyModule(names[i]);
        }

        base.Clear();
    }

    public void DestroyModule(string moduleName)
    {
        if (!openedModules.TryGetValue(moduleName, out var handle))
        {
            return;
        }

        openedModules.Remove(moduleName);
        handle.State = ModuleState.Destroyed;
        handle.Module.Destroy();
    }

    public bool IsOpened(string moduleName)
    {
        return openedModules.TryGetValue(moduleName, out var handle) && handle.State == ModuleState.Opened;
    }

    public bool TryGetModule<TModule>(string moduleName, out TModule module) where TModule : ModuleBase
    {
        module = null;
        if (!openedModules.TryGetValue(moduleName, out var handle))
        {
            return false;
        }

        module = handle.Module as TModule;
        return module != null;
    }

    private static void EnsureCreated(ModuleHandle handle)
    {
        if (!handle.Module.IsCreated)
        {
            handle.Module.Create();
        }
    }

    private static ModuleBase CreateModule(string typeName, string moduleName)
    {
        var moduleType = FindType(typeName);
        if (moduleType == null || !typeof(ModuleBase).IsAssignableFrom(moduleType))
        {
            GameDebug.LogError("ModuleMgr", $"找不到有效模块入口类型 {typeName}（module={moduleName}）");
            return null;
        }

        try
        {
            return Activator.CreateInstance(moduleType) as ModuleBase;
        }
        catch (Exception ex)
        {
            GameDebug.LogError("ModuleMgr", $"创建模块入口失败 type={typeName}, ex={ex}");
            return null;
        }
    }

    private static Type FindType(string typeName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType(typeName);
            if (type != null)
            {
                return type;
            }
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types;
            }

            if (types == null) continue;
            for (var i = 0; i < types.Length; i++)
            {
                if (types[i] != null && string.Equals(types[i].Name, typeName, StringComparison.OrdinalIgnoreCase))
                {
                    return types[i];
                }
            }
        }

        return null;
    }

    private static void OpenHandle(ModuleHandle handle, object args, Action completeCallback)
    {
        string loadingFlag = null;
        if (handle.Module.NeedShowLoadingOnOpen)
        {
            loadingFlag = $"module_open_{handle.Config.ModuleName}_{Guid.NewGuid():N}";
            UiMgr.Instance.ShowLoading(loadingFlag, $"打开模块 {handle.Config.ModuleName}...");
        }

        var finished = false;
        Action finish = () =>
        {
            if (finished) return;
            finished = true;
            if (!string.IsNullOrEmpty(loadingFlag))
            {
                UiMgr.Instance.CloseLoading(loadingFlag);
            }

            if (handle.State != ModuleState.Destroyed)
            {
                handle.State = ModuleState.Opened;
            }

            completeCallback?.Invoke();
        };

        try
        {
            handle.Module.Open(args, finish);
        }
        catch (Exception ex)
        {
            GameDebug.LogError("ModuleMgr", $"OpenHandle 异常 module={handle.Config.ModuleName}, ex={ex}");
            finish();
        }
    }
}
