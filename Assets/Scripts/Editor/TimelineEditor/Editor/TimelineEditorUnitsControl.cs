using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public class TimelineEditorUnitsControl
{
    private TimelineControl control;

    private List<UnitMono> units = new List<UnitMono>();
    public List<UnitMono> Units => units;

    private List<EditorUnitGravity> unitGravitys = new List<EditorUnitGravity>();
    public List<EditorUnitGravity> UnitGravitys => unitGravitys;

    public TimelineEditorUnitsControl(TimelineControl control)
    {
        this.control = control;
    }

    public UnitMono CreateUnit(UnitTeamType type, string path, Vector3 position, Quaternion roatation, Vector3 scale)
    {
        var logical = EditorTimelineAssetPathUtil.ToUnitLogicalName(path);
        if (string.IsNullOrEmpty(logical))
        {
            Debug.LogWarning("[TimelineEditorUnitsControl] 逻辑名为空。");
            return null;
        }

        var assetPath = EditorTimelineAssetPathUtil.ResolveUnitPrefabAssetPath(logical);
        var prefab = EditorTimelineAssetPathUtil.LoadUnitPrefab(logical);
        if (prefab == null || string.IsNullOrEmpty(assetPath))
        {
            Debug.LogWarning(
                $"[TimelineEditorUnitsControl] 无法加载预制体: {logical}（预览画质 {EditorTimelineAssetPathUtil.PreviewQuality}）");
            return null;
        }

        var instance = GameObject.Instantiate(prefab);
        // 预览实例随技能编辑器销毁，避免保存场景时把它写入场景文件。
        instance.hideFlags |= HideFlags.DontSaveInEditor;
        instance.transform.SetPositionAndRotation(position, roatation);
        instance.transform.localScale = scale;

        var host = instance;
        var unitMono = UnitMonoBinder.EnsureBound(host);
        // 与运行时 UnitModelControl 一致：用同目录 *_Node_Data.json 覆盖/补齐挂点（怪物编辑器只写 JSON）
        ApplyNodeDataJson(unitMono, assetPath, instance.transform);
        unitMono.InitTeamType(type);
        unitMono.BattleOriginPos = unitMono.transform.position;
        unitMono.BattleOriginRotation = unitMono.transform.rotation;
        unitMono.ResetAttackForce();
        AssignTransform(type, host.transform);
        return unitMono;
    }

    /// <summary>加载预制体旁 <c>{名}_Node_Data.json</c> 并 Apply 到实例。</summary>
    static void ApplyNodeDataJson(UnitMono unitMono, string prefabAssetPath, Transform modelRoot)
    {
        if (string.IsNullOrEmpty(prefabAssetPath))
        {
            return;
        }

        var dir = Path.GetDirectoryName(prefabAssetPath);
        if (string.IsNullOrEmpty(dir))
        {
            return;
        }

        dir = dir.Replace('\\', '/');
        var fileName = Path.GetFileNameWithoutExtension(prefabAssetPath);
        var jsonPath = dir + "/" + fileName + UnitNodeDataPathUtil.JsonSuffix;
        var textAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(jsonPath);
        var config = MonsterNodeDataJsonUtil.FromTextAsset(textAsset);
        UnitNodeDataApplyUtil.Apply(unitMono, modelRoot, config);
    }

    public void AssignTransform(UnitTeamType type, Transform transform)
    {
        if (transform == null)
        {
            return;
        }

        var unitMono = transform.GetComponent<UnitMono>();
        if (unitMono == null)
        {
            Debug.LogWarning($"[TimelineEditorUnitsControl] Transform '{transform.name}' 上未找到 UnitMono。");
            return;
        }

        RemoveUnit(unitMono);
        unitMono.InitTeamType(type);
        AddUnit(unitMono);
    }

    private void AddUnit(UnitMono unitMono)
    {
        if (unitMono == null || units.Contains(unitMono))
        {
            return;
        }

        units.Add(unitMono);
        var gravity = new EditorUnitGravity();
        gravity.Init(control.Timeline, unitMono);
        gravity.SetTimelineControl(control);
        unitGravitys.Add(gravity);
    }

    public EditorUnitGravity GetUnitGravity(UnitMono unitMono)
    {
        var index = units.IndexOf(unitMono);
        return index >= 0 ? unitGravitys[index] : null;
    }

    public void RemoveTransform(Transform transform, bool destroy = false)
    {
        if (transform == null)
        {
            return;
        }

        var unitMono = transform.GetComponent<UnitMono>();
        if (unitMono == null)
        {
            return;
        }

        RemoveUnit(unitMono);

        if (destroy)
        {
            GameObject.DestroyImmediate(unitMono.gameObject);
        }
    }

    private void RemoveUnit(UnitMono unitMono)
    {
        var index = units.IndexOf(unitMono);
        if (index < 0)
        {
            return;
        }

        units.RemoveAt(index);
        unitGravitys.RemoveAt(index);
    }

    /// <summary>求指定队伍所有单位的中心点（世界坐标 position 的算术平均）。</summary>
    public Vector3 GetTeamCenter(UnitTeamType teamType)
    {
        var center = Vector3.zero;
        var count = 0;
        for (int i = 0; i < units.Count; i++)
        {
            if (units[i].TeamType != teamType)
            {
                continue;
            }

            center += units[i].GetTransformPosition();
            count++;
        }

        if (count == 0)
        {
            return Vector3.zero;
        }

        return center / count;
    }

    public void OnStopPreView()
    {
        for (int i = 0; i < units.Count; i++)
        {
            units[i].SetTransformPosition(units[i].BattleOriginPos);
            units[i].Transform.rotation = units[i].BattleOriginRotation;
            units[i].gameObject.SetActive(true);
            // 还原旧版预览使用的模型子节点。
            if (units[i].ActiveRoot != null)
            {
                units[i].ActiveRoot.gameObject.SetActive(true);
            }

            unitGravitys[i].DisableForceXZ();
            unitGravitys[i].HitType = HitTranslateType.None;

            var a = units[i].Animator;
            if (a != null && a.isActiveAndEnabled)
            {
                a.speed = 0f;
                a.Play(Animator.StringToHash("Idle"), 0, 0f);
                a.Update(0f);
            }
        }
    }

    /// <summary>3D 单位由自身 3D 朝向控制，无需 2D Spine 公告板同步。</summary>
    public void UpdateSpineFacing()
    {
    }

    public void Draw()
    {
        UpdateSpineFacing();
        for (int i = 0; i < unitGravitys.Count; i++)
        {
            unitGravitys[i].OnDraw();
        }
    }
}
