using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LittleGame.Client.SkillTimeline
{
    public interface ISkillTimelineActorHost
    {
        bool TryGetUnitPosition(int unitId, out Vector3 position);
        CharacterMotion GetUnitMotion(int unitId);
        Vector3 GetLaneCenter(int lane);
        Camera GetCamera();
        Vector3 GetCameraBasePosition();
        void SetSceneDarken(float strength);
    }

    public readonly struct SkillTriggerContext
    {
        public readonly int CasterId;
        public readonly int TargetId;
        public readonly int Lane;
        public readonly string UnitKind;
        public readonly string Trigger;
        public readonly string Branch;
        public readonly int Star;
        public readonly Vector3 FallbackTargetPos;

        public SkillTriggerContext(int casterId, int targetId, int lane, string unitKind, string trigger, string branch, int star, Vector3 fallbackTargetPos)
        {
            CasterId = casterId;
            TargetId = targetId;
            Lane = lane;
            UnitKind = unitKind;
            Trigger = trigger;
            Branch = branch;
            Star = star;
            FallbackTargetPos = fallbackTargetPos;
        }
    }

    /// <summary>管理多单位并发技能时间轴实例、3D 动作、特效、弹道与轻震屏；不修改规则结算。</summary>
    public sealed class SkillTimelineDirector : MonoBehaviour
    {
        private const int MaxConcurrentPlays = 48;

        private sealed class ActivePlay
        {
            public string TimelineId;
            public SkillTriggerContext Context;
            public Vector3 CasterStart;
            public Vector3 TargetStart;
            public SkillTimelinePlayer Player;
            public readonly Dictionary<string, GameObject> ClipObjects = new Dictionary<string, GameObject>();
            public readonly HashSet<string> FiredClips = new HashSet<string>();
            public float ShakeOffsetAmp;
            public float ShakeFreq;
            public float ShakeLocalTime;
            public float DarkenStrength;
        }

        private ISkillTimelineActorHost host;
        private readonly SkillTimelineLibrary library = new SkillTimelineLibrary();
        private readonly List<ActivePlay> active = new List<ActivePlay>();
        private readonly Queue<string> recentTriggered = new Queue<string>();
        private readonly Dictionary<string, Material> materialCache = new Dictionary<string, Material>();
        private readonly Dictionary<string, GameObject> prefabCache = new Dictionary<string, GameObject>();
        private Mesh ringMesh;
        private Transform fxRoot;
        private int totalTriggered;

        public SkillTimelineLibrary Library => library;
        public int ActiveCount => active.Count;
        public int TotalTriggered => totalTriggered;

        public void Initialize(ISkillTimelineActorHost actorHost)
        {
            host = actorHost;
            if (fxRoot == null)
            {
                fxRoot = new GameObject("SkillTimelineFx").transform;
                fxRoot.SetParent(transform, false);
            }
            library.Reload();
        }

        public void ReloadLibrary()
        {
            ClearAll();
            library.Reload();
        }

        public bool Trigger(SkillTriggerContext context)
        {
            string timelineId = library.ResolveTimelineId(context.UnitKind, context.Trigger, context.Branch, context.Star);
            if (string.IsNullOrWhiteSpace(timelineId)) return false;
            return PlayTimeline(timelineId, context);
        }

        public bool PlayTimeline(string timelineId, SkillTriggerContext context)
        {
            var config = library.Get(timelineId);
            if (config == null || host == null) return false;

            if (active.Count >= MaxConcurrentPlays)
            {
                StopPlay(active[0]);
                active.RemoveAt(0);
            }

            Vector3 casterPos = host.TryGetUnitPosition(context.CasterId, out var cp) ? cp : host.GetLaneCenter(context.Lane);
            Vector3 targetPos = host.TryGetUnitPosition(context.TargetId, out var tp) ? tp : context.FallbackTargetPos;

            var play = new ActivePlay
            {
                TimelineId = timelineId,
                Context = context,
                CasterStart = casterPos,
                TargetStart = targetPos
            };

            try
            {
                play.Player = new SkillTimelinePlayer(
                    config,
                    (clip, localTime) => SampleClip(play, clip, localTime),
                    clip => ExitClip(play, clip),
                    clip => HandleHit(play, clip),
                    () => ResetPlayVisuals(play));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SkillTimeline] 播放 {timelineId} 校验失败: {ex.Message}");
                return false;
            }

            active.Add(play);
            totalTriggered++;
            recentTriggered.Enqueue(timelineId);
            while (recentTriggered.Count > 24) recentTriggered.Dequeue();

            play.Player.Play();
            if (play.Player.State == SkillPlaybackState.Completed)
            {
                StopPlay(play);
                active.Remove(play);
            }
            return true;
        }

        public void Step(float deltaTime, bool paused)
        {
            if (host == null) return;
            if (!paused && deltaTime > 0f)
            {
                for (int i = active.Count - 1; i >= 0; i--)
                {
                    var play = active[i];
                    play.ShakeOffsetAmp = 0f;
                    play.DarkenStrength = 0f;
                    play.Player.Tick(deltaTime);
                    if (play.Player.State == SkillPlaybackState.Completed || play.Player.State == SkillPlaybackState.Stopped)
                    {
                        StopPlay(play);
                        active.RemoveAt(i);
                    }
                }
            }
            ApplyCameraShake(paused);
            ApplySceneDarken(paused);
        }

        public void ClearAll()
        {
            for (int i = active.Count - 1; i >= 0; i--) StopPlay(active[i]);
            active.Clear();
            ResetCameraPosition();
            host?.SetSceneDarken(0f);
        }

        private void OnDestroy()
        {
            ClearAll();
            foreach (var kv in materialCache) if (kv.Value != null) Destroy(kv.Value);
            materialCache.Clear();
            if (ringMesh != null) Destroy(ringMesh);
        }

        public object Snapshot()
        {
            var report = library.ValidateAll();
            return new
            {
                loadedBindings = report.BindingCount,
                loadedTimelines = report.TimelineCount,
                valid = report.Valid,
                validationErrors = report.Errors.Count,
                activeCount = active.Count,
                totalTriggered,
                recentTriggered = recentTriggered.ToArray()
            };
        }

        private void SampleClip(ActivePlay play, SkillScheduledClip scheduled, float localTime)
        {
            var cfg = scheduled.Config;
            float duration = Math.Max(0.001f, scheduled.ResolvedDuration);
            float t = Mathf.Clamp01(localTime / duration);

            switch (cfg.Type)
            {
                case SkillClipType.PlayAnimation:
                    if (play.FiredClips.Add(cfg.ClipKey))
                    {
                        int unitId = cfg.TargetAnchor == SkillAnchorType.Target ? play.Context.TargetId : play.Context.CasterId;
                        var motion = host.GetUnitMotion(unitId);
                        motion?.PlayAction(cfg.ActionName, cfg.Speed);
                    }
                    break;

                case SkillClipType.PlayBullet:
                    SampleBullet(play, cfg, t);
                    break;

                case SkillClipType.PlayEffect:
                    SampleEffect(play, cfg, t);
                    break;

                case SkillClipType.PlayHit:
                    SampleHitVisual(play, cfg, t);
                    break;

                case SkillClipType.CameraShake:
                    float fade = 1f - t;
                    play.ShakeOffsetAmp = Math.Max(play.ShakeOffsetAmp, cfg.Amplitude * fade);
                    play.ShakeFreq = cfg.Frequency;
                    play.ShakeLocalTime = localTime;
                    break;

                case SkillClipType.SceneDarken:
                    float strength = Mathf.Clamp01(Mathf.Lerp(cfg.StartStrength, cfg.EndStrength, t));
                    play.DarkenStrength = Math.Max(play.DarkenStrength, strength);
                    break;

                case SkillClipType.HitStop:
                    if (play.FiredClips.Add(cfg.ClipKey))
                    {
                        int stopId = cfg.TargetAnchor == SkillAnchorType.Caster ? play.Context.CasterId : play.Context.TargetId;
                        host.GetUnitMotion(stopId)?.TriggerHitStop(scheduled.ResolvedDuration);
                    }
                    break;
            }
        }

        private void HandleHit(ActivePlay play, SkillScheduledClip scheduled)
        {
            var cfg = scheduled.Config;
            int unitId = cfg.TargetAnchor == SkillAnchorType.Caster ? play.Context.CasterId : play.Context.TargetId;
            var motion = host.GetUnitMotion(unitId);
            if (motion != null)
            {
                if (!string.IsNullOrWhiteSpace(cfg.HitActionName))
                    motion.PlayAction(cfg.HitActionName, 1f);
                Color flash = SkillTimelineJson.ParseColor(cfg.FlashColorHex, Color.white);
                motion.TriggerHitFeedback(cfg.FlashDuration > 0f ? cfg.FlashDuration : 0.12f, flash);
            }
        }

        private void SampleBullet(ActivePlay play, SkillClipConfig cfg, float t)
        {
            var go = GetOrCreateClipObject(play, cfg, true);
            if (go == null) return;

            Vector3 from = ResolveAnchor(play, cfg.FromAnchor, false) + Vector3.up * cfg.StartHeight;
            Vector3 to = ResolveAnchor(play, cfg.ToAnchor, true) + Vector3.up * cfg.EndHeight;
            Vector3 pos = Vector3.Lerp(from, to, t);
            pos.y += Mathf.Sin(t * Mathf.PI) * cfg.ArcHeight;
            go.transform.position = pos;

            Vector3 dir = to - from;
            if (dir.sqrMagnitude > 0.0001f)
                go.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        }

        private void SampleEffect(ActivePlay play, SkillClipConfig cfg, float t)
        {
            var go = GetOrCreateClipObject(play, cfg, false);
            if (go == null) return;

            Vector3 anchor = cfg.FollowAnchor
                ? ResolveAnchor(play, cfg.ToAnchor, true)
                : ResolveAnchor(play, cfg.ToAnchor, false);
            Vector3 offset = cfg.Offset != null ? cfg.Offset.ToVector3() : Vector3.zero;
            go.transform.position = anchor + offset;

            // 若使用的是内置程序化占位特效，按形状做简单缩放/展开动画提升观感。
            if (go.name.StartsWith("ProcFx_"))
            {
                float s = Math.Max(0.1f, cfg.Scale);
                switch (cfg.Shape)
                {
                    case SkillEffectShape.Ring:
                    case SkillEffectShape.SlamWave:
                        float expand = Mathf.Lerp(0.35f, 1.35f, t) * s;
                        go.transform.localScale = new Vector3(expand, 1f, expand);
                        break;
                    case SkillEffectShape.Zone:
                        float pulse = (0.92f + 0.1f * Mathf.Sin(t * Mathf.PI * 4f)) * s;
                        go.transform.localScale = new Vector3(pulse, 1f, pulse);
                        break;
                    case SkillEffectShape.Beam:
                        float beamWidth = Mathf.Lerp(1.1f, 0.25f, t) * s;
                        go.transform.localScale = new Vector3(beamWidth, s * 2.4f, beamWidth);
                        break;
                    case SkillEffectShape.Aura:
                        float aura = (0.85f + 0.2f * Mathf.Sin(t * Mathf.PI)) * s;
                        go.transform.localScale = Vector3.one * aura;
                        break;
                    default:
                        float burst = Mathf.Sin(t * Mathf.PI) * s;
                        go.transform.localScale = Vector3.one * Mathf.Max(0.05f, burst);
                        break;
                }
            }
        }

        private void SampleHitVisual(ActivePlay play, SkillClipConfig cfg, float t)
        {
            if (string.IsNullOrWhiteSpace(cfg.EffectId) && cfg.Scale <= 0f) return;
            var go = GetOrCreateClipObject(play, cfg, false);
            if (go == null) return;
            Vector3 pos = ResolveAnchor(play, SkillAnchorType.Target, true) + (cfg.Offset != null ? cfg.Offset.ToVector3() : Vector3.up * 0.65f);
            go.transform.position = pos;
            if (go.name.StartsWith("ProcFx_"))
            {
                float s = Mathf.Sin(t * Mathf.PI) * Math.Max(0.25f, cfg.Scale);
                go.transform.localScale = Vector3.one * Mathf.Max(0.05f, s);
            }
        }

        private GameObject GetOrCreateClipObject(ActivePlay play, SkillClipConfig cfg, bool isBullet)
        {
            if (play.ClipObjects.TryGetValue(cfg.ClipKey, out var existing) && existing != null)
                return existing;

            GameObject go = null;
            if (!string.IsNullOrWhiteSpace(cfg.EffectId))
            {
                var prefab = LoadEffectPrefab(cfg.EffectId);
                if (prefab != null)
                {
                    go = Instantiate(prefab, fxRoot);
                    go.name = "Fx_" + cfg.EffectId;
                    go.transform.localScale *= Math.Max(0.1f, cfg.Scale);
                    StripColliders(go);
                }
            }

            if (go == null)
            {
                go = CreateProceduralFx(cfg, isBullet);
                go.transform.SetParent(fxRoot, false);
            }

            play.ClipObjects[cfg.ClipKey] = go;
            return go;
        }

        private GameObject LoadEffectPrefab(string effectId)
        {
            if (prefabCache.TryGetValue(effectId, out var cached)) return cached;
            var loaded = Resources.Load<GameObject>("SkillEffects/" + effectId);
            prefabCache[effectId] = loaded;
            return loaded;
        }

        private GameObject CreateProceduralFx(SkillClipConfig cfg, bool isBullet)
        {
            Color color = SkillTimelineJson.ParseColor(cfg.ColorHex, new Color(0.95f, 0.78f, 0.32f, 0.9f));
            GameObject go;

            if (isBullet)
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.name = "ProcFx_Bullet_" + cfg.ClipKey;
                StripColliders(go);
                float w = Math.Max(0.05f, cfg.Width);
                go.transform.localScale = new Vector3(w, w * 2.2f, w);
                // 胶囊默认沿 Y 轴，套一层父节点使其沿 Z 轴朝前。
                var root = new GameObject("ProcFx_BulletRoot_" + cfg.ClipKey);
                go.transform.SetParent(root.transform, false);
                go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                go.GetComponent<Renderer>().sharedMaterial = GetMaterial(color);
                return root;
            }

            if (cfg.Shape == SkillEffectShape.Ring || cfg.Shape == SkillEffectShape.Zone || cfg.Shape == SkillEffectShape.SlamWave)
            {
                go = new GameObject("ProcFx_Ring_" + cfg.ClipKey);
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = GetRingMesh();
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = GetMaterial(color);
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                return go;
            }

            if (cfg.Shape == SkillEffectShape.Beam)
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                go.name = "ProcFx_Beam_" + cfg.ClipKey;
                StripColliders(go);
                go.GetComponent<Renderer>().sharedMaterial = GetMaterial(color);
                return go;
            }

            go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "ProcFx_Burst_" + cfg.ClipKey;
            StripColliders(go);
            go.GetComponent<Renderer>().sharedMaterial = GetMaterial(color);
            return go;
        }

        private void ExitClip(ActivePlay play, SkillScheduledClip scheduled)
        {
            if (play.ClipObjects.TryGetValue(scheduled.Config.ClipKey, out var go))
            {
                if (go != null) Destroy(go);
                play.ClipObjects.Remove(scheduled.Config.ClipKey);
            }
        }

        private void ResetPlayVisuals(ActivePlay play)
        {
            foreach (var kv in play.ClipObjects)
                if (kv.Value != null) Destroy(kv.Value);
            play.ClipObjects.Clear();
            play.FiredClips.Clear();
            play.ShakeOffsetAmp = 0f;
        }

        private void StopPlay(ActivePlay play)
        {
            ResetPlayVisuals(play);
            play.Player?.Stop();
        }

        private Vector3 ResolveAnchor(ActivePlay play, SkillAnchorType anchor, bool live)
        {
            switch (anchor)
            {
                case SkillAnchorType.Caster:
                    if (live && host.TryGetUnitPosition(play.Context.CasterId, out var c)) return c;
                    return play.CasterStart;
                case SkillAnchorType.Target:
                    if (live && host.TryGetUnitPosition(play.Context.TargetId, out var t)) return t;
                    return play.TargetStart;
                case SkillAnchorType.TargetGround:
                    Vector3 g = live && host.TryGetUnitPosition(play.Context.TargetId, out var tg) ? tg : play.TargetStart;
                    return new Vector3(g.x, 0.14f, g.z);
                case SkillAnchorType.LaneCenter:
                default:
                    return host.GetLaneCenter(play.Context.Lane);
            }
        }

        private void ApplyCameraShake(bool paused)
        {
            var cam = host?.GetCamera();
            if (cam == null) return;
            Vector3 basePos = host.GetCameraBasePosition();
            if (paused || active.Count == 0)
            {
                cam.transform.position = basePos;
                return;
            }

            float maxAmp = 0f;
            float freq = 24f;
            float t = 0f;
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i].ShakeOffsetAmp > maxAmp)
                {
                    maxAmp = active[i].ShakeOffsetAmp;
                    freq = active[i].ShakeFreq;
                    t = active[i].ShakeLocalTime;
                }
            }

            if (maxAmp <= 0.0005f)
            {
                cam.transform.position = basePos;
                return;
            }

            float ox = Mathf.Sin(t * freq * Mathf.PI * 2f) * maxAmp;
            float oz = Mathf.Cos(t * freq * Mathf.PI * 1.6f) * maxAmp * 0.65f;
            cam.transform.position = basePos + new Vector3(ox, 0f, oz);
        }

        private void ApplySceneDarken(bool paused)
        {
            if (host == null) return;
            if (paused || active.Count == 0)
            {
                host.SetSceneDarken(0f);
                return;
            }
            float maxDarken = 0f;
            for (int i = 0; i < active.Count; i++)
                if (active[i].DarkenStrength > maxDarken) maxDarken = active[i].DarkenStrength;
            host.SetSceneDarken(maxDarken);
        }

        private void ResetCameraPosition()
        {
            var cam = host?.GetCamera();
            if (cam != null) cam.transform.position = host.GetCameraBasePosition();
        }

        private Material GetMaterial(Color color)
        {
            string key = ColorUtility.ToHtmlStringRGBA(color);
            if (materialCache.TryGetValue(key, out var mat) && mat != null) return mat;
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            mat = new Material(shader) { color = color };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            materialCache[key] = mat;
            return mat;
        }

        private Mesh GetRingMesh()
        {
            if (ringMesh != null) return ringMesh;
            const int seg = 28;
            const float inner = 0.72f;
            const float outer = 1.0f;
            var verts = new Vector3[seg * 2];
            var tris = new int[seg * 6];
            for (int i = 0; i < seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                float c = Mathf.Cos(a);
                float s = Mathf.Sin(a);
                verts[i * 2] = new Vector3(c * inner, 0f, s * inner);
                verts[i * 2 + 1] = new Vector3(c * outer, 0f, s * outer);
                int n = (i + 1) % seg;
                int t = i * 6;
                tris[t] = i * 2; tris[t + 1] = n * 2 + 1; tris[t + 2] = i * 2 + 1;
                tris[t + 3] = i * 2; tris[t + 4] = n * 2; tris[t + 5] = n * 2 + 1;
            }
            ringMesh = new Mesh { name = "SkillTimelineRing" };
            ringMesh.vertices = verts;
            ringMesh.triangles = tris;
            ringMesh.RecalculateNormals();
            return ringMesh;
        }

        private static void StripColliders(GameObject go)
        {
            foreach (var col in go.GetComponentsInChildren<Collider>(true))
                Destroy(col);
        }
    }
}
