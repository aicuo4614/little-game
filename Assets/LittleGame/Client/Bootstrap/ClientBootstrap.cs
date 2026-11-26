using System;
using LittleGame.Client.Battle;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LittleGame.Client
{
    /// <summary>
    /// 军团客户端引导入口：
    /// 点击 Play 进入 Combat.unity 后自动走完整的 AOT MyApplication → MainHotUpdate → MVC → ModuleMgr → BattleModule → BattleView → BattleUnit 启动链路，
    /// 并保持 ClientBootstrap.Current 供玩家输入与 CombatMcp 共用同一套 BattleApplication 规则入口。
    /// </summary>
    public sealed class ClientBootstrap : MonoBehaviour
    {
        public static ClientBootstrap Current { get; private set; }
        public BattleModule Module { get; private set; }
        public BattleApplication App => Module?.App;
        public BattleView View => Module?.View;
        public BattleHud Hud => Module?.Hud;
        public BattleInput Input => Module?.Input;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Current = null; }

        /// <summary>资源与热更就绪后，由 MainHotUpdate 反射调用，避免 AOT 对业务程序集产生依赖。</summary>
        public static void Launch()
        {
            if (Current != null || SceneManager.GetActiveScene().name != "Combat") return;
            new GameObject("LittleGameClient").AddComponent<ClientBootstrap>();
        }

        private void Awake()
        {
            Current = this;
            Screen.orientation = ScreenOrientation.Portrait;

            // 2. 通过 ModuleMgr 标准入口打开 BattleModule（组装 BattleView + BattleWindow + BattleStateMachine）
            ModuleMgr.Instance.OpenModule("Battle");
            ModuleMgr.Instance.TryGetModule<BattleModule>("Battle", out var battleModule);
            Module = battleModule;

            gameObject.AddComponent<CombatDiagnostics>();
            GameDebug.Log("Client", "军团客户端启动（AOT→MainHotUpdate→MVC→ModuleMgr→BattleModule→BattleView）；场景=" + SceneManager.GetActiveScene().path);
        }

        private void Update()
        {
            Module?.Tick(Mathf.Min(Time.unscaledDeltaTime, 0.25f));
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) App?.SaveCheckpoint();
            Input?.Cancel();
        }

        private void OnApplicationFocus(bool focus)
        {
            if (!focus) Input?.Cancel();
        }

        private void OnDestroy()
        {
            if (Module != null)
            {
                ModuleMgr.Instance.DestroyModule("Battle");
                Module = null;
            }
            if (Current == this) Current = null;
        }
    }
}
