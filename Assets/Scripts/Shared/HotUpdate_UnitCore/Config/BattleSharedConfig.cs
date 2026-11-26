using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>程序与美术共用的战场数据；唯一 JSON 源文件由美术工程维护。</summary>
[Serializable]
public sealed class BattleSharedConfig
{
    private static BattleSharedConfig current;

    /// <summary>JSON 在 ResPackage 中的资源地址，不带扩展名。</summary>
    public const string AssetLocation = "Battle/BattleSharedConfig";
    /// <summary>单工程本地源文件路径，编辑器预览与运行时共享。</summary>
    public const string EditorAssetPath = "Assets/Package_Scene/Battle/BattleSharedConfig.json";

    /// <summary>运行时由战斗入口绑定；编辑器非播放状态首次访问时读取本地 JSON。</summary>
    public static BattleSharedConfig Current
    {
        get
        {
#if UNITY_EDITOR
            if (!Application.isPlaying && current is null && System.IO.File.Exists(EditorAssetPath))
            {
                current = FromJson(System.IO.File.ReadAllText(EditorAssetPath));
            }
#endif
            if (current is null)
            {
                current = CreateDefault();
            }
            return current;
        }
    }

    public static BattleSharedConfig CreateDefault()
    {
        return new BattleSharedConfig
        {
            TeamAPositions = new List<Vector3>
            {
                new Vector3(-1.8f, 0.11f, 1.65f),
                new Vector3(-1.8f, 0.11f, 0f),
                new Vector3(-1.8f, 0.11f, -1.65f),
            },
            TeamBPositions = new List<Vector3>
            {
                new Vector3(1.8f, 0.11f, 1.65f),
                new Vector3(1.8f, 0.11f, 0f),
                new Vector3(1.8f, 0.11f, -1.65f),
            },
            DebugSlotSize = new Vector2(1f, 1f),
            CameraPosition = new Vector3(0f, 8.5f, -7.5f),
            CameraEuler = new Vector3(45f, 0f, 0f),
            CameraFov = 36f,
        };
    }

    /// <summary>左方脚底世界坐标（米），下标加一对应服务端 AtPos。</summary>
    public List<Vector3> TeamAPositions = new List<Vector3>();
    /// <summary>右方脚底世界坐标（米），下标加一对应服务端 AtPos。</summary>
    public List<Vector3> TeamBPositions = new List<Vector3>();
    /// <summary>调试方格的地面宽度与深度（米）。</summary>
    public Vector2 DebugSlotSize;
    /// <summary>默认相机根节点的世界位置（米）。</summary>
    public Vector3 CameraPosition;
    /// <summary>默认相机根节点的世界欧拉角（度）。</summary>
    public Vector3 CameraEuler;
    /// <summary>默认垂直 FOV（度），也用作技能运镜的默认起点。</summary>
    public float CameraFov;

    /// <summary>解析 JSON 并检查必要配置；不使用代码内默认站位兜底。</summary>
    public static BattleSharedConfig FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("Battle shared JSON is empty.", nameof(json));
        }
        var config = JsonUtility.FromJson<BattleSharedConfig>(json)
            ?? throw new ArgumentException("Battle shared JSON must contain a config object.", nameof(json));
        config.Validate();
        return config;
    }

    /// <summary>绑定已解析的数据，供共享运镜代码访问；不持有 TextAsset 资源引用。</summary>
    public static void Bind(BattleSharedConfig config)
    {
        current = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>检查数据是否可以用于战场站位、调试方格和相机。</summary>
    public void Validate()
    {
        if (TeamAPositions is null || TeamBPositions is null || TeamAPositions.Count == 0 || TeamBPositions.Count == 0)
        {
            throw new ArgumentException("Both battle teams need at least one stand position.");
        }
        if (!IsFinite(DebugSlotSize.x) || !IsFinite(DebugSlotSize.y) || DebugSlotSize.x <= 0f || DebugSlotSize.y <= 0f)
        {
            throw new ArgumentException("Debug slot width and depth must be positive finite values.");
        }
        if (!IsFinite(CameraFov) || CameraFov < 1f || CameraFov > 179f)
        {
            throw new ArgumentException("Camera FOV must be between 1 and 179 degrees.");
        }
        ValidateVector(CameraPosition);
        ValidateVector(CameraEuler);
        for (int i = 0; i < TeamAPositions.Count; i++)
        {
            ValidateVector(TeamAPositions[i]);
        }
        for (int i = 0; i < TeamBPositions.Count; i++)
        {
            ValidateVector(TeamBPositions[i]);
        }
    }

    /// <summary>获取指定阵营的站位列表；站位数量完全由 JSON 决定。</summary>
    public List<Vector3> GetPositions(UnitTeamType team)
    {
        switch (team)
        {
            case UnitTeamType.TeamA:
                return TeamAPositions;
            case UnitTeamType.TeamB:
                return TeamBPositions;
            default:
                throw new ArgumentOutOfRangeException(nameof(team), team, "Unknown battle team.");
        }
    }

    /// <summary>按服务端一基位置编号获取脚底坐标，超出配置范围时报告错误。</summary>
    public Vector3 GetPosition(UnitTeamType team, int atPos)
    {
        List<Vector3> positions = GetPositions(team);
        if (atPos < 1 || atPos > positions.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(atPos), atPos, $"Battle stand missing: team={team}, count={positions.Count}");
        }
        return positions[atPos - 1];
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static void ValidateVector(Vector3 value)
    {
        if (!IsFinite(value.x) || !IsFinite(value.y) || !IsFinite(value.z))
        {
            throw new ArgumentException("Battle coordinates and camera angles must be finite values.");
        }
    }
}
