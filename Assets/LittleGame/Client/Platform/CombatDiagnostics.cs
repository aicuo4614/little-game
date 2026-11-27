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
        private bool startupLogged;
        private void LateUpdate()
        {
            samples[cursor] = Time.unscaledDeltaTime * 1000; cursor = (cursor + 1) % samples.Length; count = Math.Min(count + 1, samples.Length);
#if DEVELOPMENT_BUILD && UNITY_ANDROID
            if (ioDisabled || Time.unscaledTime < next || ClientBootstrap.Current == null) return;
            next = Time.unscaledTime + .5f;
            try
            {
                bool traceStartup = !startupLogged;
                startupLogged = true;
                if (traceStartup) GameDebug.Log("StartupFrame", "diagnostics snapshot begin");
                var boot = ClientBootstrap.Current;
                string path = Path.Combine(Application.persistentDataPath, "observation.json");
                // 手机持续采样不调用 Editor MCP 的完整匿名泛型快照。
                // 该路径在 Android 热更宿主发生不可捕获的原生崩溃；直接写入标量与单位状态。
                File.WriteAllText(path + ".tmp", MobileObservation(boot));
                if (traceStartup) GameDebug.Log("StartupFrame", "diagnostics snapshot complete");
                if (File.Exists(path)) File.Replace(path + ".tmp", path, null); else File.Move(path + ".tmp", path);
            }
            catch (Exception e) { ioDisabled = true; GameDebug.LogWarning("CombatDiagnostics", e.Message); }
#endif
        }

        private string MobileObservation(ClientBootstrap boot)
        {
            var session = boot.App.Session;
            using (var buffer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture))
            using (var json = new JsonTextWriter(buffer))
            {
                json.WriteStartObject();
                json.WritePropertyName("schema"); json.WriteValue("little-game.mobile-observation/v2");
                json.WritePropertyName("sample"); json.WriteValue("combat");
                json.WritePropertyName("state"); json.WriteStartObject();
                json.WritePropertyName("schemaVersion"); json.WriteValue(6);
                json.WritePropertyName("phase"); json.WriteValue(session.Phase.ToString());
                json.WritePropertyName("wave"); json.WriteValue(session.Wave);
                json.WritePropertyName("seed"); json.WriteValue(session.Seed);
                json.WritePropertyName("spins"); json.WriteValue(session.Spins);
                json.WritePropertyName("battleTime"); json.WriteValue(session.BattleTime);
                json.WritePropertyName("units"); json.WriteStartArray();
                for (int i = 0; i < session.Troops.Count; i++)
                {
                    var unit = session.Troops[i];
                    json.WriteStartObject();
                    json.WritePropertyName("id"); json.WriteValue(unit.Id);
                    json.WritePropertyName("kind"); json.WriteValue(unit.Kind.ToString());
                    json.WritePropertyName("star"); json.WriteValue(unit.Star);
                    json.WritePropertyName("lane"); json.WriteValue(unit.Lane);
                    json.WritePropertyName("x"); json.WriteValue(unit.X);
                    json.WritePropertyName("z"); json.WriteValue(unit.Z);
                    json.WritePropertyName("hp"); json.WriteValue(unit.Hp);
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WritePropertyName("enemies"); json.WriteStartArray();
                for (int i = 0; i < session.Enemies.Count; i++)
                {
                    var enemy = session.Enemies[i];
                    json.WriteStartObject();
                    json.WritePropertyName("id"); json.WriteValue(enemy.Id);
                    json.WritePropertyName("kind"); json.WriteValue(enemy.MonsterId);
                    json.WritePropertyName("x"); json.WriteValue(enemy.X);
                    json.WritePropertyName("z"); json.WriteValue(enemy.Z);
                    json.WritePropertyName("hp"); json.WriteValue(enemy.Hp);
                    json.WriteEndObject();
                }
                json.WriteEndArray(); json.WriteEndObject();
                json.WritePropertyName("view"); json.WriteStartObject();
                json.WritePropertyName("width"); json.WriteValue(Screen.width);
                json.WritePropertyName("height"); json.WriteValue(Screen.height);
                json.WriteEndObject();
                double sum = 0; for (int i = 0; i < count; i++) sum += samples[i];
                json.WritePropertyName("performance"); json.WriteStartObject();
                json.WritePropertyName("samples"); json.WriteValue(count);
                json.WritePropertyName("meanFrameMs"); json.WriteValue(count == 0 ? 0 : sum / count);
                json.WritePropertyName("note"); json.WriteValue("Android开发包帧间隔，含诊断落盘开销；完整MCP快照仅在Editor采集");
                json.WriteEndObject(); json.WriteEndObject();
                json.Flush();
                return buffer.ToString();
            }
        }
        public object Snapshot()
        {
            var copy = new float[count]; Array.Copy(samples, copy, count); Array.Sort(copy); double sum = 0; foreach (float n in copy) sum += n;
            return new { samples = count, meanFrameMs = count == 0 ? 0 : sum / count, p95FrameMs = count == 0 ? 0 : copy[(int)((count - 1) * .95f)], platform = Application.platform.ToString(), width = Screen.width, height = Screen.height, note = Application.isEditor ? "编辑器帧间隔，不能作为手机性能结论" : "Android开发包帧间隔，含诊断落盘开销" };
        }
    }
}
