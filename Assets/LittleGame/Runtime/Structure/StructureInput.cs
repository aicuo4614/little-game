using UnityEngine;

namespace LittleGame.Structure
{
    // 触屏、鼠标和 MCP 统一经过命中、起拖、取消和松手判断。
    public sealed class StructureInput : MonoBehaviour
    {
        private bool down,dragged,blockedUntilRelease;
        private Vector2 start,last;
        private string target,ui;
        public string Gesture=>!down?"idle":dragged?"orbit":"pending";
        public void Pointer(string phase,Vector2 point)
        {
            var view=StructureBootstrap.View;var session=StructureBootstrap.Session;
            if(phase=="cancel"){down=false;target=ui="";return;}
            if(phase=="begin")
            {
                down=true;dragged=false;start=last=point;ui=view.Hud.Hit(point);
                target=string.IsNullOrEmpty(ui)?view.Pick(point):"";
                if(target!="")session.Command("select",target);return;
            }
            if(!down)return;
            if(phase=="move")
            {
                var pixels=new Vector2((point.x-start.x)*Screen.width,(point.y-start.y)*Screen.height);
                if(pixels.magnitude>Mathf.Max(10,Mathf.Min(Screen.width,Screen.height)*.018f))dragged=true;
                if(dragged&&string.IsNullOrEmpty(ui))view.Orbit(point.x-last.x,point.y-last.y);
                last=point;return;
            }
            if(phase=="end")
            {
                // 松手位移也参与判定，避免丢失 move 事件后误触发。
                if(Vector2.Distance(point,start)*Mathf.Min(Screen.width,Screen.height)>.025f*Mathf.Min(Screen.width,Screen.height))dragged=true;
                down=false;
                if(dragged)return;
                if(!string.IsNullOrEmpty(ui))
                {
                    if(view.Hud.Hit(point)==ui)view.Hud.Execute(ui);return;
                }
                string release=view.Pick(point);
                if(target!=""&&target==release)session.Command("interact",target);
                else session.Command("clear_selection");
            }
        }
        private void Update()
        {
            if(Input.touchCount>1){Pointer("cancel",Vector2.zero);blockedUntilRelease=true;return;}
            if(blockedUntilRelease){if(Input.touchCount==0)blockedUntilRelease=false;return;}
            if(Input.touchCount==1)
            {
                var t=Input.GetTouch(0);var p=new Vector2(t.position.x/Screen.width,t.position.y/Screen.height);
                if(t.phase==TouchPhase.Began)Pointer("begin",p);
                else if(t.phase==TouchPhase.Moved)Pointer("move",p);
                else if(t.phase==TouchPhase.Ended)Pointer("end",p);
                else if(t.phase==TouchPhase.Canceled)Pointer("cancel",p);
                return;
            }
            var mouse=new Vector2(Input.mousePosition.x/Screen.width,Input.mousePosition.y/Screen.height);
            if(Input.GetMouseButtonDown(0))Pointer("begin",mouse);
            else if(Input.GetMouseButton(0))Pointer("move",mouse);
            if(Input.GetMouseButtonUp(0))Pointer("end",mouse);
        }
        private void OnApplicationFocus(bool focus){if(!focus)Pointer("cancel",Vector2.zero);}
        private void OnDisable(){down=false;}
    }
}
