using System.Collections.Generic;
using LittleGame.Client.SkillTimeline;
using UnityEditor;
using UnityEngine;

namespace LittleGame.Client.Editor.Timeline
{
    /// <summary>
    /// 像素级还原 Demo TimelineClipBase：
    /// 纯深蓝填充 rgb(34,77,122)、仅选中时画 2px 亮黄四边框、11pt Bold 浅灰居中文字、48 段正弦波浪线、
    /// FlashForLocate 定位闪烁，以及完整原版参数区（含 EndByOtherClipEnd + Catch）。
    /// </summary>
    public sealed class TimelineClipBase
    {
        private static readonly Color FillColor = new Color(34f / 255f, 77f / 255f, 122f / 255f, 1f);
        private static readonly Color BorderColor = new Color(1f, 0.92f, 0.35f, 1f);
        private static readonly Color ClipLabelTextColor = new Color(0xA3 / 255f, 0xA3 / 255f, 0xA3 / 255f, 1f);
        private const float BorderWidth = 2f;
        private const float PaddingRate = 0.1f;

        private static GUIStyle clipLabelStyle;

        private TimelineControl control;
        private readonly TimelineWaveBase delayWave = new TimelineWaveBase();
        private readonly TimelineLineBase nextLine = new TimelineLineBase();
        private double locateFlashUntil;

        public SkillClipConfig Config { get; private set; }
        public Rect Rect { get; private set; }
        public TimelineNodeBase StartNode { get; private set; }
        public TimelineNodeBase EndNode { get; private set; }
        public List<TimelineEventBase> Events { get; } = new List<TimelineEventBase>();

        public void Init(TimelineControl ctrl, SkillClipConfig cfg)
        {
            control = ctrl;
            Config = cfg;
            StartNode = new TimelineNodeBase();
            StartNode.Init(ctrl, this, TimelineNodeKind.Start);
            EndNode = new TimelineNodeBase();
            EndNode.Init(ctrl, this, TimelineNodeKind.End);
            RebuildEvents();
        }

        public void RebuildEvents()
        {
            Events.Clear();
            if (Config.EventConfigs == null) Config.EventConfigs = new List<SkillEventConfig>();
            for (int i = 0; i < Config.EventConfigs.Count; i++)
            {
                var ev = new TimelineEventBase();
                ev.Init(control, this, Config.EventConfigs[i]);
                Events.Add(ev);
            }
        }

        public Vector2 GetPreLinePoint() => new Vector2(Rect.x, Rect.center.y);
        public Vector2 GetEndLinePoint() => new Vector2(Rect.xMax, Rect.center.y);

        public void FlashForLocate()
        {
            locateFlashUntil = EditorApplication.timeSinceStartup + 1.2d;
            control.Timeline.Repaint();
        }

        public Rect ComputeRect(int rowIndex)
        {
            float contentStart = control.ResolveClipStart(Config);
            float contentEnd = control.ResolveClipEnd(Config);
            float itemH = control.Timeline.GetItemPixelHeight();
            float yWorld = control.Timeline.GetYByIndex(rowIndex);
            float x = control.TimeToLocalX(contentStart);
            float y = control.Timeline.WorldPixelYToLocal(control.Timeline.YToPixel(yWorld)) + PaddingRate * 0.5f * itemH;
            float w = Mathf.Max(8f, control.TimeToLocalX(contentEnd) - x);
            float h = Mathf.Max(8f, itemH - itemH * PaddingRate);
            return new Rect(x, y, w, h);
        }

        public void Draw(int rowIndex, Vector2 mousePos)
        {
            Rect = ComputeRect(rowIndex);

            // 原版纯深蓝条块 + 定位闪烁高亮
            EditorGUI.DrawRect(Rect, FillColor);
            double remaining = locateFlashUntil - EditorApplication.timeSinceStartup;
            if (remaining > 0d)
            {
                float alpha = (0.25f + 0.4f * Mathf.Abs(Mathf.Sin((float)remaining * 9f))) * Mathf.Min(1f, (float)remaining);
                EditorGUI.DrawRect(Rect, new Color(1f, 0.85f, 0.2f, alpha));
                DrawBorderRect(Rect, new Color(1f, 0.85f, 0.2f, 1f), 3f);
                control.Timeline.Repaint();
            }

            // 仅在选中时画原版 2px 亮黄边框
            if (control.SelectedClip == this)
                DrawBorderRect(Rect, BorderColor, BorderWidth);

            // DelayTime 正弦金黄波浪线（从前置点/StartTime 到内容起点）
            if (Config.DelayTime > 0.0001f)
            {
                float contentStart = control.ResolveClipStart(Config);
                float startX = control.TimeToLocalX(contentStart - Config.DelayTime);
                if (Rect.x > startX + 0.5f)
                    delayWave.Draw(new Vector2(startX, Rect.center.y), new Vector2(Rect.x, Rect.center.y));
            }

            // 事件点
            for (int i = 0; i < Events.Count; i++) Events[i].Draw();

            // 原版居中 11pt Bold 浅灰文字
            DrawClipTypeLabel();

            // 连向后继 Clip 的蓝色贝塞尔连线
            for (int i = 0; i < control.Clips.Count; i++)
            {
                var next = control.Clips[i];
                if (next.Config != null && next.Config.PreKey == Config.ClipKey)
                    nextLine.Draw(GetEndLinePoint(), new Vector2(1f, 0f), next.GetPreLinePoint(), new Vector2(-1f, 0f));
            }

            // 头尾蓝圆点
            StartNode.Draw(mousePos);
            EndNode.Draw(mousePos);
        }

        private void DrawClipTypeLabel()
        {
            if (Config == null || Rect.width <= 1f || Rect.height <= 1f) return;
            if (clipLabelStyle == null)
            {
                clipLabelStyle = new GUIStyle(EditorStyles.label)
                {
                    alignment = TextAnchor.UpperCenter,
                    clipping = TextClipping.Clip,
                    wordWrap = false,
                    fontSize = 11,
                    fontStyle = FontStyle.Bold
                };
                clipLabelStyle.normal.textColor = ClipLabelTextColor;
                clipLabelStyle.hover.textColor = ClipLabelTextColor;
                clipLabelStyle.active.textColor = ClipLabelTextColor;
                clipLabelStyle.focused.textColor = ClipLabelTextColor;
            }

            bool hasEvents = Events.Count > 0;
            clipLabelStyle.alignment = hasEvents ? TextAnchor.UpperCenter : TextAnchor.MiddleCenter;
            Rect labelRect = hasEvents
                ? new Rect(Rect.x, Rect.y + 2f, Rect.width, clipLabelStyle.lineHeight)
                : Rect;
            GUI.Label(labelRect, GetLabelText(), clipLabelStyle);
        }

        public string GetLabelText()
        {
            if (Config == null) return "Clip";
            switch (Config.Type)
            {
                case SkillClipType.PlayAnimation: return string.IsNullOrEmpty(Config.ActionName) ? "3D动作" : $"动作:{Config.ActionName}";
                case SkillClipType.PlayBullet: return string.IsNullOrEmpty(Config.EffectId) ? "弹道" : $"弹道:{Config.EffectId}";
                case SkillClipType.PlayEffect: return string.IsNullOrEmpty(Config.EffectId) ? "特效" : $"特效:{Config.EffectId}";
                case SkillClipType.PlayHit: return "受击";
                case SkillClipType.CameraShake: return "相机震屏";
                case SkillClipType.SceneDarken: return "场景压暗";
                case SkillClipType.HitStop: return "打击顿帧";
                default: return Config.Type.ToString();
            }
        }

        public static string DisplayNameForType(SkillClipType type)
        {
            switch (type)
            {
                case SkillClipType.PlayAnimation: return "播放3D动作";
                case SkillClipType.PlayBullet: return "播放弹道";
                case SkillClipType.PlayEffect: return "播放特效";
                case SkillClipType.PlayHit: return "受击反馈";
                case SkillClipType.CameraShake: return "相机震屏";
                case SkillClipType.SceneDarken: return "场景压暗";
                case SkillClipType.HitStop: return "打击顿帧";
                default: return type.ToString();
            }
        }

        public bool TryNudgeEditableDuration(float frameRate, int frameDelta)
        {
            if (Config == null || frameDelta == 0 || !string.IsNullOrEmpty(Config.EndByClipKey)) return false;
            float rate = frameRate > 0f ? frameRate : 30f;
            int frame = Mathf.RoundToInt(Config.Duration * rate);
            frame = Mathf.Max(1, frame + frameDelta);
            Config.Duration = frame / rate;
            return true;
        }

        public void DrawInspector()
        {
            EditorGUILayout.LabelField($"{GetLabelText()}参数配置", EditorStyles.boldLabel);

            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextField("类型", DisplayNameForType(Config.Type));
            EditorGUILayout.TextField("ClipKey", Config.ClipKey ?? string.Empty);
            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginChangeCheck();
            EditorGUI.BeginDisabledGroup(!string.IsNullOrEmpty(Config.PreKey));
            Config.StartTime = Mathf.Max(0f, EditorGUILayout.FloatField("开始时间", Config.StartTime));
            EditorGUI.EndDisabledGroup();

            Config.DelayTime = Mathf.Max(0f, EditorGUILayout.FloatField("延迟时间", Config.DelayTime));

            EditorGUI.BeginDisabledGroup(!string.IsNullOrEmpty(Config.EndByClipKey));
            Config.Duration = Mathf.Max(0f, EditorGUILayout.FloatField("时长", Config.Duration));
            EditorGUI.EndDisabledGroup();

            // 原版 EndByOtherClipEnd + Catch 按钮
            if (control.IsWaitingEndByOtherClipCatch(this))
                EditorGUILayout.HelpBox("Catch 模式：请在时间轴点击选择要绑定的 Clip（Esc 取消）", MessageType.Warning);

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextField("EndByOtherClipEnd", Config.EndByClipKey ?? string.Empty);
            EditorGUI.EndDisabledGroup();
            if (GUILayout.Button("Catch", GUILayout.Width(54)))
                control.BeginEndByOtherClipCatch(this);
            if (!string.IsNullOrEmpty(Config.EndByClipKey) && GUILayout.Button("清", GUILayout.Width(24)))
                Config.EndByClipKey = "";
            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextField("前置Key", Config.PreKey ?? string.Empty);
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(4);
            Config.LocalOffset = EditorGUILayout.Toggle("偏移随施法朝向旋转", Config.LocalOffset);
            Config.OrientToCaster = EditorGUILayout.Toggle("特效随施法朝向旋转", Config.OrientToCaster);
            switch (Config.Type)
            {
                case SkillClipType.PlayAnimation:
                    Config.ActionName = EditorGUILayout.TextField("动作名", Config.ActionName ?? "Attack");
                    Config.Speed = Mathf.Max(0.1f, EditorGUILayout.FloatField("播放倍速", Config.Speed));
                    Config.TargetAnchor = (SkillAnchorType)EditorGUILayout.EnumPopup("作用对象", Config.TargetAnchor);
                    break;
                case SkillClipType.PlayBullet:
                    Config.EffectId = EditorGUILayout.TextField("特效 ID", Config.EffectId ?? "");
                    Config.FromAnchor = (SkillAnchorType)EditorGUILayout.EnumPopup("发射起点", Config.FromAnchor);
                    Config.ToAnchor = (SkillAnchorType)EditorGUILayout.EnumPopup("命中终点", Config.ToAnchor);
                    Config.StartHeight = EditorGUILayout.FloatField("起点高度", Config.StartHeight);
                    Config.EndHeight = EditorGUILayout.FloatField("终点高度", Config.EndHeight);
                    Config.ArcHeight = EditorGUILayout.FloatField("弹道弧高", Config.ArcHeight);
                    Config.Width = EditorGUILayout.FloatField("宽度", Config.Width);
                    Config.Scale = EditorGUILayout.FloatField("缩放", Config.Scale);
                    Config.ColorHex = EditorGUILayout.TextField("颜色 Hex", Config.ColorHex ?? "#FFFFFF");
                    break;
                case SkillClipType.PlayEffect:
                    Config.EffectId = EditorGUILayout.TextField("特效 ID", Config.EffectId ?? "");
                    Config.ToAnchor = (SkillAnchorType)EditorGUILayout.EnumPopup("挂点位置", Config.ToAnchor);
                    Config.Shape = (SkillEffectShape)EditorGUILayout.EnumPopup("回退形状", Config.Shape);
                    Config.Scale = EditorGUILayout.FloatField("缩放", Config.Scale);
                    Config.FollowAnchor = EditorGUILayout.Toggle("跟随目标", Config.FollowAnchor);
                    if (Config.Offset == null) Config.Offset = new SkillVec3();
                    var off = EditorGUILayout.Vector3Field("位置偏移", Config.Offset.ToVector3());
                    Config.Offset = new SkillVec3(off.x, off.y, off.z);
                    Config.ColorHex = EditorGUILayout.TextField("颜色 Hex", Config.ColorHex ?? "#FFFFFF");
                    break;
                case SkillClipType.PlayHit:
                    Config.TargetAnchor = (SkillAnchorType)EditorGUILayout.EnumPopup("受击对象", Config.TargetAnchor);
                    Config.HitActionName = EditorGUILayout.TextField("受击动作", Config.HitActionName ?? "Hit");
                    Config.EffectId = EditorGUILayout.TextField("受击特效", Config.EffectId ?? "");
                    Config.FlashDuration = EditorGUILayout.FloatField("受击脉冲时长", Config.FlashDuration);
                    Config.Scale = EditorGUILayout.FloatField("特效缩放", Config.Scale);
                    break;
                case SkillClipType.CameraShake:
                    Config.Amplitude = EditorGUILayout.FloatField("振幅", Config.Amplitude);
                    Config.Frequency = EditorGUILayout.FloatField("频率", Config.Frequency);
                    break;
                case SkillClipType.SceneDarken:
                    EditorGUILayout.LabelField("Scene Darken", EditorStyles.boldLabel);
                    Config.StartStrength = EditorGUILayout.Slider("起始强度", Config.StartStrength, 0f, 1f);
                    Config.EndStrength = EditorGUILayout.Slider("结束强度", Config.EndStrength, 0f, 1f);
                    break;
                case SkillClipType.HitStop:
                    Config.TargetAnchor = (SkillAnchorType)EditorGUILayout.EnumPopup("顿帧对象", Config.TargetAnchor);
                    break;
            }

            if (EditorGUI.EndChangeCheck())
                control.MarkDirtyAndRebuild();

            EditorGUILayout.Space(4);
        }

        public static void DrawBorderRect(Rect r, Color color, float thickness)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, thickness), color);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - thickness, r.width, thickness), color);
            EditorGUI.DrawRect(new Rect(r.x, r.y, thickness, r.height), color);
            EditorGUI.DrawRect(new Rect(r.xMax - thickness, r.y, thickness, r.height), color);
        }
    }
}
