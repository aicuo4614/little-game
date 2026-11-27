# 美术资源来源与单位选型

更新：2026-11-27。用途：供后续对话持续查阅、补充资源入口与选型依据。本目录同时保存初选和后续接入记录；各阶段验证范围以对应记录为准。

## 持续维护约定

### 2026-11-27 Android 黑屏技术依据与资源路径更新

- 来源：HybridCLR 官方 [常见错误](https://www.hybridclr.cn/docs/help/commonerrors)、[MonoBehaviour 支持](https://www.hybridclr.cn/docs/basic/monobehaviour)，2026-11-27 只读核查，未迁入第三方代码或素材。适用对象为热更脚本挂载资源的 Android 打包与加载。
- 首次新包实测：Samsung SM-G9550 / Android 7.0 / ARM64，七个内置包和四个热更 DLL 已加载；原 Resources 单位外观与 UnitMono 报脚本缺失及序列化布局不一致，主包描边 Feature 同样存在热更程序集启动时序问题，随后捕获 SIGSEGV。证据：Reports/Mobile/black-screen-20261127/new-package-full.txt。
- 外观迁移至 Assets/Package_Unit/Appearance/Base，运行时地址 Appearance/{Role}；模型以 Package_Unit/Low/Base 为唯一构建产出。旧 Resources 模型移出原生 Resources 至 Art/UnitGenerated/Models，保留 GUID，外观引用已改为包内标准模型。下文旧的双份生成说明由本条覆盖。
- RocoCharacterOutlineFeature 连同 meta 移至 Assets/Scripts/Shared/Rendering，使主包 URP 配置在热更加载前即可恢复 AOT 脚本。修复后的真机验证结论以本次验收报告为准。

用户后续提供的新路径、网站、资源包、链接或美术参考，必须在当次工作中自动补入本目录和根 AGENTS.md 的来源登记，不等待再次提醒。记录提出日期、准确入口、授权原意、获取状态、用途、风险和验证阶段；重复入口合并，旧判断保留变更原因。参考不等于使用授权；本地存在不等于已导入或运行通过。

只读检查外部资源工程，按需复制模型、动作、材质、贴图及必要依赖，不整库搬迁，不修改源工程。资源进入当前工程后保留出处与修改记录；需要付款、账号操作或额外授权时，只请求最小必要协助。

## 统一获取入口

| 来源 | 入口 | 授权与当前状态 |
| --- | --- | --- |
| 当前项目 | `E:\Project\little-game\Assets\LittleGame\Resources` | CombatCharacters 已有 KayKit 四名角色及部分动作；Courtyard 已有庭院资源。既有验证见 Combat-Handoff.md，不套用到新候选 |
| 素材收藏室 | `E:\Art\material-vault`；https://myassetroom.com | 用户自有素材目录项目；目录不等于全部资源均为自有版权。只读检索 libraries 下 catalog、previews、downloads 与来源记录 |
| AutoClash | `E:\Project\AutoClash_20210125\AutoClash1` | 2026-11-24 用户确认资源可用，不再反复核对商用授权；仍需技术验证 |
| PELibDemo | `E:\Project\PELibDemo` | 2026-11-24 用户明确其中多组模型可以使用；不扩张为任意代码、SDK或网络服务授权 |
| 架构与工具参考 | `F:\Git\kabu_mobile\demo`；历史路径 `E:\Git\demo` | 已授权技术参考，路径/版本使用前确认；不自动视为所有第三方美术均获授权 |
| 玩法与镜头参考 | `E:\Project\bump` | 2026-11-24 用户新增为玩法/横向推进与战斗体验参考工程（只读检查）；未获美术资源或代码使用授权，不搬入素材 |
| 避障算法参考 | `E:\Project\AnimalBattle` | 2026-11-25 用户新增为避障插件参考工程（只读检查）；`Assets/RVO/RVO/src` 内为 Apache License 2.0 开源 RVO2 Library C#（UNC Chapel Hill），仅适配其开源 ORCA/RVO2 算法至本工程纯规则层，不搬入其他美术资源或业务代码 |
| 旧横板动作移动/AI参考 | `E:\Project\射雕1.9版本\Client` | 2026-11-25 用户提供旧作参考，要求分析移动、阻挡和 AI 并评估不用 RVO 的方案；仅授权只读分析，未授权迁入代码/美术。目录可访问，ProjectVersion 为 Unity 5.6.6f2，已完成 Character/Move、Character/AI、CharBodyController、Physics2D 碰撞矩阵及角色数据的静态分析；不运行、升级或修改源工程。 |
| 真机调试工具参考 | F:/Git/kabu_mobile/ClientExpress | 2026-11-26 用户要求只读分析。Unity 2022.3.62f3c1，已定位 Assets/3rd/StompyRobot/SRDebugger 和依赖 SRF，以及 GM 调试入口。源码可读，第三方插件迁入与运行验证均未执行。 |
| Unity Asset Store | https://assetstore.unity.com | 可自主筛选；付费由用户决定并提供，不代付款；优先查本地是否已有同包 |
| 作者网站与其他网络来源 | Kay Lousberg、Quaternius、Synty、itch.io 等 | 保留原始作者链接，区分免费、EXTRA、SOURCE，按具体来源核对许可；不从不明转载站获取付费包 |

## ClientExpress 真机调试参考

本地 SRDebugger 的 README 标明 1.13 系列与 Unity 2022.3.62 起的支持基线。StandardConsoleService 监听 Unity 的线程安全日志回调，面板可筛选日志、查看堆栈和复制内容。项目从 HotUpdate_Main 的 GM 调试页打开面板。该入口依赖业务模块已启动，无法用于观察进战斗前的挂起。插件另有早期自动初始化能力，后续如接入应从 AOT 提供开发包入口，原生闪退仍通过 ADB 捕获。SRDebugger 采用商店资产条款，SRF 单独附 MIT 许可。仅核查本地源码与配置，未运行或修改参考工程。

## 本次玩法的美术需求

我方：盾卫、风弓手、雷法师、冰法师。敌方：步兵、疾行狼、重甲兵、治疗师、石甲巨像。三兵合成最高三星，二星有技能分支。

优先保证竖屏俯视下的轮廓、兵种和敌我辨识；雷/冰法师不只靠细微颜色区分。升星可用装备、头饰、体态或明确特效形成差异，不要求每星重新做完整模型。至少核对待机、移动、攻击/施法、受击、死亡；盾卫另查防御，首领另查蓄力与重击。动作文件存在不代表挂点、发射、命中、脚底和实际规则时序已正确。

## PELibDemo 静态检查

源工程版本 Unity 2021.3.33f1；manifest 的 URP 为 12.1.13。当前目标为 Unity 2022.3.62f3c1、URP 14.0.12、Linear，不能直接认定 shader 兼容。Assets 中共发现 1,382 个 FBX、5 个 GLB、2,604 个 prefab；包含动作、武器、场景与特效，绝不是独立角色数量。机器清单见 `Reports/Art/2026-11-24-resource-inventory.json`。

### 首选试样：RPGTinyHeroWavePBR

- 路径：`E:\Project\PELibDemo\Assets\Art\3D\RPGTinyHeroWavePBR`。这正是前轮商店候选 RPG Tiny Hero Wave PBR 的同名本地资源，已无需再请求用户购买/提供；具体包版本不由名称推断。
- 有 `Prefab/ModularCharacters/MC01.prefab` 至 `MC24.prefab`，以及 MaskTintCharacters、HeadParts、Weapons。24个预制体不等于24个职业，具体装配仍待画面对照。
- 动作目录共8类：BowAndArrow、DoubleSword、MagicWand、NoWeapon、SingleSword、Spear、SwordAndShield、TwoHandSword。分别发现23、23、24、35、23、23、23、23个 FBX；以上197个仅为八类动作目录的根层FBX数量，未包含 InPlace/RootMotion 子目录；后续接入已找到并提取所需 InPlace 移动动作。不是全包动作总数，也不等于 AnimationClip 数量。
- 优先映射：盾卫→剑盾；风弓手→弓箭；雷/冰法师→法杖，利用模块装备和配色区分；步兵/重甲兵→同族轻重装，治疗师→单独装配并验证施法。
- 比现有 KayKit 更值得优先试样的原因是模块化外观、弓箭/剑盾/法杖动作目录齐全，能支持升星差异；不是已经认定画面品质更好。
- 材质同时引用 URP Lit、自定义 PBRMaskTint 和内置材质。部分 URP Lit 指向源工程其他目录中的 shader（`Assets/Art/StylizedFantasyFxPack/Shader/Lit.shader`），直接只复制模型目录会有依赖风险。迁入时重建/适配目标材质，不覆盖当前 URP shader。
- 待查：蒙皮面数、骨骼与材质数、模块多 renderer 开销、装备挂点、MaskTint 在 URP14 的效果、阴影与俯视比例、手机性能。

### 配套怪物与其他组

- `Assets/Art/3D/Monsters Ultimate Pack 02 Cute Series`：446个 FBX，31个 prefab、25个 controller。发现狼、幼狼、狼人、龙、植物、蛇、训练假人等模型；动作和变体占大量文件，不按446个怪物计。
- 狼对应疾行狼；可与 AutoClash 的同名包比较哈希、动作与贴图，避免重复导入。同包名称不等于文件完全相同。
- `Assets/Art/3D/Hovl Studio`：219个 prefab，主要为特效候选，供雷、冰、投射物演出筛选，不算角色包。
- `Assets/Art/3D/Sci Fi Hexagon Battlefields Cute Series`：科幻主题，当前遗迹奇幻方向优先级低。
- `Assets/Art/KayKit_Adventurers_1.0_FREE`：已有另一份旧版 KayKit；当前项目来自素材网的 Free 2.0，不建议用旧版覆盖现有角色和骨架。
- `Assets/Art/Hero`、`Monster` 主要是 PNG 与图集；`Pixelazia Monsters` 是精灵表、500个 .anim 和100个 prefab 的2D体系，不作为本轮3D单位主方案。Character/Characters 目录主要是鸡与火球，也不能由目录名认定为军团角色库。

## AutoClash 与素材网统一对比

| 资源 | 精确位置 | 用途与限制 |
| --- | --- | --- |
| AutoClash 骑士/法师 | `Assets/Assets/Zelda/Model`、`Animation` | KnightSK、HeavyKnightSK、FootmanSK、MageSK、BattleMageSK、WizardSK 等21个模型；动画目录有234个FBX。已有 people.jpg 外观预览；兵种覆盖仍按具体模型和动作核对，不能由文件夹名称判断来源 |
| AutoClash 石头人 | `Assets/Assets/Characters/052_Golem_StoneGolem` | GolemMesh、052_Stone Golem.prefab、controller；有两种攻击、受击、死亡、待机、跑走等动作文件。石甲巨像优先候选；需验证比例、材质、蓄力演出 |
| AutoClash 狼 | `Assets/Assets/Monsters Ultimate Pack 02 Cute Series/Wolf Cute Series` | 有 Wolf.prefab、controller，奔跑、咬击、扑击、受伤、死亡等FBX，优先于缺战斗动作的独立狼 |
| AutoClash 骷髅/兽人 | `Assets/Assets/Characters` | UndeadWarrior、BoneGuard、Lich、Grunt、Warlock 等成组目录，可作敌方备选；与我方系列的比例需对照 |
| 素材网 KayKit | `libraries/kaykit/downloads/adventurers-free-2.0.zip`、`skeletons-free-1.1.zip`、`character-animations-free-1.1.zip` | 本地已缓存；当前项目已用其中部分资源。骷髅免费版4种基础角色，巨型骷髅与死灵法师属于 EXTRA，不把宣传图当免费包内容 |
| 素材网 Polyfork 狼 | `libraries/polyfork/downloads/wolf-base.zip` | 实查GLB有1套skin、3个动画 Idle/Walk/Run，缺攻击、受击、死亡，低于已有完整动作狼的优先级 |
| 素材网 Polyfork 石傀儡 | `libraries/polyfork/downloads/stone-golem-miniature-6a84b6.zip` | 实查GLB无skin、无动画，适合摆件参考，不直接充当已可用战斗首领 |
| 素材网 Polyfork 骑士 | `libraries/polyfork/downloads/armoured-knight-hero-07121f.zip` | 实查GLB有skin但无动画；skin数量不是骨骼数，仍需动作适配 |
| 素材网 Quaternius | `libraries/quaternius` | Ultimate Monsters 有目录/预览，历史缓存记录为上游下载配额限制；当前是否恢复需另查。其他基础人体/通用动画包不能当作现成奇幻兵种 |

## 网络候选的保留入口

以下为2026-11-24前轮官方页面查询结果，价格与版本属于当时记录，购买前重新核对。

- Synty POLYGON MINI Fantasy Characters：https://syntystore.com/products/polygon-mini-fantasy-characters-pack 。当时 US$39.99，官方列60个角色、支持 Unity2022.3/URP，但明确不含动画；包括其他题材，不能全算本游戏兵种。保留为统一角色替代方案。
- Toon RTS Units：https://assetstore.unity.com/packages/3d/characters/toon-rts-units-67948 。当时 US$25；新版页面Unity6，目标2022.3需核对可得版本，不建议未经试样直接购买。
- RPG Tiny Hero Wave PBR：https://assetstore.unity.com/packages/3d/characters/humanoids/rpg-tiny-hero-wave-pbr-228701 。前轮标价US$45；现已发现PELibDemo本地包，移出待索取清单。
- Mini Legion Rock Golem：https://assetstore.unity.com/packages/3d/characters/humanoids/fantasy/mini-legion-rock-golem-pbr-hp-polyart-94707 。免费、老版本；优先检查已授权本地石头人，避免重复获取。
- KayKit：https://kaylousberg.itch.io/kaykit-adventurers 、https://kaylousberg.itch.io/kaykit-skeletons 。免费与EXTRA/SOURCE分开；后两者前轮页面分别为US$7.95/11.95起。
- Quaternius：https://quaternius.com/packs/ultimatemonsters.html 。官方声明50个动画怪物、CC0；尚未本地逐一验证。

## 综合结论与后续验证顺序

1. 优先以 PELibDemo RPGTinyHeroWavePBR 做我方剑盾、弓箭、雷/冰法师四种代表试样，并检查一至三星装配差异；保持当前 KayKit 为已接入的对照方案。
2. 疾行狼比较两工程已有同名狼包；石甲巨像优先 AutoClash StoneGolem；普通敌人与治疗师尽量从统一人形或骷髅/兽人系列配齐。
3. 在同一庭院、手机竖屏镜头、相同光照下比较轮廓、尺度、遮挡和职业区分，再决定最终组合。模块化不自动代表比简单模型更省性能。
4. 仅提取必要依赖，验证URP14材质、骨架/动作/武器挂点和规则事件时序；检查编译、Play、正常玩法与Console，再按项目流程做最新APK与真机检查。

本任务仅进行选材及规则文档维护，未修改玩法、输入或MCP接口，因此本轮MCP无需修改。未将静态文件检查写成游戏内运行通过。

## 2026-11-27 敌方怪物阵容

- 普通敌人统一使用 `Monsters Ultimate Pack 02 Cute Series`。用户随后确认首领可以沿用 AutoClash 石甲巨像。此条更新前期人形敌人及 Cute Series 龙首领候选。
- 狼人 `Werewolf`、重甲寄居蟹 `Hermit King` 和向日葵妖精 `Sunflower Fairy` 取自已授权的 AutoClash 同名包。实际迁入的 21 个模型、动作和贴图文件及哈希见 `Reports/Art/enemy-monster-sources.json`。疾行狼沿用已导入的 PELibDemo `T/Wolf Cute Series/FBX/Wolf.FBX`。
- 狼人、疾行狼、重甲寄居蟹、向日葵妖精和石甲巨像的目标表现高度分别为 1.20、1.00、1.25、1.10 和 2.45 米。疾行狼由 0.75 调至 1.00 米。复用现有 Roco 材质流程、UnitMono 挂点和战斗角色键，预制体同步生成到 `Resources/UnitModels` 和 `Package_Unit/Low/Base`。依靠规则事件结算，动作仍由原战斗流程触发。
- 动作骨架检查见 `Reports/Art/enemy-monster-build-validation.txt`。正常招募、布阵推进及第 3、5、6 波的 540×960 Unity GameView 截图见 `Reports/Art/enemy-monsters-20261127`。此轮验证范围为 Unity 编辑器。

## 2026-11-24 单位样品与 Demo 卡通渲染接入

用户新增授权：单位可以使用 Demo 工程的卡通渲染 Shader。实际入口为 `F:/Git/kabu_mobile/demo/client-demo-assets/Assets/Art/Shaders/Roco` 的 Roco_Character.shader、RocoCharacter.hlsl、RocoCharacterOutline.hlsl；配套描边 Feature 位于 client-demo 的 Rendering/Runtime/Outline。源工程只读。

- 迁入位置：`Assets/LittleGame/Shaders/Roco` 和客户端表现层中的 RocoCharacterOutlineFeature。移除源工程专用材质GUI及整块网格描边配置依赖；保留卡通明暗逻辑，补正交镜头像素描边宽度换算。源模型没有该Shader的ID染色图，关闭ID染色/AO依赖，使用现有固有色贴图。
- `Tools/Preparation/import_unit_samples.py` 记录提取文件、来源哈希与适配；`UnitAssetBuilder` 生成九类单位配置、精简预制体、独立动作资源和卡通材质。隐藏装配件、演示Animator与碰撞被移除，不让每个单位携带整套闲置装备。
- PELibDemo TinyHero 提供我方四职业及步兵/重甲兵/治疗师；狼来自 PELibDemo Cute Series；巨像使用 AutoClash 石头人的原预制体实际引用网格（Attack01_Golem_Anim.fbx 内网格），避免误用同目录旧 GolemMesh 的不同UV模型。
- `Reports/Art/unit-build-validation.txt` 记录实际骨架绑定、renderer、三角面及节点数；不是手机性能数据。运行采样见 `Reports/Art/unit-runtime-verification.json`，正常玩法回放记录见 `Reports/Combat/unity-verification.json`。Android 结果另见本轮交付说明/Reports/Mobile。
- 目前是角色样品接入；升星沿用现有比例与星级标识，尚未完成逐星专属套装。规则命中仍决定伤害，基础动作不回写结算，不把现有事件触发动作用作完整技能时序制作已完成的证明。

## 2026-11-24 整体UI改造与本轮验收范围变更

- 用户明确授权 `E:/Project/PELibDemo/Assets/Art` 内UI素材用于本项目，并要求整体改造界面。实际成套素材位于 `Art/3RD_UI`；`Art/GUI PRO Kit*` 同名根目录主要为目录/元数据，不误判为素材缺失。用户另明确本轮只要求Unity验证，无需安装手机，停止后续手机构建与安装。
- 统一选用 `3RD_UI/GUI PRO Kit/GUI PRO Kit - Fantasy RPG/ResourcesData/Sprites`，提取面板、按钮、卡片、兵种/战术图标等21张原图，保留九宫格边距；只引入图片，不引入源演示业务系统。来源哈希见 `Reports/Art/ui-source-migration.json`，复现工具 `Tools/Preparation/import_combat_ui.py`。
- 游戏实际使用 `Resources/CombatUI` 与 `CombatUiSkin`。重排标题、波次进度、城门血量和三路敌情；转盘改用兵种图标，候补区使用卡片；统一主按钮、战术指挥、分支/合成、奖励与确认弹窗的皮肤。重开、招募、分支和战术规则保持既有基线。
- 庭院调整为更中性的石材光照；单位减少头顶小字遮挡，弓手/法师的帽子颜色降饱和。巨像采用 `PolyartDefaultTex.png` 灰石图集，替代源工程红蓝敌方重染色图集；骨架与动作不变。
- MCP get_view 扩展1.0.3增加皮肤主题/缺失sprite统计与按钮宽高，仍通过共用pointer验证实际点击、起拖、取消、松手和确认。素材检查与游戏回放分开记录，不把编辑器数据当手机性能。

### 本轮最终Unity验收

- 脚本编译通过；`Reports/Combat/unity-verification.json`九组回归结论通过，覆盖正常12波获胜、少兵失败/重开、共用pointer点击/拖动/取消、分支和战术；10组UI布局的sprite、按钮范围/高度/无重叠检查通过。
- `Reports/Art/unit-final-verification.json`复核最终细环版本，正常操作至第六波、九类单位Shader、动作与规则暂停/恢复及Console四类0条通过；`units-boss-final.png`为最终实际GameView。
- 角色脚下的实心色盘已改成细阵营环，状态环独立显示；两者不加碰撞、不参与规则。
- 提取清单保留源文件与提取后哈希；四份源材质经Unity2022自动升级序列化，另记postUnityImportSha256，不误报为源工程被修改。21张UI原图文件哈希一致。
- 无提交、无推送。用户取消手机验收后未继续手机构建或安装；此前APK已过时，不用于交付最新UI。

## 2026-11-24 3D 技能时间轴、Demo 轨道编辑器与 Hovl Studio 实战特效接入

- 从 `F:/Git/kabu_mobile/demo/client-demo-assets/Assets/Scripts/Editor/TimelineEditor/TimeLineWindow` 直接迁入原版 `DMTimeArea.SimpleTimeArea` 轨道渲染引擎与 `EditorResources` 图标至 `Assets/LittleGame/Client/Editor/TimeLineWindow`，改造 `SkillTimelineEditorWindow`（`Little Game/Combat/Skill Timeline Editor`）支持缩放时间标尺、播放头/截止游标、多轨道彩色片段条、拖拽调时/缩放时长、关键帧菱形标记与前置连线、右侧属性编辑及 JSON 校验保存热重载。
- 单位战斗表现参考 Demo 客户端分层，拆分为 `CombatUnitActor`（标准挂点/动作/血条/状态环）、`CombatUnitRegistry`、`BattleActionRouter`、`BattleFloatingText` 与 `SkillTimelineDirector`；`UnitSkillBindings.json`（23 条规则）与 21 个技能 JSON 位于 `Assets/LittleGame/Resources/SkillTimelines/`。
- 从已授权 `E:/Project/PELibDemo/Assets/Art/3D/Hovl Studio` 按最小 GUID 依赖提取 18 个精选特效（共 199 文件，不含源演示脚本）至 `Assets/LittleGame/Art/SkillEffectsSources/HovlStudio`，哈希清单见 `Reports/Art/skill-effects-migration.json`；经 `SkillEffectAssetBuilder`（`Little Game/Combat/Build Skill Effects`）剥离碰撞体/刚体/灯光/音频并自动适配 73 份 URP 14 `Particles/Unlit` 材质，输出 39 个 `EffectId` 预制体至 `Assets/LittleGame/Resources/SkillEffects/`（构建日志 `Reports/Art/skill-effects-build.txt`）。


## 2026-11-25 旧横板动作工程移动与 AI 静态分析

入口 `E:\Project\射雕1.9版本\Client`（Unity 5.6.6f2）。授权仅只读分析，不迁入代码/美术。`AIController` 使用配置状态及条件连线；`AIBehaviorFindOpponent` 选目标及偏移落点，`AIBehaviorAround` 选前后侧位置并计时重选；`MoveBehaviorBase` 使用直接位移或 DOMoveX/Y，到点进入 EndMove 并切站立。主移动链路未发现真正的路径搜索，不能将 Around 视为障碍绕行。普通非障碍角色的物理盒设为 Hero 层，当前 Hero-Hero 碰撞矩阵关闭；角色 CanStop 配置有 88 条 TRUE、48 条 FALSE，阻挡能力不能脱离层矩阵理解。

适合借鉴：目标落点稳定、动作结束边界、行为状态分层；不直接照搬直线 Tween、随机偏移或关闭人物碰撞。当前推荐改为攻击站位预订、二维 A*、连续移动及阻挡等待，方案尚未实施。静态证据、当前客户端漏洞和验收要求：`Reports/Combat/side-scroller-movement-review-20261125.json`。技术文档参考：https://arongranberg.com/astar/documentation/stable/getstartedgrid.html （仅官方导航概念阅读，未下载、购买或接入该插件，无素材授权）。


## 2026-11-25 导航开源候选与需求澄清

用户最新明确不排斥RVO，允许按实际需求选算法和开源方案，不必沿用旧作；此前“不用RVO”的表述已被覆盖。以下来源均为技术研究，无美术授权、付费授权或运行时接入结论。

| 来源 | 已检查版本与许可证/依赖 | 当前验证与取舍 |
| --- | --- | --- |
| https://github.com/roy-t/AStar | `3d57efaa83c8cd217e4973fd4738c57e2f694bd2`，MIT；netstandard2.0，核心无外部包 | 26个核心源码与LICENSE缓存于`Library/NavigationReview/RoyTAStar`，六项.NET静态路径测试通过；未接入Unity。当前优先小样候选，非完整战斗导航系统；动态占位、不同体型与动作需另行处理 |
| https://github.com/ikpil/DotRecast | `6fce4ac0d59f4d4efdf9de0a9e5beae5784bca12`，Zlib；当前netstandard2.1 Core依赖Unsafe6.1.2 | 只读检查模块和csproj，未导入/编译；功能更完整但当前小平面接入成本较高 |
| https://github.com/juhgiyo/EpPathFinding.cs | `e0d3d4bf735364774912df7f5c3b44f8bec0457a`，README含MIT文本；旧.NET4.0/C5依赖需核查 | 只读源码目录/工程/README检查，未编译，不优先 |
| https://arongranberg.com/astar/documentation/stable/getstartedgrid.html 及 https://arongranberg.com/astar/docs/localavoidance.html | 官方产品文档；Local Avoidance为RVO/ORCA且属Pro功能，不能把产品免费版当成MIT开源库 | 未下载、购买或接入；可作为成熟集成候选，具体版本、许可与Unity兼容需单独实测 |
| https://docs.unity3d.com/Packages/com.unity.ai.navigation@1.1/manual/NavInnerWorkings.html | Unity官方AI Navigation1.1文档；NavMesh是空间表达，内部A*搜索、局部RVO避障 | 当前工程尚无AI Navigation/modules.ai依赖，未接入；成熟集成备选，须解决与纯规则固定步长的适配 |

完整审查、提交时间、依赖差异及推荐边界见`Reports/Combat/navigation-options-20261125.json`，Roy隔离测试见`Reports/Combat/navigation-roy-probe-20261125.json`。本轮未修改运行时或重打APK。

2026-11-25 补充官方产品信息核查：Aron Granberg [下载页](https://arongranberg.com/astar/download)当前列出 Free4.2.17（2021-11-06，声明兼容Unity2019.3+）及Pro5.4.7（声明兼容Unity2022.3+）；[官方FAQ](https://arongranberg.com/astar/documentation/stable/faq.html)允许Free用于商业项目，[购买页](https://arongranberg.com/astar/buy/)说明Pro采用Asset Store许可。仅核查声明，未下载包、未购买，也未验证本工程兼容。EpPathFinding固定提交的csproj已确认引用C5 2.4系列与.NET4.0。用户最新允许RVO，候选不因采用RVO而排除。


2026-11-25 本轮落地更新：用户要求继续推进，Roy-T.AStar固定提交的26个核心C#文件已迁入`Assets/LittleGame/ThirdParty/RoyTAStar`，以`LittleGame.RoyTAStar.asmdef`供纯规则Domain引用。修改仅增加动态通行边过滤接口；LICENSE.txt、SOURCE.json保留来源，发行随包许可副本位于`Assets/StreamingAssets/ThirdPartyNotices/RoyTAStar.txt`。未引入其他候选、未付费、未修改外部旧工程。规则层导航场景已通过；Unity正常远征正在验收，手机未验证。后续最终状态读取`Reports/Combat/astar-navigation-20261125/`。

## 2026-11-25 Demo 框架和3D技能接续

既有 Demo 来源此次用于完整 TimelineEditor/MCP Host、共享 UnitCore、3D Animator、AOT/资源热更、服务管理器、UGUI 与构建能力迁移，源工程只读。模型生成物新增 `Assets/Package_Unit/Low/Base/{Role}/{Role}.prefab`，标准技能与 VFX 使用 Package_Unit / Package_Effect；旧 Resources 时间轴镜像归档。没有新增外部素材购买或来源授权。许可沿用既有来源及嵌入包许可，验证状态和限制见 `Framework-Migration.md`。

## 2026-11-26 竖屏技能调校

继续使用既有授权的 PELibDemo/Hovl Studio 本地提取资源，来源工程未改动。当前工程保留的18份源Prefab含粒子，旧40份生成Prefab却只有Transform层级；已修复生成器并重新生成 `Assets/Package_Effect/Low/Base`，逐项核对粒子数量，不再以文件生成成功代替效果验收。缺失原Shader时从序列化属性恢复贴图，适配URP材质并保留生成资产GUID；仅拖尾材质槽按实际启用状态验证。

本项目新增程序生成的刀光弧面、雷击折线、冰晶与集火标记网格粒子（`SkillEffectAccentBuilder`、`SkillEffectAssetBuilder`），用于竖屏可读的主形状；Hovl粒子作为其他效果及辅助层，不新增外部来源或费用。40个运行时Prefab统一由Package_Effect提供；旧Resources副本不作为调效目标。当前逐技能修改、画面、双阵营/画质与实际战斗验证分别见 `Reports/Skills/20261126`，不据此宣称手机效果或性能通过。


### 2026-11-27 真机验收补记

外观与模型的 UnitPackage 路径、AOT 描边 Feature 已在 Samsung SM-G9550 / Android 7.0 实际安装运行。普通怪物在第5波战斗中显示，中文界面、技能和伤害数字可见；触屏部署与冷启动恢复通过，实战与重启日志无 Unity Error 或原生 CRASH。启动黑屏另包含完整规则诊断快照导致的原生崩溃，手机改用精简标量诊断。证据见 Reports/Mobile/black-screen-20261127/validation.json。未据此验收首领真机表现、所有技能或全部机型。


## 2026-11-27 KayKit 地图与多种族英雄方向

- 用户指定收藏室 KayKit 作为地图和英雄来源，并明确 Adventurers、Skeletons 均可作我方英雄，按种族/职业重新考虑整体角色设定。本次是分析建议，未执行角色替换、地图重做或玩法修改。此前优先 TinyHero 的选型结论在新的我方方向上已更新；敌方怪物基线本次未取消。
- 来源入口：`E:/Art/material-vault/libraries/kaykit`；只读检查 `downloads` 原包和 `source/download-receipts.json`，不修改收藏室。Adventurers 官方 https://kaylousberg.itch.io/kaykit-adventurers ，Skeletons 官方 https://kaylousberg.itch.io/kaykit-skeletons ，通用动画 https://kaylousberg.itch.io/kaykit-character-animations 。
- 已获取 `adventurers-free-2.0.zip`：人物 FBX 为 Knight、Barbarian、Mage、Ranger、Rogue、Rogue_Hooded；后两者是盗贼外观版本，不算两个独立职业。`skeletons-free-1.1.zip`：Skeleton_Mage、Skeleton_Minion、Skeleton_Rogue、Skeleton_Warrior。附武器/盾/弩/法杖等；有 `character-animations-free-1.1.zip`。原包回执记录 CC0 与包内许可。EXTRA 的工程师、德鲁伊、大野蛮人、骷髅巨像、死灵法师不在本地免费包内，未购买。
- 地图缓存包括 `dungeon-remastered-github-1.0.zip`、`forest-nature-free.zip`、`medieval-hexagon-github-1.0.zip`。Dungeon 回执固定提交 `b0ca9bd96a8072ab36a3a5464f00ed1e06a16d07`，为官方 GitHub 1.0 Godot 分发（GLB/OBJ），不能当成 itch.io 1.1 的 Unity FBX 分发。Medieval Hexagon 回执固定提交 `84fa4e91af6a88989be7c99e0891cede11f2ca38`。原始入口 https://github.com/KayKit-Game-Assets/KayKit-Dungeon-Remastered-1.0 、https://kaylousberg.itch.io/kaykit-medieval-hexagon 、https://kaylousberg.itch.io/kaykit-forest 。
- 2026-11-27 官方页面核对：Dungeon Remastered 页面重定向为 https://kaylousberg.itch.io/kaykit-dungeon-pack ，页面说明 Remastered 1.1 更名为 Dungeon Pack 1.1；森林免费版不含 EXTRA 的模块地形。已有石地/墙/门与森林摆件可优先组成遗迹林道；六边形适合作地面或远景模块，不要求改变自由布阵规则。
- 建议先做两族（人类/亡灵）、四职业（守卫/战士/射手/法师）、八英雄的交叉阵容。该方案及独立英雄ID合成、羁绊计数、转盘中奖后选英雄都只是设计建议，未覆盖当前四职业、同职业合成和二星分支基线。
- 技术风险与验证：需要重新核对 Unity URP14 材质、人物骨架与通用战斗动作、武器挂点、投射物时序、亡灵细骨在固定竖屏正交镜头的辨识、场景遮挡与手机开销。此次仅原包清单/来源检查与官方页面阅读；未验证新阵容的 Unity/真机效果。未修改运行代码和 MCP 接口；若实施，将同步英雄ID、种族/职业、羁绊、合成预览及正常输入验收。

### KayKit 接入与首关验收

- 首批模型使用同一收藏室的 Adventurers 2.0 和 Skeletons 1.1 免费包。保留无帽 Rogue，持弩小兵使用 Skeleton_Minion 加同包的 Skeleton_Crossbow。通用动作来自 Character Animations Free 1.1 的 Medium 骨架。提取工具为 `Tools/Preparation/import_kaykit_heroes.py`，源包保持只读。
- 八个英雄已由 Unity 生成 UnitPackage 模型和外观，继续采用 Roco 角色材质。生成记录 `Reports/Art/kaykit-hero-build.txt` 包含骨架、动作绑定、挂点和网格数据。首关沿用已接入的普通怪物与石甲巨像。
- 遗迹道路接入 Dungeon Remastered GitHub 1.0 的 OBJ 石门、残墙与装饰柱，以及 Forest Nature Free 的 FBX 树和纹理。保存来源许可，复用现有道路布局和正交跟随机位。
- 刺客图标取自已授权的 PELibDemo Fantasy RPG UI 的 `Component/Icon_PictoIcons_(Original)/btn_icon_dagger.png`，原图及导入记录见 `Tools/Preparation/import_combat_ui.py` 与 `Reports/Art/ui-source-migration.json`。
- Unity 编辑器正常招募和十波回放见 `Reports/Combat/hero-rework-20261127/final`，包含真实模型、Boss 波、蓝条、拖放和 Console 记录。手机表现和性能仍待真机验收。

### 2026-11-27 英雄头像实模快照

- 按用户指定方式，从八名已接入的 UnitPackage 英雄预制体拍摄，保留原模型与材质。统一略俯视三分之四头肩构图，768×768 离屏渲染后缩为 256×256，并导入为 Sprite。
- 单张输出为 Assets/Package_Unit/Portrait/Base/<英雄ID>.png，总览为 Reports/Art/HeroPortraits/hero-portraits.png。上排依次为骑士、游侠、法师、盗贼，下排为骷髅战士、持弩骷髅小兵、骷髅法师、骷髅盗贼。
- 复拍菜单 Little Game/Art/Capture Hero Portraits，代码 HeroPortraitCapture.cs。已修正首轮法师全身取景和帽盔贴边问题，八张完成总览目视检查，Console Error 为零。尚未接入战斗 HUD，手机小尺寸效果待验收。本次仅新增美术资产和编辑器工具，玩法与 MCP 协议无需变更。

### 2026-11-27 战斗 HUD 资源落地

- 沿用已授权的 PELibDemo GUI PRO Kit - Fantasy RPG 的面板、卡片、按钮和职业图标。新增同套件 PictoIcons 的 human、skull、user_1 图标用于种族、首领和人口，具体来源、目标路径、哈希登记在 Reports/Art/ui-source-migration.json，外部资源工程保持只读。
- 伤害榜和候补卡片使用本项目实模快照 Portrait 资源，统一由 UnitPackage 加载，地址 Portrait/<英雄ID>。没有新增热更脚本 Resources 镜像。
- 布局与统计验收记录位于 Reports/Combat/hud-rework-20261127，手机状态以该报告为准。



### 2026-11-27 Demo 对象池与关卡预热复核

- 既有授权来源 F:/Git/kabu_mobile/demo/client-demo/Assets/Scripts/HotUpdate_Main/Mgr/Pool，本次只读比对。现有 PoolMgr/PoolGroup/OnePool 已迁入同源实现，战斗模型和特效沿用该池。
- 本工程新增每地址至少一只休眠实例的 Prewarm、创建/复用计数；按本关全部波次怪物与招募池英雄的普攻和技能绑定收集模型、特效。特效统一使用 EffectPackage，场景退出释放；未修改外部源工程。
- 验证记录：Reports/Performance/20261127，Unity Editor 结果不代表手机或 HybridCLR 验证。


### 2026-11-27 导航性能与 Burst/Jobs 只读技术依据

- Unity 2022.3 Job System 官方： https://docs.unity3d.com/2022.3/Documentation/Manual/JobSystemNativeContainer.html 、 https://docs.unity3d.com/cn/2022.3/Manual/JobSystemCreatingJobs.html 。研究数据布局、线程读写依赖和 Schedule/Complete 时机，未据此宣称已接入多线程导航。
- Unity Burst 类型支持： https://docs.unity3d.com/Packages/com.unity.burst@1.8/manual/csharp-type-support.html 。工程现有嵌入包版本1.8.21；现有导航的托管对象图、字典和委托未被Burst编译。
- HybridCLR 官方 DOTS： https://www.hybridclr.cn/docs/basic/dots 。普通版本热更部分Burst代码以解释方式运行，后续若采用原生Burst计算内核，拟放AOT程序集，热更规则经数据接口调用；本轮仅研究，未购买或接入新版本。
- 现有Roy-T.AStar来源和MIT许可不变；本轮对同一占位快照复用边检查和穷尽搜索可达集，修复最小堆删除内部节点后堆序错误，修改说明写入Assets/LittleGame/ThirdParty/RoyTAStar/SOURCE.json。


## 2026-11-27 导航系统重新选型的新增只读来源

用户要求结合实际需求、网络和GitHub重新选型，不再继续累加局部补丁。来源审查及固定提交保存于 Reports/Combat/navigation-review-20261127/source-audit.json；本轮未安装候选或迁入新运行时代码，研究不等于素材或第三方整体许可授权。

- Unity AI Navigation1.1官方：NavInnerWorkings、AboutObstacles、MixingComponents，链接见该目录README。适用Unity2022系列的原生导航原理、RVO、切洞与组件组合。原生NavMeshAgent是研究首选候选，尚未进行本项目横向实测。
- https://github.com/snape/RVO2-CS ：Apache-2.0，当前固定5cc83e25da523ef21963f6e925920f2d3eef82fb，csproj目标net10.0，不能直接断言Unity2022兼容；只读源码/依赖审查。
- https://github.com/Nebukam/com.nebukam.orca ：固定703ec434f4a5a56863991c8954015c3a3150ec37，Burst/Jobs ORCA，Unity2022.1包，附加依赖较多；LICENSE.md为空、API为NOASSERTION，未核实可直接采用，不标成MIT已授权。
- https://github.com/sarkahn/pathfinding ：固定8d306163e6b8fe94dce21df4b0fcf7d6e2f2a087，MIT、2020年最后提交，Burst/Jobs路径内核参考，不包含完整人群/战斗站位。
- https://github.com/innocentmiau/MiHordeTraffic ：固定17ba7e847f147e40b2c9351a314b07a645732bb4，MIT，Unity6000.3与Collections2.6.8等依赖不匹配当前工程；仅研究流场分层，不迁包/升级工程，作者五千单位数字不是本项目性能。
- https://gamma-web.iacs.umd.edu/ORCA/ ：作者研究说明，用于区分局部速度避障和全局寻路；页面旧软件下载条件不替代具体GitHub版本的许可证。
- 既有 https://github.com/ikpil/DotRecast 本次复核固定6fce4ac0d59f4d4efdf9de0a9e5beae5784bca12，Zlib、netstandard2.1等目标；尚未接入，当前不优先再引入一套C# Recast/Detour栈。
