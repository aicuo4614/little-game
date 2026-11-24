using System;
using System.IO;
using System.Linq;
using LittleGame.Structure;
using Newtonsoft.Json.Linq;
using KabuUnityMcp.Editor.Extensions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public sealed class StructureMcp : IMcpExtension
{
    private static readonly StructureMcp Instance=new StructureMcp();
    static StructureMcp(){McpExtensionRegistry.Register(Instance);AssemblyReloadEvents.beforeAssemblyReload+=()=>McpExtensionRegistry.Unregister(Instance);}
    public string Id=>"little-game.structure";
    public string Version=>"1.0.0";
    public string Description=>"结构样板：固定支架、旋钮解锁、重力约束摆动、导轨抽取目标。独立于原版三关。";
    public string UnavailableReason=>!Application.isPlaying||StructureBootstrap.View==null?"请打开 Structure 场景并进入 Play":null;
    private static JObject Schema(string fields="",string required="")=>JObject.Parse("{'type':'object','properties':{"+fields+"},'required':["+required+"],'additionalProperties':false}");
    public McpExtensionOperation[] Operations=>new[]{
        new McpExtensionOperation("get_state","读取锁定、旋钮退出进度、盖板角度/角速度、静止状态、通路/安全抽取条件、事件和结果。物理步长1/120秒，重力矩驱动单轴摆；非自由刚体模拟",true,Schema()),
        new McpExtensionOperation("get_view","读取实际部件/实体尺寸/世界姿态/屏幕中心及中心命中对象、相机环绕角度、界面按钮与手势；装置不随观察转动",true,Schema()),
        new McpExtensionOperation("get_structure","结构定义和0至-85度盖板连续角度采样检查；排除预期销轴接触，验证其余固定件/模块不侵入摆动实体。模块退出用连续扫掠盒体检查",true,Schema()),
        new McpExtensionOperation("pointer","与玩家共用输入：单击旋钮/模块操作，拖动仅环绕观察；取消/多指清除手势。左下原点0到1；支持重开、结构显示等界面按钮",false,
            Schema("'phase':{'type':'string','enum':['begin','move','end','cancel']},'x':{'type':'number','minimum':0,'maximum':1},'y':{'type':'number','minimum':0,'maximum':1}","'phase','x','y'")),
        new McpExtensionOperation("interact","与玩家相同规则命令；knob解锁，gate检查连接，module检查通路和摆动速度后抽取。输入命中应另用pointer验证",false,
            Schema("'part':{'type':'string','enum':['knob','gate','module']}","'part'")),
        new McpExtensionOperation("restart","重置本关连接、运动和目标；与界面重开相同",false,Schema()),
        new McpExtensionOperation("capture_view","保存包含界面的实际相机渲染截图，540×960；编辑器截图非手机截图",true,Schema())
    };
    public McpHostResult Execute(string operation,JObject arguments)
    {
        var state=StructureBootstrap.Session;
        if(operation=="get_view")return McpHostResult.Success(StructureBootstrap.View.Snapshot());
        if(operation=="get_structure")
        {
            var collisions=new System.Collections.Generic.List<object>();float minY=float.MaxValue;
            for(int i=0;i<=850;i++)
            {
                float angle=-i*.1f*Mathf.Deg2Rad;
                foreach(var def in StructureDefinition.Parts)
                    if(def.Id!="gate"&&def.Id!="hinge"&&def.Id!="knob"&&StructureDefinition.GateOverlaps(def.Center,def.Size,angle))collisions.Add(new{angle=-i*.1f,part=def.Id});
                float y=StructureDefinition.GateCenter(angle).Y;
                float extent=Math.Abs(Mathf.Sin(angle))*StructureDefinition.Width*.5f+Math.Abs(Mathf.Cos(angle))*StructureDefinition.Height*.5f;
                minY=Math.Min(minY,y-extent);
            }
            return McpHostResult.Success(new{parts=StructureDefinition.Parts.Select(p=>new{id=p.Id,name=p.Name,center=new{x=p.Center.X,y=p.Center.Y,z=p.Center.Z},size=new{x=p.Size.X,y=p.Size.Y,z=p.Size.Z},connection=p.Connection}),geometryEnvelopeErrors=StructureBootstrap.View.ValidateGeometry(),hinge=new{x=StructureDefinition.Hinge.X,y=StructureDefinition.Hinge.Y,z=StructureDefinition.Hinge.Z},limits=new{minDegrees=-85,maxDegrees=0},equilibriumDegrees=StructureDefinition.Equilibrium*Mathf.Rad2Deg,sweep=new{sampleStepDegrees=.1f,samples=851,minGateBottom=minY,collisions},exitDistance=StructureDefinition.ExtractDistance});
        }
        if(operation=="capture_view")return McpHostResult.Success(Capture());
        if(operation=="pointer")
        {
            float x=(float)arguments["x"],y=(float)arguments["y"];
            if(float.IsNaN(x)||float.IsNaN(y)||float.IsInfinity(x)||float.IsInfinity(y)||x<0||x>1||y<0||y>1)return McpHostResult.Fail("坐标必须在0到1内");
            StructureBootstrap.Input.Pointer((string)arguments["phase"],new Vector2(x,y));
        }
        else if(operation!="get_state")state.Command(operation,(string)arguments["part"]??"");
        return McpHostResult.Success(state.Snapshot());
    }
    private static object Capture()
    {
        StructureBootstrap.View.Apply();var camera=StructureBootstrap.View.Camera;
        var old=camera.targetTexture;var active=RenderTexture.active;var target=RenderTexture.GetTemporary(540,960,24);
        var image=new Texture2D(540,960,TextureFormat.RGB24,false);
        try
        {
            camera.targetTexture=target;StructureBootstrap.View.Apply();Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=target;
            image.ReadPixels(new Rect(0,0,540,960),0,0);image.Apply();
            var path=Path.GetFullPath("Library/KabuUnityMcp/Captures/structure-"+Guid.NewGuid().ToString("N")+".png");
            Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,image.EncodeToPNG());return new{path,width=540,height=960};
        }
        finally{camera.targetTexture=old;StructureBootstrap.View.Apply();RenderTexture.active=active;RenderTexture.ReleaseTemporary(target);UnityEngine.Object.DestroyImmediate(image);}
    }
    [MenuItem("Little Game/Open Structure Sample")]
    public static void OpenSample()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("请先退出 Play");
        const string path="Assets/Scenes/Structure.unity";
        if(!File.Exists(path))
        {
            var scene=EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects,NewSceneMode.Single);EditorSceneManager.SaveScene(scene,path);
        }
        else EditorSceneManager.OpenScene(path);
        EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(path,true),new EditorBuildSettingsScene("Assets/Scenes/Game.unity",true)};
        SetPortrait();
    }
    // 编辑器取景也使用手机比例；只改变本项目 Game 视图，不修改桌面显示设置。
    private static void SetPortrait()
    {
        var assembly=typeof(EditorWindow).Assembly;
        var sizesType=assembly.GetType("UnityEditor.GameViewSizes");
        var singleton=typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
        var sizes=singleton.GetProperty("instance").GetValue(null);
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic;
        var groupType=sizesType.GetProperty("currentGroupType",flags).GetValue(sizes);
        var group=sizesType.GetMethod("GetGroup").Invoke(sizes,new[]{groupType});
        var groupClass=group.GetType();int count=(int)groupClass.GetMethod("GetTotalCount").Invoke(group,null);int index=-1;
        for(int i=0;i<count;i++)
        {
            var size=groupClass.GetMethod("GetGameViewSize").Invoke(group,new object[]{i});var type=size.GetType();
            if((int)type.GetProperty("width",flags).GetValue(size)==540&&(int)type.GetProperty("height",flags).GetValue(size)==960){index=i;break;}
        }
        if(index<0)
        {
            var kind=assembly.GetType("UnityEditor.GameViewSizeType");var type=assembly.GetType("UnityEditor.GameViewSize");
            var size=Activator.CreateInstance(type,flags,null,new object[]{Enum.Parse(kind,"FixedResolution"),540,960,"Structure Portrait"},null);
            groupClass.GetMethod("AddCustomSize").Invoke(group,new[]{size});index=count;
        }
        var window=EditorWindow.GetWindow(assembly.GetType("UnityEditor.GameView"));
        window.GetType().GetProperty("selectedSizeIndex",flags).SetValue(window,index);window.Repaint();
    }
}
