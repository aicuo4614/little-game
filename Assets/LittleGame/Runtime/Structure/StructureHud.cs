using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace LittleGame.Structure
{
    public sealed class StructureHud : MonoBehaviour
    {
        private StructureView view;
        private Canvas canvas;
        private RectTransform safe;
        private Text title,message,status;
        private readonly Dictionary<string,RectTransform> buttons=new Dictionary<string,RectTransform>();
        private readonly Dictionary<string,Text> labels=new Dictionary<string,Text>();
        public void Build(StructureView owner)
        {
            view=owner;
            var go=new GameObject("StructureInterface",typeof(Canvas));go.transform.SetParent(transform,false);canvas=go.GetComponent<Canvas>();
            canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=view.Camera;canvas.planeDistance=1;
            safe=new GameObject("SafeArea",typeof(RectTransform)).GetComponent<RectTransform>();safe.SetParent(go.transform,false);
            var header=Panel(safe,new Vector2(.5f,1),new Vector2(0,-78),new Vector2(492,116),"#EBE9DE");
            Label(header,"结构试验  /  01",new Vector2(0,35),new Vector2(450,24),14,"#637C7D");
            title=Label(header,"旋开旋钮 · 救出模块",new Vector2(0,3),new Vector2(460,38),25,"#29484C");
            status=Label(header,"固定 → 解锁 → 下摆 → 取出",new Vector2(0,-34),new Vector2(450,24),15,"#4E7870");
            var footer=Panel(safe,new Vector2(.5f,0),new Vector2(0,92),new Vector2(492,145),"#EBE9DE");
            message=Label(footer,"",new Vector2(0,35),new Vector2(450,54),19,"#29484C");
            Label(footer,"点击操作 · 拖动环绕观察",new Vector2(0,-6),new Vector2(450,24),14,"#61797A");
            Button(footer,"restart","重开",new Vector2(-165,-43),new Vector2(104,34));
            Button(footer,"reset_view","正面",new Vector2(-55,-43),new Vector2(104,34));
            Button(footer,"guides","查看结构",new Vector2(55,-43),new Vector2(104,34));
            Button(footer,"legacy","原版三关",new Vector2(165,-43),new Vector2(104,34));
            Refresh();
        }
        private RectTransform Panel(Transform parent,Vector2 anchor,Vector2 position,Vector2 size,string hex)
        {
            var go=new GameObject("Panel",typeof(RectTransform),typeof(Image));go.transform.SetParent(parent,false);var rect=go.GetComponent<RectTransform>();
            rect.anchorMin=rect.anchorMax=anchor;rect.anchoredPosition=position;rect.sizeDelta=size;
            ColorUtility.TryParseHtmlString(hex,out var color);go.GetComponent<Image>().color=color;go.GetComponent<Image>().raycastTarget=false;return rect;
        }
        private Text Label(Transform parent,string text,Vector2 pos,Vector2 size,int fontSize,string hex)
        {
            var go=new GameObject("Label",typeof(RectTransform),typeof(Text));go.transform.SetParent(parent,false);var label=go.GetComponent<Text>();
            label.font=Resources.Load<Font>("GameFont");label.fontSize=fontSize;label.text=text;label.alignment=TextAnchor.MiddleCenter;label.raycastTarget=false;
            ColorUtility.TryParseHtmlString(hex,out var color);label.color=color;label.rectTransform.anchorMin=label.rectTransform.anchorMax=new Vector2(.5f,.5f);
            label.rectTransform.anchoredPosition=pos;label.rectTransform.sizeDelta=size;return label;
        }
        private void Button(Transform parent,string id,string text,Vector2 pos,Vector2 size)
        {
            var rect=Panel(parent,new Vector2(.5f,.5f),pos,size,"#CFDCD4");buttons[id]=rect;
            labels[id]=Label(rect,text,Vector2.zero,size,16,"#355C59");
        }
        public void Refresh()
        {
            if(safe==null)return;
            var area=Screen.safeArea;canvas.scaleFactor=Mathf.Min(area.width/540,area.height/960);
            safe.anchorMin=new Vector2(area.xMin/Screen.width,area.yMin/Screen.height);safe.anchorMax=new Vector2(area.xMax/Screen.width,area.yMax/Screen.height);safe.offsetMin=safe.offsetMax=Vector2.zero;
            var s=StructureBootstrap.Session;title.text=s.Won?"模块完整取出 · 委托完成":"旋开旋钮 · 救出模块";
            message.text=s.Message;status.text=s.Won?"目标已取出，支架与盖板保留":s.Extracting?"沿导轨抽出":s.Unlocking?"旋钮退出锁孔":!s.Unlocked?"右上锁定 · 左上悬挂":s.SafeToExtract?"通路打开 · 可以取出模块":"盖板受重力下摆";
            labels["guides"].text=view.DebugVisible?"隐藏结构":"查看结构";
        }
        public string Hit(Vector2 point)
        {
            var pixel=new Vector2(point.x*Screen.width,point.y*Screen.height);
            foreach(var pair in buttons)if(RectTransformUtility.RectangleContainsScreenPoint(pair.Value,pixel,view.Camera))return pair.Key;
            // 卡片区域截断手势，避免在界面上旋转观察相机。
            if(point.y>Screen.safeArea.yMax/Screen.height-145*canvas.scaleFactor/Screen.height||point.y<Screen.safeArea.yMin/Screen.height+170*canvas.scaleFactor/Screen.height)return "panel";
            return "";
        }
        public void Execute(string id)
        {
            if(id=="restart"){StructureBootstrap.Session.Command("restart");view.Apply();}
            else if(id=="reset_view")view.ResetView();
            else if(id=="guides")view.ToggleGuides();
            else if(id=="legacy")SceneManager.LoadScene("Game");
        }
        public object Snapshot()
        {
            var list=new List<object>();foreach(var pair in buttons){var p=view.Camera.WorldToViewportPoint(pair.Value.position);list.Add(new{id=pair.Key,x=p.x,y=p.y});}
            return new{buttons=list,hint="点击操作 · 拖动环绕观察",message=message.text};
        }
    }
}
