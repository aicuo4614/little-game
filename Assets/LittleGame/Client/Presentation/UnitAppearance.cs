using UnityEngine;

namespace LittleGame.Client
{
    /// <summary>角色资源与规则解耦；普通动作、AnimatorController、武器和升星装配由资源配置。</summary>
    public sealed class UnitAppearance : ScriptableObject
    {
        // 热更脚本资源必须经 AssetBundle 反序列化，不能回退到原生 Resources。
        public static UnitAppearance Load(string role)
        {
            var address = "Appearance/" + role;
            var package = ResourceMgr.Instance.GetPackage(ResourcePackageName.UnitPackage);
            if (package == null || !package.PackageValid || !package.CheckLocationValid(address))
                throw new System.InvalidOperationException("缺少单位外观包资源: " + address);
            return MVC.ResourceService.LoadAsset<UnitAppearance>(address, ResourcePackageName.UnitPackage);
        }

        public static void Release(string role)
            => MVC.ResourceService.Release("Appearance/" + role, ResourcePackageName.UnitPackage);

        public string Source;
        public GameObject Model;
        public RuntimeAnimatorController AnimatorController;
        public AnimationClip Idle, Move, Attack, Hit, Death, Slam;
        public float Height = 1.05f;
        public float AttackSpeed = 1.5f;
    }
}
