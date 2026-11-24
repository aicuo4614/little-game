# 美术资源来源与单位选型

更新：2026-11-24。用途：供后续对话持续查阅、补充资源入口与选型依据。本目录同时保存初选和后续接入记录；各阶段验证范围以文末记录为准。

## 持续维护约定

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
| Unity Asset Store | https://assetstore.unity.com | 可自主筛选；付费由用户决定并提供，不代付款；优先查本地是否已有同包 |
| 作者网站与其他网络来源 | Kay Lousberg、Quaternius、Synty、itch.io 等 | 保留原始作者链接，区分免费、EXTRA、SOURCE，按具体来源核对许可；不从不明转载站获取付费包 |

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
