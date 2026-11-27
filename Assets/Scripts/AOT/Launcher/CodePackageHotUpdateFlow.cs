using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// AOT 启动阶段：从内置 CodePackage 加载 DLL 及 AOT 补充元数据，再反射进入 MainHotUpdate.Launch。
/// Editor 默认走 asmdef 直编，直接进入热更入口，保证点 Play 秒进。
/// </summary>
public sealed class CodePackageHotUpdateFlow
{
    private static readonly HotUpdateMgr.PackageFlowConfig[] HotUpdateConfigs =
    {
        new HotUpdateMgr.PackageFlowConfig(
            name: ResourcePackageName.CodePackage,
            dllPaths: new[]
            {
                "HotUpdate_Domain/LittleGame.Domain.dll",
                "HotUpdate_UnitCore/HotUpdate_UnitCore.dll",
                "HotUpdate_Main/HotUpdate_Main.dll",
                "LittleGame_Client/LittleGame.Client.dll",
            },
            aotManifestPath: "HotUpdate_Main/AOTMetadata/manifest"
        ),
    };

    private readonly Action<string> onInfo;
    private readonly Action<float> onProgress;

    public CodePackageHotUpdateFlow(Action<string> onInfo = null, Action<float> onProgress = null)
    {
        this.onInfo = onInfo;
        this.onProgress = onProgress;
    }

    public void Start()
    {
        onProgress?.Invoke(0f);
        onInfo?.Invoke("正在读取内置启动资源...");
        ResourceMgr.Instance.InitBuiltInPackage(ResourcePackageName.CodePackage, ok =>
        {
            if (!ok || ResourceMgr.Instance.GetPackage(ResourcePackageName.CodePackage) == null)
            {
                GameDebug.LogError("Launcher", "内置 CodePackage 初始化失败，停止启动。");
                return;
            }

            var handle = new HotUpdateMgr.FlowHandle();
            handle.OnPackageAssemblyLoadStart += _ => onInfo?.Invoke("正在加载游戏代码...");
            handle.OnAllFlowComplete += success =>
            {
                if (!success)
                {
                    GameDebug.LogError("Launcher", "内置 CodePackage 加载程序集失败");
                    return;
                }

                onProgress?.Invoke(1f);
                TryLaunch(HotUpdateMgr.Instance.AllAssemblies, "MainHotUpdate", "Launch", "HotUpdate_Main");
            };

            CoroutineRunner.Start(HotUpdateMgr.Instance.LoadReadyPackages(HotUpdateConfigs, handle));
        });
    }

    private void TryLaunch(
        IReadOnlyList<Assembly> assemblies,
        string typeName,
        string methodName,
        string fallbackAssemblyName)
    {
        if (assemblies != null)
        {
            for (var i = 0; i < assemblies.Count; i++)
            {
                var type = assemblies[i]?.GetType(typeName);
                if (type != null)
                {
                    type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
                    return;
                }
            }
        }

        var loadedType = Type.GetType($"{typeName}, {fallbackAssemblyName}");
        if (loadedType != null)
        {
            loadedType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
            return;
        }

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType(typeName);
            if (t != null)
            {
                t.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
                return;
            }
        }

        GameDebug.LogError("Launcher", $"找不到热更入口 {typeName}.{methodName}");
    }
}
