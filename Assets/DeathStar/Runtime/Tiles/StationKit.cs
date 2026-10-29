using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeathStar
{
    // The small-object kit near the player (doc/close-up-detail-plan.md, layer 3). Tile meshes under this object
    // offer slots (TileMesh.KitSlot: rectangular plates with four sharp edges); a slot holds a prop when its face's
    // station-space random number is under the density, so the choice is deterministic and matches TileSurface. Only
    // inside a ring round the ring centre (the camera, or ringCentre: the player): whole within the inner radius,
    // grown in from flat between inner and outer. Beyond the ring TileSurface folds their average darkening into the
    // plate, so the surface doesn't brighten as they leave. Drawn with GPU instancing for each camera, one draw per prop,
    // level of detail and material slot. Pipes and cables join props on the same plate (KitRoutes) and grow in with
    // the lower of their two props. In play mode the nearest ring gets box colliders.
    [ExecuteAlways]
    public sealed class StationKit : MonoBehaviour
    {
        public Transform ringCentre;
        // Levels of detail by distance from the camera (metres).
        public float level1=5,level2=10;
        public float colliderRadius=4;

        readonly List<KitRoutes.Placement> placements=new List<KitRoutes.Placement>();
        List<KitRoutes.Route> routes=new List<KitRoutes.Route>();
        float[] grows=new float[0];
        readonly Dictionary<Vector3Int,List<int>> grid=new Dictionary<Vector3Int,List<int>>();
        const float GridCell=10;
        bool dirty=true;float density=-1;
        // Copies of the tiles' materials: instancing on, and _TileKitMode 1 for props, 2 for routes.
        Material[] materials,routeMaterials;
        readonly List<Matrix4x4>[,] buckets=new List<Matrix4x4>[KitProps.Count,KitProps.Levels];
        readonly List<BoxCollider> colliders=new List<BoxCollider>();
        Transform colliderRoot;
        static readonly int CentreId=Shader.PropertyToID("_TileKitCentre"),KitId=Shader.PropertyToID("_TileKit");
        // Last Draw's prop count and CPU time (ms), for the lab.
        public int Drawn {get;private set;}
        public double DrawMs {get;private set;}
        public int Routes {get;private set;}

        public void MarkDirty()=>dirty=true;
        void OnEnable(){RenderPipelineManager.beginCameraRendering+=BeginCamera;StationLocalLights.Collect+=GatherLocalLights;dirty=true;}
        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering-=BeginCamera;StationLocalLights.Collect-=GatherLocalLights;
            Shader.SetGlobalVector(CentreId,Vector4.zero);
            foreach(var m in materials??new Material[0])if(m)Release(m);materials=null;
            foreach(var m in routeMaterials??new Material[0])if(m)Release(m);routeMaterials=null;
            foreach(var r in routes)Release(r.mesh);routes.Clear();dirty=true;
            if(colliderRoot)Release(colliderRoot.gameObject);colliders.Clear();
        }
        static void Release(Object o){if(Application.isPlaying)Destroy(o);else DestroyImmediate(o);}

        // Every tile mesh under this object that has slots, as placements in world space (from the final, possibly
        // bent, mesh: a slot is a triangle and barycentric weights, so it follows the Foundry's bend).
        void Rescan()
        {
            dirty=false;placements.Clear();grid.Clear();
            foreach(var r in routes)Release(r.mesh);routes.Clear();
            var s=StationLightingSettings.Current;
            foreach(var filter in GetComponentsInChildren<MeshFilter>())
            {
                var mesh=filter.sharedMesh;
                var level=filter.gameObject.name;
                if(mesh==null||level=="Far"||level=="Mid"||!TileMesh.KitSlots.TryGetValue(mesh,out var slots))continue;
                if(materials==null)
                {
                    var shared=filter.GetComponent<MeshRenderer>().sharedMaterials;materials=new Material[shared.Length];routeMaterials=new Material[shared.Length];
                    for(int i=0;i<shared.Length;i++)
                    {
                        if(!shared[i])continue;
                        materials[i]=new Material(shared[i]){enableInstancing=true,hideFlags=HideFlags.HideAndDontSave};materials[i].SetFloat("_TileKitMode",1);materials[i].EnableKeyword("_TILE_KIT");
                        routeMaterials[i]=new Material(shared[i]){hideFlags=HideFlags.HideAndDontSave};routeMaterials[i].SetFloat("_TileKitMode",2);routeMaterials[i].EnableKeyword("_TILE_KIT");
                    }
                }
                var v=mesh.vertices;var normals=mesh.normals;var l2w=filter.transform.localToWorldMatrix;
                foreach(var slot in slots)
                {
                    float random=Frac(slot.packed);
                    if(random>=s.kitDensity)continue;
                    Vector3 A=v[slot.a],B=v[slot.b],C=v[slot.c];
                    var centre=l2w.MultiplyPoint3x4(A*slot.bary.x+B*slot.bary.y+C*slot.bary.z);
                    var normal=l2w.MultiplyVector(Vector3.Cross(B-A,C-A)).normalized;
                    var edge=l2w.MultiplyVector(v[slot.edgeB]-v[slot.edgeA]);edge=Vector3.ProjectOnPlane(edge,normal).normalized;
                    if(Vector3.Dot(normal,l2w.MultiplyVector(normals[slot.a]))<0)normal=-normal;
                    // The first edge runs V long; the prop's long side follows the face's longer side.
                    float U=slot.size.x,V=slot.size.y;
                    var along=V>=U?edge:Vector3.Cross(normal,edge);
                    int prop=Fit(random,Mathf.Min(U,V),out float scale);
                    placements.Add(new KitRoutes.Placement{position=centre+normal*0.002f,normal=normal,along=along,scale=scale,prop=prop});
                    var key=Vector3Int.FloorToInt(centre/GridCell);
                    if(!grid.TryGetValue(key,out var list))grid.Add(key,list=new List<int>());
                    list.Add(placements.Count-1);
                }
            }
            routes=KitRoutes.Build(placements,Nearby);
            grows=new float[placements.Count];
        }
        IEnumerable<int> Nearby(Vector3 p)
        {
            var key=Vector3Int.FloorToInt(p/GridCell);
            for(int x=-1;x<=1;x++)for(int y=-1;y<=1;y++)for(int z=-1;z<=1;z++)
                if(grid.TryGetValue(key+new Vector3Int(x,y,z),out var list))foreach(int i in list)yield return i;
        }
        // The float the shader reads back from uv0.w: the same single-precision fraction.
        static float Frac(float packed)=>packed-Mathf.Floor(packed);
        // A prop chosen from the face's random number, scaled to fill at most three quarters of the plate's narrow side;
        // too small a fit falls back to the smallest prop.
        static int Fit(float random,float room,out float scale)
        {
            KitProps.Get(0,0);
            int prop=Mathf.Min(KitProps.Count-1,(int)(Frac(random*13.37f)*KitProps.Count));
            float Foot(int p)=>Mathf.Max(KitProps.Size[p].x,KitProps.Size[p].z);
            scale=Mathf.Min(1.2f,0.75f*room/Foot(prop));
            if(scale<0.5f)
            {
                int small=0;for(int p=1;p<KitProps.Count;p++)if(Foot(p)<Foot(small))small=p;
                prop=small;scale=Mathf.Min(1.2f,0.75f*room/Foot(prop));
            }
            return prop;
        }

        void GatherLocalLights(Camera camera,List<StationLocalLights.LightData> lights)
        {
            if(!StationLightingSettings.Current.kitProps||materials==null||!StationLocalLights.VisibleTo(camera,gameObject))return;
            var lampMaterial=materials[(int)TileSlot.Light];if(!lampMaterial)return;
            Color emission=lampMaterial.GetVector("_Emission");
            for(int i=0;i<placements.Count;i++) {
                float grow=grows[i];if(grow<=0)continue;
                var p=placements[i];if(p.prop!=5&&p.prop!=9)continue;
                var rotation=Quaternion.LookRotation(Vector3.Cross(p.along,p.normal),p.normal);
                var matrix=Matrix4x4.TRS(p.position,rotation,new Vector3(p.scale,p.scale*grow,p.scale));
                var plane=new Vector4(p.normal.x,p.normal.y,p.normal.z,-Vector3.Dot(p.position,p.normal));
                StationLocalLights.Append(lights,KitProps.Get(p.prop,0),(int)TileSlot.Light,matrix,emission,grow,plane);
            }
        }

        void BeginCamera(ScriptableRenderContext context,Camera camera)
        {
            if(camera.cameraType==CameraType.Preview||camera.cameraType==CameraType.Reflection)return;
            Draw(camera,ringCentre?ringCentre.position:camera.transform.position);
        }

        // Props for one camera, ring round centre. Also sets the fold the tiles' shader applies beyond the ring.
        public void Draw(Camera camera,Vector3 centre)
        {
            var s=StationLightingSettings.Current;
            if(!s.kitProps){Shader.SetGlobalVector(CentreId,Vector4.zero);Drawn=0;return;}
            if(dirty||density!=s.kitDensity){density=s.kitDensity;Rescan();}
            var clock=System.Diagnostics.Stopwatch.StartNew();
            float inner=s.kitInnerRadius,outer=Mathf.Max(inner+0.01f,s.kitOuterRadius);
            Shader.SetGlobalVector(CentreId,new Vector4(centre.x,centre.y,centre.z,1));
            Shader.SetGlobalVector(KitId,new Vector4(inner,outer,s.kitDensity,0));
            if(materials==null){Drawn=0;return;}
            for(int p=0;p<KitProps.Count;p++)for(int l=0;l<KitProps.Levels;l++)(buckets[p,l]??=new List<Matrix4x4>()).Clear();
            var eye=camera.transform.position;int drawn=0;
            System.Array.Clear(grows,0,grows.Length);
            var c0=Vector3Int.FloorToInt((centre-Vector3.one*outer)/GridCell);var c1=Vector3Int.FloorToInt((centre+Vector3.one*outer)/GridCell);
            for(int x=c0.x;x<=c1.x;x++)for(int y=c0.y;y<=c1.y;y++)for(int z=c0.z;z<=c1.z;z++)
            {
                if(!grid.TryGetValue(new Vector3Int(x,y,z),out var list))continue;
                foreach(int i in list)
                {
                    var pl=placements[i];
                    float grow=Mathf.Clamp01((outer-Vector3.Distance(pl.position,centre))/(outer-inner));
                    grows[i]=grow;
                    if(grow<=0)continue;
                    float d=Vector3.Distance(pl.position,eye);
                    int level=d<level1?0:d<level2?1:2;
                    var rotation=Quaternion.LookRotation(Vector3.Cross(pl.along,pl.normal),pl.normal);
                    buckets[pl.prop,level].Add(Matrix4x4.TRS(pl.position,rotation,new Vector3(pl.scale,pl.scale*grow,pl.scale)));
                    drawn++;
                }
            }
            for(int p=0;p<KitProps.Count;p++)for(int l=0;l<KitProps.Levels;l++)
            {
                var list=buckets[p,l];if(list.Count==0)continue;
                var mesh=KitProps.Get(p,l);
                for(int sub=0;sub<mesh.subMeshCount&&sub<materials.Length;sub++)
                {
                    if(mesh.GetIndexCount(sub)==0||materials[sub]==null)continue;
                    var rp=new RenderParams(materials[sub]){camera=camera,shadowCastingMode=ShadowCastingMode.On,receiveShadows=true,
                        worldBounds=new Bounds(centre,Vector3.one*(outer*2+2)),layer=gameObject.layer};
                    Graphics.RenderMeshInstanced(rp,mesh,sub,list);
                }
            }
            // Routes: flattened onto their plate with the lower of their props' growth (and dithered out as they go).
            foreach(var route in routes)
            {
                float grow=Mathf.Min(grows[route.a],grows[route.b]);
                if(grow<=0||routeMaterials[(int)route.slot]==null)continue;
                var flatten=Matrix4x4.Translate(route.plane)*Matrix4x4.Rotate(Quaternion.FromToRotation(Vector3.up,route.normal))*
                    Matrix4x4.Scale(new Vector3(1,grow,1))*Matrix4x4.Rotate(Quaternion.FromToRotation(route.normal,Vector3.up))*Matrix4x4.Translate(-route.plane);
                var rp=new RenderParams(routeMaterials[(int)route.slot]){camera=camera,shadowCastingMode=ShadowCastingMode.On,receiveShadows=true,layer=gameObject.layer};
                Graphics.RenderMesh(rp,route.mesh,0,flatten);
                drawn++;
            }
            Drawn=drawn;DrawMs=clock.Elapsed.TotalMilliseconds;
            Routes=routes.Count;
        }

        // Box colliders on the props in the nearest ring (play mode), round the player.
        void LateUpdate()
        {
            if(!Application.isPlaying)return;
            var s=StationLightingSettings.Current;
            var centreObject=ringCentre?ringCentre:Camera.main?Camera.main.transform:null;
            if(dirty)Rescan();
            int used=0;
            if(s.kitProps&&centreObject)
            {
                var centre=centreObject.position;
                if(!colliderRoot){colliderRoot=new GameObject("Kit colliders").transform;colliderRoot.SetParent(transform,false);}
                var key=Vector3Int.FloorToInt(centre/GridCell);
                for(int x=-1;x<=1;x++)for(int y=-1;y<=1;y++)for(int z=-1;z<=1;z++)
                {
                    if(!grid.TryGetValue(key+new Vector3Int(x,y,z),out var list))continue;
                    foreach(int i in list)
                    {
                        var pl=placements[i];
                        if(Vector3.Distance(pl.position,centre)>colliderRadius)continue;
                        if(used==colliders.Count){var go=new GameObject("Kit collider");go.transform.SetParent(colliderRoot,false);colliders.Add(go.AddComponent<BoxCollider>());}
                        var box=colliders[used++];var size=KitProps.Size[pl.prop]*pl.scale;
                        box.transform.SetPositionAndRotation(pl.position,Quaternion.LookRotation(Vector3.Cross(pl.along,pl.normal),pl.normal));
                        box.center=new Vector3(0,size.y*0.5f,0);box.size=size;box.enabled=true;
                    }
                }
            }
            for(int i=used;i<colliders.Count;i++)colliders[i].enabled=false;
        }
    }
}
