#if DEVELOPMENT_BUILD && UNITY_ANDROID
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace LittleGame
{
    // 仅开发包启用：向本机私有目录输出只读观测，不提供调试玩法指令。
    [DefaultExecutionOrder(200)]
    public sealed class MobileDiagnostics : MonoBehaviour
    {
        private float next;
        private void LateUpdate()
        {
            if(Time.unscaledTime<next) return;
            bool structure=Structure.StructureBootstrap.View!=null;
            if(!structure&&GameBootstrap.View==null)return;
            next=Time.unscaledTime+.5f;
            var data=new { sample=structure?"structure":"legacy",state=structure?Structure.StructureBootstrap.Session.Snapshot():GameBootstrap.Session.Snapshot(),view=structure?Structure.StructureBootstrap.View.Snapshot():GameBootstrap.View.InteractionSnapshot(),performance=FrameMetrics.Current.Snapshot() };
            string path=Path.Combine(Application.persistentDataPath,"observation.json");
            File.WriteAllText(path+".tmp",JsonConvert.SerializeObject(data));
            if(File.Exists(path)) File.Delete(path);
            File.Move(path+".tmp",path);
        }
    }
}
#endif
