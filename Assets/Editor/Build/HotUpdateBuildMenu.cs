using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace LittleGame.Editor.Build
{
    /// <summary>
    /// 单工程 HybridCLR + YooAsset 热更新构建菜单入口（对齐 Demo Build 管线）。
    /// 提供热更程序集检查、AOT 泛型元数据导出目录准备及 YooAsset Collector 快速打开入口。
    /// </summary>
    public static class HotUpdateBuildMenu
    {
        public const string CodePackageDir = "Assets/Package_Code";

        [MenuItem("Kabu/Build/检查热更与资源包配置", false, 3001)]
        public static void ValidateHotUpdateSetup()
        {
            LittleGameStackSetup.Configure();
        }

        [MenuItem("Kabu/Build/打开 YooAsset 资源收集器", false, 3002)]
        public static void OpenYooAssetCollector()
        {
            if (!EditorApplication.ExecuteMenuItem("YooAsset/AssetBundle Collector"))
            {
                Debug.LogWarning("[HotUpdateBuildMenu] 未找到 YooAsset/AssetBundle Collector 菜单项。");
            }
        }
    }
}
