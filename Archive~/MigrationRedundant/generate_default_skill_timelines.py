import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT_DIR = ROOT / "Assets" / "LittleGame" / "Resources" / "SkillTimelines"
SKILLS_DIR = OUT_DIR / "Skills"


def anim(key, action="Attack", start=0.0, duration=0.35, speed=1.0, target="Caster", events=None, pre=""):
    return {
        "ClipKey": key,
        "PreKey": pre,
        "Type": "PlayAnimation",
        "StartTime": start,
        "DelayTime": 0.0,
        "Duration": duration,
        "ActionName": action,
        "Speed": speed,
        "TargetAnchor": target,
        "EventConfigs": events or [],
    }


def bullet(key, effect_id, color, duration=0.18, width=0.12, arc=0.22, pre="", delay=0.0, start=0.0, from_a="Caster", to_a="Target"):
    return {
        "ClipKey": key,
        "PreKey": pre,
        "Type": "PlayBullet",
        "StartTime": start,
        "DelayTime": delay,
        "Duration": duration,
        "EffectId": effect_id,
        "FromAnchor": from_a,
        "ToAnchor": to_a,
        "StartHeight": 0.72,
        "EndHeight": 0.65,
        "ArcHeight": arc,
        "Width": width,
        "Scale": 1.0,
        "ColorHex": color,
        "EventConfigs": [],
    }


def fx(key, effect_id, shape, color, anchor="Target", duration=0.32, scale=0.85, offset=(0.0, 0.65, 0.0), pre="", delay=0.0, start=0.0, follow=False):
    return {
        "ClipKey": key,
        "PreKey": pre,
        "Type": "PlayEffect",
        "StartTime": start,
        "DelayTime": delay,
        "Duration": duration,
        "EffectId": effect_id,
        "ToAnchor": anchor,
        "Shape": shape,
        "Offset": {"x": offset[0], "y": offset[1], "z": offset[2]},
        "Scale": scale,
        "FollowAnchor": follow,
        "ColorHex": color,
        "EventConfigs": [],
    }


def hit(key, color="#FFFFFF", effect_id="", scale=0.45, pre="", delay=0.0, start=0.0, duration=0.14, hit_action="Hit", target="Target"):
    return {
        "ClipKey": key,
        "PreKey": pre,
        "Type": "PlayHit",
        "StartTime": start,
        "DelayTime": delay,
        "Duration": duration,
        "TargetAnchor": target,
        "HitActionName": hit_action,
        "FlashColorHex": color,
        "FlashDuration": 0.12,
        "EffectId": effect_id,
        "Offset": {"x": 0.0, "y": 0.65, "z": 0.0},
        "Scale": scale,
        "ColorHex": color,
        "EventConfigs": [],
    }


def shake(key, amp=0.14, freq=28.0, duration=0.18, pre="", delay=0.0, start=0.0):
    return {
        "ClipKey": key,
        "PreKey": pre,
        "Type": "CameraShake",
        "StartTime": start,
        "DelayTime": delay,
        "Duration": duration,
        "Amplitude": amp,
        "Frequency": freq,
        "EventConfigs": [],
    }


def darken(key, start_s=0.45, end_s=0.0, duration=0.35, pre="", delay=0.0, start=0.0):
    return {
        "ClipKey": key,
        "PreKey": pre,
        "Type": "SceneDarken",
        "StartTime": start,
        "DelayTime": delay,
        "Duration": duration,
        "StartStrength": start_s,
        "EndStrength": end_s,
        "EventConfigs": [],
    }


def hitstop(key, duration=0.07, target="Target", pre="", delay=0.0, start=0.0):
    return {
        "ClipKey": key,
        "PreKey": pre,
        "Type": "HitStop",
        "StartTime": start,
        "DelayTime": delay,
        "Duration": duration,
        "TargetAnchor": target,
        "EventConfigs": [],
    }


def timeline(tid, name, clips):
    return {
        "SchemaVersion": 1,
        "TimelineId": tid,
        "DisplayName": name,
        "DurationBindClipKey": "",
        "Clips": clips,
    }


def build_all():
    skills = [
        # 盾卫
        timeline("shield_basic", "盾卫普攻", [
            anim("c_anim", "Attack", 0.0, 0.32, 1.0, events=[{"EventKey": "ev_hit", "Time": 0.12, "EventName": "hit"}]),
            fx("c_slash", "fx_shield_slash", "Burst", "#F0D28B", "Target", 0.18, 0.45, pre="ev_hit"),
            hit("c_hit", "#FFE8A8", "fx_hit_physical", 0.42, pre="ev_hit"),
        ]),
        timeline("shield_skill", "盾卫护盾猛击", [
            anim("c_anim", "Attack", 0.0, 0.40, 0.95, events=[{"EventKey": "ev_impact", "Time": 0.14, "EventName": "impact"}]),
            fx("c_aura", "fx_shield_guard", "Ring", "#5EC8FF", "Caster", 0.45, 0.95, offset=(0.0, 0.16, 0.0), start=0.02, follow=True),
            fx("c_bash", "fx_shield_bash", "Burst", "#FFD66E", "Target", 0.24, 0.72, pre="ev_impact"),
            hit("c_hit", "#FFF0B3", "fx_hit_heavy", 0.55, pre="ev_impact"),
            shake("c_shake", 0.08, 24.0, 0.14, pre="ev_impact"),
        ]),
        timeline("shield_bastion", "盾卫·堡垒壁垒", [
            anim("c_anim", "Attack", 0.0, 0.42, 0.9, events=[{"EventKey": "ev_impact", "Time": 0.14, "EventName": "impact"}]),
            fx("c_wall", "fx_shield_bastion", "Ring", "#45B8FF", "Caster", 0.65, 1.35, offset=(0.0, 0.18, 0.0), start=0.0, follow=True),
            fx("c_aura", "fx_shield_dome", "Aura", "#8CE0FF", "Caster", 0.50, 0.9, offset=(0.0, 0.6, 0.0), start=0.05, follow=True),
            hit("c_hit", "#FFE49E", "fx_hit_heavy", 0.6, pre="ev_impact"),
            shake("c_shake", 0.10, 24.0, 0.16, pre="ev_impact"),
        ]),
        timeline("shield_warcry", "盾卫·战吼激励", [
            anim("c_anim", "Attack", 0.0, 0.42, 1.0, events=[{"EventKey": "ev_cry", "Time": 0.10, "EventName": "cry"}]),
            fx("c_wave", "fx_warcry_wave", "Ring", "#FFB84D", "LaneCenter", 0.55, 1.65, offset=(0.0, 0.18, 0.0), pre="ev_cry"),
            hit("c_hit", "#FFD27A", "fx_hit_heavy", 0.55, pre="ev_cry"),
            shake("c_shake", 0.09, 26.0, 0.14, pre="ev_cry"),
        ]),

        # 风弓手
        timeline("archer_basic", "风弓手普攻", [
            anim("c_anim", "Attack", 0.0, 0.30, 1.15, events=[{"EventKey": "ev_shot", "Time": 0.08, "EventName": "shot"}]),
            bullet("c_arrow", "fx_arrow_basic", "#A8F07A", duration=0.14, width=0.07, arc=0.18, pre="ev_shot"),
            hit("c_hit", "#D7FFB8", "fx_hit_arrow", 0.35, pre="c_arrow"),
        ]),
        timeline("archer_skill", "风弓手疾风箭", [
            anim("c_anim", "Attack", 0.0, 0.34, 1.1, events=[{"EventKey": "ev_shot", "Time": 0.09, "EventName": "shot"}]),
            bullet("c_arrow", "fx_arrow_wind", "#6BFF95", duration=0.16, width=0.12, arc=0.20, pre="ev_shot"),
            fx("c_wind", "fx_wind_burst", "Ring", "#7DFFAC", "TargetGround", 0.30, 0.95, offset=(0.0, 0.16, 0.0), pre="c_arrow"),
            hit("c_hit", "#B9FFCF", "fx_hit_wind", 0.52, pre="c_arrow"),
        ]),
        timeline("archer_pierce", "风弓手·穿透箭", [
            anim("c_anim", "Attack", 0.0, 0.34, 1.15, events=[{"EventKey": "ev_shot", "Time": 0.08, "EventName": "shot"}]),
            darken("c_darken", 0.32, 0.0, 0.34, start=0.0),
            bullet("c_arrow", "fx_arrow_pierce", "#FFE066", duration=0.14, width=0.15, arc=0.06, pre="ev_shot"),
            fx("c_trail", "fx_pierce_flash", "Burst", "#FFF099", "Target", 0.25, 0.85, pre="c_arrow"),
            hit("c_hit", "#FFF5B8", "fx_hit_pierce", 0.58, pre="c_arrow"),
            hitstop("c_stop", 0.06, "Target", pre="c_arrow"),
            shake("c_shake", 0.09, 30.0, 0.14, pre="c_arrow"),
        ]),
        timeline("archer_gale", "风弓手·飓风击退", [
            anim("c_anim", "Attack", 0.0, 0.34, 1.15, events=[{"EventKey": "ev_shot", "Time": 0.08, "EventName": "shot"}]),
            bullet("c_arrow", "fx_arrow_gale", "#4FFFD0", duration=0.15, width=0.16, arc=0.22, pre="ev_shot"),
            fx("c_gale", "fx_gale_ring", "SlamWave", "#66FFE0", "TargetGround", 0.38, 1.35, offset=(0.0, 0.18, 0.0), pre="c_arrow"),
            hit("c_hit", "#B8FFF0", "fx_hit_wind", 0.56, pre="c_arrow"),
            hitstop("c_stop", 0.05, "Target", pre="c_arrow"),
            shake("c_shake", 0.08, 28.0, 0.12, pre="c_arrow"),
        ]),

        # 雷法师
        timeline("storm_basic", "雷法师普攻", [
            anim("c_anim", "Attack", 0.0, 0.35, 1.05, events=[{"EventKey": "ev_cast", "Time": 0.10, "EventName": "cast"}]),
            bullet("c_orb", "fx_lightning_orb", "#9E8CFF", duration=0.16, width=0.11, arc=0.28, pre="ev_cast"),
            hit("c_hit", "#D0C4FF", "fx_hit_lightning", 0.42, pre="c_orb"),
        ]),
        timeline("storm_pool", "雷法师·落雷区域", [
            anim("c_anim", "Attack", 0.0, 0.40, 1.0, events=[{"EventKey": "ev_cast", "Time": 0.12, "EventName": "cast"}]),
            darken("c_darken", 0.46, 0.0, 0.44, pre="ev_cast"),
            fx("c_bolt", "fx_lightning_strike", "Beam", "#C8B8FF", "TargetGround", 0.28, 0.62, offset=(0.0, 0.15, 0.0), pre="ev_cast"),
            fx("c_zone", "fx_storm_zone", "Zone", "#8A6BFF", "TargetGround", 0.75, 1.15, offset=(0.0, 0.16, 0.0), pre="ev_cast", delay=0.05),
            hit("c_hit", "#E2D9FF", "fx_hit_lightning", 0.55, pre="ev_cast", delay=0.05),
            hitstop("c_stop", 0.07, "Target", pre="ev_cast", delay=0.05),
            shake("c_shake", 0.11, 32.0, 0.18, pre="ev_cast", delay=0.05),
        ]),
        timeline("storm_chain", "雷法师·连锁闪电", [
            anim("c_anim", "Attack", 0.0, 0.38, 1.05, events=[{"EventKey": "ev_cast", "Time": 0.10, "EventName": "cast"}]),
            darken("c_darken", 0.44, 0.0, 0.38, pre="ev_cast"),
            bullet("c_arc", "fx_chain_lightning", "#7CE8FF", duration=0.12, width=0.14, arc=0.35, pre="ev_cast"),
            fx("c_bolt", "fx_lightning_strike", "Beam", "#9CEEFF", "TargetGround", 0.24, 0.52, offset=(0.0, 0.15, 0.0), pre="c_arc"),
            hit("c_hit", "#D4F7FF", "fx_hit_lightning", 0.58, pre="c_arc"),
            hitstop("c_stop", 0.06, "Target", pre="c_arc"),
            shake("c_shake", 0.10, 34.0, 0.16, pre="c_arc"),
        ]),
        timeline("storm_tick", "雷区持续电击", [
            fx("c_spark", "fx_storm_tick", "Burst", "#A994FF", "Target", 0.16, 0.42, start=0.0),
            hit("c_hit", "#DCD1FF", "", 0.32, start=0.02, hit_action=""),
        ]),

        # 冰法师
        timeline("frost_basic", "冰法师普攻", [
            anim("c_anim", "Attack", 0.0, 0.35, 1.05, events=[{"EventKey": "ev_cast", "Time": 0.10, "EventName": "cast"}]),
            bullet("c_shard", "fx_ice_shard", "#66D9FF", duration=0.16, width=0.10, arc=0.16, pre="ev_cast"),
            hit("c_hit", "#B8F0FF", "fx_hit_ice", 0.42, pre="c_shard"),
        ]),
        timeline("frost_freeze", "冰法师·寒霜冻结", [
            anim("c_anim", "Attack", 0.0, 0.40, 1.0, events=[{"EventKey": "ev_cast", "Time": 0.12, "EventName": "cast"}]),
            bullet("c_lance", "fx_ice_lance", "#4CCBFF", duration=0.16, width=0.13, arc=0.18, pre="ev_cast"),
            fx("c_ring", "fx_freeze_ring", "Ring", "#7AE8FF", "TargetGround", 0.55, 1.25, offset=(0.0, 0.17, 0.0), pre="c_lance"),
            fx("c_crystal", "fx_freeze_aura", "Aura", "#B3F4FF", "Target", 0.65, 0.78, offset=(0.0, 0.55, 0.0), pre="c_lance", follow=True),
            hit("c_hit", "#CCF7FF", "fx_hit_ice", 0.55, pre="c_lance"),
        ]),
        timeline("frost_icearmor", "冰法师·极寒冰甲", [
            anim("c_anim", "Attack", 0.0, 0.40, 1.0, events=[{"EventKey": "ev_cast", "Time": 0.12, "EventName": "cast"}]),
            fx("c_armor", "fx_ice_armor", "Ring", "#5CE1FF", "LaneCenter", 0.60, 1.55, offset=(0.0, 0.18, 0.0), pre="ev_cast"),
            bullet("c_lance", "fx_ice_lance", "#7AE8FF", duration=0.16, width=0.12, arc=0.18, pre="ev_cast"),
            hit("c_hit", "#CCF7FF", "fx_hit_ice", 0.52, pre="c_lance"),
        ]),

        # 敌方通用普攻 / 治疗 / 石甲巨像
        timeline("enemy_melee_basic", "敌方近战攻击", [
            anim("c_anim", "Attack", 0.0, 0.34, 1.0, events=[{"EventKey": "ev_hit", "Time": 0.12, "EventName": "hit"}]),
            fx("c_claw", "fx_enemy_hit", "Burst", "#FF6B57", "Target", 0.16, 0.42, pre="ev_hit"),
            hit("c_hit", "#FFA396", "fx_hit_enemy", 0.38, pre="ev_hit"),
        ]),
        timeline("healer_heal", "治疗师回血", [
            anim("c_anim", "Attack", 0.0, 0.40, 1.0, events=[{"EventKey": "ev_heal", "Time": 0.14, "EventName": "heal"}]),
            bullet("c_orb", "fx_heal_orb", "#6DFF9B", duration=0.18, width=0.11, arc=0.45, pre="ev_heal"),
            fx("c_aura", "fx_heal_ring", "Ring", "#5CFF8D", "TargetGround", 0.50, 1.05, offset=(0.0, 0.17, 0.0), pre="c_orb", follow=True),
        ]),
        timeline("giant_telegraph", "石甲巨像·蓄力预警", [
            fx("c_charge", "fx_boss_charge", "Ring", "#FF4A3D", "Caster", 0.85, 2.2, offset=(0.0, 0.20, 0.0), start=0.0, follow=True),
            shake("c_rumble", 0.06, 18.0, 0.30, start=0.0),
        ]),
        timeline("giant_slam", "石甲巨像·裂地重击", [
            anim("c_anim", "Slam", 0.0, 0.62, 1.0, events=[{"EventKey": "ev_slam", "Time": 0.18, "EventName": "slam"}]),
            darken("c_darken_in", 0.20, 0.62, 0.18, start=0.0),
            darken("c_darken_out", 0.62, 0.0, 0.48, pre="ev_slam"),
            fx("c_wave", "fx_boss_slam_wave", "SlamWave", "#FF5E3A", "LaneCenter", 0.58, 1.85, offset=(0.0, 0.20, 0.0), pre="ev_slam"),
            fx("c_pillar", "fx_boss_slam_burst", "Burst", "#FFA05C", "LaneCenter", 0.38, 1.20, offset=(0.0, 0.45, 0.0), pre="ev_slam"),
            hitstop("c_stop", 0.09, "Caster", pre="ev_slam"),
            shake("c_shake", 0.26, 26.0, 0.36, pre="ev_slam"),
        ]),

        # 战术指挥与升星演出
        timeline("tactic_protect", "战术·一路护盾", [
            fx("c_ring", "fx_tactic_protect", "Ring", "#4DD2FF", "LaneCenter", 0.65, 1.45, offset=(0.0, 0.19, 0.0), start=0.0),
        ]),
        timeline("tactic_focus", "战术·集火标记", [
            fx("c_mark", "fx_tactic_focus", "Ring", "#FF5252", "TargetGround", 0.55, 1.15, offset=(0.0, 0.19, 0.0), start=0.0, follow=True),
        ]),
        timeline("unit_upgrade", "军团升星·分支觉醒", [
            darken("c_darken", 0.30, 0.0, 0.42, start=0.0),
            fx("c_lvlup", "fx_level_up", "Aura", "#FFD85C", "Caster", 0.62, 1.15, offset=(0.0, 0.16, 0.0), start=0.0, follow=True),
            shake("c_shake", 0.06, 24.0, 0.12, start=0.04),
        ]),
    ]

    bindings = {
        "SchemaVersion": 1,
        "Bindings": [
            # 盾卫
            {"UnitKind": "Shield", "Trigger": "BasicAttack", "Branch": "", "MinStar": 1, "TimelineId": "shield_basic", "Note": "盾卫普攻"},
            {"UnitKind": "Shield", "Trigger": "Skill", "Branch": "", "MinStar": 1, "TimelineId": "shield_skill", "Note": "盾卫基础技能"},
            {"UnitKind": "Shield", "Trigger": "Skill", "Branch": "Bastion", "MinStar": 2, "TimelineId": "shield_bastion", "Note": "二星堡垒分支"},
            {"UnitKind": "Shield", "Trigger": "Skill", "Branch": "WarCry", "MinStar": 2, "TimelineId": "shield_warcry", "Note": "二星战吼分支"},
            # 风弓手
            {"UnitKind": "Archer", "Trigger": "BasicAttack", "Branch": "", "MinStar": 1, "TimelineId": "archer_basic", "Note": "风弓手普攻"},
            {"UnitKind": "Archer", "Trigger": "Skill", "Branch": "", "MinStar": 1, "TimelineId": "archer_skill", "Note": "风弓手基础技能"},
            {"UnitKind": "Archer", "Trigger": "Skill", "Branch": "Pierce", "MinStar": 2, "TimelineId": "archer_pierce", "Note": "二星穿透分支"},
            {"UnitKind": "Archer", "Trigger": "Skill", "Branch": "Gale", "MinStar": 2, "TimelineId": "archer_gale", "Note": "二星飓风分支"},
            # 雷法师
            {"UnitKind": "Storm", "Trigger": "BasicAttack", "Branch": "", "MinStar": 1, "TimelineId": "storm_basic", "Note": "雷法师普攻"},
            {"UnitKind": "Storm", "Trigger": "Skill", "Branch": "", "MinStar": 1, "TimelineId": "storm_pool", "Note": "雷法师基础雷区"},
            {"UnitKind": "Storm", "Trigger": "Skill", "Branch": "StormPool", "MinStar": 2, "TimelineId": "storm_pool", "Note": "二星强化雷池"},
            {"UnitKind": "Storm", "Trigger": "Skill", "Branch": "ChainLightning", "MinStar": 2, "TimelineId": "storm_chain", "Note": "二星连锁闪电"},
            {"UnitKind": "Storm", "Trigger": "SkillTick", "Branch": "", "MinStar": 1, "TimelineId": "storm_tick", "Note": "雷区跳字电击"},
            # 冰法师
            {"UnitKind": "Frost", "Trigger": "BasicAttack", "Branch": "", "MinStar": 1, "TimelineId": "frost_basic", "Note": "冰法师普攻"},
            {"UnitKind": "Frost", "Trigger": "Skill", "Branch": "", "MinStar": 1, "TimelineId": "frost_freeze", "Note": "冰法师基础冻结"},
            {"UnitKind": "Frost", "Trigger": "Skill", "Branch": "DeepFreeze", "MinStar": 2, "TimelineId": "frost_freeze", "Note": "二星深度冻结"},
            {"UnitKind": "Frost", "Trigger": "Skill", "Branch": "IceArmor", "MinStar": 2, "TimelineId": "frost_icearmor", "Note": "二星极寒冰甲"},
            # 敌方
            {"UnitKind": "*", "Trigger": "EnemyAttack", "Branch": "", "MinStar": 1, "TimelineId": "enemy_melee_basic", "Note": "敌方近战普攻"},
            {"UnitKind": "Healer", "Trigger": "Heal", "Branch": "", "MinStar": 1, "TimelineId": "healer_heal", "Note": "治疗师回血"},
            {"UnitKind": "Giant", "Trigger": "BossTelegraph", "Branch": "", "MinStar": 1, "TimelineId": "giant_telegraph", "Note": "巨像蓄力"},
            {"UnitKind": "Giant", "Trigger": "BossSlam", "Branch": "", "MinStar": 1, "TimelineId": "giant_slam", "Note": "巨像重击"},
            # 战术与升星
            {"UnitKind": "*", "Trigger": "Protect", "Branch": "", "MinStar": 1, "TimelineId": "tactic_protect", "Note": "战术保护一路"},
            {"UnitKind": "*", "Trigger": "Focus", "Branch": "", "MinStar": 1, "TimelineId": "tactic_focus", "Note": "战术集火目标"},
            {"UnitKind": "*", "Trigger": "Upgrade", "Branch": "", "MinStar": 1, "TimelineId": "unit_upgrade", "Note": "军团合成升星/分支觉醒"},
        ],
    }

    SKILLS_DIR.mkdir(parents=True, exist_ok=True)
    for sk in skills:
        path = SKILLS_DIR / f"{sk['TimelineId']}.json"
        path.write_text(json.dumps(sk, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    (OUT_DIR / "UnitSkillBindings.json").write_text(json.dumps(bindings, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Generated {len(skills)} skill timelines and {len(bindings['Bindings'])} unit bindings in {OUT_DIR}")


if __name__ == "__main__":
    build_all()
