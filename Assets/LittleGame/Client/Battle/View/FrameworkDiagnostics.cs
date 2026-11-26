using System;
using System.Collections.Generic;
using System.Linq;
using YooAsset;

namespace LittleGame.Client.Battle
{
    /// <summary>只读诊断真实启动与资源所有权，不用架构名称代替接入证据。</summary>
    public static class FrameworkDiagnostics
    {
        public static object Snapshot(BattleView view)
        {
            var entries = new List<ResourceEntrySnapshot>();
            MVC.ResourceService.CopyEntriesSnapshot(entries);
            return new
            {
                resourceMode = LaunchRuntimeResourceUtil.UseRuntimeResourcePipeline ? "HostPlayMode+OfflineLaunch" : "EditorSimulateMode",
                aotStarted = MyApplication.Instance != null,
                mainLaunched = MainHotUpdate.IsLaunched,
                mvcInitialized = MVC.IsInitialized,
                battleModuleOpen = ModuleMgr.Instance.IsOpened("Battle"),
                qualityInitialized = GfxQualityMgr.Instance.HasInitialized,
                qualityTier = GfxQualityMgr.Instance.CurrentTier.ToString(),
                packages = new[] { ResourcePackageName.LaunchPackage, ResourcePackageName.CodePackage,
                    ResourcePackageName.MainPackage, ResourcePackageName.ModulePackage, ResourcePackageName.UnitPackage,
                    ResourcePackageName.EffectPackage, ResourcePackageName.ScenePackage }
                    .Select(name => new { name = name.ToString(), manifestReady = YooAssets.TryGetPackage(name.ToString())?.PackageValid ?? false }),
                resourceEntries = entries,
                pool = new { active = view.PrefabPool.ActiveCount, idle = view.PrefabPool.IdleCount, prefabCount = view.PrefabPool.PoolCount },
                windows = UiMgr.Instance.OpenedWindows.Select(w => new { name = w.Window.GetType().Name, opened = w.Window.IsOpened }),
                note = "这是当前 Unity 宿主的资源与框架状态；Editor 的 DLL 使用编译程序集，不代表手机 HybridCLR 注入通过"
            };
        }
    }
}
