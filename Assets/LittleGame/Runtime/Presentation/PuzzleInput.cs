using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections.Generic;

namespace LittleGame
{
    [DefaultExecutionOrder(-100)]
    public sealed class PuzzleInput : MonoBehaviour
    {
        private Vector2 start,last,orbitCenter;
        private float orbitRadius;
        private string part;
        public const float DoubleClickSeconds=.36f;
        private bool pressed,uiGesture,dragged,onMachine;
        private string previousPart;
        private float previousTime=-10,pressTime;
        private int previousRevision=-1;
        private Vector2 previousClick;
        public string Gesture => pinching?"pinch":!pressed ? "idle" : uiGesture ? "ui" : !dragged ? "pending" : onMachine ? "orbit" : "background";
        private float dragThreshold;
        private int finger=-1,pinchA=-1,pinchB=-1;
        private bool pinching,releaseLatch,hardwarePinch;
        private Vector2 pinchCenter;
        private float pinchDistance;
        private float uiBlockUntil;
        public bool BlocksUi => pinching||releaseLatch||Time.unscaledTime<uiBlockUntil;
        private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
        private PointerEventData pointerEvent;

        public void Pointer(string phase,Vector2 position)
        {
            if(pinching){if(phase=="cancel")Pinch("cancel",Vector2.zero,Vector2.zero);return;}
            switch(phase)
            {
                case "begin": Begin(position); break;
                case "move": Move(position); break;
                case "end": End(position); break;
                case "cancel": Cancel(true); break;
            }
        }

        private bool OverUi(Vector2 position)
        {
            if(EventSystem.current==null) return false;
            if(pointerEvent==null) pointerEvent=new PointerEventData(EventSystem.current);
            pointerEvent.position=position;
            uiHits.Clear();
            EventSystem.current.RaycastAll(pointerEvent,uiHits);
            return uiHits.Count>0;
        }

        private void Update()
        {
            if(Input.touchCount>1)uiBlockUntil=Time.unscaledTime+.2f;
            if(Input.touchCount>2){Pinch("cancel",Vector2.zero,Vector2.zero);releaseLatch=true;return;}
            if(Input.touchCount==2)
            {
                if(releaseLatch)return;
                hardwarePinch=true;
                var a=Input.GetTouch(0);var b=Input.GetTouch(1);
                if(!pinching){pinchA=a.fingerId;pinchB=b.fingerId;Pinch("begin",a.position,b.position);}
                else if(a.fingerId==pinchA&&b.fingerId==pinchB)Pinch("move",a.position,b.position);
                else {Pinch("cancel",a.position,b.position);releaseLatch=true;}
                return;
            }
            if(hardwarePinch&&pinching){Pinch("end",Vector2.zero,Vector2.zero);releaseLatch=true;}
            if(releaseLatch){if(Input.touchCount==0)releaseLatch=false;return;}
            if(Input.touchCount==1)
            {
                var touch=Input.GetTouch(0);
                if(touch.phase==TouchPhase.Began) { Begin(touch.position);finger=touch.fingerId; }
                if(touch.fingerId!=finger) return;
                if(touch.phase==TouchPhase.Moved) Move(touch.position);
                if(touch.phase==TouchPhase.Ended) End(touch.position);
                if(touch.phase==TouchPhase.Canceled) Cancel();
                return;
            }
            if(pinching)return;
            if(Input.touchCount==0)hardwarePinch=false;
            if(finger>=0) { Cancel();return; }
            if(Input.mouseScrollDelta.y!=0 && InsideWorkspace(Input.mousePosition) && !OverUi(Input.mousePosition))
            {
                var pivot=Vector2.Scale(Input.mousePosition,new Vector2(1f/Screen.width,1f/Screen.height))-GameBootstrap.View.ScreenCenter;
                GameBootstrap.Session.Inspect(Mathf.Pow(1.15f,Input.mouseScrollDelta.y),pivot.x,pivot.y,0,0);
            }
            if(Input.GetMouseButtonDown(0)) Begin(Input.mousePosition);
            if(Input.GetMouseButton(0)) Move(Input.mousePosition);
            if(Input.GetMouseButtonUp(0)) End(Input.mousePosition);
        }

        private bool InsideWorkspace(Vector2 p)
        {return GameBootstrap.View.Inspection.Region.Contains(new Vector2(p.x/Screen.width,p.y/Screen.height));}
        public void Pinch(string phase,Vector2 a,Vector2 b)
        {
            if(phase=="end"||phase=="cancel"){pinching=hardwarePinch=false;Cancel(true);return;}
            if(phase=="begin")
            {
                uiBlockUntil=Time.unscaledTime+.2f;pinching=false;Cancel(true);
                if(!InsideWorkspace(a)||!InsideWorkspace(b)||OverUi(a)||OverUi(b))return;
                pinchDistance=Vector2.Distance(a,b);if(pinchDistance<12)return;
                pinchCenter=(a+b)*.5f;pinching=true;return;
            }
            if(!pinching)return;
            float distance=Vector2.Distance(a,b);if(distance<12)return;
            var center=(a+b)*.5f;
            if(Mathf.Abs(distance-pinchDistance)<.05f && Vector2.Distance(center,pinchCenter)<.05f)return;
            var old=GameBootstrap.View.ScreenCenter;
            GameBootstrap.Session.Inspect(distance/pinchDistance,pinchCenter.x/Screen.width-old.x,pinchCenter.y/Screen.height-old.y,
                (center.x-pinchCenter.x)/Screen.width,(center.y-pinchCenter.y)/Screen.height);
            pinchDistance=distance;pinchCenter=center;
        }

        private void Begin(Vector2 point)
        {
            Cancel(false);
            pressTime=Time.realtimeSinceStartup;
            start=last=point;pressed=true;part=null;onMachine=false;
            orbitCenter=Vector2.Scale(GameBootstrap.View.ScreenCenter,new Vector2(Screen.width,Screen.height));
            orbitRadius=Mathf.Max(1,Mathf.Min(Screen.width,Screen.height)*.4f);
            dragThreshold=Mathf.Max(6,Mathf.Min(Screen.width,Screen.height)*.012f);
            dragged=false;
            uiGesture=!InsideWorkspace(point)||OverUi(point);
            if(uiGesture) { previousPart=null;return; }
            part=GameBootstrap.View.Pick(point,out onMachine);
            onMachine|=GameBootstrap.Session.IsAnimating;

        }

        private void Move(Vector2 point)
        {
            if(!pressed || uiGesture) return;
            if(!dragged && (point-start).magnitude<dragThreshold) return;
            dragged=true;previousPart=null;
            if(onMachine)
            {
                var from=(last-orbitCenter)/orbitRadius;
                var to=(point-orbitCenter)/orbitRadius;
                GameBootstrap.Session.Orbit(from.x,from.y,to.x,to.y);
            }
            last=point;
        }

        private void End(Vector2 point)
        {
            if(!pressed || uiGesture) { Cancel();return; }
            Move(point);
            if(!dragged && Time.realtimeSinceStartup-pressTime<.6f)
            {
                bool twice=previousRevision==GameBootstrap.Session.Revision && part!=null && part==previousPart && Time.realtimeSinceStartup-previousTime<=DoubleClickSeconds
                    && Vector2.Distance(point,previousClick)<=Mathf.Min(Screen.width,Screen.height)*.08f;
                GameBootstrap.Session.Execute(part==null?"clear_selection":twice?"disassemble":"select",part);
                previousPart=twice?null:part;previousTime=Time.realtimeSinceStartup;previousClick=point;previousRevision=GameBootstrap.Session.Revision;
            }
            else previousPart=null;
            Cancel(false);
        }

        private void OnDisable() { pinching=releaseLatch=hardwarePinch=false;Cancel(true); }
        private void OnApplicationFocus(bool focused) { if(!focused) {pinching=releaseLatch=hardwarePinch=false;Cancel(true);} }
        private void Cancel(bool forgetClick=true)
        {
            pressed=uiGesture=dragged=onMachine=false;part=null;finger=-1;
            if(forgetClick) previousPart=null;
        }
    }
}
