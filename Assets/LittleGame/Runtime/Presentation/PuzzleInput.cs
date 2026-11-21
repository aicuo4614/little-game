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
        public string Gesture => !pressed ? "idle" : uiGesture ? "ui" : !dragged ? "pending" : onMachine ? "orbit" : "background";
        private float dragThreshold;
        private int finger=-1;
        private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
        private PointerEventData pointerEvent;

        public void Pointer(string phase,Vector2 position)
        {
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
            if(Input.touchCount>1) { Cancel(true); return; }
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
            if(finger>=0) { Cancel();return; }
            if(Input.GetMouseButtonDown(0)) Begin(Input.mousePosition);
            if(Input.GetMouseButton(0)) Move(Input.mousePosition);
            if(Input.GetMouseButtonUp(0)) End(Input.mousePosition);
        }

        private void Begin(Vector2 point)
        {
            Cancel(false);
            pressTime=Time.realtimeSinceStartup;
            start=last=point;pressed=true;part=null;onMachine=false;
            orbitCenter=Camera.main.WorldToScreenPoint(GameBootstrap.View.Machine.position);
            orbitRadius=Mathf.Max(1,Mathf.Min(Screen.width,Screen.height)*.4f);
            dragThreshold=Mathf.Max(6,Mathf.Min(Screen.width,Screen.height)*.012f);
            dragged=false;
            uiGesture=OverUi(point);
            if(uiGesture) { previousPart=null;return; }
            if(Physics.Raycast(Camera.main.ScreenPointToRay(point),out var hit))
            {
                onMachine=hit.collider.transform.IsChildOf(GameBootstrap.View.Machine);
                var target=hit.collider.GetComponentInParent<MachinePart>();
                if(target!=null && GameBootstrap.Session.Accessible(target.Id)) part=target.Id;
            }

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

        private void OnDisable() { Cancel(true); }
        private void OnApplicationFocus(bool focused) { if(!focused) Cancel(true); }
        private void Cancel(bool forgetClick=true)
        {
            pressed=uiGesture=dragged=onMachine=false;part=null;finger=-1;
            if(forgetClick) previousPart=null;
        }
    }
}
