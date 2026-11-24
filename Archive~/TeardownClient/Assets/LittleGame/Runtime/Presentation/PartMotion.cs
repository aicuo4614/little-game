using UnityEngine;

namespace LittleGame
{
    // 先沿装配外法线完全脱离，再移到设备前方安全平面，向外弧线回收，最后落格。
    public sealed class PartMotion
    {
        private readonly Transform part,assembly;
        private readonly Collider[] colliders;
        private readonly Renderer[] renderers;
        private readonly UnityEngine.Rendering.ShadowCastingMode[] shadows;
        private readonly Vector3 origin,scale,visualCenter;
        private readonly Bounds assemblyBounds;
        private readonly float minLocalY,basePartRadius;
        private readonly float modelRadius;
        private float deviceRadius,partRadius;
        private readonly bool screw;
        private readonly CollectionBar collection;
        private readonly InspectionViewport viewport;
        private readonly Camera camera;
        private bool initialized,wasRemoved,transferring;
        private Vector3 start,lifted,safe,outward,center,transferStart,transferScale,hingeArm;
        private Quaternion releaseRotation;
        private Quaternion transferRotation;
        public string Stage {get;private set;}="attached";
        public Vector3 VisualCenter => part.TransformPoint(visualCenter);
        public float FrontClearance {get;private set;}
        public float OutwardClearance {get;private set;}
        public PartMotion(Transform part,CollectionBar collection,Bounds assemblyBounds,float deviceRadius,InspectionViewport viewport,Camera camera)
        {
            this.part=part;assembly=part.parent;this.collection=collection;this.viewport=viewport;
            this.assemblyBounds=assemblyBounds;this.modelRadius=deviceRadius/assembly.lossyScale.x;this.camera=camera;
            origin=part.localPosition;scale=part.localScale;colliders=part.GetComponentsInChildren<Collider>();renderers=part.GetComponentsInChildren<Renderer>();
            shadows=new UnityEngine.Rendering.ShadowCastingMode[renderers.Length];for(int i=0;i<renderers.Length;i++)shadows[i]=renderers[i].shadowCastingMode;
            screw=part.name.Contains("screw");
            bool first=true;Bounds bounds=new Bounds();
            foreach(var r in part.GetComponentsInChildren<Renderer>()) if(r.enabled) {if(first){bounds=r.bounds;first=false;}else bounds.Encapsulate(r.bounds);}
            visualCenter=part.InverseTransformPoint(bounds.center);
            minLocalY=assembly.InverseTransformPoint(bounds.min).y;
            basePartRadius=(bounds.extents.magnitude+Vector3.Distance(bounds.center,part.position));
        }
        public void RestoreAttachedPose()
        {
            if(part.parent==assembly) { if(part.localPosition!=origin)part.localPosition=origin;if(part.localRotation!=Quaternion.identity)part.localRotation=Quaternion.identity; }
        }
        private static float Ease(float t) { return Mathf.SmoothStep(0,1,Mathf.Clamp01(t)); }
        public void Apply(bool removed,PuzzleSession state)
        {
            float t=removed?(state.AnimationPart==part.name?state.AnimationProgress:1):0;
            if(initialized && !removed && !wasRemoved)return;
            if(!removed)
            {
                if(part.parent!=assembly)part.SetParent(assembly,false);
                InspectionViewport.SetLayer(part,InspectionViewport.DeviceLayer);
                part.localPosition=origin;part.localRotation=Quaternion.identity;part.localScale=scale;Stage="attached";
            }
            else
            {
                var destination=collection.Parking(part.name);
                var parkedScale=scale*collection.Scale(part.name);
                if(!wasRemoved)
                {
                    transferring=false;
                    deviceRadius=modelRadius*assembly.lossyScale.x;
                    partRadius=basePartRadius;
                    start=assembly.InverseTransformPoint(part.position);
                    part.SetParent(assembly.parent,true);InspectionViewport.SetLayer(part,InspectionViewport.FlightLayer);
                }
                // 脱离阶段跟随整件根节点，进入转移阶段后固定飞行起点。
                if(t<.45f || !transferring)
                {
                    center=assembly.position;outward=-viewport.Forward;
                    var worldStart=assembly.TransformPoint(start);
                    float lift=(assemblyBounds.max.y-minLocalY+.25f)*assembly.lossyScale.y;
                    hingeArm=state.IsSuitcase&&part.name=="cover"
                        ? assembly.right*(assemblyBounds.extents.x*assembly.lossyScale.x) : Vector3.zero;
                    releaseRotation=hingeArm==Vector3.zero?Quaternion.identity:Quaternion.AngleAxis(74,assembly.forward);
                    lifted=worldStart+(hingeArm==Vector3.zero?assembly.up*Mathf.Max(.35f,lift):releaseRotation*hingeArm-hingeArm);
                    float front=deviceRadius+partRadius+.3f;
                    safe=lifted+outward*Mathf.Max(.2f,front-Vector3.Dot(lifted-center,outward));
                }

                if(t>=1)
                {
                    if(Stage!="parked")InspectionViewport.SetLayer(part,0);
                    Stage="parked";
                    part.SetPositionAndRotation(destination,collection.ParkingRotation);
                    part.localScale=parkedScale;
                }
                else if(t<.45f)
                {
                    Stage=t<.24f?"release":"clear_body";
                    var position=t<.24f?Vector3.Lerp(assembly.TransformPoint(start),lifted,Ease(t/.24f)):
                        Vector3.Lerp(lifted,safe,Ease((t-.24f)/.21f));
                    var spin=screw?Quaternion.AngleAxis(720*Ease(t/.24f),assembly.up):
                        Quaternion.Slerp(Quaternion.identity,releaseRotation,Ease(t/.24f));
                    if(hingeArm!=Vector3.zero&&t<.24f)position=assembly.TransformPoint(start)+spin*hingeArm-hingeArm;
                    part.SetPositionAndRotation(viewport.FlightPosition(position),viewport.FlightRotation*spin*assembly.rotation);
                    part.localScale=Vector3.Lerp(scale*assembly.lossyScale.x*state.Zoom,parkedScale,Ease((t-.24f)/.28f));
                    FrontClearance=Vector3.Dot(position-center,outward)-deviceRadius-partRadius;
                    OutwardClearance=Vector3.Dot(position-assembly.TransformPoint(start),assembly.up)-(assemblyBounds.max.y-minLocalY)*assembly.lossyScale.y;
                }
                else
                {
                    if(!transferring)
                    {
                        transferring=true;
                        transferStart=viewport.FlightPosition(safe);
                        transferRotation=viewport.FlightRotation*releaseRotation*assembly.rotation;
                        transferScale=Vector3.Lerp(scale*assembly.lossyScale.x*state.Zoom,parkedScale,Ease((.45f-.24f)/.28f));
                        FrontClearance=Vector3.Dot(safe-center,outward)-deviceRadius-partRadius;
                    }
                    float transfer=Ease((t-.45f)/.38f);
                    float v=1-transfer;
                    var forward=camera.transform.forward;
                    var approach=destination+forward*Mathf.Min(-.25f,Vector3.Dot(transferStart-destination,forward));
                    float side=Vector3.Dot(destination-transferStart,camera.transform.right)<0?-1:1;
                    var a=transferStart+camera.transform.right*side*.65f;
                    var b=approach+camera.transform.right*side*.35f;
                    Stage=t<.83f?"transfer":"settle";
                    part.position=t<.83f?v*v*v*transferStart+3*v*v*transfer*a+3*v*transfer*transfer*b+transfer*transfer*transfer*approach:
                        Vector3.Lerp(approach,destination,Ease((t-.83f)/.17f));
                    part.localScale=Vector3.Lerp(transferScale,parkedScale,transfer);
                    part.rotation=Quaternion.Slerp(transferRotation,collection.ParkingRotation,transfer);
                }
            }
            foreach(var collider in colliders)if(collider!=null)collider.enabled=!removed;
            if(!initialized || removed!=wasRemoved)for(int i=0;i<renderers.Length;i++)if(renderers[i]!=null)renderers[i].shadowCastingMode=removed?UnityEngine.Rendering.ShadowCastingMode.Off:shadows[i];initialized=true;wasRemoved=removed;
        }
    }
}
