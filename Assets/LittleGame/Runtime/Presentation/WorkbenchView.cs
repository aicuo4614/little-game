using System.Collections.Generic;
using UnityEngine;

namespace LittleGame
{
    // 固定环境不依赖规则；设备可在桌面上方自由检查，零件归入独立托盘。
    public sealed class WorkbenchView : MonoBehaviour
    {
        private readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        private readonly List<Mesh> meshes = new List<Mesh>();
        public const float DeskY = -4.15f;
        public void Build()
        {
            Box("OakDesk",new Vector3(0,DeskY-.3f,1),new Vector3(30,.5f,32),"#C0A17B");
            for(int i=-7;i<8;i++)
                Box("WoodSeam",new Vector3(i*2.2f,DeskY-.039f,1),new Vector3(.018f,.008f,32),"#AF906D");
            Box("MatEdge",new Vector3(0,DeskY+.015f,0),new Vector3(9.6f,.09f,10),"#294D52");
            Box("RepairMat",new Vector3(0,DeskY+.07f,0),new Vector3(9.35f,.045f,9.75f),"#426F70");
            for(int i=-8;i<=8;i++)
            {
                Box("Grid",new Vector3(i*.5f,DeskY+.096f,0),new Vector3(.012f,.003f,9.15f),"#527E7D");
                Box("Grid",new Vector3(0,DeskY+.096f,i*.5f),new Vector3(8.8f,.003f,.012f),"#527E7D");
            }
            // 右侧工具停留在工作区外，均不创建碰撞体。
            Box("ScrewdriverGrip",new Vector3(3.55f,DeskY+.23f,-2.1f),new Vector3(.35f,.32f,1.55f),"#DBA45F");
            Box("ScrewdriverShaft",new Vector3(3.55f,DeskY+.23f,-.85f),new Vector3(.09f,.09f,1.1f),"#AEBFC0");
            Box("PryTool",new Vector3(-3.55f,DeskY+.17f,-2),new Vector3(.3f,.13f,2.1f),"#D6DBCC");
            var groups=new Dictionary<Material,List<CombineInstance>>();
            var filters=GetComponentsInChildren<MeshFilter>();
            foreach(var filter in filters)
            {
                var material=filter.GetComponent<Renderer>().sharedMaterial;
                if(!groups.ContainsKey(material)) groups[material]=new List<CombineInstance>();
                groups[material].Add(new CombineInstance{mesh=filter.sharedMesh,transform=transform.worldToLocalMatrix*filter.transform.localToWorldMatrix});
                filter.gameObject.SetActive(false);
            }
            foreach(var group in groups)
            {
                var mesh=new Mesh();mesh.name="WorkbenchCombined";mesh.CombineMeshes(group.Value.ToArray());meshes.Add(mesh);
                var go=new GameObject("WorkbenchSurface",typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(transform,false);
                go.GetComponent<MeshFilter>().sharedMesh=mesh;go.GetComponent<MeshRenderer>().sharedMaterial=group.Key;
            }
        }
        private Transform Box(string name,Vector3 position,Vector3 scale,string color)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name=name;go.transform.SetParent(transform,false);
            go.transform.localPosition=position;go.transform.localScale=scale;
            Destroy(go.GetComponent<Collider>());
            var mesh=DeviceMesh.RoundedBox(.07f);meshes.Add(mesh);
            go.GetComponent<MeshFilter>().sharedMesh=mesh;
            if(!materials.TryGetValue(color,out var material))
            {
                material=new Material(Resources.Load<Material>("DeviceMaterial"));
                ColorUtility.TryParseHtmlString(color,out var value);material.color=value;
                material.SetFloat("_Smoothness",.18f);materials.Add(color,material);
            }
            go.GetComponent<Renderer>().sharedMaterial=material;
            return go.transform;
        }
        private void OnDestroy()
        {
            foreach(var item in materials.Values) Destroy(item);
            foreach(var item in meshes) Destroy(item);
        }
    }
}
