using System.Linq;
using LittleGame;
using Newtonsoft.Json.Linq;
using KabuUnityMcp.Editor.Extensions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public sealed class PuzzleMcp : IMcpExtension
{
    private static readonly PuzzleMcp Extension = new PuzzleMcp();
    static PuzzleMcp()
    {
        McpExtensionRegistry.Register(Extension);
        AssemblyReloadEvents.beforeAssemblyReload += () => McpExtensionRegistry.Unregister(Extension);
    }
    public string Id => "little-game.puzzle";
    public string Version => "3.1.0";
    public string Description => "电器回收站：拖动旋转、单击选中、双击拆卸，两关按依赖回收全部部件。";
    public string UnavailableReason => !Application.isPlaying || GameBootstrap.Session == null ? "请打开 Game 场景并进入 Play" : null;
    private static JObject Schema(string properties = "", string required = "")
    {
        return JObject.Parse("{'type':'object','properties':{" + properties + "},'required':[" + required + "],'additionalProperties':false}");
    }
    private static string PartSchema => "'part':{'type':'string','enum':"+JArray.FromObject(DeviceLevels.All.SelectMany(l=>l.Parts).Select(p=>p.Id).Distinct()).ToString(Newtonsoft.Json.Formatting.None)+"}";
    public McpExtensionOperation[] Operations => new[]
    {
        new McpExtensionOperation("get_state","读取level/levelCount/device/totalParts/recoveredCount/parts（detached与collected区分）、视角、连接状态、动画、操作结果、failureSequence/failedPart及可执行动作；等待动画完成再拆下一件",true,Schema()),
        new McpExtensionOperation("get_view","读取部件姿态、屏幕坐标、gesture、双击时间窗、feedback、collection行列/几何中心/尺寸/filled、部件visualCenter及motion阶段/安全间距；已删除旧拖出向量和预览",true,Schema()),
        new McpExtensionOperation("pointer","玩家共用入口：任何机器部件上起拖只旋转；单击选中，同一部件0.36秒内双击尝试拆卸。拖动/取消/UI会打断双击，背景拖动无效。x/y为0到1，原点左下",false,
            Schema("'phase':{'type':'string','enum':['begin','move','end','cancel']},'x':{'type':'number'},'y':{'type':'number'}","'phase','x','y'")),
        new McpExtensionOperation("performance","读取最近240帧间隔、均值、P95、目标帧率和采样平台。编辑器结果包含编辑器开销",true,Schema()),
        new McpExtensionOperation("rotate","屏幕坐标系自由旋转。delta 绕屏幕上轴，tilt 绕屏幕右轴，roll 绕屏幕前轴。单位度，无角度限制或吸附；回收动画中锁定朝向以保持安全路径",false,
            Schema("'delta':{'type':'number'},'tilt':{'type':'number'},'roll':{'type':'number'}","'delta'")),
        new McpExtensionOperation("orbit","虚拟轨迹球旋转，灵敏度1.8，与玩家拖动相同。坐标相对机身投影中心，以旋转半径归一化",false,
            Schema("'fromX':{'type':'number'},'fromY':{'type':'number'},'toX':{'type':'number'},'toY':{'type':'number'}","'fromX','fromY','toX','toY'")),
        new McpExtensionOperation("flip","快速切换到正面或背面标准视角",false,Schema()),
        new McpExtensionOperation("select","选择当前可见可操作部件，与玩家命中规则一致",false,Schema(PartSchema,"'part'")),
        new McpExtensionOperation("disassemble","双击对应的正常拆卸指令：全部部件共用依赖与动画校验，不要求拖动方向；失败增加failureSequence并记录failedPart/blocker",false,Schema(PartSchema,"'part'")),
        new McpExtensionOperation("clear_selection","取消选中部件",false,Schema()),
        new McpExtensionOperation("restart","重开当前关卡，恢复设备和回收栏",false,Schema()),
        new McpExtensionOperation("next_level","完成当前关且动画结束后进入下一关，末关拒绝",false,Schema()),
        new McpExtensionOperation("load_level","与界面关卡选择按钮相同，选择第1或第2关进行试玩，动画中拒绝切换",false,
            Schema("'level':{'type':'integer','minimum':1,'maximum':2}","'level'"))
    };
    public McpHostResult Execute(string operation, JObject arguments)
    {
        if(operation=="load_level")
        {
            GameBootstrap.Session.LoadLevel((int)arguments["level"]);
            return McpHostResult.Success(GameBootstrap.Session.Snapshot());
        }
        if (operation == "get_view")
            return McpHostResult.Success(GameBootstrap.View.InteractionSnapshot());
        if (operation == "pointer")
        {
            float x=(float)arguments["x"],y=(float)arguments["y"];
            if(float.IsNaN(x) || float.IsNaN(y) || x<0 || x>1 || y<0 || y>1)
                return McpHostResult.Fail("视口坐标必须在0到1范围内");
            GameBootstrap.Input.Pointer((string)arguments["phase"],new Vector2(x*Screen.width,y*Screen.height));
            return McpHostResult.Success(GameBootstrap.Session.Snapshot());
        }
        if (operation == "performance")
            return McpHostResult.Success(FrameMetrics.Current.Snapshot());
        if (operation == "rotate")
            GameBootstrap.Session.Rotate((float)arguments["delta"], (float?)arguments["tilt"] ?? 0, (float?)arguments["roll"] ?? 0);
        else if (operation == "orbit")
            GameBootstrap.Session.Orbit((float)arguments["fromX"], (float)arguments["fromY"],
                (float)arguments["toX"], (float)arguments["toY"]);
        else if (operation == "flip")
            GameBootstrap.Session.Flip();
        else if (operation != "get_state")
            GameBootstrap.Session.Execute(operation, (string)arguments["part"]);
        return McpHostResult.Success(GameBootstrap.Session.Snapshot());
    }

    [MenuItem("Little Game/Build WebGL")]
    private static void BuildWebGl()
    {
        EditorApplication.update -= BuildWebGlNow;
        EditorApplication.update += BuildWebGlNow;
    }

    private static void BuildWebGlNow()
    {
        EditorApplication.update -= BuildWebGlNow;
        var report = BuildPipeline.BuildPlayer(new[] { "Assets/Scenes/Game.unity" },
            "Builds/GameWebGL", BuildTarget.WebGL, BuildOptions.None);
        Debug.Log($"Game WebGL: {report.summary.result}, errors={report.summary.totalErrors}, bytes={report.summary.totalSize}");
    }

    [MenuItem("Little Game/Create Game Scene")]
    private static void CreateScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Game.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Game.unity", true) };
    }
}
