using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEngine;

namespace LittleGame.Client.SkillTimeline
{
    public enum SkillClipType
    {
        PlayAnimation,
        PlayBullet,
        PlayEffect,
        PlayHit,
        CameraShake,
        SceneDarken,
        HitStop
    }

    public enum SkillAnchorType
    {
        Caster,
        Target,
        LaneCenter,
        TargetGround
    }

    public enum SkillEffectShape
    {
        Burst,
        Ring,
        Beam,
        Zone,
        Aura,
        SlamWave
    }

    [Serializable]
    public sealed class SkillVec3
    {
        public float x;
        public float y;
        public float z;

        public SkillVec3() { }
        public SkillVec3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public Vector3 ToVector3() => new Vector3(x, y, z);
    }

    [Serializable]
    public sealed class SkillEventConfig
    {
        public string EventKey = "";
        public float Time;
        public string EventName = "";
    }

    /// <summary>技能时间轴片段配置，供 JSON 序列化、编辑器预览与实时并发播放共用。</summary>
    [Serializable]
    public sealed class SkillClipConfig
    {
        public string ClipKey = "";
        public string PreKey = "";
        public string EndByClipKey = "";
        [JsonConverter(typeof(StringEnumConverter))]
        public SkillClipType Type = SkillClipType.PlayEffect;
        public float StartTime;
        public float DelayTime;
        public float Duration = 0.25f;
        public List<SkillEventConfig> EventConfigs = new List<SkillEventConfig>();

        // PlayAnimation
        public string ActionName = "Attack";
        public float Speed = 1f;
        [JsonConverter(typeof(StringEnumConverter))]
        public SkillAnchorType TargetAnchor = SkillAnchorType.Caster;

        // PlayBullet / PlayEffect / PlayHit
        public string EffectId = "";
        [JsonConverter(typeof(StringEnumConverter))]
        public SkillAnchorType FromAnchor = SkillAnchorType.Caster;
        [JsonConverter(typeof(StringEnumConverter))]
        public SkillAnchorType ToAnchor = SkillAnchorType.Target;
        [JsonConverter(typeof(StringEnumConverter))]
        public SkillEffectShape Shape = SkillEffectShape.Burst;
        public SkillVec3 Offset = new SkillVec3(0f, 0.65f, 0f);
        public float StartHeight = 0.65f;
        public float EndHeight = 0.65f;
        public float ArcHeight = 0.25f;
        public float Width = 0.14f;
        public float Scale = 1f;
        public bool FollowAnchor;
        public string ColorHex = "#F5C24C";

        // PlayHit
        public string HitActionName = "Hit";
        public string FlashColorHex = "#FFFFFF";
        public float FlashDuration = 0.12f;

        // CameraShake
        public float Amplitude = 0.14f;
        public float Frequency = 28f;

        // SceneDarken（参考 Demo TimelineClipSceneDarken：起止压暗强度 0..1）
        public float StartStrength = 0.55f;
        public float EndStrength = 0.0f;
    }

    [Serializable]
    public sealed class SkillTimelineConfig
    {
        public int SchemaVersion = 1;
        public string TimelineId = "";
        public string DisplayName = "";
        public string DurationBindClipKey = "";
        public List<SkillClipConfig> Clips = new List<SkillClipConfig>();
    }

    [Serializable]
    public sealed class UnitSkillBindingEntry
    {
        public string UnitKind = "";
        public string Trigger = "";
        public string Branch = "";
        public int MinStar = 1;
        public string TimelineId = "";
        public string Note = "";
    }

    [Serializable]
    public sealed class UnitSkillBindingsConfig
    {
        public int SchemaVersion = 1;
        public List<UnitSkillBindingEntry> Bindings = new List<UnitSkillBindingEntry>();
    }

    public static class SkillTimelineJson
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            Converters = new List<JsonConverter> { new StringEnumConverter() }
        };

        public static string Serialize(object value) => JsonConvert.SerializeObject(value, Settings);

        public static T Deserialize<T>(string json) => JsonConvert.DeserializeObject<T>(json, Settings);

        public static Color ParseColor(string hex, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            return ColorUtility.TryParseHtmlString(hex, out var parsed) ? parsed : fallback;
        }
    }
}
