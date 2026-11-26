using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>固定竖屏取景、测试距离和只读诊断。预览设置不写入技能，不改变实战规则。</summary>
internal static class TimelinePortraitPreview
{
    private static float[] V(Vector3 v) => new[] { v.x, v.y, v.z };
    private static void RequirePreviewScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode
            || UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != TimelineEditorSceneBuilder.ScenePath)
            throw new InvalidOperationException("请先停止 Play 并打开技能编辑器场景；竖屏预览命令不会修改实战场景。");
    }
    internal static void FrameSceneView()
    {
        if (SceneView.lastActiveSceneView)
            SceneView.lastActiveSceneView.LookAt(new Vector3(.25f, .7f, 0f),
                PortraitBattleCameraSettings.Rotation, Detail ? 6f : 13f, true);
    }
    private const string ModeKey = "LittleGame.SkillPreview.Mode";
    private const string DistanceKey = "LittleGame.SkillPreview.Distance";
    internal static bool Detail => SessionState.GetBool(ModeKey, false);
    internal static float Distance(TimelineControl control)
    {
        float configured = SessionState.GetFloat(DistanceKey, 0f);
        if (configured > 0f) return configured;
        if (control.Config?.Clips.Any(c => c is TimelineClipPlayBulletConfig) == true) return 3.6f;
        return control.Config?.PreviewCasterAddress == "Giant/Giant" ? 2.4f : 1.3f;
    }

    internal static Vector3 Position(TimelineControl control, UnitTeamType team, int index)
        => new Vector3((team == UnitTeamType.TeamA ? -1f : 1f) * Distance(control) * 0.5f,
            0.11f, index == 0 ? 0f : index == 1 ? 2.15f : -2.15f);

    internal static void ApplyCamera(BattleCameraMono mono)
    {
        if (!mono || !mono.MyCamera) return;
        mono.RootTrans.SetPositionAndRotation(PortraitBattleCameraSettings.LookTarget + PortraitBattleCameraSettings.Offset,
            PortraitBattleCameraSettings.Rotation);
        mono.ShakeTrans.localPosition = Vector3.zero;
        mono.ShakeTrans.localRotation = Quaternion.identity;
        mono.ContentTrans.localPosition = Vector3.zero;
        mono.ContentTrans.localRotation = Quaternion.identity;
        mono.MyCamera.orthographic = true;
        mono.MyCamera.orthographicSize = Detail ? 4.6f : PortraitBattleCameraSettings.OrthographicSize;
        mono.MyCamera.aspect = PortraitBattleCameraSettings.Aspect;
    }

    internal static Camera Camera()
    {
        var root = EditorTimelineUtil.FindBattleCameraGameObject();
        return root ? BattleCameraMonoBinder.EnsureBound(root).MyCamera : null;
    }

    internal static object Configure(EditorTimeLine window, JObject args)
    {
        RequirePreviewScene();
        if (window.IsRecording || window.IsPlayheadLerping)
            throw new InvalidOperationException("录制或定位结束后才能调整预览布局。");
        string mode = (string)args["mode"] ?? (Detail ? "Detail" : "Battle");
        if (mode != "Battle" && mode != "Detail") throw new ArgumentException("mode 为 Battle 或 Detail。");
        float distance = (float?)args["distance"] ?? 0f;
        if (args["distance"] != null && (!float.IsFinite(distance) || distance < 0.8f || distance > 12f))
            throw new ArgumentException("distance 为 0.8..12 米；省略恢复按技能自动选择。");
        window.StopPreView();
        SessionState.SetBool(ModeKey, mode == "Detail");
        SessionState.SetFloat(DistanceKey, distance);
        var control = window.TimelineControl;
        foreach (var unit in control.UnitsControl.Units)
        {
            int index = unit.transform == control.MyTransform ? 0 :
                control.UnitsControl.Units.Where(u => u.transform != control.MyTransform).ToList().IndexOf(unit);
            unit.BattleOriginPos = Position(control, unit.TeamType, index);
            unit.transform.position = unit.BattleOriginPos;
        }
        ApplyCamera(BattleCameraMonoBinder.EnsureBound(EditorTimelineUtil.FindBattleCameraGameObject()));
        FrameSceneView();
        TimelinePortraitPreviewWindow.Open();
        return Diagnostics(window);
    }

    internal static object Capture(EditorTimeLine window, JObject args)
    {
        RequirePreviewScene();
        if (window.IsPlaying || window.IsRecording || window.IsPlayheadLerping)
            throw new InvalidOperationException("先 seek/pause 到目标时间，再截图。");
        int width = (int?)args["width"] ?? 540;
        if (width < 180 || width > 1080) throw new ArgumentException("width 为 180..1080。");
        int height = Mathf.RoundToInt(width * 16f / 9f);
        var camera = Camera();
        if (!camera) throw new InvalidOperationException("未找到预览相机。");
        string dir = Path.GetFullPath("Library/KabuUnityMcp/SkillCaptures");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"{window.TimelineControl.Config.TimelineId}_{(Detail ? "detail" : "battle")}_{window.RunningTime:F3}_{Guid.NewGuid():N}.png");
        var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
        Texture2D texture = null;
        try
        {
            Render(camera, rt);
            var old = RenderTexture.active;
            try
            {
                RenderTexture.active = rt;
                texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally { RenderTexture.active = old; }
        }
        finally
        {
            if (texture) UnityEngine.Object.DestroyImmediate(texture);
            RenderTexture.ReleaseTemporary(rt);
        }
        return new { path, width, height, source = "preview-camera-render (no overlay UI)", state = Diagnostics(window) };
    }

    internal static void Render(Camera camera, RenderTexture target)
    {
        var old = camera.targetTexture;
        float aspect = camera.aspect;
        try
        {
            camera.aspect = PortraitBattleCameraSettings.Aspect;
            camera.targetTexture = target;
            camera.Render();
        }
        finally { camera.targetTexture = old; camera.aspect = aspect; }
    }

    internal static object Diagnostics(EditorTimeLine window)
    {
        RequirePreviewScene();
        var camera = Camera();
        var control = window.TimelineControl;
        var effects = Resources.FindObjectsOfTypeAll<ParticleSystem>()
            .Where(p => p.gameObject.scene == camera.gameObject.scene).ToArray();
        return new
        {
            mode = Detail ? "Detail (magnified; not battle scale)" : "Battle (actual default scale)",
            timelineId = control.Config.TimelineId, time = window.RunningTime,
            playback = control.SharedPlayback.Diagnostics,
            distance = Distance(control), aspect = PortraitBattleCameraSettings.Aspect,
            camera = new { instanceId = camera.GetInstanceID(), camera.orthographic, camera.orthographicSize,
                position = V(camera.transform.position), euler = V(camera.transform.eulerAngles) },
            units = control.UnitsControl.Units.Select(unit => new
            {
                name = unit.name, team = unit.TeamType.ToString(), position = V(unit.transform.position),
                viewport = V(camera.WorldToViewportPoint(unit.transform.position + Vector3.up * 0.7f)),
                sockets = unit.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Socket_") || t.name.StartsWith("Node_"))
                    .Select(t => new { t.name, position = V(t.position) })
            }).ToArray(),
            particles = effects.Select(p => new
            {
                p.name, position = V(p.transform.position), p.particleCount,
                active = p.gameObject.activeInHierarchy, rendererEnabled = p.GetComponent<Renderer>()?.enabled,
                renderMode = p.GetComponent<ParticleSystemRenderer>()?.renderMode.ToString(), trailsEnabled = p.trails.enabled,
                rendererBounds = p.GetComponent<Renderer>() is Renderer r ? new { center = V(r.bounds.center), size = V(r.bounds.size) } : null,
                loop = p.main.loop, duration = p.main.duration, maxParticles = p.main.maxParticles,
                materials = p.GetComponent<Renderer>()?.sharedMaterials.Select(m => new
                { name = m ? m.name : "missing", shader = m && m.shader ? m.shader.name : "missing", supported = m && m.shader && m.shader.isSupported }).ToArray()
            }).ToArray(),
            runtimeVerified = false
        };
    }
}

/// <summary>独立固定 9:16 监视窗，不受 GameView 当前自由比例及停靠尺寸影响。</summary>
internal sealed class TimelinePortraitPreviewWindow : EditorWindow
{
    private RenderTexture target;
    private double nextRepaint;
    [MenuItem("Kabu/技能竖屏预览")]
    internal static void Open()
    {
        var window = GetWindow<TimelinePortraitPreviewWindow>(false, "技能竖屏预览");
        window.minSize = new Vector2(260, 420);
        window.Show();
    }
    private void OnEnable() => EditorApplication.update += Tick;
    private void Tick()
    {
        if (EditorApplication.timeSinceStartup < nextRepaint) return;
        nextRepaint = EditorApplication.timeSinceStartup + 1.0 / 30.0;
        Repaint();
    }
    private void OnDisable()
    {
        EditorApplication.update -= Tick;
        if (target) { target.Release(); DestroyImmediate(target); }
    }
    private void OnGUI()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode
            || UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != TimelineEditorSceneBuilder.ScenePath)
        {
            EditorGUILayout.HelpBox("竖屏监视窗仅用于技能编辑器场景。", MessageType.Info);
            return;
        }
        var window = Resources.FindObjectsOfTypeAll<EditorTimeLine>().FirstOrDefault(w => w.TimelineControl != null);
        if (!window) { EditorGUILayout.HelpBox("先打开技能编辑器。", MessageType.Info); return; }
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        using (new EditorGUI.DisabledScope(window.IsRecording || window.IsPlayheadLerping))
        {
            if (GUILayout.Button("实战比例", EditorStyles.toolbarButton)) TimelinePortraitPreview.Configure(window, JObject.FromObject(new { mode = "Battle" }));
            if (GUILayout.Button("细节放大", EditorStyles.toolbarButton)) TimelinePortraitPreview.Configure(window, JObject.FromObject(new { mode = "Detail" }));
        }
        GUILayout.Label($"9:16 · {(TimelinePortraitPreview.Detail ? "细节放大" : "实战比例")} · {window.RunningTime:F3}s");
        var camera = TimelinePortraitPreview.Camera();
        if (!camera) return;
        if (!target) { target = new RenderTexture(540, 960, 24) { hideFlags = HideFlags.HideAndDontSave }; target.Create(); }
        if (Event.current.type == EventType.Repaint) TimelinePortraitPreview.Render(camera, target);
        float height = Mathf.Max(1, Mathf.Min(position.height - 48, position.width / PortraitBattleCameraSettings.Aspect));
        float width = height * PortraitBattleCameraSettings.Aspect;
        GUI.DrawTexture(new Rect((position.width - width) * 0.5f, 44, width, height), target, ScaleMode.StretchToFill, false);
    }
}
