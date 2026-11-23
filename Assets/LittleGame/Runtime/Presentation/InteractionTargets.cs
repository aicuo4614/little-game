using System.Collections.Generic;
using UnityEngine;

namespace LittleGame
{
    // 小物件的通用交互代理。真实网格碰撞负责遮挡，扩大代理只用于触摸容错。
    public sealed class InteractionTargets
    {
        public const float MinimumModelSide=.7f; // 原螺丝直径0.35，最小边长为其两倍。
        public const float MinimumScreenFraction=.07f;
        private sealed class Target
        {
            public string Id;public Transform Part;public BoxCollider Proxy;public Vector3 Size;public bool Small,Assist;
        }
        private readonly List<Target> targets=new List<Target>();
        private readonly InspectionViewport viewport;
        private readonly Transform machine;
        public string LastPart {get;private set;}="";
        public string LastMethod {get;private set;}="none";
        private const int GeometryMask=1<<InspectionViewport.DeviceLayer;
        public InteractionTargets(MachineModel model,DeviceLevel level,InspectionViewport viewport)
        {
            this.viewport=viewport;machine=model.transform;
            foreach(var definition in level.Parts)
            {
                var part=model.Parts[definition.Id];bool first=true;Bounds b=new Bounds();
                foreach(var r in part.GetComponentsInChildren<Renderer>())if(r.enabled){if(first){b=r.bounds;first=false;}else b.Encapsulate(r.bounds);}
                var go=new GameObject("TouchTarget");go.layer=InspectionViewport.TargetLayer;go.transform.SetParent(part,false);
                go.transform.localPosition=part.InverseTransformPoint(b.center);
                var size=new Vector3(Mathf.Max(MinimumModelSide,b.size.x),Mathf.Max(MinimumModelSide,b.size.y),Mathf.Max(MinimumModelSide,b.size.z));
                var collider=go.AddComponent<BoxCollider>();
                collider.size=new Vector3(size.x/part.lossyScale.x,size.y/part.lossyScale.y,size.z/part.lossyScale.z);
                targets.Add(new Target{Id=definition.Id,Part=part,Proxy=collider,Size=size,Small=b.size.x<=.8f&&b.size.z<=.8f,Assist=b.size.x<MinimumModelSide||b.size.z<MinimumModelSide});
            }
        }
        private Vector2 ScreenPoint(Vector3 world)
        {
            var p=viewport.Project(world);
            return new Vector2(p.x*Screen.width,p.y*Screen.height);
        }
        private Rect ScreenRect(Target target)
        {
            var box=target.Proxy;var lo=new Vector2(float.MaxValue,float.MaxValue);var hi=new Vector2(float.MinValue,float.MinValue);
            for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2)
            {
                var p=ScreenPoint(box.transform.TransformPoint(box.center+Vector3.Scale(box.size,new Vector3(x,y,z))*.5f));
                lo=Vector2.Min(lo,p);hi=Vector2.Max(hi,p);
            }
            var center=ScreenPoint(box.transform.position);
            float floor=Mathf.Min(Screen.width,Screen.height)*MinimumScreenFraction;
            var size=Vector2.Max(hi-lo,Vector2.one*floor);return new Rect(center-size*.5f,size);
        }
        private bool Visible(Target target)
        {
            var p=ScreenPoint(target.Proxy.transform.position);
            if(!GameBootstrap.View.Inspection.Region.Contains(new Vector2(p.x/Screen.width,p.y/Screen.height)))return false;
            if(!Physics.Raycast(viewport.ScreenRay(p),out var hit,100,GeometryMask))return false;
            var marker=hit.collider.GetComponentInParent<MachinePart>();return marker!=null&&marker.Id==target.Id;
        }
        public string Pick(Vector2 point,out bool onMachine)
        {
            Physics.SyncTransforms();LastPart="";LastMethod="none";onMachine=false;string direct=null;
            if(Physics.Raycast(viewport.ScreenRay(point),out var hit,100,GeometryMask))
            {
                onMachine=hit.collider.transform.IsChildOf(machine);
                var marker=hit.collider.GetComponentInParent<MachinePart>();
                if(marker!=null&&GameBootstrap.Session.Accessible(marker.Id))direct=marker.Id;
            }
            var exact=targets.Find(t=>t.Id==direct);
            // 直接命中小件优先，扩大的邻居区域不能抢走这次点击。
            if(exact!=null&&exact.Small){LastPart=direct;LastMethod="geometry";return direct;}
            Target nearest=null;float distance=float.MaxValue;
            foreach(var target in targets)
            {
                if(!target.Assist||!GameBootstrap.Session.Accessible(target.Id)||!ScreenRect(target).Contains(point)||!Visible(target))continue;
                var p=ScreenPoint(target.Proxy.transform.position);float d=(point-p).sqrMagnitude;
                if(d<distance){distance=d;nearest=target;}
            }
            if(nearest!=null){onMachine=true;LastPart=nearest.Id;LastMethod="minimum_target";return nearest.Id;}
            LastPart=direct??"";LastMethod=direct==null?"none":"geometry";return direct;
        }
        public object Snapshot(string id)
        {
            var target=targets.Find(t=>t.Id==id);var rect=ScreenRect(target);
            var actual=Vector3.Scale(target.Proxy.size,target.Proxy.transform.lossyScale)/machine.lossyScale.x;
            return new {minimumModelSide=MinimumModelSide,colliderEnabled=target.Proxy.enabled,colliderLayer=target.Proxy.gameObject.layer,modelSize=new {x=actual.x,y=actual.y,z=actual.z},
                rect=new {xMin=rect.xMin/Screen.width,yMin=rect.yMin/Screen.height,xMax=rect.xMax/Screen.width,yMax=rect.yMax/Screen.height},
                minimumPixels=Mathf.Min(Screen.width,Screen.height)*MinimumScreenFraction,visible=Visible(target),small=target.Small,assist=target.Assist};
        }
    }
}
