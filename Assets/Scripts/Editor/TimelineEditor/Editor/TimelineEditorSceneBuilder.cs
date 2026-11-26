using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 重建技能编辑器预览场景（无缺失 Prefab）并生成本项目 3D 军团的全部 Demo 标准格式 TimelineConfig 技能资产。
/// </summary>
public static class TimelineEditorSceneBuilder
{
    public const string ScenePath = "Assets/Scripts/Editor/TimelineEditor/Scene/TimelineEditor.unity";
    public const string CameraPrefabPath = "Assets/Package_Scene/Battle/BattleCamera.prefab";
    public const string TimelineDir = "Assets/Package_Unit/Timeline/Base";

    [MenuItem("Kabu/重建技能编辑器场景与默认技能", false, 2003)]
    public static void BuildSceneAndDefaultTimelines()
    {
        BuildBattleCameraPrefabAndScene();
        BuildAllDefaultSkillTimelines();
        AssetDatabase.Refresh();
        Debug.Log("[TimelineEditorSceneBuilder] 已重建竖屏预览场景和相机，仅补齐缺失的默认技能，不覆盖已调校 JSON。");
    }

    public static void BuildBattleCameraPrefabAndScene()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CameraPrefabPath));
        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));

        // 1. 生成 BattleCamera Prefab（根 BattleCamera -> ShakeTrans -> ContentTrans(Camera)）
        var camRoot = new GameObject("BattleCamera");
        var shake = new GameObject("ShakeTrans").transform;
        shake.SetParent(camRoot.transform, false);
        var content = new GameObject("ContentTrans").transform;
        content.SetParent(shake, false);

        var cam = content.gameObject.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.145f, 0.165f, 0.225f, 1f);
        cam.fieldOfView = BattleSharedConfig.Current.CameraFov;
        cam.orthographic = true;
        cam.orthographicSize = PortraitBattleCameraSettings.OrthographicSize;
        cam.aspect = PortraitBattleCameraSettings.Aspect;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 120f;
        var urp = cam.GetUniversalAdditionalCameraData();
        urp.renderType = CameraRenderType.Base;
        urp.renderPostProcessing = true;

        var mono = BattleCameraMonoBinder.EnsureBound(camRoot);
        mono.RootTrans = camRoot.transform;
        mono.ShakeTrans = shake;
        mono.ContentTrans = content;
        mono.MyCamera = cam;

        var cameraPrefab = PrefabUtility.SaveAsPrefabAsset(camRoot, CameraPrefabPath);
        Object.DestroyImmediate(camRoot);

        // 2. 重建干净的 TimelineEditor.unity 场景
        var prevScene = EditorSceneManager.GetActiveScene().path;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camInstance = (GameObject)PrefabUtility.InstantiatePrefab(cameraPrefab, scene);
        camInstance.name = "BattleCamera";
        camInstance.transform.position = PortraitBattleCameraSettings.LookTarget + PortraitBattleCameraSettings.Offset;
        camInstance.transform.rotation = PortraitBattleCameraSettings.Rotation;

        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.18f;
        light.color = new Color(1f, 0.96f, 0.88f);
        light.shadows = LightShadows.Soft;
        lightGo.transform.rotation = Quaternion.Euler(46f, -28f, 0f);

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.65f, 0.71f, 0.82f);
        RenderSettings.ambientEquatorColor = new Color(0.57f, 0.59f, 0.62f);
        RenderSettings.ambientGroundColor = new Color(0.32f, 0.35f, 0.40f);

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "PreviewGround";
        ground.transform.position = new Vector3(0f, 0f, 0f);
        ground.transform.localScale = new Vector3(4f, 1f, 3f);
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.color = new Color(0.26f, 0.28f, 0.32f, 1f);
        var matPath = "Assets/Package_Scene/Battle/TimelinePreviewGround.mat";
        AssetDatabase.CreateAsset(mat, matPath);
        ground.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(matPath);

        var gizmoHost = new GameObject("EditorTimelineMono");
        gizmoHost.AddComponent<EditorTimelineMono>();

        EditorSceneManager.SaveScene(scene, ScenePath);
        if (!string.IsNullOrEmpty(prevScene) && prevScene != ScenePath && File.Exists(prevScene))
        {
            EditorSceneManager.OpenScene(prevScene);
        }
    }

    public static void BuildAllDefaultSkillTimelines()
    {
        Directory.CreateDirectory(TimelineDir);

        var skills = new List<TimelineConfig>
        {
            // 盾卫
            CreateMeleeSkill("shield_basic", "盾卫普攻", "Shield/Shield", "Attack", "fx_shield_slash", "fx_hit_physical", 0.20f, 0f, 0f),
            CreateMeleeSkill("shield_skill", "盾卫基础盾击", "Shield/Shield", "Attack", "fx_shield_bash", "fx_hit_heavy", 0.22f, 0.08f, 0f),
            CreateMeleeSkill("shield_bastion", "盾卫二星堡垒", "Shield/Shield", "Attack", "fx_shield_bastion", "fx_hit_heavy", 0.24f, 0.10f, 0.25f),
            CreateMeleeSkill("shield_warcry", "盾卫二星战吼", "Shield/Shield", "Slam", "fx_warcry_wave", "fx_hit_heavy", 0.28f, 0.14f, 0.30f),

            // 弓手
            CreateRangedSkill("archer_basic", "风弓手普攻", "Archer/Archer", "Attack", "fx_pierce_flash", "fx_arrow_basic", "fx_hit_arrow", 18f, 0.14f, TimelineBulletTrajectoryType.Linear, 0f),
            CreateRangedSkill("archer_skill", "风弓手风箭", "Archer/Archer", "Attack", "fx_wind_burst", "fx_arrow_wind", "fx_hit_wind", 20f, 0.16f, TimelineBulletTrajectoryType.Linear, 0.08f),
            CreateRangedSkill("archer_pierce", "风弓手二星穿透箭", "Archer/Archer", "Attack", "fx_pierce_flash", "fx_arrow_pierce", "fx_hit_pierce", 24f, 0.16f, TimelineBulletTrajectoryType.Linear, 0.10f),
            CreateRangedSkill("archer_gale", "风弓手二星飓风箭", "Archer/Archer", "Attack", "fx_gale_ring", "fx_arrow_gale", "fx_hit_wind", 18f, 0.18f, TimelineBulletTrajectoryType.Parabola, 0.12f),

            // 雷法
            CreateRangedSkill("storm_basic", "雷法师普攻", "Storm/Storm", "Attack", "fx_pierce_flash", "fx_lightning_orb", "fx_hit_lightning", 15f, 0.16f, TimelineBulletTrajectoryType.Parabola, 0.06f),
            CreateRangedSkill("storm_pool", "雷法师雷池召唤", "Storm/Storm", "Attack", "fx_storm_zone", "fx_lightning_orb", "fx_lightning_strike", 16f, 0.18f, TimelineBulletTrajectoryType.Parabola, 0.14f, 0.22f),
            CreateRangedSkill("storm_chain", "雷法师连锁闪电", "Storm/Storm", "Attack", "fx_chain_lightning", "fx_lightning_orb", "fx_hit_lightning", 22f, 0.15f, TimelineBulletTrajectoryType.Linear, 0.12f),
            CreateEffectOnlySkill("storm_tick", "雷区跳字电击", "Storm/Storm", "fx_storm_tick", "fx_hit_lightning", 0.30f),

            // 冰法
            CreateRangedSkill("frost_basic", "冰法师普攻", "Frost/Frost", "Attack", "fx_freeze_aura", "fx_ice_shard", "fx_hit_ice", 16f, 0.16f, TimelineBulletTrajectoryType.Linear, 0.05f),
            CreateRangedSkill("frost_freeze", "冰法师冰冻术", "Frost/Frost", "Attack", "fx_freeze_ring", "fx_ice_lance", "fx_hit_ice", 18f, 0.18f, TimelineBulletTrajectoryType.Linear, 0.12f, 0.25f),
            CreateRangedSkill("frost_icearmor", "冰法师极寒冰甲", "Frost/Frost", "Attack", "fx_ice_armor", "fx_ice_shard", "fx_hit_ice", 16f, 0.18f, TimelineBulletTrajectoryType.Parabola, 0.08f),

            // 敌方
            CreateMeleeSkill("enemy_melee_basic", "敌方近战普攻", "Grunt/Grunt", "Attack", "fx_enemy_hit", "fx_hit_enemy", 0.20f, 0f, 0f),
            CreateRangedSkill("healer_heal", "治疗师回血", "Healer/Healer", "Attack", "fx_heal_ring", "fx_heal_orb", "fx_heal_ring", 14f, 0.18f, TimelineBulletTrajectoryType.Parabola, 0f),
            CreateEffectOnlySkill("giant_telegraph", "巨像蓄力预警", "Giant/Giant", "fx_boss_charge", null, 0.80f),
            CreateMeleeSkill("giant_slam", "巨像重击", "Giant/Giant", "Slam", "fx_boss_slam_wave", "fx_boss_slam_burst", 0.32f, 0.28f, 0.45f),

            // 战术 & 升级
            CreateEffectOnlySkill("tactic_protect", "战术护盾保护", "Shield/Shield", "fx_tactic_protect", null, 0.80f),
            CreateEffectOnlySkill("tactic_focus", "战术集火标记", "Archer/Archer", "fx_tactic_focus", null, 0.65f),
            CreateEffectOnlySkill("unit_upgrade", "军团升星光效", "Shield/Shield", "fx_level_up", null, 0.85f),
        };

        var utf8NoBom = new UTF8Encoding(false);
        foreach (var cfg in skills)
        {
            var json = TimelineJsonUtil.ToJson(cfg, true);
            var path = Path.Combine(TimelineDir, cfg.TimelineId + ".json");
            if (!File.Exists(path)) File.WriteAllText(path, json, utf8NoBom);
        }
    }

    private static TimelineConfig CreateRangedSkill(
        string id,
        string desc,
        string caster,
        string animState,
        string castFx,
        string bulletFx,
        string hitFx,
        float bulletSpeed,
        float launchTime,
        TimelineBulletTrajectoryType trajectory,
        float shakeStrength,
        float darkenStrength = 0f)
    {
        var clips = new List<TimelineClipConfigBase>
        {
            new TimelineClipSetCastTargetsConfig
            {
                ClipKey = id + "_cast_targets",
                StartTime = 0f,
                Duration = 0f,
                PreviewTargets = new List<string> { "$enemy:0" }
            },
            new TimelineClipSetHitTargetsConfig
            {
                ClipKey = id + "_hit_targets",
                StartTime = 0f,
                Duration = 0f,
                PreviewSource = "$caster",
                PreviewTargets = new List<string> { "$enemy:0" }
            },
            new TimelineClipPlayAnimatorConfig
            {
                Type = TimelineClipType.PlayAnimator,
                ClipKey = id + "_anim",
                StartTime = 0f,
                Duration = 0.45f,
                PrefabPath = caster,
                ParamName = animState,
                AnimatorStateSpeed = 1f,
                AffectedByHitStop = true,
                EventConfigs = new List<TimelineEventConfigBase>
                {
                    new TimelineEventConfigBase
                    {
                        EventKey = id + "_evt_launch",
                        TimelineEventType = TimelineEventType.CustomEvent,
                        ValueType = ValueType.Normal,
                        EventName = "launch",
                        Time = launchTime
                    }
                }
            },
            new TimelineClipPlayEffectConfig
            {
                Type = TimelineClipType.PlayEffect,
                ClipKey = id + "_cast_fx",
                PreKey = id + "_evt_launch",
                StartTime = launchTime,
                Duration = 0.35f,
                EffectPath = castFx,
                Subject = TimelineUnitSubjectType.Caster,
                PointData = new TimelineClipPointData
                {
                    OffsetNodeType = UnitNodeType.Custom1,
                    OffsetScale = Vector3.one,
                    DontFollow = true
                }
            },
            new TimelineClipPlayBulletConfig
            {
                Type = TimelineClipType.PlayBullet,
                ClipKey = id + "_bullet",
                PreKey = id + "_evt_launch",
                StartTime = launchTime,
                Duration = 0.26f,
                Path = bulletFx,
                MoveSpeed = bulletSpeed,
                MoveDelayTime = 0f,
                TrajectoryType = trajectory,
                ArcHeightFactor = trajectory == TimelineBulletTrajectoryType.Parabola ? 0.32f : 0f,
                FacingType = TimelineBulletFacingType.Trajectory3D,
                TargetBindingKey = id + "_hit_targets",
                StartData = new TimelineClipPointData
                {
                    OffsetNodeType = UnitNodeType.Custom1,
                    OffsetScale = Vector3.one
                },
                EndData = new TimelineClipPointData
                {
                    OffsetNodeType = UnitNodeType.BodyCenter,
                    OffsetScale = Vector3.one
                }
            },
            new TimelineClipPlayHitConfig
            {
                Type = TimelineClipType.PlayHit,
                ClipKey = id + "_hit",
                PreKey = id + "_bullet",
                StartTime = launchTime + 0.26f,
                Duration = 0.42f,
                TargetBindingKey = id + "_hit_targets",
                AnimatorParamName = "Hit",
                AnimatorDelayTime = 0f,
                EffectPath = hitFx,
                AffectedByHitStop = true,
                UnitNodePointData = new TimelineClipPointData
                {
                    OffsetNodeType = UnitNodeType.BodyCenter,
                    OffsetScale = Vector3.one,
                    DontFollow = true
                }
            }
        };

        if (shakeStrength > 0f)
        {
            clips.Add(new TimelineClipCameraShakeConfig
            {
                Type = TimelineClipType.CameraShake,
                ClipKey = id + "_shake",
                PreKey = id + "_bullet",
                StartTime = launchTime + 0.26f,
                Duration = 0.22f,
                PositionStrength = new Vector3(shakeStrength, shakeStrength * 0.8f, 0f),
                Vibrato = 18,
                Randomness = 90f,
                EnvelopeType = TimelineCameraShakeEnvelopeType.FadeOut
            });
        }

        if (darkenStrength > 0f)
        {
            clips.Add(new TimelineClipSceneDarkenConfig
            {
                Type = TimelineClipType.SceneDarken,
                ClipKey = id + "_darken",
                StartTime = 0f,
                Duration = 0.45f,
                StartStrength = 0f,
                EndStrength = darkenStrength
            });
        }

        return new TimelineConfig
        {
            TimelineId = id,
            Description = desc,
            PreviewCasterAddress = caster,
            DurationBindClipKey = id + "_hit",
            Clips = clips
        };
    }

    private static TimelineConfig CreateMeleeSkill(
        string id,
        string desc,
        string caster,
        string animState,
        string castFx,
        string hitFx,
        float hitTime,
        float shakeStrength,
        float darkenStrength)
    {
        var clips = new List<TimelineClipConfigBase>
        {
            new TimelineClipSetCastTargetsConfig
            {
                ClipKey = id + "_cast_targets",
                StartTime = 0f,
                Duration = 0f,
                PreviewTargets = new List<string> { "$enemy:0" }
            },
            new TimelineClipSetHitTargetsConfig
            {
                ClipKey = id + "_hit_targets",
                StartTime = 0f,
                Duration = 0f,
                PreviewSource = "$caster",
                PreviewTargets = new List<string> { "$enemy:0" }
            },
            new TimelineClipPlayAnimatorConfig
            {
                Type = TimelineClipType.PlayAnimator,
                ClipKey = id + "_anim",
                StartTime = 0f,
                Duration = animState == "Slam" ? 0.65f : 0.45f,
                PrefabPath = caster,
                ParamName = animState,
                AnimatorStateSpeed = 1f,
                AffectedByHitStop = true,
                EventConfigs = new List<TimelineEventConfigBase>
                {
                    new TimelineEventConfigBase
                    {
                        EventKey = id + "_evt_hit",
                        TimelineEventType = TimelineEventType.CustomEvent,
                        ValueType = ValueType.Normal,
                        EventName = "hit",
                        Time = hitTime
                    }
                }
            },
            new TimelineClipPlayEffectConfig
            {
                Type = TimelineClipType.PlayEffect,
                ClipKey = id + "_cast_fx",
                PreKey = id + "_evt_hit",
                StartTime = hitTime,
                Duration = 0.40f,
                EffectPath = castFx,
                Subject = TimelineUnitSubjectType.Caster,
                PointData = new TimelineClipPointData
                {
                    OffsetNodeType = UnitNodeType.BodyCenter,
                    OffsetPos = new Vector3(0f, 0f, 0.45f),
                    OffsetScale = Vector3.one,
                    DontFollow = true
                }
            },
            new TimelineClipPlayHitConfig
            {
                Type = TimelineClipType.PlayHit,
                ClipKey = id + "_hit",
                PreKey = id + "_evt_hit",
                StartTime = hitTime,
                Duration = 0.42f,
                TargetBindingKey = id + "_hit_targets",
                AnimatorParamName = "Hit",
                AnimatorDelayTime = 0f,
                EffectPath = hitFx,
                AffectedByHitStop = true,
                UnitNodePointData = new TimelineClipPointData
                {
                    OffsetNodeType = UnitNodeType.BodyCenter,
                    OffsetScale = Vector3.one,
                    DontFollow = true
                }
            }
        };

        if (shakeStrength > 0f)
        {
            clips.Add(new TimelineClipCameraShakeConfig
            {
                Type = TimelineClipType.CameraShake,
                ClipKey = id + "_shake",
                PreKey = id + "_evt_hit",
                StartTime = hitTime,
                Duration = 0.25f,
                PositionStrength = new Vector3(shakeStrength, shakeStrength, 0f),
                Vibrato = 20,
                Randomness = 90f,
                EnvelopeType = TimelineCameraShakeEnvelopeType.FadeOut
            });
        }

        if (darkenStrength > 0f)
        {
            clips.Add(new TimelineClipSceneDarkenConfig
            {
                Type = TimelineClipType.SceneDarken,
                ClipKey = id + "_darken",
                StartTime = 0f,
                Duration = 0.55f,
                StartStrength = 0f,
                EndStrength = darkenStrength
            });
        }

        return new TimelineConfig
        {
            TimelineId = id,
            Description = desc,
            PreviewCasterAddress = caster,
            DurationBindClipKey = id + "_hit",
            Clips = clips
        };
    }

    private static TimelineConfig CreateEffectOnlySkill(
        string id,
        string desc,
        string caster,
        string mainFx,
        string hitFx,
        float duration)
    {
        var clips = new List<TimelineClipConfigBase>
        {
            new TimelineClipSetHitTargetsConfig
            {
                ClipKey = id + "_hit_targets",
                StartTime = 0f,
                Duration = 0f,
                PreviewSource = "$caster",
                PreviewTargets = new List<string> { "$enemy:0" }
            },
            new TimelineClipPlayEffectConfig
            {
                Type = TimelineClipType.PlayEffect,
                ClipKey = id + "_fx",
                StartTime = 0f,
                Duration = duration,
                EffectPath = mainFx,
                Subject = TimelineUnitSubjectType.Caster,
                PointData = new TimelineClipPointData
                {
                    OffsetNodeType = UnitNodeType.Root,
                    OffsetScale = Vector3.one,
                    DontFollow = true
                }
            }
        };

        if (!string.IsNullOrEmpty(hitFx))
        {
            clips.Add(new TimelineClipPlayHitConfig
            {
                ClipKey = id + "_hit",
                StartTime = 0.05f,
                Duration = 0.35f,
                TargetBindingKey = id + "_hit_targets",
                AnimatorParamName = "Hit",
                EffectPath = hitFx,
                UnitNodePointData = new TimelineClipPointData
                {
                    OffsetNodeType = UnitNodeType.BodyCenter,
                    OffsetScale = Vector3.one,
                    DontFollow = true
                }
            });
        }

        return new TimelineConfig
        {
            TimelineId = id,
            Description = desc,
            PreviewCasterAddress = caster,
            DurationBindClipKey = id + "_fx",
            Clips = clips
        };
    }
}
