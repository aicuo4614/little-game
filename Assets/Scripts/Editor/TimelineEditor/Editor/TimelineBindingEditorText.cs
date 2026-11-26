/// <summary>对象绑定编辑器界面文案。</summary>
public static class TimelineBindingEditorText
{
    /// <summary>整条时间轴的预览施法者选择框。</summary>
    public static string PreviewCaster = "预览主角";
    /// <summary>清除文档的主角覆盖。</summary>
    public static string ResetPreviewCaster = "恢复默认";
    /// <summary>主角字段的保存和默认语义。</summary>
    public static string PreviewCasterHelp = "拖入角色预制体，随当前 Timeline 保存。未设置时使用乖乖虎，仅用于编辑器预览。";
    /// <summary>非法拖拽资源提示。</summary>
    public static string InvalidPreviewCaster = "请选择 Package_Unit/Unit 下可加载的角色预制体。";
    /// <summary>录制或定位期间禁止替换角色。</summary>
    public static string PreviewCasterBusy = "请结束录制或等待定位完成后再切换预览主角。";

    /// <summary>共享播放器的定帧合并语义。</summary>
    public static string HitStopOverlapHelp = "定帧期间全局时间轴与特效继续；匹配单位的动画/位移暂停。同一单位的重叠定帧按时间并集计算，不重复累加；不同单位独立计算。";
    /// <summary>历史类型的菜单标识。</summary>
    public static string PreviewOnlySuffix = "（仅预览）";
    /// <summary>历史类型的替代建议。</summary>
    public static string PreviewOnlyHelp = "该历史片段仅支持编辑器预览，实战会拒绝。建议使用 3D Animator 动画、单位位移/旋转/缩放、目标运镜或子弹片段替代。";
    /// <summary>命中点的协议限制。</summary>
    public static string HitPointHelp = "当前战斗协议仅支持零个或一个命中点。多个 PlayHit 会被实战拒绝；请用一个 PlayHit 显示本次施法结果，零个时在演出结束显示。2D 单位的受击特效以 TeamA 为制作基准，按效果来源阵营镜像局部形状与横向偏移；世界坐标和群体中心除外。";
    /// <summary>兼容检查入口。</summary>
    public static string Validate = "检查";
    /// <summary>检查结果标题。</summary>
    public static string ValidationTitle = "技能兼容检查";
    /// <summary>兼容检查的可读结果。</summary>
    public static string ValidationSummary = "编辑配置：{0}\n战斗配置：{1}\n本地资源：{2}\n\n实战尚未验证：请更新资源包后检查实际角色动作与对象绑定。";
    /// <summary>单项检查通过。</summary>
    public static string Passed = "通过";
    /// <summary>保存成功标题。</summary>
    public static string SaveSucceeded = "保存成功";
    /// <summary>保存失败标题。</summary>
    public static string SaveFailed = "保存失败";
    /// <summary>覆盖冲突确认。</summary>
    public static string OverwriteConflict = "文件已在磁盘上改变，是否用当前编辑内容覆盖？";
    /// <summary>确认与取消文案。</summary>
    public static string Confirm = "确定";
    /// <summary>取消保存。</summary>
    public static string Cancel = "取消";

    /// <summary>单位位移终点偏移采用的坐标空间。</summary>
    public static string OffsetSpace = "偏移空间";
    /// <summary>接近方向偏移的坐标轴说明。</summary>
    public static string ApproachOffsetHelp = "X 沿起点→参考点，负 X 停在目标前；Y 向上；Z 为侧向。单位：米。进入片段时固定坐标系，不受模型缩放影响。";
    /// <summary>单位片段的作用角色。</summary>
    public static string Subject = "作用单位";
    /// <summary>作用单位的绑定。</summary>
    public static string SubjectBinding = "单位对象绑定";
    /// <summary>空间参考对象。</summary>
    public static string Reference = "参考对象";
    /// <summary>旋转起点。</summary>
    public static string StartEuler = "起点相对角度";
    /// <summary>旋转终点。</summary>
    public static string EndEuler = "终点相对角度";
    /// <summary>缩放起点。</summary>
    public static string StartScale = "起点缩放倍率";
    /// <summary>缩放终点。</summary>
    public static string EndScale = "终点缩放倍率";
    /// <summary>时长模式。</summary>
    public static string DurationMode = "时长模式";
    /// <summary>旋转速度。</summary>
    public static string AngularSpeed = "角速度（度/秒）";
    /// <summary>缩放速度。</summary>
    public static string ScaleSpeed = "缩放倍率/秒";
    /// <summary>局部变换的基准说明。</summary>
    public static string TransformHint = "相对进入片段时的表现姿态计算；Spine 的左右朝向与表现旋转分别控制。";
    /// <summary>施法对象配置 Clip 名称。</summary>
    public static string CastObjects = "配置施法对象";
    /// <summary>效果来源与目标配置 Clip 名称。</summary>
    public static string EffectObjects = "配置效果来源和目标";
    /// <summary>预览配置与实战数据的关系提示。</summary>
    public static string PreviewHint = "此处指定模拟角色，实战由程序传入真实来源和目标。";
    /// <summary>绑定选择字段名。</summary>
    public static string Binding = "对象绑定";
    /// <summary>默认绑定选项。</summary>
    public static string LatestBinding = "前方最近的「配置施法对象」";
    /// <summary>按消费片段开始时间查找前方最近的效果配置。</summary>
    public static string LatestEffectBinding = "前方最近的「配置效果来源和目标」";
    /// <summary>模拟效果来源选择字段名。</summary>
    public static string Source = "模拟效果来源";
    /// <summary>移动单位。</summary>
    public static string MoveUnit = "移动单位";
    /// <summary>移动对象绑定。</summary>
    public static string MovementBinding = "移动对象绑定";
    /// <summary>终点参考。</summary>
    public static string Endpoint = "终点参考";
    /// <summary>终点对象绑定。</summary>
    public static string EndpointBinding = "终点对象绑定";
    /// <summary>多个参考对象。</summary>
    public static string GroupSelection = "多个参考对象";
    /// <summary>时长（秒）。</summary>
    public static string Duration = "时长（秒）";
    /// <summary>速度（米/秒）。</summary>
    public static string Speed = "速度（米/秒）";
    /// <summary>朝向移动方向。</summary>
    public static string FaceDirection = "朝向移动方向";
    /// <summary>世界位置。</summary>
    public static string WorldPosition = "世界位置";
    /// <summary>初始位置偏移。</summary>
    public static string InitialOffset = "初始位置偏移";
    /// <summary>终点。</summary>
    public static string Destination = "终点";
    /// <summary>未配置移动单位，请先配置对应的对象绑定。。</summary>
    public static string MissingMovingUnit = "未配置移动单位，请先配置对应的对象绑定。";
    /// <summary>未配置终点参考对象。。</summary>
    public static string MissingDestination = "未配置终点参考对象。";
    /// <summary>片段时长。</summary>
    public static string ClipDuration = "片段时长";
    /// <summary>施法者。</summary>
    public static string Caster = "施法者";
    /// <summary>效果来源。</summary>
    public static string EffectSource = "效果来源";
    /// <summary>效果目标。</summary>
    public static string EffectTargets = "效果目标";
    /// <summary>与效果目标同队的其他单位。</summary>
    public static string EffectTargetTeammates = "效果目标的其他队友";
    /// <summary>施法对象。</summary>
    public static string CastTargets = "施法对象";
    /// <summary>显隐作用于整个战斗。</summary>
    public static string VisibilityBattle = "整个战斗";
    /// <summary>与施法者同队的其他单位。</summary>
    public static string VisibilityCasterTeammates = "施法者的其他队友";
    /// <summary>所有友敌单位中排除施法者，不包含场景。</summary>
    public static string VisibilityOtherUnits = "施法者之外的其他单位";
    /// <summary>与施法对象同队的其他单位。</summary>
    public static string VisibilityCastTargetTeammates = "施法对象的其他队友";
    /// <summary>与效果来源同队的其他单位。</summary>
    public static string VisibilityEffectSourceTeammates = "效果来源的其他队友";
    /// <summary>施法对象绑定选择器。</summary>
    public static string CastTargetBinding = "施法对象绑定";
    /// <summary>效果来源和效果目标绑定选择器。</summary>
    public static string EffectBinding = "效果绑定";
    /// <summary>初始位置。</summary>
    public static string InitialPosition = "初始位置";
    /// <summary>在位移开始时按距离选择参考对象。</summary>
    public static string Nearest = "距离移动单位最近的对象";
    /// <summary>中心点。</summary>
    public static string Center = "中心点";
    /// <summary>侧向力。</summary>
    public static string SidewaysForce = "侧向力";
    /// <summary>向前力。</summary>
    public static string ForwardForce = "向前力";
    /// <summary>水平力。</summary>
    public static string HorizontalForce = "水平力";
    /// <summary>竖直力。</summary>
    public static string VerticalForce = "竖直力";
    /// <summary>当前 Spine 未包含动画。。</summary>
    public static string NoSpineAnimations = "当前 Spine 未包含动画。";
    /// <summary>Spine 动画。</summary>
    public static string SpineAnimation = "Spine 动画";
    /// <summary>播放速度。</summary>
    public static string AnimationSpeed = "播放速度";
    /// <summary>动画时长。</summary>
    public static string AnimationDuration = "动画时长";
    /// <summary>Spine 受击动画延迟。</summary>
    public static string HitAnimationDelay = "Spine 受击动画延迟";
    /// <summary>同阵营预览角色名称。</summary>
    public static string AllyActor = "己方第 {0} 个";
    /// <summary>敌对阵营预览角色名称。</summary>
    public static string EnemyActor = "敌方第 {0} 个";
    /// <summary>绑定片段名称和时间轴中的行号。</summary>
    public static string BindingClip = "{0}（Clip 在第 {1} 行）";
    /// <summary>滚动画布并闪烁提示对应片段。</summary>
    public static string Locate = "定位";
}
