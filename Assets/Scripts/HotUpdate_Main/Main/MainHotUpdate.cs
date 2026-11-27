using System;
using UnityEngine;

/// <summary>
/// 热更程序集主启动入口（对齐 Demo MainHotUpdate）：
/// 由 AOT CodePackageHotUpdateFlow 反射调用，完成 MVC.Init()、内容包就绪与主业务启动。
/// </summary>
public static class MainHotUpdate
{
    private static readonly ResourcePackageName[] StartupContentPackages =
    {
        ResourcePackageName.MainPackage,
        ResourcePackageName.ModulePackage,
        ResourcePackageName.UnitPackage,
        ResourcePackageName.EffectPackage,
        ResourcePackageName.ScenePackage,
    };

    private static bool launched;
    public static bool IsLaunched => launched;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        launched = false;
    }

    public static void Launch()
    {
        if (launched)
        {
            return;
        }

        launched = true;
        MVC.Init();
        QualityTierMgr.Instance.Init();

        // 先激活所有内置内容包的清单，再允许业务创建对象。
        var pending = StartupContentPackages.Length;
        bool failed = false;
        for (var i = 0; i < StartupContentPackages.Length; i++)
        {
            var pkg = StartupContentPackages[i];
            ResourceMgr.Instance.InitBuiltInPackage(pkg, async ok =>
            {
                failed |= !ok;
                pending--;
                if (pending == 0)
                {
                    if (failed)
                    {
                        GameDebug.LogError("MainHotUpdate", "内容包初始化失败，停止进入战斗。");
                        return;
                    }
                    try
                    {
                        await GfxQualityMgr.Instance.InitAsync();
                        GameDebug.Log("MainHotUpdate", "MVC / Service / Mgr / 内置资源包启动完成");
                        var entry = Type.GetType("LittleGame.Client.ClientBootstrap, LittleGame.Client", throwOnError: true);
                        entry.GetMethod("Launch", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static).Invoke(null, null);
                    }
                    catch (Exception ex)
                    {
                        GameDebug.LogError("MainHotUpdate", $"进入战斗失败: {ex}");
                    }
                }
            });
        }
    }
}
