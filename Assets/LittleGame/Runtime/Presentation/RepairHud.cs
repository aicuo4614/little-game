using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace LittleGame
{
    // 界面只显示会话状态，所有按钮仍调用共用规则指令。
    public sealed class RepairHud : MonoBehaviour
    {
        public static RepairHud Current { get; private set; }
        private Text stage,tip,actionLabel,progress,brand;
        private Text levelLabel;
        private Button restartButton,flipButton,levelButton;
        private void Awake() { Current=this; }
        private Button action;
        private Image fill;
        private Sprite sprite;
        private Texture2D texture;
        private int revision=-1;
        private string cachedGesture;
        public void Build()
        {
            texture=new Texture2D(64,64,TextureFormat.RGBA32,false);
            var pixels=new Color[4096];
            for(int y=0;y<64;y++) for(int x=0;x<64;x++)
            {
                var q=new Vector2(Mathf.Abs(x-31.5f),Mathf.Abs(y-31.5f))-Vector2.one*20;
                float d=new Vector2(Mathf.Max(q.x,0),Mathf.Max(q.y,0)).magnitude+Mathf.Min(Mathf.Max(q.x,q.y),0)-11;
                pixels[y*64+x]=new Color(1,1,1,Mathf.Clamp01(.5f-d));
            }
            texture.SetPixels(pixels);texture.Apply(false,true);
            sprite=Sprite.Create(texture,new Rect(0,0,64,64),new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect,new Vector4(12,12,12,12));
            var canvas=new GameObject("RepairInterface",typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            canvas.transform.SetParent(transform,false);
            var c=canvas.GetComponent<Canvas>();c.renderMode=RenderMode.ScreenSpaceCamera;c.worldCamera=Camera.main;c.planeDistance=2;
            var scaler=canvas.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(540,960);scaler.matchWidthOrHeight=Screen.width>(Screen.height*.7f)?1:0;
            if(EventSystem.current==null) new GameObject("EventSystem",typeof(EventSystem),typeof(StandaloneInputModule));
            var safe=new GameObject("SafeArea",typeof(RectTransform)).GetComponent<RectTransform>();safe.SetParent(canvas.transform,false);
            safe.anchorMin=new Vector2(Screen.safeArea.xMin/Screen.width,Screen.safeArea.yMin/Screen.height);
            safe.anchorMax=new Vector2(Screen.safeArea.xMax/Screen.width,Screen.safeArea.yMax/Screen.height);safe.offsetMin=safe.offsetMax=Vector2.zero;
            var header=Panel(safe,"TaskCard",new Vector2(.5f,1),new Vector2(0,-72),new Vector2(492,108),"#F2EDDF");
            header.GetComponent<Image>().raycastTarget=true;
            brand=TextAt(header,"电器回收站  /  001",new Vector2(-118,30),new Vector2(210,24),14,"#637A74");
            stage=TextAt(header,"旧掌机 · 拆解回收",new Vector2(-63,0),new Vector2(315,34),23,"#293E40");
            levelButton=MakeButton(header,"第二关",new Vector2(192,-5),new Vector2(78,28),()=>GameBootstrap.Session.LoadLevel(GameBootstrap.Session.LevelIndex==1?2:1),"#DEE4D6","#476F63");
            levelLabel=levelButton.GetComponentInChildren<Text>();
            progress=TextAt(header,"0 / 7",new Vector2(194,28),new Vector2(70,24),15,"#426E65");
            Panel(header,"ProgressTrack",new Vector2(.5f,.5f),new Vector2(0,-33),new Vector2(444,5),"#D6DDCF");
            fill=Panel(header,"Progress",new Vector2(.5f,.5f),new Vector2(0,-33),new Vector2(444,5),"#4B8B78").GetComponent<Image>();
            fill.rectTransform.pivot=new Vector2(0,.5f);fill.rectTransform.anchoredPosition=new Vector2(-222,-33);
            var footer=Panel(safe,"OperationCard",new Vector2(.5f,0),new Vector2(0,76),new Vector2(492,110),"#F2EDDF");
            footer.GetComponent<Image>().raycastTarget=true;
            tip=TextAt(footer,"拖动机器，自由翻看",new Vector2(0,18),new Vector2(450,44),18,"#354F4F");
            action=MakeButton(footer,"回收完成",new Vector2(0,17),new Vector2(280,42),()=>GameBootstrap.Session.Execute(GameBootstrap.Session.LevelIndex<DeviceLevels.All.Length?"next_level":"restart"),"#356F65","#FFFFFF");
            actionLabel=action.GetComponentInChildren<Text>();
            restartButton=MakeButton(footer,"重开",new Vector2(-198,-31),new Vector2(70,28),()=>GameBootstrap.Session.Execute("restart"),"#E2E5D9","#536963");
            flipButton=MakeButton(footer,"翻面",new Vector2(198,-31),new Vector2(70,28),()=>GameBootstrap.Session.Flip(),"#E2E5D9","#536963");
            TextAt(footer,"拖动旋转 · 双击拆卸",new Vector2(0,-32),new Vector2(300,25),15,"#6C7D72");
        }
        private RectTransform Panel(Transform parent,string name,Vector2 anchor,Vector2 pos,Vector2 size,string color)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(Image));go.transform.SetParent(parent,false);
            var rect=go.GetComponent<RectTransform>();rect.anchorMin=rect.anchorMax=anchor;rect.anchoredPosition=pos;rect.sizeDelta=size;
            var image=go.GetComponent<Image>();image.sprite=sprite;image.type=Image.Type.Sliced;
            ColorUtility.TryParseHtmlString(color,out var value);image.color=value;image.raycastTarget=false;return rect;
        }
        private Text TextAt(Transform parent,string text,Vector2 pos,Vector2 size,int fontSize,string color)
        {
            var go=new GameObject("Label",typeof(RectTransform),typeof(Text));go.transform.SetParent(parent,false);
            var t=go.GetComponent<Text>();t.font=Resources.Load<Font>("GameFont");t.fontSize=fontSize;t.text=text;t.alignment=TextAnchor.MiddleCenter;
            ColorUtility.TryParseHtmlString(color,out var value);t.color=value;t.raycastTarget=false;
            t.rectTransform.anchorMin=t.rectTransform.anchorMax=new Vector2(.5f,.5f);t.rectTransform.anchoredPosition=pos;t.rectTransform.sizeDelta=size;return t;
        }
        private Button MakeButton(Transform parent,string title,Vector2 pos,Vector2 size,UnityEngine.Events.UnityAction callback,string background,string foreground)
        {
            var rect=Panel(parent,title,new Vector2(.5f,.5f),pos,size,background);rect.GetComponent<Image>().raycastTarget=true;
            TextAt(rect,title,Vector2.zero,size,18,foreground);var button=rect.gameObject.AddComponent<Button>();button.onClick.AddListener(callback);return button;
        }
        private void LateUpdate()
        {
            var s=GameBootstrap.Session;if(stage==null || s==null) return;
            string gesture=GameBootstrap.Input.Gesture;
            if(revision==s.Revision && cachedGesture==gesture) return;
            revision=s.Revision;cachedGesture=gesture;
            brand.text="电器回收站  /  "+s.LevelIndex.ToString("000");levelLabel.text=s.LevelIndex==1?"第二关":"第一关";
            stage.text=s.Won?"拆解完成 · 全部回收":s.Level.Title+(s.Cover?" · 拆开外壳":" · 拆解回收");
            progress.text=s.RecoveredCount+" / "+s.Level.Parts.Length;fill.rectTransform.sizeDelta=new Vector2(444*Mathf.Clamp01((float)s.RecoveredCount/s.Level.Parts.Length),5);
            tip.gameObject.SetActive(!s.Won);
            tip.text=s.IsAnimating?"正在回收 · "+s.Name(s.AnimationPart):!string.IsNullOrEmpty(s.Blocker)?s.Message:
                string.IsNullOrEmpty(s.Selected)?!s.BackVisible?"拖动设备翻到背面，找到外壳螺丝":"单击选中 · 双击拆卸":
                s.Name(s.Selected)+"已选中 · 双击拆卸";
            levelButton.interactable=flipButton.interactable=!s.IsAnimating;
            action.gameObject.SetActive(s.Won && !s.IsAnimating);
            actionLabel.text=s.LevelIndex<DeviceLevels.All.Length?"下一关 · 电脑机箱":"回收完成 · 再来一次";
        }

        public object Snapshot()
        {
            var buttons=new System.Collections.Generic.List<object>();
            foreach(var pair in new[]{new {id="restart",button=restartButton},new {id="flip",button=flipButton},new {id="replay",button=action},new {id="switch_level",button=levelButton}})
            {
                var p=Camera.main.WorldToViewportPoint(pair.button.transform.position);
                buttons.Add(new {id=pair.id,x=p.x,y=p.y,visible=pair.button.gameObject.activeInHierarchy,interactable=pair.button.interactable});
            }
            return new {hint="拖动旋转 · 双击拆卸",buttons};
        }
        private void OnDestroy() { if(Current==this) Current=null; if(sprite!=null) Destroy(sprite);if(texture!=null) Destroy(texture); }
    }
}
