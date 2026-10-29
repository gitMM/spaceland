using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace DeathStar
{
    // Explicit sources shared by tile meshes, instanced props and other station objects.
    // Geometry extraction is cached per final mesh (after bending), never per frame.
    public static class StationLocalLights
    {
        public struct Lamp { public Vector3 a,b; public Color color; public float radius; public Vector4 plane; }
        public struct LightData { public Vector4 a,b,color,plane; }
        public static event Action<Camera,List<LightData>> Collect;
        public static int VisibleCount {get;internal set;}
        sealed class Cache { public readonly Dictionary<int,Lamp[]> slots=new Dictionary<int,Lamp[]>(); }
        static readonly ConditionalWeakTable<Mesh,Cache> cache=new ConditionalWeakTable<Mesh,Cache>();
        // Sparse vertex metadata, like TileMesh.KitSlots. CurvedTileMesh transforms these
        // normals alongside the geometry; each connected lens then gets its own plane.
        internal static readonly ConditionalWeakTable<Mesh,Dictionary<int,Vector3>> MountingNormals=new ConditionalWeakTable<Mesh,Dictionary<int,Vector3>>();
        public static bool VisibleTo(Camera camera,GameObject owner)
        {
            if(!owner||!owner.activeInHierarchy||(camera.cullingMask&(1<<owner.layer))==0)return false;
            if(camera.scene.IsValid()&&camera.scene!=owner.scene)return false;
            #if UNITY_EDITOR
            if(UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(owner.scene)&&camera.scene!=owner.scene)return false;
            #endif
            return true;
        }
        public static void Gather(Camera camera,List<LightData> lights) => Collect?.Invoke(camera,lights);
        // Call after modifying an existing mesh in place; replacing a mesh needs no invalidation.
        public static void Invalidate(Mesh mesh){if(mesh)cache.Remove(mesh);}

        public static void Append(List<LightData> output,Mesh mesh,int slot,Matrix4x4 matrix,Color emission,
            float strength=1,Vector4 plane=default)
        {
            if(!mesh||strength<=0)return;
            float scale=Mathf.Max(matrix.GetColumn(0).magnitude,Mathf.Max(matrix.GetColumn(1).magnitude,matrix.GetColumn(2).magnitude));
            foreach(var lamp in Extract(mesh,slot)) {
                Vector3 a=matrix.MultiplyPoint3x4(lamp.a),b=matrix.MultiplyPoint3x4(lamp.b);
                Color c=lamp.color*emission;
                var mount=plane;
                if(((Vector3)mount).sqrMagnitude<.5f&&((Vector3)lamp.plane).sqrMagnitude>.5f) {
                    mount=matrix.inverse.transpose*lamp.plane;
                    mount/=Mathf.Max(1e-8f,((Vector3)mount).magnitude);
                }
                output.Add(new LightData{a=new Vector4(a.x,a.y,a.z,lamp.radius*scale),
                    b=new Vector4(b.x,b.y,b.z,strength),color=new Vector4(c.r,c.g,c.b,0),plane=mount});
            }
        }

        public static Lamp[] Extract(Mesh mesh,int slot)
        {
            if(!mesh||!mesh.isReadable||slot<0||slot>=mesh.subMeshCount||mesh.GetIndexCount(slot)==0)return Array.Empty<Lamp>();
            var entry=cache.GetValue(mesh,_=>new Cache());
            if(entry.slots.TryGetValue(slot,out var cached))return cached;
            var vertices=mesh.vertices;var colors=mesh.colors;var triangles=mesh.GetTriangles(slot);
            var parent=new int[triangles.Length];for(int i=0;i<parent.Length;i++)parent[i]=i;
            int Root(int i){while(parent[i]!=i){parent[i]=parent[parent[i]];i=parent[i];}return i;}
            void Join(int a,int b){parent[Root(a)]=Root(b);}
            var welded=new Dictionary<Vector3Int,int>();
            for(int i=0;i<triangles.Length;i++) {
                var p=vertices[triangles[i]];var key=Vector3Int.RoundToInt(p*10000);
                if(welded.TryGetValue(key,out int other))Join(i,other);else welded.Add(key,i);
                if(i%3!=0)Join(i,i-i%3);
            }
            var groups=new Dictionary<int,List<int>>();
            for(int i=0;i<triangles.Length;i++){int r=Root(i);if(!groups.TryGetValue(r,out var g))groups.Add(r,g=new List<int>());g.Add(triangles[i]);}
            var result=new List<Lamp>();
            MountingNormals.TryGetValue(mesh,out var mountingNormals);
            foreach(var g in groups.Values) {
                var bounds=new Bounds(vertices[g[0]],Vector3.zero);Color color=Color.black;
                foreach(int index in g){bounds.Encapsulate(vertices[index]);color+=colors.Length==vertices.Length?colors[index]:Color.white;}
                color/=g.Count;
                // A bent tile's mesh is already in world orientation. Find the lens's principal
                // direction instead of aligning its strip with a world-space bounding-box axis.
                var points=new HashSet<Vector3>();foreach(int index in g)points.Add(vertices[index]);
                Vector3 centre=Vector3.zero;foreach(var p in points)centre+=p;centre/=points.Count;
                Vector3 cx=Vector3.zero,cy=Vector3.zero,cz=Vector3.zero;
                foreach(var p in points){var d=p-centre;cx+=d*d.x;cy+=d*d.y;cz+=d*d.z;}
                var size=bounds.size;int axis=size.y>size.x?1:0;if(size.z>size[axis])axis=2;
                var direction=Vector3.zero;direction[axis]=1;
                for(int i=0;i<16;i++) {
                    var next=cx*direction.x+cy*direction.y+cz*direction.z;
                    if(next.sqrMagnitude<1e-16f)break;direction=next.normalized;
                }
                float low=float.MaxValue,high=float.MinValue,width=0;
                foreach(var p in points){var d=p-centre;float t=Vector3.Dot(d,direction);low=Mathf.Min(low,t);high=Mathf.Max(high,t);width=Mathf.Max(width,2*(d-direction*t).magnitude);}
                float halfLength=Mathf.Max(0,(high-low-width)*.5f);centre+=direction*((low+high)*.5f);
                var mount=Vector4.zero;var facing=Vector3.zero;
                if(mountingNormals!=null)foreach(int index in g)if(mountingNormals.TryGetValue(index,out var normal))facing+=normal;
                if(facing.sqrMagnitude>1e-8f) {
                    facing.Normalize();float back=float.MaxValue;
                    foreach(var p in points)back=Mathf.Min(back,Vector3.Dot(p,facing));
                    mount=new Vector4(facing.x,facing.y,facing.z,-back);
                }
                result.Add(new Lamp{a=centre-direction*halfLength,b=centre+direction*halfLength,color=color,
                    radius=Mathf.Clamp(.6f+width*2,.65f,1.5f),plane=mount});
            }
            cached=result.ToArray();entry.slots.Add(slot,cached);return cached;
        }
    }

}
