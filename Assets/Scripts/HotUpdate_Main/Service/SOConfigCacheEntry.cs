using System.Threading.Tasks;
using UnityEngine;

/// <summary>单一配置类型的资源定位、加载任务与持有状态。</summary>
internal sealed class SOConfigCacheEntry
{
    internal string Location;
    internal ResourcePackageName PackageName;
    internal ScriptableObject Asset;
    internal bool Acquired;
    internal bool Invalidated;
    internal TaskCompletionSource<ScriptableObject> Completion = new TaskCompletionSource<ScriptableObject>();
}
