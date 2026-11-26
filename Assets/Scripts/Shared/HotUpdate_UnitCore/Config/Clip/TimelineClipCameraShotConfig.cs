using System;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// 相机运镜模板：相对施法者/目标生成 Push-in / Orbit 路径，朝向独立 LookAt，可选 FOV。
/// </summary>
[Serializable]
public class TimelineClipCameraShotConfig : TimelineClipConfigBase
{
    /// <summary>运镜模板。旧 JSON 缺字段为推近。</summary>
    public TimelineCameraShotType ShotType;

    /// <summary>锁定战斗基准偏航，使用平面构图路径。历史配置缺省为 3D，新建片段默认开启。</summary>
    public bool Use2DComposition;
    /// <summary>2D 环绕、螺旋、推拉升降的横移量（米）。正向相机右侧，阵营镜像时反向。</summary>
    public float PlanarTravel = 2f;

    /// <summary>新版注视端；旧 JSON 缺字段时按 LookAtType/LookAtNodeType/PivotOffset 解析。</summary>
    public TimelineCameraPivotData LookPivot;
    /// <summary>新版起始端；过肩时作为肩位，独立于终点偏移。旧 JSON 缺字段保留旧语义。</summary>
    public TimelineCameraPivotData FromPivot;

    /// <summary>
    /// 注视枢轴。默认自身。Target / 中点依赖施法对象绑定。挂点见 <see cref="LookAtNodeType"/>。
    /// 换枢轴 / 甩镜：作为终点枢轴。所有类型再加 <see cref="PivotOffset"/>。旧 LookAt=自身偏移 按自身解析。
    /// </summary>
    public TimelineCameraShotLookAtType LookAtType = TimelineCameraShotLookAtType.Self;

    /// <summary>
    /// 注视挂点。默认 Spine。None 按 Transform（角色根）。缺节点时回退 Transform。旧 JSON 缺字段为 Spine。
    /// </summary>
    public UnitNodeType LookAtNodeType = UnitNodeType.Spine;

    /// <summary>
    /// 换枢轴 / 甩镜：起始注视枢轴。过肩：不用（肩位走 LookAtFromNodeType）。
    /// 其它类型不用。默认自身。Target / 中点依赖施法对象绑定。
    /// </summary>
    public TimelineCameraShotLookAtType LookAtFromType = TimelineCameraShotLookAtType.Self;

    /// <summary>
    /// 换枢轴 / 甩镜：起始注视挂点。过肩：施法者肩位挂点。默认 Spine。None 按 Transform。
    /// 其它类型不用。旧 JSON 缺字段为 Spine。
    /// </summary>
    public UnitNodeType LookAtFromNodeType = UnitNodeType.Spine;

    /// <summary>
    /// 枢轴世界轴偏移（米）。X 右、Y 高、Z 前。默认 (0,0,0)。
    /// 枢轴 = 注视点 + 本偏移。阵营镜像时随枢轴翻 X。
    /// </summary>
    public Vector3 PivotOffset;

    /// <summary>
    /// 从参考机位接到本条动作。默认勾选。
    /// 参考机位 = 上一条 CameraShot 终点，没有则用 BattleSharedConfig（不抓实时机位，正倒 scrub 一致）。
    /// 不勾则按距离/高度摆绝对起点。
    /// </summary>
    public bool FromCurrentPose = true;

    /// <summary>
    /// 不勾「从当前机位起」时的起点水平距离（米）。推近、拉远、螺旋、推拉升降环绕用。
    /// 勾选时起点锁上一条终点 / 默认机位，本字段不参与采样。
    /// </summary>
    public float StartDistance = 8f;

    /// <summary>
    /// 推近 / 变焦推：终点水平距离。环绕：半径。螺旋 / 推拉升降环绕：终点水平距离（米）。
    /// 过肩：肩位沿「背向注视」退后的水平距离（米）。
    /// </summary>
    public float Distance = 4f;

    /// <summary>
    /// 不勾「从当前机位起」时的起始高度（米）。环绕为全程高度。
    /// 过肩：相对肩位挂点的高度（米）。
    /// </summary>
    public float Height = 2f;

    /// <summary>
    /// 拉远：在起点水平距离上再加这么多米（取绝对值）。
    /// 升起/降下的高度变化（米，取绝对值，方向由类型决定）。
    /// 横移 / 起重机：左右平移距离（米）；正=看向枢轴时向右，负=向左。阵营镜像会翻号。
    /// 螺旋 / 推拉升降环绕：高度变化（米）；正=升高，负=降低。环绕角为 0 则水平角不变。
    /// 过肩：肩侧偏移（米）；正=向右（看向目标时），负=向左。阵营镜像翻号。
    /// </summary>
    public float MoveAmount = 2f;

    /// <summary>
    /// 起重机：高度变化（米）；正=升高，负=降低。其它类型不用。旧 JSON 缺字段为 0。
    /// </summary>
    public float BoomAmount = 2f;

    /// <summary>
    /// Orbit：起始水平角（度）。仅 <see cref="FromCurrentPose"/> 关闭时生效。
    /// 0 = 从「施法者→目标」反方向看过来（正面偏后）。
    /// 甩镜 / 起重机绝对机位：绕枢轴的水平站位角，同样 0=正后方；勾「从当前机位起」时不用。
    /// </summary>
    public float OrbitStartYaw = -20f;

    /// <summary>
    /// Orbit：转角（度）。正=绕世界 Y 逆时针，负=顺时针。
    /// FromCurrentPose 时：从起点参考机位的水平角再转这么多；关闭时：从 OrbitStartYaw 转到 OrbitStartYaw+本值。
    /// </summary>
    public float OrbitAngle = 50f;

    /// <summary>
    /// 定格：勾选后沿用进入机位（上一条运镜终点，没有则默认战斗机位），不读 <see cref="HoldOffset"/>。
    /// 机位相对枢轴的偏移整段锁死；枢轴锁死时相机也不动。旧 JSON 缺字段为 false。
    /// </summary>
    public bool HoldKeepEntryPose;

    /// <summary>
    /// 定格机位：相对注视枢轴的世界轴偏移（米）。X 右、Y 高、Z 前。
    /// 仅 <see cref="HoldKeepEntryPose"/> 关闭时生效。阵营镜像时 X 翻号。缺字段时为战斗默认机位相对原点的偏移。
    /// </summary>
    public Vector3 HoldOffset = new Vector3(0f, 9.1f, -13.4f);

    /// <summary>
    /// 定格：勾选后注视基点（及「保持进入机位」时的进入机位）只用本条第 0 帧；之后角色/注视点再动也不跟。
    /// 枢轴偏移、机位坐标仍用当前配置相对锁住的基点重算，改完立刻可见。默认关闭。旧 JSON 缺字段为 false。FOV 仍按时间插值。
    /// </summary>
    public bool HoldLockFirstFramePose;

    /// <summary>路径参数缓动（归一化时间）。定格只作用于 FOV。</summary>
    public Ease PathEase = Ease.InOutSine;

    /// <summary>
    /// 是否驱动 FOV。关闭则不改 Camera.fieldOfView。
    /// </summary>
    public bool AnimateFov;

    /// <summary>
    /// FOV 起点（度）。仅 AnimateFov 且 <see cref="FromCurrentPose"/> 关闭时生效；
    /// FromCurrentPose 时从起点参考机位的 FOV 接到 FovEnd。
    /// </summary>
    public float FovStart = 60f;

    /// <summary>FOV 终点（度）。仅 AnimateFov 时生效。</summary>
    public float FovEnd = 32f;

    /// <summary>
    /// TeamB 施法时是否按战场中线镜像构图（位置翻 X，朝向用镜像后 LookAt 重算）。默认勾选。
    /// </summary>
    public bool MirrorByTeam = true;

    /// <summary>
    /// 锁定水平：LookAt 用世界 up，禁止荷兰角。默认勾选。
    /// </summary>
    public bool LockCameraUpright = true;
}
