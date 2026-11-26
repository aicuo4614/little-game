using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace LittleGame.Client.Editor
{
    /// <summary>给移植特效补充竖屏下清晰可读的主形状；使用网格粒子，共享播放器直接采样。</summary>
    internal static class SkillEffectAccentBuilder
    {
        internal static void Add(GameObject root, GameObject source, string id, Shader shader)
        {
            bool lightning = id == "fx_lightning_strike" || id == "fx_hit_lightning" || id == "fx_storm_tick";
            bool freeze = id == "fx_freeze_ring" || id == "fx_hit_ice";
            bool focus = id == "fx_tactic_focus";
            if (!lightning && !freeze && !focus) return;
            source.SetActive(false);
            string shapeName = lightning ? "LightningAccent" : freeze ? "FreezeAccent" : "FocusAccent";
            string meshPath = "Assets/LittleGame/Art/SkillEffectsGenerated/" + shapeName + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (!mesh)
            {
                var verts = new List<Vector3>(); var tris = new List<int>();
                void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
                {
                    int start = verts.Count; verts.AddRange(new[] { a, b, c, d });
                    tris.AddRange(new[] { start, start+1, start+2, start, start+2, start+3 });
                }
                if (lightning)
                {
                    var points = new[] { new Vector3(0f, 1.8f, 0f), new Vector3(.16f,1.25f,0f),
                        new Vector3(-.12f,.85f,0f), new Vector3(.13f,.45f,0f), Vector3.zero };
                    for (int i=0; i<points.Length-1; i++)
                    {
                        var a=points[i]; var b=points[i+1];
                        Quad(a+Vector3.right*.035f,b+Vector3.right*.035f,b-Vector3.right*.035f,a-Vector3.right*.035f);
                        Quad(a+Vector3.forward*.035f,b+Vector3.forward*.035f,b-Vector3.forward*.035f,a-Vector3.forward*.035f);
                    }
                }
                else if (freeze)
                {
                    for (int i=0;i<6;i++)
                    {
                        float angle=i*Mathf.PI/3;
                        var center=new Vector3(Mathf.Sin(angle)*.65f,0f,Mathf.Cos(angle)*.65f);
                        var peak=center+Vector3.up*(i%2==0 ? 1.0f : .7f);
                        var right=new Vector3(.16f,0,0); var forward=new Vector3(0,0,.16f);
                        Quad(center-right,center+forward,peak,peak);
                        Quad(center+forward,center+right,peak,peak);
                        Quad(center+right,center-forward,peak,peak);
                        Quad(center-forward,center-right,peak,peak);
                    }
                }
                else
                {
                    for(int i=0;i<4;i++)
                    {
                        float angle=i*Mathf.PI*.5f;
                        var r=new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle));
                        var side=new Vector3(r.z,0,-r.x)*.055f;
                        Quad(r*.45f+side,r*.7f+side,r*.7f-side,r*.45f-side);
                    }
                }
                mesh=new Mesh{name=shapeName}; mesh.SetVertices(verts); mesh.SetTriangles(tris,0);
                mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,meshPath);
            }
            const string matPath="Assets/LittleGame/Art/SkillEffectsGenerated/Materials/ReadableAccent.mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if(!material)
            {
                material=new Material(shader){name="ReadableAccent",renderQueue=3000};
                material.SetFloat("_Surface",1);material.SetFloat("_Blend",2);
                material.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);material.SetFloat("_DstBlend",(float)BlendMode.One);
                material.SetFloat("_ZWrite",0);material.SetFloat("_Cull",(float)CullMode.Off);
                material.SetColor("_BaseColor",Color.white);material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                AssetDatabase.CreateAsset(material,matPath);
            }
            var go=new GameObject(shapeName);go.transform.SetParent(root.transform,false);
            go.transform.localPosition=Vector3.up*.03f;
            var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main;main.loop=false;main.duration=freeze ? .4f : focus ? .65f : .18f;
            main.startLifetime=main.duration;main.startSpeed=0;main.startSize=1;main.maxParticles=1;
            main.scalingMode=ParticleSystemScalingMode.Hierarchy;
            main.startColor=lightning?new Color(.75f,.85f,1.8f,1):freeze?new Color(.5f,1.2f,1.8f,.85f):new Color(1.8f,.8f,.25f,1);
            var shape=ps.shape;shape.enabled=false;
            var emission=ps.emission;emission.rateOverTime=0;emission.SetBursts(new[]{new ParticleSystem.Burst(0,1)});
            var fade=ps.colorOverLifetime;fade.enabled=true;var gradient=new Gradient();
            gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
                new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(.8f,.5f),new GradientAlphaKey(0,1)});
            fade.color=gradient;
            var renderer=go.GetComponent<ParticleSystemRenderer>();renderer.renderMode=ParticleSystemRenderMode.Mesh;
            renderer.alignment=ParticleSystemRenderSpace.Local;renderer.mesh=mesh;renderer.sharedMaterial=material;
        }
    }
}
