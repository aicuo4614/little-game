using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace LittleGame.Client
{
    /// <summary>场景只负责视觉，隔栏位置读取规则状态，不参与碰撞、射程和胜负。</summary>
    public sealed class CourtyardEnvironment : MonoBehaviour
    {
        private readonly List<Transform> dividers = new List<Transform>();
        private readonly List<Material> materials = new List<Material>();
        private readonly List<Transform> runes = new List<Transform>();
        private Material stone, pale, dark, earth, moss, brass, blue;
        private bool fencesUp = true;
        private float transition = 1;
        private int modelInstances, missingModels;
        public void Build()
        {
            stone = Material("Weathered limestone", new Color(.42f,.48f,.49f));
            pale = Material("Carved capstone", new Color(.73f,.72f,.67f));
            dark = Material("Recessed stone", new Color(.22f,.29f,.31f));
            earth = Material("Island foundation", new Color(.23f,.29f,.25f));
            moss = Material("Muted vegetation", new Color(.25f,.4f,.27f));
            brass = Material("Gate trim", new Color(.6f,.43f,.23f));
            blue = Material("Rune inlay", new Color(.25f,.55f,.61f));
            var floorA = Material("Paving ash", new Color(.53f,.56f,.57f));
            var floorB = Material("Paving variation", new Color(.50f,.53f,.55f));
            Cube("Floating foundation",new Vector3(0,-.55f,.1f),new Vector3(10.6f,.95f,12.1f),earth);
            Cube("Foundation rim",new Vector3(0,-.12f,.1f),new Vector3(10.3f,.16f,11.8f),dark);
            // 大块静态地砖使用来源模型，地面接缝保持低对比，角色和技能更易读。
            for(int x=0;x<8;x++) for(int z=0;z<10;z++)
                Place("brick_"+(1+(x+z)%4),new Vector3((x-3.5f)*1.04f,0,(z-4.5f)*1.04f+.1f),new Vector3(1.035f,.10f,1.035f),(x+z)%3==0?floorA:floorB,true);
            for(int side=-1;side<=1;side+=2)
            {
                for(int i=0;i<5;i++)
                {
                    Place("pillar_narrow_1",new Vector3(side*4.5f,0,-3.8f+i*2),new Vector3(.7f,i==4?1.8f:1.15f,.7f),pale);
                    if(i<4) Place("wall_element_tileable_4meters_top",new Vector3(side*4.5f,0,-2.8f+i*2),new Vector3(.32f,.55f,1.65f),stone,true);
                    Place("Rock_1",new Vector3(side*(4.2f+(i%2)*.35f),.04f,-3.1f+i*1.8f),new Vector3(.55f,.38f,.6f),dark);
                }
                for(int i=0;i<8;i++) Place("nw_Grass",new Vector3(side*(4.0f+(i%2)*.45f),.06f,-3.8f+i*1.1f),new Vector3(.42f,.35f,.45f),moss);
                Place("pillar",new Vector3(side*3.7f,0,4.7f),new Vector3(.9f,2.1f,.9f),pale);
                Place("Crystal_2",new Vector3(side*3.7f,2.15f,4.7f),new Vector3(.33f,.6f,.33f),blue);
                Place("Tree_04",new Vector3(side*5.0f,-.08f,2.5f),new Vector3(1.3f,1.7f,1.3f),moss);
                Place("pillar_short_slim_1",new Vector3(side*4.2f,0,-4.8f),new Vector3(.6f,.8f,.6f),pale);
            }
            // 敌方拱门作为远端视觉焦点，不给战斗规则增加实体障碍。
            Place("archway",new Vector3(0,-.02f,5.05f),new Vector3(5.6f,2.7f,.85f),pale,true);
            Cube("Dark gateway",new Vector3(0,1.1f,5.36f),new Vector3(3.4f,2.2f,.1f),dark);
            for(int lane=0;lane<3;lane++)
            {
                float x=(lane-1)*2.5f;
                Cube("Gate threshold",new Vector3(x,.08f,-4.46f),new Vector3(2.35f,.16f,.65f),pale);
                Cube("Gate brass inlay",new Vector3(x,.17f,-4.46f),new Vector3(1.9f,.015f,.10f),brass);
                for(int j=-1;j<=1;j++) Place("brick_3",new Vector3(x+j*.72f,.1f,-4.92f),new Vector3(.67f,.35f,.3f),stone,true);
                for(int row=0;row<3;row++)
                {
                    float z=Combat.BattleDefinitions.RowZ(row);
                    foreach(float dx in new[]{-.78f,.78f}) Cube("Formation corner",new Vector3(x+dx,.112f,z),new Vector3(.08f,.012f,.5f),blue);
                }
            }
            for(int side=-1;side<=1;side+=2)
                for(int i=0;i<10;i++)
                {
                    var root=new GameObject("Retractable divider").transform;root.SetParent(transform,false);root.localPosition=new Vector3(side*1.25f,0,-3.7f+i*.89f);
                    var block=Place("brick_2",Vector3.zero,new Vector3(.20f,.28f,.76f),pale,true);block.SetParent(root,false);
                    dividers.Add(root);
                    var rune=Cube("Divider socket",new Vector3(side*1.25f,.105f,-3.7f+i*.89f),new Vector3(.29f,.015f,.82f),dark);runes.Add(rune.transform);
                }
            // 地面装饰属于静态层。批处理只合并环境，不合并动画隔栏和角色。
            var staticRoot=new GameObject("Static courtyard");staticRoot.transform.SetParent(transform,false);
            var dynamicRoots=new HashSet<Transform>(dividers);
            foreach(Transform child in transform.Cast<Transform>().ToArray()) if(child!=staticRoot.transform&&!dynamicRoots.Contains(child))child.SetParent(staticRoot.transform,true);
            StaticBatchingUtility.Combine(staticRoot);
        }
        public void Apply(bool raised,float dt)
        {
            fencesUp=raised;transition=Mathf.MoveTowards(transition,raised?1:0,dt*1.7f);
            foreach(var t in dividers){var p=t.localPosition;p.y=Mathf.Lerp(-.43f,0,transition);t.localPosition=p;}
        }
        public object Snapshot()=>new {theme="ruined-courtyard",modelInstances,missingModels,dividerSegments=dividers.Count,fencesUp,transition,shader=stone.shader.name,shaderSupported=stone.shader.isSupported,renderers=GetComponentsInChildren<Renderer>().Length,staticBatchedRenderers=GetComponentsInChildren<Renderer>().Count(r=>r.isPartOfStaticBatch),colliders=GetComponentsInChildren<Collider>().Length,missingMaterials=GetComponentsInChildren<Renderer>().Count(r=>r.sharedMaterials.Any(m=>m==null||m.shader==null||!m.shader.isSupported)),note="环境只读规则，不添加玩法碰撞；静态层合批，隔栏独立动画"};
        private Material Material(string name,Color color)
        {
            var shader=Resources.Load<Shader>("CourtyardSceneShader");
            if(shader==null)shader=Shader.Find("Scene/Simple");
            if(shader==null||!shader.isSupported)throw new InvalidOperationException("Demo Scene/Simple Shader未就绪");
            var mat=new Material(shader){name=name};mat.SetColor("_Color",color);mat.enableInstancing=true;materials.Add(mat);return mat;
        }
        private Transform Place(string asset,Vector3 position,Vector3 size,Material material,bool stretch=false)
        {
            var prefab=Resources.Load<GameObject>("Courtyard/"+asset);
            if(prefab==null){missingModels++;throw new InvalidOperationException("场景模型缺失："+asset);}
            var go=Instantiate(prefab,transform);go.name=asset;var t=go.transform;t.localPosition=Vector3.zero;t.localRotation=Quaternion.identity;
            var renderers=go.GetComponentsInChildren<Renderer>();var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
            var scale=new Vector3(size.x/Mathf.Max(.001f,bounds.size.x),size.y/Mathf.Max(.001f,bounds.size.y),size.z/Mathf.Max(.001f,bounds.size.z));
            if(!stretch)scale=Vector3.one*Mathf.Min(scale.x,scale.y,scale.z);
            t.localScale=Vector3.Scale(t.localScale,scale);t.localPosition=position-new Vector3(bounds.center.x*scale.x,bounds.min.y*scale.y,bounds.center.z*scale.z);
            foreach(var r in renderers){r.sharedMaterials=Enumerable.Repeat(material,r.sharedMaterials.Length).ToArray();r.shadowCastingMode=ShadowCastingMode.On;r.receiveShadows=true;}
            foreach(var c in go.GetComponentsInChildren<Collider>())Destroy(c);
            modelInstances++;return t;
        }
        private GameObject Cube(string name,Vector3 position,Vector3 size,Material material)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(transform,false);go.transform.localPosition=position;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=material;Destroy(go.GetComponent<Collider>());return go;
        }
        private void OnDestroy(){foreach(var m in materials)Destroy(m);}
    }
}
