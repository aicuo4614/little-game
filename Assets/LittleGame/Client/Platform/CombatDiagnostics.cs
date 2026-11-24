using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace LittleGame.Client
{
    public sealed class CombatDiagnostics : MonoBehaviour
    {
        private readonly float[] samples = new float[240];
        private int count, cursor;
        private float next;
        private bool ioDisabled;
        private void LateUpdate()
        {
            samples[cursor] = Time.unscaledDeltaTime * 1000; cursor = (cursor + 1) % samples.Length; count = Math.Min(count + 1, samples.Length);
#if DEVELOPMENT_BUILD && UNITY_ANDROID
            if (ioDisabled || Time.unscaledTime < next || ClientBootstrap.Current == null) return;
            next = Time.unscaledTime + .5f;
            try
            {
                var boot = ClientBootstrap.Current;
                string path = Path.Combine(Application.persistentDataPath, "observation.json");
                File.WriteAllText(path + ".tmp", JsonConvert.SerializeObject(new { sample = "combat", state = boot.App.Session.Snapshot(), view = boot.Hud.Snapshot(), performance = Snapshot() }));
                if (File.Exists(path)) File.Replace(path + ".tmp", path, null); else File.Move(path + ".tmp", path);
            }
            catch (Exception e) { ioDisabled = true; GameDebug.LogWarning("CombatDiagnostics", e.Message); }
#endif
        }
        public object Snapshot()
        {
            var copy = new float[count]; Array.Copy(samples, copy, count); Array.Sort(copy); double sum = 0; foreach (float n in copy) sum += n;
            return new { samples = count, meanFrameMs = count == 0 ? 0 : sum / count, p95FrameMs = count == 0 ? 0 : copy[(int)((count - 1) * .95f)], platform = Application.platform.ToString(), width = Screen.width, height = Screen.height, note = Application.isEditor ? "编辑器帧间隔，不能作为手机性能结论" : "Android开发包帧间隔，含诊断落盘开销" };
        }
    }
}
