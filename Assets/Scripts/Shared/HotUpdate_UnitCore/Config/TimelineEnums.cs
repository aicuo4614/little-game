/// <summary>Duration / 事件时间语义、单位查询与节点类型（Timeline / Unit 共用枚举）。</summary>
public enum DurationType
{
    Time,
    Speed,
}

public enum ValueType
{
    Normal,
    Percent,
}

public enum UnitNodeType
{
    None = 0,
    Root,
    LeftHand,
    RightHand,
    Spine,
    Head,
    /// <summary>单位 GameObject 自身 Transform（非骨骼 Root）。</summary>
    Transform,
    Custom1 = 40,
    Custom2,
    Custom3,
    /// <summary>角色身体中心；优先显式挂点，Spine 使用本体渲染范围中心。</summary>
    BodyCenter = 43,
    GroupCenter = 50,
    World = 51,
    UnitOriginPos = 52,
}

/// <summary>单位阵营。</summary>
public enum UnitTeamType
{
    TeamAll = 0,
    /// <summary>A 队。</summary>
    TeamA = 1,
    /// <summary>B 队。</summary>
    TeamB = 2,
}
