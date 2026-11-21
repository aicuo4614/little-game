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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Session = null; View = null; Input = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartGame()
        {
            if (SceneManager.GetActiveScene().name != "Game") return;
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
