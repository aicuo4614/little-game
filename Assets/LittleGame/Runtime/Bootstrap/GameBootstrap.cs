using UnityEngine;
using UnityEngine.SceneManagement;

namespace LittleGame
{
    public sealed class GameBootstrap : MonoBehaviour
    {
        public static PuzzleSession Session { get; private set; }
        public static MachineView View { get; private set; }
        public static PuzzleInput Input { get; private set; }

        private void Update() { Session?.Advance(Time.unscaledDeltaTime); }
        public static void OpenStructureSample() { SceneManager.LoadScene("Structure"); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Session = null; View = null; Input = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartGame()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            TryStartGame();
        }
        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) { TryStartGame(); }
        private static void TryStartGame()
        {
            if (SceneManager.GetActiveScene().name != "Game") return;
            if (FindObjectOfType<GameBootstrap>() != null) return;
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = Application.isMobilePlatform ? 0 : 1;
            Session = new PuzzleSession();
            var root = new GameObject("GameSession");
            root.AddComponent<GameBootstrap>();
            root.AddComponent<FrameMetrics>();
            View = root.AddComponent<MachineView>();
            Input = root.AddComponent<PuzzleInput>();
#if DEVELOPMENT_BUILD && UNITY_ANDROID
            root.AddComponent<MobileDiagnostics>();
#endif
        }
    }
}
