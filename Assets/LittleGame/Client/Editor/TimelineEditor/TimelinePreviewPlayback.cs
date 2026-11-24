using System;
using System.Collections.Generic;
using System.IO;
using LittleGame.Client.SkillTimeline;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace LittleGame.Client.Editor.Timeline
{
    /// <summary>
    /// 独立技能预览场景宿主与非 Play 逐帧预览引擎（对标 Demo TimelinePreviewPlayback + TimelineEditor.unity）：
    /// 1. 负责自动创建、切换与恢复专用预览场景 TimelineEditor.unity；
    /// 2. 在非 Play 模式下自动摆放 3D 施法者、敌方目标、庭院舞台与预览相机；
    /// 3. 与实战共用 SkillTimelinePlayer.Evaluate(time)，对 3D 骨架动画、弹道、粒子系统与相机震屏做确定性逐帧采样。
    /// </summary>
    public sealed class TimelinePreviewPlayback
    {
        public const string PreviewScenePath = "Assets/LittleGame/Client/Editor/TimelineEditor/Scene/TimelineEditor.unity";
        public const string PreviousScenePrefsKey = "LittleGame.EditorTimeLine.PreviousScenePath";

        public static readonly string[] PreviewRoles =
        {
            "Shield", "Archer", "Storm", "Frost", "Grunt", "Runner", "Armored", "Healer", "Giant"
        };

        private static readonly Vector3 CameraBasePos = new Vector3(0f, 9.2f, -7.2f);
        private static readonly Vector3 CameraLookTarget = new Vector3(0f, 0.5f, 0.2f);
        private static readonly Vector3 CasterPos = new Vector3(0f, 0.11f, -1.65f);
        private static readonly Vector3 TargetPos = new Vector3(0f, 0.11f, 1.85f);

        private GameObject stageRoot;
        private Camera previewCamera;
        private GameObject casterObj;
        private GameObject targetObj;
        private Animation casterAnim;
        private Animation targetAnim;
        private Vector3 casterBaseScale = Vector3.one;
        private Vector3 targetBaseScale = Vector3.one;
        private UnitAppearance casterAppearance;
        private UnitAppearance targetAppearance;

        private string casterRole = "Archer";
        private string targetRole = "Grunt";
        private bool casterIsEnemy;

        private SkillTimelinePlayer player;
        private string cachedJson;
        private string rejectedJson;
        private float sampledTime = -1f;
        private bool dirty = true;

        private readonly Dictionary<string, GameObject> spawnedFx = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, Material> procMaterials = new Dictionary<string, Material>();
        private float shakeAmp;
        private float shakeFreq = 26f;
        private float shakeTime;
        private float previewDarken;
        private Light previewSun;
        private bool casterAnimatedThisFrame;
        private bool targetAnimatedThisFrame;

        public string CasterRole => casterRole;
        public string TargetRole => targetRole;
        public bool CasterIsEnemy => casterIsEnemy;

        public static bool IsPreviewSceneActive() =>
            SceneManager.GetActiveScene().path == PreviewScenePath;

        /// <summary>确保专用预览场景存在并切换进去，同时记住原场景路径供关闭时恢复。</summary>
        public static bool OpenPreviewSceneIfNeeded()
        {
            if (Application.isPlaying) return false;
            EnsurePreviewSceneAssetExists();

            var active = SceneManager.GetActiveScene();
            if (active.path == PreviewScenePath) return true;

            if (!string.IsNullOrEmpty(active.path))
                EditorPrefs.SetString(PreviousScenePrefsKey, active.path);

            if (active.isDirty && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return false;

            EditorSceneManager.OpenScene(PreviewScenePath, OpenSceneMode.Single);
            return true;
        }

        /// <summary>关闭技能编辑器时自动恢复打开前的场景。</summary>
        public static void RestorePreviousSceneIfNeeded()
        {
            if (Application.isPlaying || !IsPreviewSceneActive()) return;
            string prev = EditorPrefs.GetString(PreviousScenePrefsKey, "");
            if (!string.IsNullOrEmpty(prev) && File.Exists(prev))
                EditorSceneManager.OpenScene(prev, OpenSceneMode.Single);
        }

        private static void EnsurePreviewSceneAssetExists()
        {
            if (File.Exists(PreviewScenePath)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(PreviewScenePath));

            var prevActive = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);

            var camGo = new GameObject("BattleCamera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = false;
            cam.fieldOfView = 34f;
            cam.transform.position = CameraBasePos;
            cam.transform.LookAt(CameraLookTarget);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.11f, 0.11f, 0.15f, 1f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 80f;

            var sunGo = new GameObject("PreviewSun");
            var light = sunGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.96f, 0.88f);
            light.intensity = 1.2f;
            light.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.Euler(50f, -35f, 0f);

            EditorSceneManager.SaveScene(scene, PreviewScenePath);
            EditorSceneManager.CloseScene(scene, true);
            if (prevActive.IsValid()) SceneManager.SetActiveScene(prevActive);
            AssetDatabase.ImportAsset(PreviewScenePath);
        }

        public void EnsureStage()
        {
            if (Application.isPlaying || !IsPreviewSceneActive()) return;

            // 查找或创建预览相机
            if (previewCamera == null)
            {
                var camGo = GameObject.Find("BattleCamera");
                previewCamera = camGo != null ? camGo.GetComponent<Camera>() : Camera.main;
            }
            if (previewSun == null)
            {
                var sunGo = GameObject.Find("PreviewSun");
                if (sunGo != null) previewSun = sunGo.GetComponent<Light>();
            }
            if (previewCamera != null)
            {
                previewCamera.allowHDR = true;
                var urpCam = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(previewCamera);
                urpCam.renderPostProcessing = true;
                previewCamera.transform.position = CameraBasePos;
                previewCamera.transform.LookAt(CameraLookTarget);
            }

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.64f, 0.70f, 0.80f);
            RenderSettings.ambientEquatorColor = new Color(0.56f, 0.58f, 0.61f);
            RenderSettings.ambientGroundColor = new Color(0.31f, 0.34f, 0.39f);

            if (stageRoot == null)
            {
                var existing = GameObject.Find("__TimelinePreviewStage");
                if (existing != null) UnityEngine.Object.DestroyImmediate(existing);
                stageRoot = new GameObject("__TimelinePreviewStage") { hideFlags = HideFlags.DontSave };
                var vol = BattleSceneView.SetupSelectiveEffectBloom(stageRoot.transform);
                vol.gameObject.hideFlags = HideFlags.DontSave;
                BuildPreviewFloor(stageRoot.transform);
                RebuildPreviewActors();
                FocusSceneViewOnStage();
            }
        }

        public void SetPreviewRoles(string caster, string target, bool enemyTeam)
        {
            bool changed = casterRole != caster || targetRole != target || casterIsEnemy != enemyTeam;
            casterRole = string.IsNullOrEmpty(caster) ? "Archer" : caster;
            targetRole = string.IsNullOrEmpty(target) ? "Grunt" : target;
            casterIsEnemy = enemyTeam;
            if (changed && stageRoot != null)
            {
                Stop();
                RebuildPreviewActors();
                Invalidate();
            }
        }

        /// <summary>根据当前加载的技能 JSON 自动推导最匹配的预览施法者与靶子角色。</summary>
        public void AutoSelectRolesForTimeline(string timelineId, SkillTimelineLibrary library)
        {
            if (string.IsNullOrEmpty(timelineId) || library?.Bindings?.Bindings == null) return;
            foreach (var b in library.Bindings.Bindings)
            {
                if (b == null || b.TimelineId != timelineId) continue;
                if (b.UnitKind == "*") continue;
                bool isEnemy = b.UnitKind == "Grunt" || b.UnitKind == "Runner" || b.UnitKind == "Armored" || b.UnitKind == "Healer" || b.UnitKind == "Giant";
                string target = isEnemy ? "Shield" : "Grunt";
                SetPreviewRoles(b.UnitKind, target, isEnemy);
                return;
            }
        }

        public void Invalidate()
        {
            dirty = true;
            sampledTime = -1f;
        }

        public void Evaluate(SkillTimelineConfig config, float time)
        {
            if (Application.isPlaying || !IsPreviewSceneActive()) return;
            EnsureStage();
            if (config == null || config.Clips == null || config.Clips.Count == 0 || time < 0f)
            {
                Stop();
                return;
            }

            string currentJson = dirty || player == null ? SkillTimelineJson.Serialize(config) : cachedJson;
            dirty = false;
            if (player == null || cachedJson != currentJson)
            {
                ClearSpawnedFx();
                player = null;
                if (currentJson == rejectedJson) return;
                try
                {
                    player = new SkillTimelinePlayer(
                        config,
                        SampleClip,
                        ExitClip,
                        null,
                        ResetSampleState);
                    cachedJson = currentJson;
                    rejectedJson = null;
                    sampledTime = -1f;
                }
                catch
                {
                    rejectedJson = currentJson;
                    player = null;
                    return;
                }
            }

            if (Mathf.Approximately(sampledTime, time)) return;

            shakeAmp = 0f;
            previewDarken = 0f;
            casterAnimatedThisFrame = false;
            targetAnimatedThisFrame = false;
            ResetActorBaseTransform();

            player.Evaluate(time);

            // 若当前时刻未播放攻击/受击动作，采样默认 Idle 姿态，避免角色卡死在最后一帧
            if (!casterAnimatedThisFrame) SampleRoleAnimation(casterAnim, casterAppearance, "Idle", time, 1f);
            if (!targetAnimatedThisFrame) SampleRoleAnimation(targetAnim, targetAppearance, "Idle", time, 1f);
            ApplyPreviewCameraShake();
            ApplyPreviewDarken(previewDarken);

            sampledTime = time;
            SceneView.RepaintAll();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        public void Stop()
        {
            player?.Stop();
            player = null;
            sampledTime = -1f;
            shakeAmp = 0f;
            previewDarken = 0f;
            ClearSpawnedFx();
            ResetActorBaseTransform();
            SampleRoleAnimation(casterAnim, casterAppearance, "Idle", 0f, 1f);
            SampleRoleAnimation(targetAnim, targetAppearance, "Idle", 0f, 1f);
            ApplyPreviewCameraShake();
            ApplyPreviewDarken(0f);
            SceneView.RepaintAll();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        public void Dispose()
        {
            Stop();
            if (stageRoot != null)
            {
                UnityEngine.Object.DestroyImmediate(stageRoot);
                stageRoot = null;
            }
            foreach (var kv in procMaterials)
                if (kv.Value != null) UnityEngine.Object.DestroyImmediate(kv.Value);
            procMaterials.Clear();
        }

        private void RebuildPreviewActors()
        {
            if (casterObj != null) UnityEngine.Object.DestroyImmediate(casterObj);
            if (targetObj != null) UnityEngine.Object.DestroyImmediate(targetObj);

            Vector3 cPos = casterIsEnemy ? TargetPos : CasterPos;
            Vector3 tPos = casterIsEnemy ? CasterPos : TargetPos;
            float cYaw = casterIsEnemy ? 180f : 0f;
            float tYaw = casterIsEnemy ? 0f : 180f;

            casterObj = SpawnPreviewUnit("PreviewCaster_" + casterRole, casterRole, cPos, cYaw, out casterAnim, out casterAppearance, out casterBaseScale);
            targetObj = SpawnPreviewUnit("PreviewTarget_" + targetRole, targetRole, tPos, tYaw, out targetAnim, out targetAppearance, out targetBaseScale);
            SampleRoleAnimation(casterAnim, casterAppearance, "Idle", 0f, 1f);
            SampleRoleAnimation(targetAnim, targetAppearance, "Idle", 0f, 1f);
        }

        private GameObject SpawnPreviewUnit(
            string name,
            string role,
            Vector3 worldPos,
            float yaw,
            out Animation anim,
            out UnitAppearance appearance,
            out Vector3 baseScale)
        {
            anim = null;
            baseScale = Vector3.one;
            appearance = Resources.Load<UnitAppearance>("UnitAppearances/" + role);

            var root = new GameObject(name) { hideFlags = HideFlags.DontSave };
            root.transform.SetParent(stageRoot.transform, false);
            root.transform.position = worldPos;
            root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            if (appearance != null && appearance.Model != null)
            {
                var model = UnityEngine.Object.Instantiate(appearance.Model, root.transform);
                model.name = "Model_" + role;
                model.hideFlags = HideFlags.DontSave;
                var renderers = model.GetComponentsInChildren<Renderer>();
                Bounds bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(Vector3.zero, Vector3.one);
                foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                float scale = appearance.Height / Mathf.Max(0.01f, bounds.size.y);
                model.transform.localScale *= scale;
                model.transform.localPosition = new Vector3(0f, -bounds.min.y * scale, 0f);
                baseScale = model.transform.localScale;

                anim = model.GetComponent<Animation>() ?? model.AddComponent<Animation>();
                AddClip(anim, "Idle", appearance.Idle, true);
                AddClip(anim, "Walk", appearance.Move, true);
                AddClip(anim, "Attack", appearance.Attack, false);
                AddClip(anim, "Hit", appearance.Hit, false);
                AddClip(anim, "Death", appearance.Death, false);
                AddClip(anim, "Slam", appearance.Slam, false);
            }
            return root;
        }

        private static void AddClip(Animation anim, string alias, AnimationClip clip, bool loop)
        {
            if (anim == null || clip == null) return;
            anim.AddClip(clip, alias);
            anim[alias].wrapMode = loop ? WrapMode.Loop : WrapMode.Once;
        }

        private static void SampleRoleAnimation(Animation anim, UnitAppearance appearance, string alias, float time, float speed)
        {
            if (anim == null || string.IsNullOrEmpty(alias)) return;
            var state = anim[alias];
            if (state == null) return;

            foreach (AnimationState s in anim) s.enabled = false;
            float mult = alias == "Attack" && appearance != null ? appearance.AttackSpeed * Mathf.Max(0.1f, speed) : Mathf.Max(0.1f, speed);
            float sampleT = time * mult;
            if (state.wrapMode == WrapMode.Loop && state.length > 0.001f)
                sampleT %= state.length;
            else
                sampleT = Mathf.Clamp(sampleT, 0f, state.length);

            state.enabled = true;
            state.weight = 1f;
            state.time = sampleT;
            anim.Sample();
        }

        private void SampleClip(SkillScheduledClip scheduled, float localTime)
        {
            var cfg = scheduled.Config;
            float duration = Mathf.Max(0.001f, scheduled.ResolvedDuration);
            float t = Mathf.Clamp01(localTime / duration);

            switch (cfg.Type)
            {
                case SkillClipType.PlayAnimation:
                    if (cfg.TargetAnchor == SkillAnchorType.Target)
                    {
                        SampleRoleAnimation(targetAnim, targetAppearance, cfg.ActionName, localTime, cfg.Speed);
                        targetAnimatedThisFrame = true;
                    }
                    else
                    {
                        SampleRoleAnimation(casterAnim, casterAppearance, cfg.ActionName, localTime, cfg.Speed);
                        casterAnimatedThisFrame = true;
                    }
                    break;

                case SkillClipType.PlayBullet:
                {
                    var go = GetOrSpawnFx(cfg, true);
                    Vector3 from = ResolveAnchor(cfg.FromAnchor) + Vector3.up * cfg.StartHeight;
                    Vector3 to = ResolveAnchor(cfg.ToAnchor) + Vector3.up * cfg.EndHeight;
                    Vector3 pos = Vector3.Lerp(from, to, t);
                    pos.y += Mathf.Sin(t * Mathf.PI) * cfg.ArcHeight;
                    go.transform.position = pos;
                    Vector3 dir = to - from;
                    if (dir.sqrMagnitude > 0.0001f)
                        go.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
                    SimulateParticles(go, localTime);
                    break;
                }

                case SkillClipType.PlayEffect:
                {
                    var go = GetOrSpawnFx(cfg, false);
                    Vector3 offset = cfg.Offset != null ? cfg.Offset.ToVector3() : Vector3.zero;
                    go.transform.position = ResolveAnchor(cfg.ToAnchor) + offset;
                    SimulateParticles(go, localTime);
                    break;
                }

                case SkillClipType.PlayHit:
                {
                    if (!string.IsNullOrEmpty(cfg.HitActionName))
                    {
                        SampleRoleAnimation(targetAnim, targetAppearance, cfg.HitActionName, localTime, 1f);
                        targetAnimatedThisFrame = true;
                    }
                    if (targetAnim != null)
                    {
                        float pulseDur = Mathf.Max(0.04f, cfg.FlashDuration);
                        float pt = Mathf.Clamp01(localTime / pulseDur);
                        float k = Mathf.Sin(pt * Mathf.PI);
                        targetAnim.transform.localScale = targetBaseScale * (1f + 0.14f * k);
                    }
                    if (!string.IsNullOrEmpty(cfg.EffectId))
                    {
                        var go = GetOrSpawnFx(cfg, false);
                        Vector3 offset = cfg.Offset != null ? cfg.Offset.ToVector3() : Vector3.up * 0.65f;
                        go.transform.position = ResolveAnchor(SkillAnchorType.Target) + offset;
                        SimulateParticles(go, localTime);
                    }
                    break;
                }

                case SkillClipType.CameraShake:
                    shakeAmp = Mathf.Max(shakeAmp, cfg.Amplitude * (1f - t));
                    shakeFreq = cfg.Frequency;
                    shakeTime = localTime;
                    break;

                case SkillClipType.SceneDarken:
                    previewDarken = Mathf.Max(previewDarken, Mathf.Clamp01(Mathf.Lerp(cfg.StartStrength, cfg.EndStrength, t)));
                    break;

                case SkillClipType.HitStop:
                    break;
            }
        }

        private void ExitClip(SkillScheduledClip scheduled)
        {
            if (spawnedFx.TryGetValue(scheduled.Config.ClipKey, out var go))
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
                spawnedFx.Remove(scheduled.Config.ClipKey);
            }
        }

        private void ResetSampleState()
        {
            ClearSpawnedFx();
            ResetActorBaseTransform();
        }

        private void ClearSpawnedFx()
        {
            foreach (var kv in spawnedFx)
                if (kv.Value != null) UnityEngine.Object.DestroyImmediate(kv.Value);
            spawnedFx.Clear();
        }

        private void ResetActorBaseTransform()
        {
            if (casterAnim != null) casterAnim.transform.localScale = casterBaseScale;
            if (targetAnim != null) targetAnim.transform.localScale = targetBaseScale;
        }

        private GameObject GetOrSpawnFx(SkillClipConfig cfg, bool isBullet)
        {
            if (spawnedFx.TryGetValue(cfg.ClipKey, out var existing) && existing != null)
                return existing;

            GameObject go = null;
            if (!string.IsNullOrWhiteSpace(cfg.EffectId))
            {
                var prefab = Resources.Load<GameObject>("SkillEffects/" + cfg.EffectId);
                if (prefab != null)
                {
                    go = UnityEngine.Object.Instantiate(prefab, stageRoot.transform);
                    go.name = "PreviewFx_" + cfg.EffectId;
                    go.hideFlags = HideFlags.DontSave;
                    go.transform.localScale *= Mathf.Max(0.1f, cfg.Scale);
                }
            }

            if (go == null)
            {
                go = GameObject.CreatePrimitive(isBullet ? PrimitiveType.Capsule : PrimitiveType.Sphere);
                go.name = "PreviewProcFx_" + cfg.ClipKey;
                go.hideFlags = HideFlags.DontSave;
                go.transform.SetParent(stageRoot.transform, false);
                go.transform.localScale = Vector3.one * Mathf.Max(0.15f, cfg.Scale * 0.45f);
                var col = go.GetComponent<Collider>();
                if (col != null) UnityEngine.Object.DestroyImmediate(col);
                var mat = GetProcMaterial(SkillTimelineJson.ParseColor(cfg.ColorHex, Color.yellow));
                go.GetComponent<Renderer>().sharedMaterial = mat;
            }

            spawnedFx[cfg.ClipKey] = go;
            return go;
        }

        private static void SimulateParticles(GameObject go, float localTime)
        {
            if (go == null) return;
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                ps.Simulate(Mathf.Max(0f, localTime), false, true, false);
        }

        private Vector3 ResolveAnchor(SkillAnchorType anchor)
        {
            Vector3 c = casterObj != null ? casterObj.transform.position : CasterPos;
            Vector3 t = targetObj != null ? targetObj.transform.position : TargetPos;
            switch (anchor)
            {
                case SkillAnchorType.Caster: return c;
                case SkillAnchorType.Target: return t;
                case SkillAnchorType.TargetGround: return new Vector3(t.x, 0.14f, t.z);
                case SkillAnchorType.LaneCenter:
                default: return new Vector3(0f, 0.14f, 0.1f);
            }
        }

        private void ApplyPreviewDarken(float strength)
        {
            float factor = Mathf.Lerp(1f, 0.28f, Mathf.Clamp01(strength));
            if (previewSun != null) previewSun.intensity = 1.2f * factor;
            RenderSettings.ambientSkyColor = new Color(0.64f, 0.70f, 0.80f) * factor;
            RenderSettings.ambientEquatorColor = new Color(0.56f, 0.58f, 0.61f) * factor;
            RenderSettings.ambientGroundColor = new Color(0.31f, 0.34f, 0.39f) * factor;
            if (previewCamera != null) previewCamera.backgroundColor = new Color(0.11f, 0.11f, 0.15f, 1f) * factor;
        }

        private void ApplyPreviewCameraShake()
        {
            if (previewCamera == null) return;
            if (shakeAmp <= 0.0005f)
            {
                previewCamera.transform.position = CameraBasePos;
                previewCamera.transform.LookAt(CameraLookTarget);
                return;
            }
            float ox = Mathf.Sin(shakeTime * shakeFreq * Mathf.PI * 2f) * shakeAmp;
            float oz = Mathf.Cos(shakeTime * shakeFreq * Mathf.PI * 1.6f) * shakeAmp * 0.65f;
            previewCamera.transform.position = CameraBasePos + new Vector3(ox, 0f, oz);
            previewCamera.transform.LookAt(CameraLookTarget);
        }

        private void BuildPreviewFloor(Transform parent)
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "PreviewCourtyardFloor";
            floor.hideFlags = HideFlags.DontSave;
            floor.transform.SetParent(parent, false);
            floor.transform.position = new Vector3(0f, -0.05f, 0.1f);
            floor.transform.localScale = new Vector3(8.4f, 0.2f, 8.8f);
            var col = floor.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.DestroyImmediate(col);
            floor.GetComponent<Renderer>().sharedMaterial = GetProcMaterial(new Color(0.32f, 0.34f, 0.38f), false);

            // 中路参考线
            var lane = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lane.name = "PreviewLaneGuide";
            lane.hideFlags = HideFlags.DontSave;
            lane.transform.SetParent(parent, false);
            lane.transform.position = new Vector3(0f, 0.06f, 0.1f);
            lane.transform.localScale = new Vector3(2.4f, 0.02f, 7.6f);
            var lcol = lane.GetComponent<Collider>();
            if (lcol != null) UnityEngine.Object.DestroyImmediate(lcol);
            lane.GetComponent<Renderer>().sharedMaterial = GetProcMaterial(new Color(0.40f, 0.43f, 0.48f), false);
        }

        private Material GetProcMaterial(Color color, bool unlit = true)
        {
            string key = (unlit ? "U_" : "L_") + ColorUtility.ToHtmlStringRGBA(color);
            if (procMaterials.TryGetValue(key, out var mat) && mat != null) return mat;
            var shader = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit") ?? Shader.Find("Sprites/Default");
            mat = new Material(shader) { color = color, hideFlags = HideFlags.DontSave };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            procMaterials[key] = mat;
            return mat;
        }

        private static void FocusSceneViewOnStage()
        {
            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.LookAt(CameraLookTarget, Quaternion.Euler(40f, 0f, 0f), 8.5f);
            }
        }
    }
}
