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
    public string Version => "3.3.2";
    public string Description => "废品回收箱：拖动旋转、单击选中、双击拆卸，三关共用拆取和回收规则，行李箱含衣物与夹层照片。";
    public string UnavailableReason => !Application.isPlaying || GameBootstrap.Session == null ? "请打开 Game 场景并进入 Play" : null;
    private static JObject Schema(string properties = "", string required = "")
    {
        return JObject.Parse("{'type':'object','properties':{" + properties + "},'required':[" + required + "],'additionalProperties':false}");
    }
    private static string PartSchema => "'part':{'type':'string','enum':"+JArray.FromObject(DeviceLevels.All.SelectMany(l=>l.Parts).Select(p=>p.Id).Distinct()).ToString(Newtonsoft.Json.Formatting.None)+"}";
    public McpExtensionOperation[] Operations => new[]
    {
        new McpExtensionOperation("get_state","读取zoom/minZoom/maxZoom/pan、level/levelCount/device/totalParts/recoveredCount/parts（detached与collected区分）、视角、连接状态、动画、操作结果、failureSequence/failedPart及可执行动作；等待动画完成再拆下一件",true,Schema()),
        new McpExtensionOperation("get_view","读取部件姿态、屏幕坐标、gesture、双击时间窗、feedback、collection行列/几何中心/尺寸/filled、部件visualCenter及motion阶段/安全间距，target最小碰撞体/屏幕命中范围和hit结果，workspace裁剪区域与tray布局边界",true,Schema()),
        new McpExtensionOperation("pointer","玩家共用入口：任何机器部件上起拖只旋转；单击选中，同一部件0.36秒内双击尝试拆卸。拖动/取消/UI会打断双击，背景拖动无效。x/y为0到1，原点左下",false,
            Schema("'phase':{'type':'string','enum':['begin','move','end','cancel']},'x':{'type':'number'},'y':{'type':'number'}","'phase','x','y'")),
        new McpExtensionOperation("pinch","通过玩家共用双指入口缩放和双指移动观察；phase为begin/move/end/cancel，坐标左下原点归一化，缩放1至2.4倍，动画中仍可缩放和平移",false,
            Schema("'phase':{'type':'string','enum':['begin','move','end','cancel']},'x1':{'type':'number'},'y1':{'type':'number'},'x2':{'type':'number'},'y2':{'type':'number'}","'phase','x1','y1','x2','y2'")),
        new McpExtensionOperation("capture_view","合成设备操作区、回收层与环境UI截图，尺寸16至2048；单独截Main Camera不能验证多路合成",true,
            Schema("'width':{'type':'integer','minimum':16,'maximum':2048},'height':{'type':'integer','minimum':16,'maximum':2048}","'width','height'")),
        new McpExtensionOperation("performance","读取最近240帧间隔、均值、P95、目标帧率和采样平台。编辑器结果包含编辑器开销",true,Schema()),
        new McpExtensionOperation("open_structure","与界面试玩结构样板按钮共用入口，切换到独立 Structure 场景；之后使用 little-game.structure",false,Schema()),
        new McpExtensionOperation("rotate","屏幕坐标系自由旋转。delta 绕屏幕上轴，tilt 绕屏幕右轴，roll 绕屏幕前轴。单位度，无角度限制或吸附；动画中仍可旋转整个物品根节点",false,
            Schema("'delta':{'type':'number'},'tilt':{'type':'number'},'roll':{'type':'number'}","'delta'")),
        new McpExtensionOperation("orbit","虚拟轨迹球旋转，灵敏度1.8，与玩家拖动相同。坐标相对机身投影中心，以旋转半径归一化",false,
            Schema("'fromX':{'type':'number'},'fromY':{'type':'number'},'toX':{'type':'number'},'toY':{'type':'number'}","'fromX','fromY','toX','toY'")),
        new McpExtensionOperation("flip","旋转整个物品根节点到正面或背面，动画中可用",false,Schema()),
        new McpExtensionOperation("select","选择当前可见可操作部件，与玩家命中规则一致",false,Schema(PartSchema,"'part'")),
        new McpExtensionOperation("disassemble","双击对应的正常拆卸指令：全部部件共用依赖与动画校验，不要求拖动方向；失败增加failureSequence并记录failedPart/blocker",false,Schema(PartSchema,"'part'")),
        new McpExtensionOperation("clear_selection","取消选中部件",false,Schema()),
        new McpExtensionOperation("restart","重开当前关卡，恢复设备和回收栏",false,Schema()),
        new McpExtensionOperation("next_level","完成当前关且动画结束后进入下一关，末关拒绝",false,Schema()),
        new McpExtensionOperation("load_level","与界面关卡选择按钮相同，选择掌机、电脑机箱或红色行李箱进行试玩，动画中拒绝切换",false,
            Schema("'level':{'type':'integer','minimum':1,'maximum':" + DeviceLevels.All.Length + "}","'level'"))
    };
    private static bool Valid(float value){return !float.IsNaN(value)&&!float.IsInfinity(value)&&value>=0&&value<=1;}
    public McpHostResult Execute(string operation, JObject arguments)
    {
        if(operation=="open_structure"){GameBootstrap.OpenStructureSample();return McpHostResult.Success(new{scene="Structure",extension="little-game.structure"});}
        if(operation=="capture_view")return McpHostResult.Success(PuzzleCapture.Capture((int)arguments["width"],(int)arguments["height"]));
        if(operation=="pinch")
        {
            float x1=(float)arguments["x1"],y1=(float)arguments["y1"],x2=(float)arguments["x2"],y2=(float)arguments["y2"];
            if(!Valid(x1)||!Valid(y1)||!Valid(x2)||!Valid(y2))return McpHostResult.Fail("坐标必须在0到1内");
            GameBootstrap.Input.Pinch((string)arguments["phase"],new Vector2(x1*Screen.width,y1*Screen.height),new Vector2(x2*Screen.width,y2*Screen.height));
            return McpHostResult.Success(GameBootstrap.Session.Snapshot());
        }
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
        var report = BuildPipeline.BuildPlayer(new[] { "Assets/Scenes/Structure.unity", "Assets/Scenes/Game.unity" },
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
