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
        public float Bottom { get;private set; }
        public float Top { get;private set; }
        public void Build(MachineModel model,DeviceLevel level,Camera camera)
        {
            ParkingRotation=Quaternion.LookRotation(camera.transform.up,-camera.transform.forward);
            int columns=8;int rows=level.Parts.Length>8?2:1;
            float height=rows==1?.14f:.108f;
            float cellWidth=2*camera.orthographicSize*Mathf.Min(camera.aspect,9f/16)*.94f/columns;
            float cellHeight=2*camera.orthographicSize*height;
            Bottom=rows==1?.78f-height*.5f:.68f-height*.5f;
            Top=(rows==1?.78f:.80f)+height*.5f;
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
                float viewportY=rows==1?.78f:row==0?.80f:.68f;
                Vector3 center=camera.transform.position+camera.transform.forward*8
                    +camera.transform.up*((viewportY-.5f)*2*camera.orthographicSize)
                    +camera.transform.right*((column-(columns-1)*.5f)*cellWidth);
                Vector3 outward=-camera.transform.forward;
                var slot=new Slot{Id=id,Row=row,Column=column,Center=center,Scale=display,
                    Size=new Vector2(b.size.x*display+.14f,b.size.z*display+.14f)};
                Shape("AlignedCell",center,new Vector3(cellWidth-.055f,.025f,cellHeight),false,"#DBD4C2");
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
        public Vector3 Parking(string id) { return slots.Find(s=>s.Id==id).Position; }
        public float Scale(string id) { return slots.Find(s=>s.Id==id).Scale; }
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
                var p=Camera.main.WorldToViewportPoint(slot.Position);var center=Camera.main.WorldToViewportPoint(slot.Center);
                result.Add(new {part=slot.Id,index=result.Count,row=slot.Row,column=slot.Column,x=p.x,y=p.y,
                    center=new {x=center.x,y=center.y},width=slot.Size.x,length=slot.Size.y,displayScale=slot.Scale,filled=slot.Filled,
                    worldPosition=new {x=slot.Position.x,y=slot.Position.y,z=slot.Position.z}});
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
