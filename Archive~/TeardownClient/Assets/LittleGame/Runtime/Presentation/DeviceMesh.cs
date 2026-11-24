using System.Collections.Generic;
using UnityEngine;

namespace LittleGame
{
    // 圆角壳体带两圈倒角，供原型设备外壳与内部零件共用。
    public static class DeviceMesh
    {
        public static Mesh RoundedFrame()
        {
            const int steps = 12;
            int count = 4 * (steps + 1);
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int ring = 0; ring < 4; ring++)
            {
                bool inner = ring >= 2;
                float factor = inner ? .9f : 1;
                float y = ring == 0 || ring == 3 ? -.5f : .5f;
                for (int corner = 0; corner < 4; corner++)
                {
                    float cx = corner == 0 || corner == 3 ? .36f : -.36f;
                    float cz = corner < 2 ? .36f : -.36f;
                    for (int step = 0; step <= steps; step++)
                    {
                        float angle = (corner * 90 + step * 90f / steps) * Mathf.Deg2Rad;
                        vertices.Add(new Vector3((cx + Mathf.Cos(angle) * .14f) * factor,
                            y, (cz + Mathf.Sin(angle) * .14f) * factor));
                    }
                }
            }
            for (int ring = 0; ring < 4; ring++)
            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                int a = ring * count + i;
                int b = ring * count + next;
                int c = ((ring + 1) % 4) * count + i;
                int d = ((ring + 1) % 4) * count + next;
                triangles.Add(a);triangles.Add(c);triangles.Add(b);
                triangles.Add(b);triangles.Add(c);triangles.Add(d);
            }
            // 每个面带独立法线，顶面保持平整，避免与侧壁平均后形成充气般的光照。
            var flatVertices = new List<Vector3>();
            var flatTriangles = new List<int>();
            for(int i=0;i<triangles.Count;i++)
            {
                flatTriangles.Add(i);
                flatVertices.Add(vertices[triangles[i]]);
            }
            var mesh = new Mesh { name = "HollowCaseRim" };
            mesh.SetVertices(flatVertices);mesh.SetTriangles(flatTriangles, 0);
            mesh.RecalculateNormals();mesh.RecalculateBounds();
            return mesh;
        }

        public static Mesh RoundedBox(float radius)
        {
            const int steps = 8;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            float[] heights = { -.5f, -.38f, .38f, .5f };
            float[] insets = { .035f, 0, 0, .035f };
            int count = 4 * (steps + 1);
            for (int ring = 0; ring < 4; ring++)
            {
                float edge = .5f - insets[ring];
                float r = Mathf.Min(radius, edge);
                for (int corner = 0; corner < 4; corner++)
                {
                    float angleStart = corner * 90;
                    float cx = corner == 0 || corner == 3 ? edge-r : -edge+r;
                    float cz = corner < 2 ? edge-r : -edge+r;
                    for (int step = 0; step <= steps; step++)
                    {
                        float angle = (angleStart + step*90f/steps)*Mathf.Deg2Rad;
                        vertices.Add(new Vector3(cx+Mathf.Cos(angle)*r,heights[ring],cz+Mathf.Sin(angle)*r));
                    }
                }
            }
            for (int ring = 0; ring < 3; ring++)
                for (int i = 0; i < count; i++)
                {
                    int a=ring*count+i, b=ring*count+(i+1)%count;
                    triangles.Add(a); triangles.Add(b+count); triangles.Add(b);
                    triangles.Add(a); triangles.Add(a+count); triangles.Add(b+count);
                }
            // 上下平面独立顶点，避免圆角侧面的法线污染整个平面。
            int bottomRing=vertices.Count;
            for(int i=0;i<count;i++) vertices.Add(vertices[i]);
            int topRing=vertices.Count;
            for(int i=0;i<count;i++) vertices.Add(vertices[3*count+i]);
            int bottom=vertices.Count;
            vertices.Add(new Vector3(0,-.5f,0));
            int top=vertices.Count;
            vertices.Add(new Vector3(0,.5f,0));
            for(int i=0;i<count;i++)
            {
                triangles.Add(bottom);triangles.Add(bottomRing+i);triangles.Add(bottomRing+(i+1)%count);
                triangles.Add(top);triangles.Add(topRing+(i+1)%count);triangles.Add(topRing+i);
            }
            var uv=new List<Vector2>(vertices.Count);
            foreach(var vertex in vertices) uv.Add(new Vector2(vertex.x+.5f,vertex.z+.5f));
            var mesh=new Mesh { name="RoundedDevicePart" };
            mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
            return mesh;
        }
    }
}
