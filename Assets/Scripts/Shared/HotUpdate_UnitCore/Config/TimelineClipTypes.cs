using System;

/// <summary>Timeline Clip 类型与移动/受力等相关枚举；以及 Clip/Event Key 生成。</summary>
public static class TimelineKeys
{
    /// <summary>生成全局唯一 ClipKey。</summary>
    public static string GenerateClipKey()
    {
        return "clip_" + Guid.NewGuid().ToString("N");
    }

    /// <summary>生成全局唯一 EventKey。</summary>
    public static string GenerateEventKey()
    {
        return "event_" + Guid.NewGuid().ToString("N");
    }
}

public enum TimelineClipType
{
    PlayAnimator = 2,
    PlayBullet = 7,
    PlayEffect = 8,
    PlayHit = 10,
    Translate = 11,
    Rotate = 12,
    Scale = 13,
    CameraShake = 15,
    // 16 为已移除的旧设置目标片段。
    Force = 17,
    Follow = 18,
    SceneDarken = 19,
    Move = 21,
    RoleRotateToTarget = 22,
    /// <summary>角色定帧（HitStop）：全局时间轴继续，匹配查询的单位动画/位移逻辑时间暂停。</summary>
    HitStop = 23,
    /// <summary>换天空盒：替换 RenderSettings.skybox 材质引用，离开 Clip 还原。</summary>
    SetSkybox = 24,
    /// <summary>显隐：主角 / 目标 / 整个战斗（可多选排除）；结束时重置默认不勾。</summary>
    Visibility = 25,
    /// <summary>相机运动轨迹动画：BattleCamera 挂主角根后播放 AnimationClip（编辑器先实现）。</summary>
    CameraPathAnim = 26,
    /// <summary>隐藏战场 UI：瞬时；按层多选隐藏 Top/Bottom/Float/Foot；GM 始终跟随；结束时重置默认不勾。</summary>
    HideBattleUI = 27,
    /// <summary>相机运镜模板：相对角色的 Push-in / Orbit + LookAt + 可选 FOV。</summary>
    CameraShot = 28,
    /// <summary>相机运镜过渡：上一条运镜模板终点（或默认机位）→ 下一条 CameraShot 模板起点。</summary>
    CameraShotBlend = 29,
    /// <summary>播放施法者的 Spine 动画。</summary>
    PlaySpine = 30,
    /// <summary>绑定本次施法对象。</summary>
    SetCastTargets = 31,
    /// <summary>绑定本次效果的来源和目标。</summary>
    SetHitTargets = 32,
}

public enum SelfOrTargetType
{
    Self,
    Target,
}

public enum RotateMovePointType
{
    RotateToSelf,
    RotateToTarget,
    RotateToSelfTargetCenter,
    None,
}

public enum TranslateMovePointType
{
    TranslateToSelf,
    TranslateToTarget,
    TranslateToSelfTargetCenter,
    None,
}

public enum TimelineMoveEntityType
{
    Camera,
    Effect,
}

public enum CoordinateSystem
{
    Local,
    World,
}

public enum PointOffsetValueType
{
    UnitLocal,
    World,
    ArcFromChord,
}

public enum HitTranslateType
{
    Attack,
    HitBack,
    HitFly,
    None,
}

/// <summary>
/// PlayBullet 弹道类型。数值写入 JSON，禁止改已有取值。
/// 缺字段时反序列化为 0，即抛物线（与旧技能一致）。
/// </summary>
public enum TimelineBulletTrajectoryType
{
    /// <summary>抛物线：二次贝塞尔，控制点在弦中点上方。</summary>
    Parabola = 0,
    /// <summary>直线：起点到终点匀速插值；终点在 Init 时锁定。</summary>
    Linear = 1,
}

/// <summary>
/// CameraShake 幅度包络。数值写入 JSON，禁止改已有取值。
/// 缺字段时反序列化为 0，即衰减（对齐旧 DOTween fadeOut 默认）。
/// </summary>
public enum TimelineCameraShakeEnvelopeType
{
    /// <summary>衰减：满幅度线性收到 0。</summary>
    FadeOut = 0,
    /// <summary>恒定：全程满幅度。</summary>
    Constant = 1,
    /// <summary>增强：从 0 线性拉到满幅度。</summary>
    FadeIn = 2,
    /// <summary>先强后弱：中间满幅度，两头为 0。</summary>
    FadeInOut = 3,
}

/// <summary>
/// 相机运镜模板类型。数值写入 JSON，禁止改已有取值。
/// 缺字段时反序列化为 0，即推近。
/// </summary>
public enum TimelineCameraShotType
{
    /// <summary>推近：水平角、高度锁在起点，只把水平距离收到终点。</summary>
    PushIn = 0,
    /// <summary>环绕：绕枢轴水平转角，半径与高度按模板，始终 LookAt 枢轴。</summary>
    Orbit = 1,
    /// <summary>拉远：水平角、高度锁在起点，只把水平距离收到更远的终点。</summary>
    PullOut = 2,
    /// <summary>升起：水平角、距离锁在起点，只抬高。</summary>
    BoomUp = 3,
    /// <summary>降下：水平角、距离锁在起点，只降低。</summary>
    BoomDown = 4,
    /// <summary>横移：沿看向枢轴时的左右方向平移，高度不变。</summary>
    Truck = 5,
    /// <summary>螺旋：从起点同时转水平角、改半径和高度。</summary>
    Helix = 6,
    /// <summary>换枢轴：机位锁定，LookAt 从起始枢轴插到终点枢轴。</summary>
    PivotBlend = 7,
    /// <summary>推拉升降环绕：同时改水平距离、高度和水平角。环绕角为 0 则只推拉升降。</summary>
    DollyBoom = 8,
    /// <summary>定格：停在 <see cref="TimelineClipCameraShotConfig.HoldOffset"/>，相对注视枢轴，不移动。</summary>
    Hold = 9,
    /// <summary>甩镜：机位锁定，朝向从起始枢轴水平扫到终点枢轴（中间不跟注视点插值）。</summary>
    WhipPan = 10,
    /// <summary>变焦推：沿看向轴收/放距离，同时强制驱动 FOV 对冲（眩晕/希区柯克变焦）。</summary>
    DollyZoom = 11,
    /// <summary>过肩：落在施法者肩后看向注视枢轴（通常目标）的构图；可从当前机位接到该构图。</summary>
    Ots = 12,
    /// <summary>起重机：同时横移 + 升降（对角线）；横移用 MoveAmount，高度变化用 BoomAmount。</summary>
    Crane = 13,
}

/// <summary>
/// 相机运镜 LookAt 枢轴。数值写入 JSON，禁止改已有取值。
/// </summary>
public enum TimelineCameraShotLookAtType
{
    /// <summary>施法者位置。再加 <see cref="TimelineClipCameraShotConfig.PivotOffset"/>。</summary>
    Self = 0,
    /// <summary>施法对象中心。再加 PivotOffset。</summary>
    Target = 1,
    /// <summary>施法者与目标中点。再加 PivotOffset。</summary>
    SelfTargetCenter = 2,
    /// <summary>旧 JSON：按自身 + PivotOffset 解析。勿改数值，勿在新数据里写。</summary>
    SelfOffset = 3,
}
