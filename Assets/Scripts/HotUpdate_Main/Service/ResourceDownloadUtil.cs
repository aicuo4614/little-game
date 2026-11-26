using System;
using System.Threading.Tasks;
using YooAsset;

/// <summary>UI 框架资源依赖的下载执行端；沿用 Demo 的 YooAsset 按地址下载，不引入旧资源下载管理界面。</summary>
public static class ResourceDownloadUtil
{
    /// <summary>确保指定地址及所需依赖位于本地；返回是否发生了下载。</summary>
    public static async Task<bool> EnsureByLocationAsync(string location, ResourcePackageName packageName,
        Action<float> onProgress = null, bool recursiveDownload = true)
    {
        var package = YooAssets.GetPackage(packageName.ToPackageString());
        var downloader = package.CreateBundleDownloader(location, recursiveDownload, 10, 3);
        if (downloader.TotalDownloadCount == 0)
        {
            onProgress?.Invoke(1f);
            return false;
        }
        downloader.DownloadUpdateCallback = data => onProgress?.Invoke(data.Progress);
        downloader.BeginDownload();
        await downloader.Task;
        if (downloader.Status != EOperationStatus.Succeed)
        {
            throw new InvalidOperationException($"Resource download failed: {packageName}/{location}");
        }
        return true;
    }
}
