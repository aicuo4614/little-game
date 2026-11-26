using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LittleGame.Combat;
using Newtonsoft.Json;
using UnityEngine;

namespace LittleGame.Client.Battle
{
    [Serializable]
    public sealed class UnitSkillBindingItem
    {
        public string UnitKind = "*";
        public string Trigger = "BasicAttack";
        public string Branch = "";
        public int MinStar = 1;
        public string TimelineId = "";
        public string Note = "";
    }

    [Serializable]
    public sealed class UnitSkillBindingTable
    {
        public int SchemaVersion = 1;
        public List<UnitSkillBindingItem> Bindings = new List<UnitSkillBindingItem>();
    }

    /// <summary>
    /// 加载 Demo 标准格式 TimelineConfig（Assets/Package_Unit/Timeline/Base/*.json）与单位技能绑定表，
    /// 并直接通过共享 TimelineSchedule 导出规则层 BattleSkillDefinition，保证编辑器时间轴与规则层同源。
    /// </summary>
    public sealed class BattleSkillLibrary
    {
        public const string BindingsAssetPath = "Assets/Package_Unit/Skill/Base/UnitSkillBindings.json";
        public const string BindingsResourcePath = "SkillTimelines/UnitSkillBindings";
        public const string TimelineEditorDir = "Assets/Package_Unit/Timeline/Base";
        public const string TimelineResourcesDir = "Timeline/Base";

        private readonly Dictionary<string, TimelineConfig> timelines =
            new Dictionary<string, TimelineConfig>(StringComparer.OrdinalIgnoreCase);
        private UnitSkillBindingTable bindings = new UnitSkillBindingTable();

        public IReadOnlyDictionary<string, TimelineConfig> Timelines => timelines;
        public UnitSkillBindingTable Bindings => bindings;

        public void Reload()
        {
            timelines.Clear();
            var service = MVC.ResourceService;
            const string bindingAddress = "Skill/UnitSkillBindings";
            var bindAsset = service.LoadAsset<TextAsset>(bindingAddress, ResourcePackageName.UnitPackage);
            if (bindAsset == null) throw new InvalidDataException("缺少技能绑定表: " + bindingAddress);
            try
            {
                bindings = JsonConvert.DeserializeObject<UnitSkillBindingTable>(bindAsset.text)
                    ?? throw new InvalidDataException("技能绑定表为空");
            }
            finally { service.Release(bindingAddress, ResourcePackageName.UnitPackage); }

            // 单一资产来源：编辑器保存的 Package_Unit JSON 同样由 YooAsset 提供给实战。
            foreach (var id in bindings.Bindings.Select(b => b.TimelineId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct())
            {
                string address = "Timeline/" + id;
                var asset = service.LoadAsset<TextAsset>(address, ResourcePackageName.UnitPackage);
                if (asset == null) throw new InvalidDataException("缺少技能时间轴: " + address);
                try
                {
                    var config = TimelineJsonUtil.FromJson(asset.text);
                    if (config == null || config.TimelineId != id) throw new InvalidDataException("技能 ID 与绑定不符: " + address);
                    timelines.Add(id, config);
                }
                finally { service.Release(address, ResourcePackageName.UnitPackage); }
            }
        }

        public TimelineConfig Get(string timelineId)
        {
            if (string.IsNullOrWhiteSpace(timelineId)) return null;
            return timelines.TryGetValue(timelineId, out var cfg) ? cfg : null;
        }

        public string ResolveTimelineId(string unitKind, string trigger, string branch = "", int star = 1)
        {
            if (bindings?.Bindings == null || bindings.Bindings.Count == 0) return null;
            UnitSkillBindingItem best = null;
            int bestScore = -1;
            foreach (var b in bindings.Bindings)
            {
                if (b == null || string.IsNullOrWhiteSpace(b.TimelineId)) continue;
                bool unitMatch = string.Equals(b.UnitKind, unitKind, StringComparison.OrdinalIgnoreCase) || b.UnitKind == "*";
                bool triggerMatch = string.Equals(b.Trigger, trigger, StringComparison.OrdinalIgnoreCase);
                if (!unitMatch || !triggerMatch) continue;

                bool hasBranch = !string.IsNullOrWhiteSpace(b.Branch);
                if (hasBranch && !string.Equals(b.Branch, branch, StringComparison.OrdinalIgnoreCase)) continue;
                if (star < Math.Max(1, b.MinStar)) continue;

                int score = (b.UnitKind == "*" ? 0 : 100) + (hasBranch ? 50 : 0) + Math.Max(1, b.MinStar) * 10;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = b;
                }
            }
            return best?.TimelineId;
        }

        /// <summary>直接从共享 TimelineSchedule 提取 LaunchAt / ImpactAt / RecoveryEnd 导出给纯规则层。</summary>
        public BattleSkillDefinition[] BuildDefinitions()
        {
            var list = new List<BattleSkillDefinition>();
            if (bindings?.Bindings == null) return list.ToArray();

            foreach (var b in bindings.Bindings)
            {
                var cfg = Get(b.TimelineId);
                if (cfg == null) continue;
                var def = ExtractDefinition(cfg, b);
                if (def != null)
                {
                    list.Add(def);
                }
            }
            return list.ToArray();
        }

        private static BattleSkillDefinition ExtractDefinition(TimelineConfig cfg, UnitSkillBindingItem binding)
        {
            float launchAt = 0.14f;
            float impactAt = 0.24f;
            float recoveryEnd = 0.48f;

            try
            {
                var schedule = new TimelineSchedule(cfg);
                float totalEnd = 0f;
                float? bulletStart = null;
                float? hitStart = null;
                float? animEnd = null;

                foreach (var clip in cfg.Clips)
                {
                    if (clip == null) continue;
                    float start = schedule.GetStart(clip);
                    float end = schedule.GetEnd(clip);
                    if (end > totalEnd) totalEnd = end;

                    if (clip is TimelineClipPlayBulletConfig && !bulletStart.HasValue)
                    {
                        bulletStart = start;
                    }
                    else if (clip is TimelineClipPlayHitConfig && !hitStart.HasValue)
                    {
                        hitStart = start;
                    }
                    else if (clip is TimelineClipPlayAnimatorConfig && !animEnd.HasValue)
                    {
                        animEnd = end;
                    }
                }

                if (bulletStart.HasValue)
                {
                    launchAt = Mathf.Max(0.05f, bulletStart.Value);
                    impactAt = hitStart.HasValue ? Mathf.Max(launchAt + 0.05f, hitStart.Value) : launchAt + 0.22f;
                }
                else if (hitStart.HasValue)
                {
                    launchAt = Mathf.Max(0.05f, hitStart.Value * 0.65f);
                    impactAt = Mathf.Max(launchAt, hitStart.Value);
                }

                recoveryEnd = Mathf.Max(impactAt + 0.08f, animEnd ?? totalEnd, 0.35f);
            }
            catch (Exception ex)
            {
                GameDebug.LogWarning("BattleSkillLibrary", $"推导技能时序 {cfg.TimelineId} 失败，使用默认时序: {ex.Message}");
            }

            var definition = new BattleSkillDefinition
            {
                UnitKind = binding.UnitKind,
                Trigger = binding.Trigger,
                Branch = binding.Branch ?? string.Empty,
                MinStar = Math.Max(1, binding.MinStar),
                TimelineId = cfg.TimelineId,
                LaunchAt = launchAt,
                ImpactAt = impactAt,
                RecoveryEnd = recoveryEnd,
                InterruptOnControl = true,
                TrackTarget = true,
            };
            var projectile = cfg.Clips.OfType<TimelineClipPlayBulletConfig>().FirstOrDefault();
            if (projectile != null)
            {
                var source = ReadPoint(cfg.PreviewCasterAddress, projectile.StartData);
                definition.ProjectileSpeed = projectile.MoveSpeed;
                definition.ProjectileDelay = projectile.MoveDelayTime;
                definition.ProjectileMinFlight = TimelineClipPlayBulletUtil.MinFlyDuration;
                definition.ProjectileSourceX = source.x;
                definition.ProjectileSourceY = source.y;
                definition.ProjectileSourceZ = source.z;
                var hit = cfg.Clips.OfType<TimelineClipPlayHitConfig>().FirstOrDefault();
                if (hit != null && hit.PreKey == projectile.ClipKey) definition.ProjectileImpactLag = hit.DelayTime;
                foreach (string role in new[] { "Grunt", "Runner", "Armored", "Healer", "Giant", "Shield", "Archer", "Storm", "Frost" })
                {
                    var targetPoint = ReadPoint(role + "/" + role, projectile.EndData);
                    definition.ProjectileTargetPoints[role] = new[] { targetPoint.x, targetPoint.y, targetPoint.z };
                }
            }
            return definition;
        }

        private static Vector3 ReadPoint(string role, TimelineClipPointData point)
        {
            string address = "Unit/Low/" + role;
            var service = MVC.ResourceService;
            var prefab = service.LoadAsset<GameObject>(address, ResourcePackageName.UnitPackage);
            try
            {
                if (!prefab) throw new InvalidDataException("缺少弹道挂点资源：" + address);
                var mono = prefab.GetComponent<UnitMono>();
                var node = mono ? mono.FindNodeTransform(point.OffsetNodeType) : null;
                if (!node) node = prefab.transform;
                return prefab.transform.InverseTransformPoint(node.TransformPoint(point.OffsetPos));
            }
            finally { service.Release(address, ResourcePackageName.UnitPackage); }
        }
    }
}
