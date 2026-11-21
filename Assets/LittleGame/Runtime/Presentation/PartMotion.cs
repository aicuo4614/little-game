using UnityEngine;

namespace LittleGame
{
    // 先沿装配外法线完全脱离，再移到设备前方安全平面，向外弧线回收，最后落格。
    public sealed class PartMotion
    {
        private readonly Transform part,assembly;
        private readonly Collider[] colliders;
        private readonly Vector3 origin,scale,destination,visualCenter;
        private readonly Bounds assemblyBounds;
        private readonly float parkedScale,deviceRadius,minLocalY,partRadius;
        private readonly bool screw;
        private readonly Quaternion parkingRotation;
        private readonly Camera camera;
        private bool initialized,wasRemoved;
        private float previousProgress=-1;
        private Vector3 start,lifted,safe,approach,outward,flightScale,center;
        private Quaternion flightRotation;
        public string Stage {get;private set;}="attached";
        public Vector3 VisualCenter => part.TransformPoint(visualCenter);
        public float FrontClearance {get;private set;}
        public float OutwardClearance {get;private set;}
        public PartMotion(Transform part,Vector3 destination,float parkedScale,Quaternion parkingRotation,Bounds assemblyBounds,float deviceRadius,Camera camera)
        {
            this.part=part;assembly=part.parent;this.destination=destination;this.parkedScale=parkedScale;
            this.parkingRotation=parkingRotation;this.assemblyBounds=assemblyBounds;this.deviceRadius=deviceRadius;this.camera=camera;
            origin=part.localPosition;scale=part.localScale;colliders=part.GetComponentsInChildren<Collider>();screw=part.name.Contains("screw");
            bool first=true;Bounds bounds=new Bounds();
            foreach(var r in part.GetComponentsInChildren<Renderer>()) if(r.enabled) {if(first){bounds=r.bounds;first=false;}else bounds.Encapsulate(r.bounds);}
            visualCenter=part.InverseTransformPoint(bounds.center);
            minLocalY=assembly.InverseTransformPoint(bounds.min).y;
            partRadius=(bounds.extents.magnitude+Vector3.Distance(bounds.center,part.position))*Mathf.Max(1,parkedScale/assembly.lossyScale.x);
        }
        public void RestoreAttachedPose()
        {
            if(part.parent==assembly) { if(part.localPosition!=origin)part.localPosition=origin;if(part.localRotation!=Quaternion.identity)part.localRotation=Quaternion.identity; }
        }
        private static float Ease(float t) { return Mathf.SmoothStep(0,1,Mathf.Clamp01(t)); }
        public void Apply(bool removed,PuzzleSession state)
        {
            float t=removed?(state.AnimationPart==part.name?state.AnimationProgress:1):0;
            if(initialized && removed && wasRemoved && t==1 && previousProgress==1)return;
            if(initialized && !removed && !wasRemoved)return;
            if(!removed)
            {
                if(part.parent!=assembly)part.SetParent(assembly,false);
                part.localPosition=origin;part.localRotation=Quaternion.identity;part.localScale=scale;Stage="attached";
            }
            else
            {
                if(!wasRemoved)
                {
                    start=part.position;flightScale=part.lossyScale;flightRotation=part.rotation;center=assembly.position;outward=-camera.transform.forward;
                    float lift=(assemblyBounds.max.y-minLocalY+.25f)*assembly.lossyScale.y;
                    lifted=start+assembly.up*Mathf.Max(.35f,lift);
                    float front=deviceRadius+partRadius+.3f;
                    safe=lifted+outward*Mathf.Max(.2f,front-Vector3.Dot(lifted-center,outward));
                    approach=destination+outward*(Vector3.Dot(safe-center,outward)-Vector3.Dot(destination-center,outward));
                    part.SetParent(assembly.parent,true);
                }
                float transfer=Ease((t-.45f)/.38f);
                if(t<.24f) { Stage="release";part.position=Vector3.Lerp(start,lifted,Ease(t/.24f)); }
                else if(t<.45f) { Stage="clear_body";part.position=Vector3.Lerp(lifted,safe,Ease((t-.24f)/.21f)); }
                else if(t<.83f)
                {
                    Stage="transfer";float u=transfer,v=1-u;float side=destination.x<center.x?-1:1;
                    var a=safe+camera.transform.right*side*.65f;var b=approach+camera.transform.right*side*.35f;
                    part.position=v*v*v*safe+3*v*v*u*a+3*v*u*u*b+u*u*u*approach;
                }
                else { Stage=t>=1?"parked":"settle";part.position=Vector3.Lerp(approach,destination,Ease((t-.83f)/.17f)); }
                part.localScale=Vector3.Lerp(flightScale,scale*parkedScale,transfer);
                part.rotation=Quaternion.Slerp(flightRotation,parkingRotation,transfer);
                if(screw && t<.24f)part.rotation*=Quaternion.AngleAxis(720*Ease(t/.24f),Vector3.up);
                FrontClearance=Vector3.Dot(part.position-center,outward)-deviceRadius-partRadius;
                OutwardClearance=Vector3.Dot(part.position-start,assembly.up)-(assemblyBounds.max.y-minLocalY)*assembly.lossyScale.y;
            }
            foreach(var collider in colliders)if(collider!=null)collider.enabled=!removed;initialized=true;wasRemoved=removed;previousProgress=t;
        }
    }
}
