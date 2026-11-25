using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LittleGame.Client
{
    public sealed class ClientBootstrap : MonoBehaviour
    {
        public static ClientBootstrap Current { get; private set; }
        public BattleApplication App { get; private set; }
        public BattleSceneView View { get; private set; }
        public BattleHud Hud { get; private set; }
        public BattleInput Input { get; private set; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Current = null; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded; SceneManager.sceneLoaded += OnSceneLoaded;
            TryBoot();
        }
        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => TryBoot();
        private static void TryBoot()
        {
            if (Current != null || SceneManager.GetActiveScene().name != "Combat") return;
            new GameObject("LittleGameClient").AddComponent<ClientBootstrap>();
        }
        private void Awake()
        {
            Current = this;
            if (!Application.isMobilePlatform && QualitySettings.names != null && QualitySettings.names.Length > 0)
                QualitySettings.SetQualityLevel(QualitySettings.names.Length - 1, true);
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            Screen.orientation = ScreenOrientation.Portrait;
            var store = new LocalRunStore(Path.Combine(Application.persistentDataPath, "combat-run-v1.json"));
            App = new BattleApplication(store); App.PersistenceError += message => GameDebug.LogWarning("CombatSave", message);
            View = gameObject.AddComponent<BattleSceneView>(); View.Build(App);
            App.Session.ConfigureSkills(View.TimelineDirector.Library.BuildDefinitions());
            Hud = gameObject.AddComponent<BattleHud>(); Hud.Build(App, View);
            Input = gameObject.AddComponent<BattleInput>(); Input.Bind(App, View, Hud);
            App.AttachView(Hud); gameObject.AddComponent<CombatDiagnostics>();
            GameDebug.Log("Client", "军团客户端启动；MCP与玩家共用指令，场景=" + SceneManager.GetActiveScene().path);
        }
        private void Update() => App?.Update(Mathf.Min(Time.unscaledDeltaTime, .25f));
        private void OnApplicationPause(bool paused) { if (paused) App?.SaveCheckpoint(); Input?.Cancel(); }
        private void OnApplicationFocus(bool focus) { if (!focus) Input?.Cancel(); }
        private void OnDestroy() { App?.Dispose(); if (Current == this) Current = null; }
    }
}
