using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

/// <summary>公开编排动作与配置字段契约，Clip 默认值由编辑器工厂生成。</summary>
internal static class TimelineEditorMcpCatalog
{
    internal static string[] Edits { get; } = new[]
    {
        "add_clip(type, patch?, index?, alias?)", "patch_clip(key, patch)", "remove_clip(key)",
        "duplicate_clip(key, startTime?, patch?, index?, alias?)", "paste_clip(startTime?, patch?, index?, alias?)",
        "move_clip(key, seconds|frames)", "resize_clip(key, frames)", "reorder_clip(key, index)",
        "layer_up(key)", "layer_down(key)", "add_event(key=ClipKey, patch?, alias?)",
        "patch_event(key=EventKey, patch)", "remove_event(key=EventKey)",
        "connect(from=ClipKey|EventKey, to=ClipKey)", "disconnect_input(key=ClipKey)",
        "disconnect_outputs(key=ClipKey|EventKey)", "patch_timeline(patch={TimelineId,Description,PreviewCasterAddress,DurationBindClipKey})"
    };

    internal static string[] Commands { get; } = new[]
    {
        "new(timelineId, revision, discardChanges?)", "load(path, discardChanges?)",
        "save(revision, path?, overwrite?)", "save_as(revision, path, overwrite?)",
        "undo(revision)", "redo(revision)", "validate()", "copy_clip(key, path?)",
        "select(target=clip|head|tail|event, key?|query?)", "step(frames=1)",
        "assets(type=Prefab|AnimationClip|Material|TextAsset, query?, offset?, limit?, scope=runtime|all; default runtime only Package_* assets)",
        "animations(clipKey?)", "preview_unit(revision, address?)", "preview_units()",
        "timeline_view(frameRate?, displayFrames?, frameSnap?, edgeSnap?, cutoffSeconds?)",
        "select_next(query)", "locate_clip(key)",
        "preview_stage(mode=Battle|Detail, distance? [0.8..12 metres]; editor-only, resets playback; opens fixed 9:16 monitor)",
        "capture(width?=540 [180..1080]; height=width*16/9; paused only; PNG camera render, excludes overlay UI)",
        "diagnostics() [current camera, unit/socket viewport positions, active effect particles/materials; no runtime acceptance]"
    };

    internal static object Describe(TimelineControl factory, TimelineClipType type)
    {
        var example = factory.CreateEmptyClip(type).Config;
        var serializer = JsonSerializer.Create(TimelineJsonUtil.ConcreteConfig);
        var contract = (JsonObjectContract)serializer.ContractResolver.ResolveContract(example.GetType());
        var defaults = JObject.FromObject(example, serializer);
        defaults.Remove("ClipKey");
        foreach (var field in defaults.Properties().Where(field => !IsVisibleField(type, field.Name)).ToArray())
        {
            field.Remove();
        }
        return new
        {
            id = (int)type,
            type = type.ToString(),
            label = UnitFeatureTypeDisplay.GetDisplayName(type),
            purpose = Purpose(type),
            canCreate = factory.CanAddClip(type),
            menu = factory.CanAddClip(type) ? UnitFeatureTypeDisplay.GetAddClipMenuPath(type) : null,
            needsCastTargets = TimelineEditorTargetUtil.ClipTypeRequiresSetTargets(type),
            sceneHandles = SceneHandles(type),
            battle = TimelinePlayback.Supports(example),
            restrictions = type == TimelineClipType.PlayHit ? TimelineBindingEditorText.HitPointHelp
                : type == TimelineClipType.PlayBullet ? "FacingType 默认 Trajectory3D；CameraBillboard 对齐独立视觉子节点。BillboardAngleOffset 为素材角度修正（度）。无需在 Prefab 上挂业务脚本。"
                : type == TimelineClipType.HitStop ? TimelineBindingEditorText.HitStopOverlapHelp
                : !TimelinePlayback.Supports(example) ? TimelineBindingEditorText.PreviewOnlyHelp : null,
            playback = TimelinePlayback.Supports(example) ? "shared:TimelinePlayback" : "editor-only adapter (shared scheduler)",
            defaults,
            fields = contract.Properties.Where(field => !field.Ignored && field.Readable && IsVisibleField(type, field.PropertyName))
                .Select(field => new
                {
                    name = field.PropertyName,
                    type = field.PropertyType.Name,
                    writable = field.Writable && IsPatchField(type, field.PropertyName),
                    description = field.PropertyName == "Subject" && HasSimpleSubject(type)
                        ? "作用对象为施法者、效果来源或效果目标，自动取片段开始时的绑定。"
                        : type == TimelineClipType.Visibility && field.PropertyName == "TargetBindingKey"
                            ? "施法对象绑定 ClipKey，留空取显隐开始时最近的施法对象绑定。"
                            : FieldDescription(field.PropertyName),
                    choices = field.PropertyName == "Subject" && HasSimpleSubject(type)
                        ? new[] { "Caster", "EffectSource", "EffectTargets" }
                        : field.PropertyType.IsEnum ? Enum.GetNames(field.PropertyType) : null,
                    nestedFields = NestedFields(field.PropertyType),
                    flags = field.PropertyType.IsDefined(typeof(FlagsAttribute), false)
                }).ToArray()
        };
    }

    private static string[] SceneHandles(TimelineClipType type)
    {
        return type switch
        {
            TimelineClipType.CameraShot => new[] { "LookPivot.Offset / FromPivot.Offset：两端独立的世界轴偏移（米），按阵营镜像 X。" },
            TimelineClipType.Move => new[]
            {
                "OffestPos：相对于平移参考点的终点偏移。平移模式为 None 时不生效。",
                "OffsetEuler：相对于朝向参考点的终点欧拉角。相机锁定水平时 roll 不生效。"
            },
            TimelineClipType.PlayBullet => new[]
            {
                "StartData.OffsetPos / OffsetEuler / OffsetScale：子弹起点手柄，按 StartData 的坐标和挂点设置解释。"
            },
            TimelineClipType.Translate => new[]
            {
                "PointData.OffsetPos：按 OffsetSpace 解释终点手柄；ApproachDirection 的 X 沿进入时起点→参考点，Y 向上，Z 为侧向，单位米。WorldPosition 终点保持绝对世界坐标。"
            },
            _ => Array.Empty<string>()
        };
    }

    private static object NestedFields(Type type)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(System.Collections.Generic.List<>))
        {
            type = type.GetGenericArguments()[0];
        }
        if (type == typeof(UnityEngine.Vector3))
        {
            return new[] { "x: Single", "y: Single", "z: Single" };
        }
        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type.Assembly != typeof(TimelineConfig).Assembly)
        {
            return null;
        }
        return type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Select(field => new
            {
                name = field.Name,
                type = field.FieldType.Name,
                description = type == typeof(TimelineCameraPivotData) ? PivotFieldDescription(field.Name) : FieldDescription(field.Name),
                choices = type == typeof(TimelineCameraPivotData) && field.Name == "NodeType"
                    ? Enum.GetValues(typeof(UnitNodeType)).Cast<UnitNodeType>().Where(TimelineCameraPivotPlayback.IsNode).Select(node => node.ToString()).ToArray()
                    : field.FieldType.IsEnum ? Enum.GetNames(field.FieldType) : null,
                nestedFields = NestedFields(field.FieldType)
            }).ToArray();
    }

    private static string PivotFieldDescription(string field) => field switch
    {
        "Subject" => "从施法者、施法对象、效果来源和效果对象中选择。",
        "BindingKey" => "绑定片段键；空值按进入时间选择对应类型的绑定，施法者无需绑定。",
        "Selection" => "最近或最远以施法者为参照，在进入时选定。Center 取全体中心。",
        "NodeType" => "注视节点。缺少挂点时回退至身体中心。",
        "IncludeCaster" => "取施法者与选定对象参考点的中点。",
        "Offset" => "世界轴 XYZ 偏移（米），阵营镜像翻转 X。",
        "LockAtEntry" => "锁定本端进入时位置；关闭时随已选角色移动。",
        _ => FieldDescription(field)
    };

    private static bool HasSimpleSubject(TimelineClipType type)
        => type == TimelineClipType.PlayEffect || type == TimelineClipType.Translate;

    private static bool IsVisibleField(TimelineClipType type, string field)
    {
        if (HasSimpleSubject(type) && field == "TargetBindingKey")
        {
            return false;
        }
        return type != TimelineClipType.Translate
            || field is not ("SubjectBindingKey" or "DestinationBindingKey" or "DurationType");
    }

    internal static bool IsPatchField(TimelineClipType type, string field)
        => IsVisibleField(type, field) && field != "ClipKey" && field != "Type" && field != "PreKey"
            && field != "EventConfigs" && field != "LerpValueAfterMoveTo"
            && (type != TimelineClipType.CameraShot || field is not
                ("LookAtType" or "LookAtNodeType" or "LookAtFromType" or "LookAtFromNodeType" or "PivotOffset" or "TargetBindingKey"));

    private static string Purpose(TimelineClipType type)
    {
        return type switch
        {
            TimelineClipType.PlayAnimator => "播放施法者的 3D Animator 动画，时长由状态 Clip 长度与速度确定。预览与实战共用。",
            TimelineClipType.PlaySpine => "历史兼容占位（本项目使用 3D Animator，请优先使用 PlayAnimator）。",
            TimelineClipType.PlayBullet => "从绑定的效果来源向每个效果目标播放一份子弹，可设置起终点、轨迹和延迟。预览与实战共用。",
            TimelineClipType.PlayEffect => "每个作用单位各播放一份特效，分别按其挂点定位和采样，无对象时不播放。",
            TimelineClipType.PlayHit => "播放效果目标的受击动画与特效，并提供命中时机。",
            TimelineClipType.Translate => "移动选中的单位到指定目的地。目标组使用 ReferenceSelection，按各移动单位在片段开始时的位置确定终点。",
            TimelineClipType.Rotate => "让单位按角度或朝向目标旋转。",
            TimelineClipType.Scale => "按时间或速度改变单位缩放。",
            TimelineClipType.CameraShake => "按不同振幅包络驱动镜头震动。",
            TimelineClipType.SetCastTargets => "绑定本次施法对象，供位移或运镜等片段消费。",
            TimelineClipType.SetHitTargets => "绑定效果来源和目标，供受击等片段消费。",
            TimelineClipType.Force => "施加攻击、击退或击飞表现力。",
            TimelineClipType.Follow => "让指定对象跟随相机或效果配置指向的目标。",
            TimelineClipType.SceneDarken => "控制场景压暗及其过渡，结束保持终值，整段停止或取消时恢复。预览与实战共用。",
            TimelineClipType.Move => "为相机或特效设置运动、旋转和空间参考点。",
            TimelineClipType.RoleRotateToTarget => "令角色朝向施法对象。",
            TimelineClipType.HitStop => "暂停绑定单位的局部时间，全局 Timeline 继续。",
            TimelineClipType.SetSkybox => "预览与实战共用天空盒切换；实战预加载 ScenePackage 材质，结束可选恢复，整段停止或取消时恢复。",
            TimelineClipType.Visibility => "预览与实战共用显隐。对象在片段起点确定，切换单位根节点及战场建筑渲染；演出停止时恢复。",
            TimelineClipType.CameraPathAnim => "用 AnimationClip 播放预设相机轨迹。",
            TimelineClipType.HideBattleUI => "实战按层隐藏战斗 UI，GM 始终跟随；结束可选恢复，整段停止或取消时恢复原始显隐。美术预览无战斗 UI。",
            TimelineClipType.CameraShot => "支持固定战斗偏航的 2D 构图及 3D 运镜模板。",
            TimelineClipType.CameraShotBlend => "过渡至指定或自动选中的下一条运镜。",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "请补充新增 Clip 的 MCP 用途说明。")
        };
    }

    private static string FieldDescription(string field)
    {
        return field switch
        {
            "Type" => "Clip 类型标识，创建后固定。",
            "ClipKey" => "Clip 持久标识，创建时生成。",
            "StartTime" => "起点秒数。有前置连线时，由前置 Clip 或事件求值。",
            "DelayTime" => "在原始或求值起点之后追加的秒数。",
            "Duration" => "内容时长（秒）。自动时长或结束绑定会覆盖计算结果。",
            "PreKey" => "前置 ClipKey 或 EventKey，使用 connect 和 disconnect 更新。",
            "EventConfigs" => "保存的片内事件，使用事件操作单独编辑。",
            "EndByOtherClipEnd" => "对齐指定 Clip 的内容结束。空值清除绑定。",
            "TargetBindingKey" => "对象绑定 ClipKey。留空使用对应上下文的默认绑定。",
            "SubjectBindingKey" => "作用单位绑定 ClipKey。留空取本片段开始前最近的同类绑定；施法者不需要绑定。",
            "DestinationBindingKey" => "目的地参考对象绑定，留空自动解析。",
            "NextCameraShotClipKey" => "过渡的目的运镜。留空时自动查找后继运镜。",
            "DurationType" => "时长参数采用固定秒数还是速度计算。",
            "DurationPraram" => "由 DurationType 决定的秒数或速度。",
            "Speed" => "播放或运动速度，含义由 Clip 类型决定。",
            "AnimationName" or "SpineAnimationName" or "AnimatorParamName" or "ParamName" => "动画名称，使用 animations 查询预览资源。",
            "EffectPath" or "PrefabPath" or "Path" => "预制体逻辑资源地址，保留工程已有资源格式。",
            "SkyboxMaterialPath" or "AnimationClipPath" => "Unity 资源路径，可使用 assets 查询。",
            "PreviewSource" => "$caster 为自己，$ally:n 为相对友方，$enemy:n 为相对敌方；切预览阵营时关系不变。只接受相对角色键。",
            "PreviewTargets" => "相对预览单位键列表，使用 preview_units 查询；$caster/$ally:n/$enemy:n。实战对象由协议传入。",
            "OffsetSpace" => "单位位移终点偏移空间：ApproachDirection=起点→参考点建轴，X 前后/Y 高度/Z 侧向，负 X 停在目标前，单位世界米；NodeLocal=挂点旋转缩放；World=相对参考点的世界方向偏移。WorldPosition 终点始终为绝对世界位置。默认 ApproachDirection。",
            "QueryPointData" => "查找作用对象，再使用挂点数据计算特效位置。",
            "Side" => "查询使用的相对阵营。",
            "Selection" => "从匹配单位中选取哪些对象。",
            "MaxRange" => "从查询原点计算的最大距离，小于等于 0 时不限。",
            "PointData" or "UnitNodePointData" => "单位参考节点及局部偏移。",
            "OffsetNodeType" or "AttachNodeType" or "FollowNodeType" => "挂点或跟随参考节点。",
            "OffsetPos" or "OffestPos" => "相对于参考节点或参考点的位置偏移（米）。",
            "OffsetEuler" => "相对参考朝向的欧拉角（度）。",
            "OffsetScale" or "Scale" => "XYZ 缩放倍率。",
            "NeedSetParent" => "将对象挂到参考节点下。",
            "DontFollow" => "未挂载时仅首帧应用位姿，随后停在世界落点。",
            "UseOriginalRotation" => "Inspector 的重置旋转。忽略 prefab 原旋转，再应用配置偏移。",
            "UseOriginalScale" => "Inspector 的重置缩放。忽略 prefab 原缩放，从单位缩放起算。",
            "OverlayCharacterFacing" => "重置旋转时叠加作用角色的水平朝向。",
            "StartData" or "EndData" => "弹道起点或终点的参考节点和位姿偏移。",
            "MoveSpeed" => "弹道飞行速度。",
            "FacingType" => "Trajectory3D 保留原有三维朝向；CameraBillboard 让独立 Spine 子节点面向相机并沿画面飞行方向旋转。",
            "BillboardAngleOffset" => "CameraBillboard 的素材角度修正（度）；朝右为 0，朝上为 -90。",
            "MoveDelayTime" => "子弹在起点停留的秒数，之后开始飞行。",
            "DelayScaleKeys" => "延迟段的缩放关键帧。要求起点重置缩放且 MoveDelayTime 大于 0。",
            "TrajectoryType" => "直线或抛物线弹道。",
            "ArcHeightFactor" => "抛物线弧高相对于起终点距离的比例。",
            "SpineAnimationDelay" or "AnimatorDelayTime" => "相对命中点的动画启动延迟（秒）。",
            "AnimatorStateSpeed" => "Animator 片段使用的动画速度倍率。",
            "Loop" => "是否循环播放动画。",
            "AffectedByHitStop" => "局部时间是否受命中的角色定帧影响。",
            "MoveEntityType" => "受操作的对象类型，例如相机或指定效果。",
            "Subject" => "作用单位：施法者、施法对象、效果来源或效果目标。多个单位分别执行。",
            "Destination" => "移动目的地的类型。",
            "ReferenceSelection" => "终点或朝向参考为施法对象、效果目标时生效。Nearest/Farthest 按片段开始时到各作用单位的距离选一个参考，Center 取全部参考点的中心。",
            "IsRotateToAtStart" => "位移开始时令单位朝向移动目标。",
            "TranslateMovePointType" or "RotateMovePointType" => "相对于施法者、对象或中点计算移动或旋转。",
            "MoveToEase" or "PathEase" or "BlendEase" => "时间进度的缓动曲线。",
            "StartEuler" or "EndEuler" => "相对进入片段时表现姿态的欧拉角（度），允许超过 360 度。",
            "StartScale" or "EndScale" => "相对进入片段时表现缩放的 XYZ 倍率，不改写预制体基准大小。",
            "FollowLagTime" => "跟随的平滑滞后时间（秒），0 表示立即跟随。",
            "LerpValueAfterMoveTo" => "旧版跟随速率，仅供加载迁移到 FollowLagTime。",
            "FastApproachFinalEuler" => "加快向目标旋转的速度。",
            "FastApproachFinalEulerSpeed" => "追近速度（0 到 1），仅启用 FastApproachFinalEuler 时使用。",
            "PositionStrength" or "RotationStrength" => "每个轴的震动幅度，位移为距离，旋转为角度。",
            "Vibrato" => "震动频次参数。",
            "Randomness" => "震动方向的随机角度范围。",
            "EnvelopeType" => "震动幅度随时间增长或衰减的方式。",
            "ShotType" => "选择推近、环绕、定格等运镜模板。",
            "LookAtType" or "LookAtNodeType" => "注视对象或其挂点，目标和中点依赖施法绑定。",
            "LookAtFromType" or "LookAtFromNodeType" => "换枢轴或甩镜的起始注视参考。过肩使用起始挂点作肩位。",
            "Use2DComposition" => "开启时锁定战斗初始偏航，保留俯仰、平移和 FOV。新片段默认开启；旧配置缺省时按 3D 播放。",
            "PlanarTravel" => "2D 环绕类的横移量（米）。正值沿固定机位的右向，阵营镜像时反向。",
            "LookPivot" => "注视端。缺失时读取旧的 LookAtType、LookAtNodeType 和 PivotOffset。",
            "FromPivot" => "换枢轴、绝对起点甩镜或过肩使用的独立起始端。缺失时读取旧字段。",
            "PivotOffset" => "注视枢轴的 XYZ 世界轴偏移（米）。阵营镜像时翻转 X。",
            "FromCurrentPose" => "从前一条运镜终点或默认机位接入。关闭可指定起始构图。",
            "StartDistance" => "起点水平距离（米）。关闭 FromCurrentPose 时对支持的模板生效。",
            "Distance" => "水平距离（米），含义随运镜模板而变，例如推近终点、环绕半径或过肩后退。",
            "Height" => "机位高度（米）。环绕使用全程高度，过肩相对肩位。",
            "MoveAmount" => "模板的移动量（米），例如拉远、横移、升降或过肩侧移。",
            "BoomAmount" => "起重机模板的高度变化（米），正值升高。",
            "OrbitStartYaw" or "OrbitAngle" => "绕世界 Y 轴的起始站位角或转过的角度（度）。",
            "HoldKeepEntryPose" => "定格沿用进入机位，开启时忽略 HoldOffset。",
            "HoldOffset" => "定格机位相对枢轴的偏移（米）。",
            "HoldLockFirstFramePose" => "定格时锁住首帧的注视基点。",
            "AnimateFov" => "是否驱动相机视场角。",
            "FovStart" => "起始视场角（度）。启用 AnimateFov 并关闭 FromCurrentPose 时使用。",
            "FovEnd" => "终止视场角（度）。启用 AnimateFov 时使用。",
            "BlendFov" => "目标运镜驱动 FOV 时，过渡同时插值其视场角。",
            "MirrorByTeam" => "TeamB 施法时按战场中线镜像。",
            "LockCameraUpright" => "用世界上方向保持相机水平，避免横滚。",
            "HitTranslateType" => "选择攻击位移、击退或击飞。",
            "Force" => "施加的 XYZ 力向量。",
            "StartStrength" or "EndStrength" => "场景压暗的起始或终止强度。",
            "Show" => "显示或隐藏目标。",
            "TargetType" => "显隐作用对象。OtherUnits 为施法者之外的所有友敌单位，不含场景。Teammates 选项作用于任一已绑定对象的同队单位，排除所有已绑定对象本身。",
            "EffectBindingKey" => "效果绑定 ClipKey，留空取显隐开始时最近的效果来源和目标绑定。",
            "BattleExcludeTypes" => "整个战斗显隐时，排除选中的阵营类别和绑定单位。",
            "HideLayers" => "隐藏的战斗 UI 层，允许组合。",
            "ResetOnEnd" => "片段结束时恢复进入片段之前的状态。",
            _ => null
        };
    }
}
