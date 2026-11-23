using System.Collections.Generic;
using UnityEngine;
using NVector=System.Numerics.Vector3;

namespace LittleGame.Structure
{
    public sealed class StructureHit : MonoBehaviour { public string Id; }

    // 只读取结构和会话状态。绕物观察不旋转装置，不改变重力或连接坐标。
    public sealed class StructureView : MonoBehaviour
    {
        public Camera Camera {get;private set;}
        public StructureHud Hud {get;private set;}
        public readonly Dictionary<string,Transform> Parts=new Dictionary<string,Transform>();
        public bool DebugVisible {get;private set;}
        public float Yaw {get;private set;}=-12;
        public float Pitch {get;private set;}=17;
        private readonly List<Material> materials=new List<Material>();
        private readonly List<GameObject> guides=new List<GameObject>();
        private Transform gate,knob,module;
        public static Vector3 V(NVector value)=>new Vector3(value.X,value.Y,value.Z);
        public void Build()
        {
            Camera=UnityEngine.Camera.main;
            Camera.orthographic=true;Camera.clearFlags=CameraClearFlags.SolidColor;Camera.backgroundColor=Hex("#172832");
            Camera.nearClipPlane=.1f;Camera.farClipPlane=60;
            RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=Hex("#ADBCC4");
            foreach(var light in FindObjectsOfType<Light>()) light.enabled=false;
            var lamp=new GameObject("StructureKey").AddComponent<Light>();lamp.transform.SetParent(transform,false);
            lamp.type=LightType.Directional;lamp.intensity=1.2f;lamp.transform.rotation=Quaternion.Euler(35,-25,0);lamp.shadows=LightShadows.Soft;
            Shape(transform,"Table",new Vector3(0,-.3f,0),new Vector3(14,.12f,14),"#243C46");
            foreach(var def in StructureDefinition.Parts)
            {
                var root=new GameObject(def.Name);root.transform.SetParent(transform,false);root.transform.position=V(def.Center);
                root.AddComponent<StructureHit>().Id=def.Id;Parts.Add(def.Id,root.transform);
                if(def.Id=="gate")
                {
                    root.transform.position=V(StructureDefinition.Hinge);gate=root.transform;
                    Shape(root.transform,"Panel",V(StructureDefinition.GateOffset),new Vector3(def.Size.X,def.Size.Y,.10f),def.Color,false);
                    var solid=root.AddComponent<BoxCollider>();solid.center=V(StructureDefinition.GateOffset);solid.size=V(def.Size);
                    for(int i=0;i<5;i++)Shape(root.transform,"Vent",new Vector3(1.35f,-.45f-i*.2f,-.055f),new Vector3(1.55f,.045f,.01f),"#754F3D",false);
                }
                else if(def.Id=="knob"||def.Id=="hinge")
                {
                    Shape(root.transform,"Cylinder",Vector3.zero,def.Id=="knob"?new Vector3(.4f,.4f,.22f):V(def.Size),def.Color,true,PrimitiveType.Cylinder);
                    if(def.Id=="knob")Shape(root.transform,"Grip",new Vector3(0,0,-.14f),new Vector3(.32f,.085f,.08f),"#6C5637",false);
                }
                else if(def.Id=="module")Shape(root.transform,"Body",new Vector3(0,0,.06f),new Vector3(1.05f,.55f,.58f),def.Color);
                else Shape(root.transform,"Body",Vector3.zero,V(def.Size),def.Color);
            }
            knob=Parts["knob"];module=Parts["module"];
            Shape(module,"Chip",new Vector3(0,-.025f,-.27f),new Vector3(.42f,.2f,.08f),"#163E42",false);
            for(int i=0;i<5;i++)Shape(module,"Contact",new Vector3(-.36f+i*.18f,-.19f,-.2425f),new Vector3(.08f,.13f,.025f),"#E5C77B",false);
            foreach(float x in new[]{-.4f,.4f})Shape(module,"HandleSupport",new Vector3(x,.16f,-.27f),new Vector3(.08f,.1f,.08f),"#D6DFD5",false);
            Shape(module,"PullHandle",new Vector3(0,.16f,-.32f),new Vector3(.88f,.08f,.06f),"#D6DFD5",false);
            BuildGuides();
            Hud=gameObject.AddComponent<StructureHud>();Hud.Build(this);
            UpdateCamera();Apply();
        }
        private static Color Hex(string value){ColorUtility.TryParseHtmlString(value,out var color);return color;}
        private GameObject Shape(Transform parent,string name,Vector3 center,Vector3 size,string color,bool collision=true,PrimitiveType type=PrimitiveType.Cube)
        {
            var obj=GameObject.CreatePrimitive(type);obj.name=name;obj.transform.SetParent(parent,false);obj.transform.localPosition=center;
            if(type==PrimitiveType.Cylinder){obj.transform.localRotation=Quaternion.Euler(90,0,0);obj.transform.localScale=new Vector3(size.x,size.z*.5f,size.y);}
            else obj.transform.localScale=size;
            var source=Resources.Load<Material>("DeviceMaterial");
            var mat=source!=null?new Material(source):new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color=Hex(color);mat.SetFloat("_Smoothness",.28f);obj.GetComponent<Renderer>().sharedMaterial=mat;materials.Add(mat);
            if(!collision)Destroy(obj.GetComponent<Collider>());
            return obj;
        }
        public void Orbit(float dx,float dy)
        {Yaw=Mathf.Clamp(Yaw+dx*140,-65,65);Pitch=Mathf.Clamp(Pitch-dy*100,5,42);UpdateCamera();}
        public void ResetView(){Yaw=-12;Pitch=17;UpdateCamera();}
        public void ToggleGuides(){DebugVisible=!DebugVisible;foreach(var item in guides)item.SetActive(DebugVisible);}
        private void UpdateCamera()
        {
            var center=new Vector3(-.45f,1.8f,-.05f);
            var direction=Quaternion.Euler(Pitch,Yaw,0)*Vector3.back;
            Camera.transform.SetPositionAndRotation(center+direction*10,Quaternion.LookRotation(-direction,Vector3.up));
            Camera.orthographicSize=Mathf.Max(3.35f,2.9f/Camera.aspect);
        }
        public string Pick(Vector2 viewport)
        {
            var ray=Camera.ViewportPointToRay(viewport);
            if(Physics.Raycast(ray,out var hit,30))
            {
                var owner=hit.collider.GetComponentInParent<StructureHit>();
                if(owner!=null)return owner.Id;
            }
            // 小旋钮有屏幕命中下限；必须没有实体遮挡才使用。
            var point=Camera.WorldToViewportPoint(knob.position);
            float pixels=Vector2.Distance(new Vector2(point.x*Screen.width,point.y*Screen.height),new Vector2(viewport.x*Screen.width,viewport.y*Screen.height));
            if(!StructureBootstrap.Session.Unlocked&&pixels<Mathf.Min(Screen.width,Screen.height)*.055f)
            {
                var toward=knob.position-Camera.transform.position;
                if(!Physics.Raycast(Camera.transform.position,toward.normalized,out var obstacle,toward.magnitude-.25f)||obstacle.collider.GetComponentInParent<StructureHit>()?.Id=="knob")return "knob";
            }
            return "";
        }
        private void LateUpdate(){Apply();}
        public void Apply()
        {
            if(gate==null)return;
            UpdateCamera();
            var s=StructureBootstrap.Session;
            gate.localRotation=Quaternion.AngleAxis(s.Angle*Mathf.Rad2Deg,Vector3.forward);
            var home=V(StructureDefinition.Find("knob").Center);
            float u=s.UnlockProgress;
            // 先退出锁孔，再停靠到盖板扫掠范围以外；在此之前规则不释放铰链。
            knob.position=home+Vector3.back*(Mathf.Min(u/.65f,1)*.34f)+Vector3.right*(Mathf.Clamp01((u-.65f)/.35f)*.82f);
            knob.rotation=Quaternion.AngleAxis(-u*720,Vector3.forward);
            module.position=V(StructureDefinition.ModuleHome)+Vector3.back*(s.ExtractProgress*StructureDefinition.ExtractDistance);
            Physics.SyncTransforms();Hud?.Refresh();
        }
        private void Line(Transform parent,string name,Vector3[] points,Color color)
        {
            var go=new GameObject(name);go.transform.SetParent(parent,false);var line=go.AddComponent<LineRenderer>();
            // 使用已纳入构建的资源材质，避免运行时 Shader.Find 的辅助线 shader 被裁剪。
            var mat=new Material(Resources.Load<Material>("DeviceMaterial"));mat.color=color;
            mat.EnableKeyword("_EMISSION");mat.SetColor("_EmissionColor",color*.65f);materials.Add(mat);
            line.sharedMaterial=mat;line.useWorldSpace=false;line.widthMultiplier=.018f;line.positionCount=points.Length;line.SetPositions(points);
            guides.Add(go);go.SetActive(false);
        }
        private void BuildGuides()
        {
            var half=V(StructureDefinition.Find("gate").Size)*.5f;var c=V(StructureDefinition.GateOffset);
            Line(gate,"GateBoundary",new[]{c+new Vector3(-half.x,-half.y,-.09f),c+new Vector3(half.x,-half.y,-.09f),c+new Vector3(half.x,half.y,-.09f),c+new Vector3(-half.x,half.y,-.09f),c+new Vector3(-half.x,-half.y,-.09f)},Color.yellow);
            Line(transform,"HingeAxis",new[]{V(StructureDefinition.Hinge)+Vector3.back*.7f,V(StructureDefinition.Hinge)+Vector3.forward*.5f},Color.cyan);
            foreach(float x in new[]{.125f,1.175f})
                Line(transform,"ExitPath",new[]{new Vector3(x,3.1f,.3f),new Vector3(x,3.1f,-2)},Color.green);
        }
        public object Snapshot()
        {
            Apply();var parts=new List<object>();
            foreach(var def in StructureDefinition.Parts)
            {
                var t=Parts[def.Id];var center=def.Id=="gate"?gate.TransformPoint(V(StructureDefinition.GateOffset)):t.position;
                var screen=Camera.WorldToViewportPoint(center);
                parts.Add(new{id=def.Id,name=def.Name,connection=def.Connection,position=Point(t.position),rotation=new{x=t.rotation.x,y=t.rotation.y,z=t.rotation.z,w=t.rotation.w},center=Point(center),size=Point(V(def.Size)),screen=new{x=screen.x,y=screen.y},hitAtCenter=Pick(screen)});
            }
            return new{width=Screen.width,height=Screen.height,rotationTarget="observation_camera",yaw=Yaw,pitch=Pitch,deviceRotation=new{x=transform.rotation.x,y=transform.rotation.y,z=transform.rotation.z,w=transform.rotation.w},hinge=Point(V(StructureDefinition.Hinge)),debugVisible=DebugVisible,gesture=StructureBootstrap.Input.Gesture,parts,hud=Hud.Snapshot()};
        }
        private static object Point(Vector3 p)=>new{x=p.x,y=p.y,z=p.z};
        public string[] ValidateGeometry()
        {
            var errors=new List<string>();
            foreach(var def in StructureDefinition.Parts)
            {
                var root=Parts[def.Id];var envelope=new Bounds(def.Id=="gate"?V(StructureDefinition.GateOffset):Vector3.zero,V(def.Size)+Vector3.one*.002f);
                foreach(var filter in root.GetComponentsInChildren<MeshFilter>())
                {
                    var bounds=filter.sharedMesh.bounds;
                    for(int i=0;i<8;i++)
                    {
                        var p=bounds.center+Vector3.Scale(bounds.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                        if(!envelope.Contains(root.InverseTransformPoint(filter.transform.TransformPoint(p)))){errors.Add(def.Id+"/"+filter.name);break;}
                    }
                }
            }
            return errors.ToArray();
        }
        private void OnDestroy(){foreach(var material in materials)Destroy(material);}
    }
}
