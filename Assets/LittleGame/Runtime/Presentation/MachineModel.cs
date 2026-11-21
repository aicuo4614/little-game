using System.Collections.Generic;
using UnityEngine;

namespace LittleGame
{
    // 所有部件在机身局部空间建模，脱离动作同样沿局部坐标执行。
    public sealed class MachineModel : MonoBehaviour
    {
        public float BoundingRadius { get; private set; }
        public readonly Dictionary<string, Transform> Parts = new Dictionary<string, Transform>();
        public readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
        private readonly Dictionary<string, Material> palette = new Dictionary<string, Material>();
        private readonly List<Mesh> meshes = new List<Mesh>();

        public Bounds AssemblyBounds { get;private set; }
        public void Build(int level=1)
        {
            if(level==2) BuildComputer();else BuildHandheld();
            GroupChassis();FinishBuild();
        }
        private void BuildHandheld()
        {
            Part("body", new Vector3(0,-.25f,0), new Vector3(4.4f,.7f,5.6f), "#CAD1D2");
            BuildCaseRim();
            Part("tray", new Vector3(0,.12f,0), new Vector3(4.05f,.12f,5.2f), "#1B2734");
            Part("cover", new Vector3(0,.65f,0), new Vector3(4.25f,.28f,5.4f), "#69818C", true);
            var ribs = Part("coverPanel",new Vector3(0,.81f,-.65f),new Vector3(3,.035f,2.7f),"#51657D");
            ribs.SetParent(Parts["cover"],true);
            for(int i=0;i<6;i++)
            {
                var vent=Part("vent"+i,new Vector3(-.55f+i*.22f,.82f,1.2f),new Vector3(.08f,.025f,.65f),"#172432");
                vent.SetParent(Parts["cover"],true);
            }
            Screw("screw_a",new Vector3(-1.65f,.85f,-1.95f));
            Screw("screw_b",new Vector3(1.65f,.85f,1.95f));
            Part("board",new Vector3(-.65f,.3f,0),new Vector3(2.35f,.16f,4.35f),"#247B65",true);
            Attach("chip","board",new Vector3(-.65f,.46f,.5f),new Vector3(.95f,.18f,1.05f),"#182332");
            for(int i=0;i<7;i++)
            {
                Attach("pin"+i,"board",new Vector3(-1.06f+i*.14f,.44f,1.1f),new Vector3(.06f,.07f,.22f),"#BECBC6");
                Attach("contact"+i,"board",new Vector3(-1.5f+i*.25f,.4f,-1.86f),new Vector3(.14f,.04f,.38f),"#E5BE69");
            }
            for(int i=0;i<4;i++)
                Attach("trace"+i,"board",new Vector3(-1.48f+i*.15f,.4f,.2f),new Vector3(.025f,.02f,1.6f),"#8ACDB1");
            Part("battery",new Vector3(1.1f,.4f,.1f),new Vector3(1.15f,.38f,3.4f),"#33475F",true);
            Attach("batteryStripe","battery",new Vector3(1.1f,.61f,.1f),new Vector3(.8f,.025f,2.1f),"#4A617C");
            Attach("batteryLip","battery",new Vector3(.45f,.4f,-.8f),new Vector3(.7f,.14f,.3f),"#D2DBDD");
            Part("socket",new Vector3(.25f,.49f,1.6f),new Vector3(.75f,.1f,.4f),"#D6DFDF");
            Part("cable",new Vector3(.25f,.6f,1.6f),new Vector3(.65f,.12f,.3f),"#EA9166",true);
            Part("ribbon",new Vector3(.65f,.55f,1.6f),new Vector3(.7f,.06f,.27f),"#D7A064");
            Screw("board_screw",new Vector3(-1.4f,.48f,-1.3f));
            // 机身正面位于内部结构的另一侧。
            Part("frontBezel",new Vector3(0,-.65f,.9f),new Vector3(3.7f,.12f,2.9f),"#263446");
            Part("frontScreen",new Vector3(0,-.73f,.9f),new Vector3(3.15f,.055f,2.3f),"#284751");
            Part("dpadH",new Vector3(1.05f,-.73f,-1.3f),new Vector3(1,.18f,.32f),"#1B293A");
            Part("dpadV",new Vector3(1.05f,-.73f,-1.3f),new Vector3(.32f,.18f,1),"#1B293A");
            Part("buttonA",new Vector3(-1.25f,-.73f,-1.1f),new Vector3(.46f,.15f,.46f),"#D88765",false,PrimitiveType.Cylinder);
            Part("buttonB",new Vector3(-.7f,-.73f,-1.65f),new Vector3(.46f,.15f,.46f),"#568C9D",false,PrimitiveType.Cylinder);
            Part("startKey",new Vector3(0,-.73f,-2.25f),new Vector3(.65f,.1f,.13f),"#33485A");
            Part("usb",new Vector3(0,-.24f,-2.82f),new Vector3(.7f,.2f,.07f),"#111E2B");
            Part("power",new Vector3(2.23f,-.25f,1.4f),new Vector3(.14f,.28f,.6f),"#D59765");
            for(int i=0;i<4;i++)
                Part("speaker"+i,new Vector3(.6f+i*.18f,-.64f,-2.25f),new Vector3(.045f,.04f,.5f),"#1B293A");
        }
        private void GroupChassis()
        {
            var root=new GameObject("chassis");root.transform.SetParent(transform,false);root.AddComponent<MachinePart>().Id="chassis";
            var children=new List<Transform>();foreach(Transform child in transform)children.Add(child);
            foreach(var child in children) if(child!=root.transform && child.GetComponent<MachinePart>()==null)child.SetParent(root.transform,true);
            Parts["chassis"]=root.transform;Materials["chassis"]=Materials["body"];
        }
        private void FinishBuild()
        {
            var renderers = GetComponentsInChildren<Renderer>();
            var bounds = renderers[0].bounds;
            foreach(var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            BoundingRadius = bounds.extents.magnitude;
            AssemblyBounds=new Bounds(Vector3.zero,bounds.size);
            // 完整装配体的几何中心作为旋转根节点，避免正反面厚度带来的偏心。
            foreach(Transform child in transform) child.localPosition -= bounds.center;
            CombineDetails();

        }

        private void BuildComputer()
        {
            Part("body",new Vector3(0,-1.3f,0),new Vector3(4.4f,.16f,6.4f),"#34424C");
            Part("caseLeft",new Vector3(-2.15f,0,0),new Vector3(.18f,2.6f,6.4f),"#34424C");
            Part("caseRight",new Vector3(2.15f,0,0),new Vector3(.18f,2.6f,6.4f),"#34424C");
            Part("caseTop",new Vector3(0,0,3.1f),new Vector3(4.4f,2.6f,.18f),"#34424C");
            Part("caseBottom",new Vector3(0,0,-3.1f),new Vector3(4.4f,2.6f,.18f),"#34424C");
            Part("tray",new Vector3(0,.03f,0),new Vector3(4.05f,.12f,5.2f),"#1B2734");
            Part("cover",new Vector3(0,1.4f,0),new Vector3(4.15f,.18f,6.15f),"#607889",true);
            for(int i=0;i<7;i++) Attach("caseVent"+i,"cover",new Vector3(-1.05f+i*.35f,1.51f,.7f),new Vector3(.16f,.025f,2.5f),"#263A45");
            Screw("screw_a",new Vector3(-1.7f,1.58f,2.65f));
            Screw("screw_b",new Vector3(1.7f,1.58f,2.65f));
            Screw("case_screw_c",new Vector3(-1.7f,1.58f,-2.65f));
            Part("board",new Vector3(-.1f,.19f,.55f),new Vector3(3.6f,.16f,4),"#276D70",true);
            for(int i=0;i<7;i++) Attach("boardTrace"+i,"board",new Vector3(-1.45f+i*.43f,.29f,.5f),new Vector3(.035f,.025f,3.1f),"#7FC1B4");
            Attach("cpuSocket","board",new Vector3(-.6f,.33f,1.1f),new Vector3(1.2f,.16f,1.2f),"#B8C5BC");
            Attach("cpu","board",new Vector3(-.6f,.44f,1.1f),new Vector3(.7f,.08f,.7f),"#718A8E");
            for(int i=0;i<4;i++) Attach("capacitor"+i,"board",new Vector3(-1.45f,.4f,.3f+i*.35f),new Vector3(.18f,.22f,.18f),"#C4C9C1");
            Part("power_unit",new Vector3(0,.5f,-1.95f),new Vector3(3.65f,.72f,1.05f),"#384658",true);
            for(int i=0;i<7;i++) Attach("powerGrill"+i,"power_unit",new Vector3(-1.25f+i*.4f,.88f,-1.95f),new Vector3(.14f,.025f,.72f),"#8B9AA0");
            Part("cable",new Vector3(1.35f,.66f,-.95f),new Vector3(.6f,.27f,.4f),"#DDA056",true);
            Part("ribbon",new Vector3(.85f,.58f,-1.15f),new Vector3(1,.12f,.2f),"#C18445");
            Part("gpu",new Vector3(-.05f,.65f,-.4f),new Vector3(2.85f,.5f,.8f),"#416A5B",true);
            Attach("gpuFanA","gpu",new Vector3(-.65f,.96f,-.4f),new Vector3(.6f,.09f,.6f),"#1C3038");
            Attach("gpuFanB","gpu",new Vector3(.5f,.96f,-.4f),new Vector3(.6f,.09f,.6f),"#1C3038");
            for(int i=0;i<5;i++) Attach("gpuPin"+i,"gpu",new Vector3(-.6f+i*.25f,.52f,.06f),new Vector3(.14f,.09f,.2f),"#D9B66C");
            Screw("gpu_screw",new Vector3(-1.76f,.78f,-.4f));
            Part("ram",new Vector3(1.08f,.55f,1.35f),new Vector3(.38f,.4f,1.55f),"#396C45",true);
            for(int i=0;i<3;i++) Attach("ramChip"+i,"ram",new Vector3(1.08f,.78f,.87f+i*.47f),new Vector3(.28f,.07f,.3f),"#1B3033");
            Part("ram_clip",new Vector3(1.08f,.7f,2.35f),new Vector3(.6f,.23f,.27f),"#E3BD75",true);
            Part("cooler",new Vector3(-.6f,.78f,1.1f),new Vector3(1.35f,.62f,1.35f),"#9EAEB2",true);
            Attach("fanOuter","cooler",new Vector3(-.6f,1.13f,1.1f),new Vector3(1.05f,.08f,1.05f),"#243946");
            Attach("fanCenter","cooler",new Vector3(-.6f,1.19f,1.1f),new Vector3(.32f,.08f,.32f),"#91BFC3");
            for(int i=0;i<4;i++) Attach("fanBlade"+i,"cooler",new Vector3(-.94f+i*.22f,1.18f,1.1f),new Vector3(.09f,.035f,.7f),"#536F7C");
            Screw("cooler_screw",new Vector3(-1.18f,1.23f,1.67f));
            Screw("board_screw",new Vector3(-1.65f,.4f,2.1f));
            // 方正主机比例、独立前置双风扇面板、I/O接口和脚垫，与掌机壳体区分。
            Part("frontPanel",new Vector3(0,-1.42f,0),new Vector3(4.05f,.08f,6.1f),"#202D38");
            Part("backInset",new Vector3(0,-1.48f,.3f),new Vector3(3.6f,.035f,4.8f),"#344651");
            for(int i=0;i<7;i++)Part("backVent"+i,new Vector3(-1.2f+i*.4f,-1.52f,-1.9f),new Vector3(.13f,.035f,.65f),"#17232B");
            Part("towerFascia",new Vector3(2.28f,0,0),new Vector3(.16f,2.55f,6.15f),"#17232B");
            foreach(float z in new[]{-1.05f,1.05f})
            {
                var fan=Part("intake"+z,new Vector3(2.4f,0,z),new Vector3(1.85f,.09f,1.85f),"#77AFBC",false,PrimitiveType.Cylinder);fan.localRotation=Quaternion.Euler(0,0,90);
                var hub=Part("intakeHub"+z,new Vector3(2.47f,0,z),new Vector3(1.5f,.04f,1.5f),"#243946",false,PrimitiveType.Cylinder);hub.localRotation=Quaternion.Euler(0,0,90);
                Part("fanCrossA"+z,new Vector3(2.51f,0,z),new Vector3(.035f,1.18f,.15f),"#557E8C");
                Part("fanCrossB"+z,new Vector3(2.51f,0,z),new Vector3(.035f,.15f,1.18f),"#557E8C");
            }
            var power=Part("powerButton",new Vector3(2.43f,.58f,2.65f),new Vector3(.3f,.06f,.3f),"#A7D5CE",false,PrimitiveType.Cylinder);power.localRotation=Quaternion.Euler(0,0,90);
            for(int i=0;i<2;i++)Part("frontUsb"+i,new Vector3(2.43f,-.3f-i*.45f,2.65f),new Vector3(.04f,.28f,.13f),"#90A5AC");
            foreach(float x in new[]{-1.55f,1.55f})foreach(float y in new[]{-.9f,.9f})Part("caseFoot"+x+y,new Vector3(x,y,-3.35f),new Vector3(.62f,.62f,.3f),"#14222A");
            for(int i=0;i<4;i++)Part("rearIo"+i,new Vector3(-2.26f,-.3f,1.7f-i*.45f),new Vector3(.06f,1.1f,.2f),"#A2AFB5");

        }

        private void BuildCaseRim()
        {
            var rim = Part("bodyRim",new Vector3(0,.33f,0),new Vector3(4.4f,.48f,5.6f),"#CAD1D2");
            var mesh = DeviceMesh.RoundedFrame();
            meshes.Add(mesh);
            rim.GetComponent<MeshFilter>().sharedMesh = mesh;
            Object.Destroy(rim.GetComponent<Collider>());
            // 碰撞体只覆盖四边，打开后能真实命中内部部件。
            var left = rim.gameObject.AddComponent<BoxCollider>();
            left.center = new Vector3(-.475f,0,0);left.size = new Vector3(.05f,1,.72f);
            var right = rim.gameObject.AddComponent<BoxCollider>();
            right.center = new Vector3(.475f,0,0);right.size = new Vector3(.05f,1,.72f);
            var top = rim.gameObject.AddComponent<BoxCollider>();
            top.center = new Vector3(0,0,.475f);top.size = new Vector3(.72f,1,.05f);
            var bottom = rim.gameObject.AddComponent<BoxCollider>();
            bottom.center = new Vector3(0,0,-.475f);bottom.size = new Vector3(.72f,1,.05f);
        }

        private void Attach(string id,string parent,Vector3 position,Vector3 scale,string color)
        { Part(id,position,scale,color).SetParent(Parts[parent],true); }

        private void Screw(string id,Vector3 position)
        {
            Part(id,position,new Vector3(.35f,.1f,.35f),"#C6D5DF",true,PrimitiveType.Cylinder);
            var slot=Part(id+"_slot",position+Vector3.up*.11f,new Vector3(.24f,.025f,.06f),"#24384A");
            slot.SetParent(Parts[id],true);
        }

        private Transform Part(string id, Vector3 position, Vector3 scale, string color,
            bool interactive=false,PrimitiveType type=PrimitiveType.Cube)
        {
            var go=GameObject.CreatePrimitive(type);
            go.name=id; go.transform.SetParent(transform,false);
            go.transform.localPosition=position;go.transform.localScale=scale;
            if(type==PrimitiveType.Cube)
            {
                var mesh=DeviceMesh.RoundedBox(id=="body" || id=="cover" ? .14f : .055f);
                go.GetComponent<MeshFilter>().sharedMesh=mesh;meshes.Add(mesh);
            }
            bool metal=id.Contains("screw") || id.StartsWith("pin") || id.StartsWith("contact");
            string key=color+(metal?"metal":"surface");
            Material material;
            if(interactive || !palette.TryGetValue(key,out material))
            {
                material=new Material(Resources.Load<Material>("DeviceMaterial"));
                ColorUtility.TryParseHtmlString(color,out var value);material.color=value;
                float smoothness=id=="frontScreen"?.4f:metal?.5f:.28f;
                material.SetFloat("_Smoothness",smoothness);
                material.SetFloat("_Metallic",metal?.8f:id=="frontScreen"?.05f:0);
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor",Color.black);
                if(!interactive) palette[key]=material;
            }
            go.GetComponent<Renderer>().sharedMaterial=material;
            if(interactive) go.AddComponent<MachinePart>().Id=id;
            // 非交互模型保留碰撞体，用于阻止点击穿透外壳。
            Parts[id]=go.transform;Materials[id]=material;
            return go.transform;
        }

        private void CombineDetails()
        {
            // 相同零件下、相同材质的细节合并，保持原碰撞体与拾取关系。
            var groups=new Dictionary<Transform,Dictionary<Material,List<MeshFilter>>>();
            foreach(var filter in GetComponentsInChildren<MeshFilter>())
            {
                var marker=filter.GetComponentInParent<MachinePart>();
                Transform owner=marker!=null?marker.transform:transform;
                if(filter.transform==owner) continue;
                var material=filter.GetComponent<Renderer>().sharedMaterial;
                if(!groups.TryGetValue(owner,out var byMaterial))
                    groups[owner]=byMaterial=new Dictionary<Material,List<MeshFilter>>();
                if(!byMaterial.TryGetValue(material,out var list))
                    byMaterial[material]=list=new List<MeshFilter>();
                list.Add(filter);
            }
            foreach(var owner in groups)
            foreach(var group in owner.Value)
            {
                if(group.Value.Count<2) continue;
                var instances=new CombineInstance[group.Value.Count];
                for(int i=0;i<instances.Length;i++)
                {
                    var filter=group.Value[i];
                    instances[i]=new CombineInstance
                    {
                        mesh=filter.sharedMesh,
                        transform=owner.Key.worldToLocalMatrix*filter.transform.localToWorldMatrix
                    };
                    filter.GetComponent<Renderer>().enabled=false;
                }
                var mesh=new Mesh { name="CombinedDeviceDetails" };
                mesh.CombineMeshes(instances,true,true);
                meshes.Add(mesh);
                var combined=new GameObject("CombinedDetails",typeof(MeshFilter),typeof(MeshRenderer));
                combined.transform.SetParent(owner.Key,false);
                combined.GetComponent<MeshFilter>().sharedMesh=mesh;
                combined.GetComponent<MeshRenderer>().sharedMaterial=group.Key;
            }
        }

        private void OnDestroy()
        {
            foreach(var material in new HashSet<Material>(Materials.Values)) Destroy(material);
            foreach(var mesh in meshes) Destroy(mesh);
        }
    }
}
