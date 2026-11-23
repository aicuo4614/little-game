using UnityEngine;
using UnityEngine.SceneManagement;

namespace LittleGame.Structure
{
    public sealed class StructureBootstrap : MonoBehaviour
    {
        public static StructureSession Session {get;private set;}
        public static StructureView View {get;private set;}
        public static StructureInput Input {get;private set;}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics(){Session=null;View=null;Input=null;}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartGame()
        {
            SceneManager.sceneLoaded-=OnScene;SceneManager.sceneLoaded+=OnScene;
            TryStart(SceneManager.GetActiveScene());
        }
        private static void OnScene(Scene scene,LoadSceneMode mode){TryStart(scene);}
        private static void TryStart(Scene scene)
        {
            if(scene.name!="Structure"||FindObjectOfType<StructureBootstrap>()!=null)return;
            Application.targetFrameRate=60;QualitySettings.vSyncCount=Application.isMobilePlatform?0:1;
            Session=new StructureSession();var root=new GameObject("StructureSample");
            root.AddComponent<StructureBootstrap>();View=root.AddComponent<StructureView>();View.Build();Input=root.AddComponent<StructureInput>();
#if DEVELOPMENT_BUILD && UNITY_ANDROID
            root.AddComponent<FrameMetrics>();root.AddComponent<MobileDiagnostics>();
#endif
        }
        private void Update(){Session.Advance(Time.deltaTime);}
    }
}
