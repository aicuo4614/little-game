using System.Collections.Generic;
using UnityEngine;

namespace LittleGame
{
    // 所有轮廓、边框和落位共享同一几何中心及相机平面，消除厚度投影造成的偏移。
    public sealed class CollectionBar : MonoBehaviour
    {
        private sealed class Slot
        {
            public string Id;public Vector3 Position,Center;public Vector2 Size;public float Scale;
            public Material Material;public GameObject Ghost;public bool Filled;public int Row,Column;
        }
        private readonly List<Slot> slots=new List<Slot>();
        private readonly List<Material> materials=new List<Material>();
        private readonly List<Mesh> meshes=new List<Mesh>();
        public Quaternion ParkingRotation { get;private set; }
        private const int Columns=8;
        private int rows;
        private Camera camera;
        public float Bottom { get;private set; }
        public float Top { get;private set; }
        public void Build(MachineModel model,DeviceLevel level,Camera camera)
        {
            ParkingRotation=Quaternion.LookRotation(camera.transform.up,-camera.transform.forward);
            this.camera=camera;
            rows=Mathf.CeilToInt(level.Parts.Length/(float)Columns);
            const int columns=Columns;
            const float cellWidth=1,cellHeight=.84f;
            for(int index=0;index<level.Parts.Length;index++)
            {
                string id=level.Parts[index].Id;var part=model.Parts[id];
                var filters=new List<MeshFilter>();Bounds b=new Bounds();bool first=true;
                foreach(var filter in part.GetComponentsInChildren<MeshFilter>())
                {
                    var renderer=filter.GetComponent<Renderer>();if(!renderer.enabled) continue;
                    filters.Add(filter);if(first) { b=renderer.bounds;first=false; } else b.Encapsulate(renderer.bounds);
                }
                float display=Mathf.Min((cellWidth-.25f)/b.size.x,(cellHeight-.25f)/b.size.z);
                display=Mathf.Min(display,id.Contains("screw")?.95f:.65f);
                int row=index/columns,column=index%columns;
                int rowCount=Mathf.Min(columns,level.Parts.Length-row*columns);
                Vector3 center=camera.transform.up*(-(row+.5f)*cellHeight)
                    +camera.transform.right*((column-(rowCount-1)*.5f)*cellWidth);
                Vector3 outward=-camera.transform.forward;
                var slot=new Slot{Id=id,Row=row,Column=column,Center=center,Scale=display,
                    Size=new Vector2(b.size.x*display+.14f,b.size.z*display+.14f)};
                Shape("AlignedCell",center,new Vector3(cellWidth-.055f,.025f,cellHeight-.035f),false,"#DBD4C2");
                bool round=id.Contains("screw");
                Shape("SlotRim",center+outward*.025f,new Vector3(slot.Size.x,.02f,slot.Size.y),round,"#ECE7D6");
                var liner=Shape("SlotLiner",center+outward*.04f,new Vector3(slot.Size.x-.055f,.012f,slot.Size.y-.055f),round,"#78948A");
                slot.Material=liner.GetComponent<Renderer>().sharedMaterial;
                slot.Position=center+outward*(.06f+b.size.y*display*.5f)-ParkingRotation*((b.center-part.position)*display);
                var instances=new List<CombineInstance>();
                foreach(var filter in filters)
                    instances.Add(new CombineInstance{mesh=filter.sharedMesh,transform=Matrix4x4.Translate(-b.center)*filter.transform.localToWorldMatrix});
                var mesh=new Mesh();mesh.CombineMeshes(instances.ToArray());meshes.Add(mesh);
                var ghost=new GameObject("Outline_"+id,typeof(MeshFilter),typeof(MeshRenderer));ghost.transform.SetParent(transform,false);
                ghost.transform.SetPositionAndRotation(center+outward*.055f,ParkingRotation);
                ghost.transform.localScale=new Vector3(display,.015f,display);
                ghost.GetComponent<MeshFilter>().sharedMesh=mesh;ghost.GetComponent<MeshRenderer>().sharedMaterial=Material("#B9C8B9");
                ghost.GetComponent<Renderer>().shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                slot.Ghost=ghost;slots.Add(slot);
            }
        }
        public void Layout(Rect safe, float headerBottom)
        {
            float cellPixels=Mathf.Min(safe.width*.88f/Columns,safe.height*.058f);
            float height=rows*.84f*cellPixels/Screen.height;
            Top=headerBottom-8f*RepairHud.Scale/Screen.height;
            Bottom=Top-height;
            transform.position=camera.ViewportToWorldPoint(new Vector3(safe.center.x/Screen.width,Top,8));
            transform.localScale=Vector3.one*(cellPixels*2*camera.orthographicSize/Screen.height);
        }
        public Vector3 Parking(string id) { return transform.TransformPoint(slots.Find(s=>s.Id==id).Position); }
        public float Scale(string id) { return slots.Find(s=>s.Id==id).Scale*transform.lossyScale.x; }
        public void Refresh(PuzzleSession session)
        {
            foreach(var slot in slots)
            {
                bool filled=session.Recovered(slot.Id);
                if(filled==slot.Filled) continue;slot.Filled=filled;slot.Ghost.SetActive(!filled);
                ColorUtility.TryParseHtmlString(filled?"#46AD75":"#78948A",out var color);slot.Material.color=color;
            }
        }
        public object Snapshot()
        {
            var result=new List<object>();
            foreach(var slot in slots)
            {
                var world=Parking(slot.Id);var p=camera.WorldToViewportPoint(world);var center=camera.WorldToViewportPoint(transform.TransformPoint(slot.Center));
                result.Add(new {part=slot.Id,index=result.Count,row=slot.Row,column=slot.Column,x=p.x,y=p.y,
                    center=new {x=center.x,y=center.y},width=slot.Size.x*transform.lossyScale.x,length=slot.Size.y*transform.lossyScale.x,displayScale=Scale(slot.Id),filled=slot.Filled,
                    worldPosition=new { x=world.x,y=world.y,z=world.z }});
            }
            return result;
        }
        private Material Material(string color)
        {
            var m=new Material(Resources.Load<Material>("ArrowMaterial"));ColorUtility.TryParseHtmlString(color,out var c);m.color=c;materials.Add(m);return m;
        }
        private Transform Shape(string name,Vector3 position,Vector3 scale,bool round,string color)
        {
            var go=GameObject.CreatePrimitive(round?PrimitiveType.Cylinder:PrimitiveType.Cube);go.name=name;go.transform.SetParent(transform,false);
            go.transform.SetPositionAndRotation(position,ParkingRotation);
            go.transform.localScale=new Vector3(scale.x,round?scale.y*.5f:scale.y,scale.z);Destroy(go.GetComponent<Collider>());
            if(!round) { var mesh=DeviceMesh.RoundedBox(.12f);meshes.Add(mesh);go.GetComponent<MeshFilter>().sharedMesh=mesh; }
            var r=go.GetComponent<Renderer>();r.sharedMaterial=Material(color);r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;return go.transform;
        }
        private void OnDestroy() { foreach(var m in materials) Destroy(m);foreach(var m in meshes) Destroy(m); }
    }
}
