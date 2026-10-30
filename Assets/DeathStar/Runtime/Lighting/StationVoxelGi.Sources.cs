using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeathStar
{
    public sealed partial class StationVoxelGi
    {
        [StructLayout(LayoutKind.Sequential)]
        struct Triangle {public Vector3 a,b,c,normal,albedo,emission;public float area;}
        sealed class Source {public int hash;public Triangle[] triangles;public Bounds bounds;}
        readonly List<MeshRenderer> renderers=new(),rendererScratch=new();
        readonly List<LODGroup> lodScratch=new();
        readonly List<GameObject> roots=new();
        readonly List<Material> materials=new();
        readonly List<Vector3> vertices=new();
        readonly List<Color> colors=new();
        readonly List<Vector4> overlays=new();
        readonly List<int> indices=new(),staleSources=new();
        readonly List<Triangle> triangleScratch=new();
        readonly HashSet<Renderer> excluded=new();
        readonly Dictionary<int,Source> sourceCache=new();
        readonly Dictionary<int,int> sourceKeys=new();
        readonly List<int> sourceOrder=new();
        readonly List<Source> sources=new();
        struct MaterialState {
            public int id,shader;
            public bool supported,surface,hasBase,hasMetal,hasEmission;
            public Vector4 color,emission;
            public float metal;
        }
        // Refreshed for each scan: edits to shared materials need no separate notification.
        readonly Dictionary<Material,MaterialState> scanMaterials=new();
        readonly Dictionary<Shader,bool> scanShaders=new();
        static readonly Unity.Profiling.ProfilerMarker collectMarker=new("Station voxel source scan");
        static readonly int BaseColorId=Shader.PropertyToID("_BaseColor"),MetallicId=Shader.PropertyToID("_Metallic"),EmissionId=Shader.PropertyToID("_Emission");

        int Collect(Scene scene,int cullingMask,StationLightingSettings settings)
        {
            using var sample=collectMarker.Auto();
            using var scan=Scan(scene,cullingMask,settings);
            while(scan.MoveNext()){}
            return scanHash;
        }

        IEnumerator<int> backgroundScan;
        int scanHash,scanChanges;
        double scanStarted;
        bool scanOverBudget,scanReady;
        // Root discovery, LOD enumeration and renderer checks yield independently. Nothing
        // consumes these scratch lists until the scan completes (the published volume stays live).
        IEnumerator<int> Scan(Scene scene,int cullingMask,StationLightingSettings settings,bool snapshotGeometry=false)
        {
            // Acknowledge only changes known when this snapshot started. Later
            // notifications must survive publication and request a follow-up scan.
            scanChanges=changeRevision;
            scanMaterials.Clear();scanShaders.Clear();
            scanOverBudget=false;int capturedTriangles=0;
            renderers.Clear();roots.Clear();excluded.Clear();sourceKeys.Clear();sourceOrder.Clear();
            if(scene.IsValid()&&scene.isLoaded)scene.GetRootGameObjects(roots);
            foreach(var root in roots) {
                if(!root)continue;
                lodScratch.Clear();root.GetComponentsInChildren(false,lodScratch);
                yield return 0;
                foreach(var group in lodScratch) {
                    if(!group)continue;
                    var lods=group.GetLODs();
                    for(int i=0;i<lods.Length-1;i++)foreach(var r in lods[i].renderers)excluded.Add(r);
                    yield return 0;
                }
            }
            var sceneKey=new HashCode();
            foreach(var root in roots) {
                if(!root)continue;
                rendererScratch.Clear();root.GetComponentsInChildren(false,rendererScratch);
                yield return 0;
                foreach(var r in rendererScratch) {
                    yield return 0;
                    if(!r||!r.enabled||!r.gameObject.activeInHierarchy||excluded.Contains(r)||(cullingMask&(1<<r.gameObject.layer))==0)continue;
                    var mesh=r.GetComponent<MeshFilter>()?.sharedMesh;
                    if(!mesh||!mesh.isReadable)continue;
                    var key=new HashCode();key.Add(mesh.GetInstanceID());key.Add(mesh.vertexCount);key.Add(mesh.subMeshCount);
                    key.Add(r.localToWorldMatrix);key.Add(mesh.bounds);
                    key.Add(settings.defaultTileTint);key.Add(settings.habitatTintEnabled);if(settings.habitatTintEnabled)key.Add(settings.habitatTint);key.Add(settings.emission);
                    materials.Clear();r.GetSharedMaterials(materials);
                    bool supported=false;
                    // Include null/unsupported slots and their positions: moving a
                    // material between submeshes changes the voxelized surface.
                    key.Add(materials.Count);
                    for(int slot=0;slot<materials.Count;slot++) {
                        var material=ReadMaterial(materials[slot]);key.Add(material.id);key.Add(material.shader);
                        if(slot>=mesh.subMeshCount || !material.supported)continue;
                        supported=true;
                        if(material.hasBase)key.Add(material.color);
                        if(material.hasMetal)key.Add(material.metal);
                        if(material.hasEmission)key.Add(material.emission);
                    }
                    if(!supported)continue;
                    int rendererId=r.GetInstanceID(),hash=key.ToHashCode();
                    sourceKeys[rendererId]=hash;sourceOrder.Add(rendererId);renderers.Add(r);sceneKey.Add(rendererId);sceneKey.Add(hash);
                    // Snapshot changed geometry with the same matrix/material values used for
                    // its key. Completing a sliced scan must not read a newer moving source.
                    if(snapshotGeometry&&!scanOverBudget) {
                        if(!sourceCache.TryGetValue(rendererId,out var cached)||cached.hash!=hash) {
                            if(!CacheSource(r,rendererId,hash,settings,MaxTriangles-capturedTriangles))scanOverBudget=true;
                            else cached=sourceCache[rendererId];
                        }
                        if(!scanOverBudget) {
                            if(cached.triangles.Length>MaxTriangles-capturedTriangles)scanOverBudget=true;
                            else capturedTriangles+=cached.triangles.Length;
                        }
                    }
                }
            }
            staleSources.Clear();foreach(int key in sourceCache.Keys)if(!sourceKeys.ContainsKey(key))staleSources.Add(key);
            foreach(int key in staleSources)sourceCache.Remove(key);
            scanHash=sceneKey.ToHashCode();
        }

        void CancelScan(){backgroundScan?.Dispose();backgroundScan=null;scanReady=false;}
        bool CollectSlice(Scene scene,int cullingMask,StationLightingSettings settings,double now)
        {
            using var sample=collectMarker.Auto();
            if(backgroundScan==null){backgroundScan=Scan(scene,cullingMask,settings,true);scanStarted=now;}
            long start=System.Diagnostics.Stopwatch.GetTimestamp();
            // Preserve progress without a deadline drain. One hierarchy query or
            // source extraction is indivisible, so this remains a soft budget.
            do {
                if(!backgroundScan.MoveNext()){CancelScan();return true;}
            } while((System.Diagnostics.Stopwatch.GetTimestamp()-start)*1000.0/System.Diagnostics.Stopwatch.Frequency<.75);
            return false;
        }

        MaterialState ReadMaterial(Material material)
        {
            if(!material)return default;
            if(scanMaterials.TryGetValue(material,out var state))return state;
            state.id=material.GetInstanceID();var shader=material.shader;state.shader=shader?shader.GetInstanceID():0;
            bool allowed=false;
            if(shader&&!scanShaders.TryGetValue(shader,out allowed)) {
                string name=shader.name;allowed=name=="DeathStar/Tile Surface"||name=="DeathStar/Tile Glass";
                scanShaders.Add(shader,allowed);
            }
            state.supported=allowed&&material.GetShaderPassEnabled("ShadowCaster");
            if(state.supported)state.surface=shader.name=="DeathStar/Tile Surface";
            if(state.supported) {
                state.hasBase=material.HasProperty(BaseColorId);if(state.hasBase)state.color=material.GetVector(BaseColorId);
                state.hasMetal=material.HasProperty(MetallicId);if(state.hasMetal)state.metal=material.GetFloat(MetallicId);
                state.hasEmission=material.HasProperty(EmissionId);if(state.hasEmission)state.emission=material.GetVector(EmissionId);
            }
            scanMaterials.Add(material,state);return state;
        }

        bool PrepareSources(StationLightingSettings settings,out int count,out int refreshed)
        {
            sources.Clear();count=0;refreshed=0;
            if(scanOverBudget)return false;
            for(int i=0;i<sourceOrder.Count;i++) {
                int id=sourceOrder[i],hash=sourceKeys[id];
                if(!sourceCache.TryGetValue(id,out var source)||source.hash!=hash) {
                    if(!renderers[i]||!CacheSource(renderers[i],id,hash,settings,MaxTriangles-count))return false;
                    source=sourceCache[id];refreshed++;
                }
                if(source.triangles.Length>MaxTriangles-count)return false;
                sources.Add(source);count+=source.triangles.Length;
            }
            return true;
        }

        bool CacheSource(MeshRenderer renderer,int id,int hash,StationLightingSettings settings,int remaining)
        {
            if(!ExtractSource(renderer,settings,remaining))return false;
            sourceCache[id]=new Source{hash=hash,triangles=triangleScratch.ToArray(),bounds=renderer.bounds};return true;
        }

        bool ExtractSource(MeshRenderer renderer,StationLightingSettings settings,int remaining)
        {
            triangleScratch.Clear();
            var mesh=renderer.GetComponent<MeshFilter>().sharedMesh;
            extractedMeshes.GetValue(mesh,_=>new object());
            materials.Clear();renderer.GetSharedMaterials(materials);
            // Reject oversized inputs before expanding managed triangle arrays.
            long upperBound=0;
            for(int sub=0;sub<mesh.subMeshCount&&sub<materials.Count;sub++)if(ReadMaterial(materials[sub]).supported) {
                upperBound+=mesh.GetIndexCount(sub)/3;
                if(upperBound>remaining)return false;
            }
            vertices.Clear();mesh.GetVertices(vertices);colors.Clear();mesh.GetColors(colors);
            overlays.Clear();mesh.GetUVs(3,overlays);
            var matrix=renderer.localToWorldMatrix;
            Vector3 habitat=RGB(TileLook.FamilyColor(TileFamily.Habitat));
            for(int sub=0;sub<mesh.subMeshCount&&sub<materials.Count;sub++) {
                var material=ReadMaterial(materials[sub]);if(!material.supported)continue;
                Vector3 albedo=material.surface?RGB(material.color)*(1-material.metal)*.96f:Vector3.zero;
                Vector3 emission=material.surface?RGB(material.emission)*settings.emission:Vector3.zero;
                indices.Clear();mesh.GetTriangles(indices,sub);
                for(int i=0;i<indices.Count;i+=3) {
                    int ia=indices[i],ib=indices[i+1],ic=indices[i+2];
                    Vector3 a=matrix.MultiplyPoint3x4(vertices[ia]),b=matrix.MultiplyPoint3x4(vertices[ib]),c=matrix.MultiplyPoint3x4(vertices[ic]);
                    Vector3 normal=Vector3.Cross(b-a,c-a);float area=normal.sqrMagnitude;if(area<1e-12f)continue;
                    Vector3 tint=colors.Count==vertices.Count?RGB((colors[ia]+colors[ib]+colors[ic])/3):Vector3.one;
                    Vector3 diffuse=Vector3.Scale(albedo,tint);
                    if(settings.habitatTintEnabled&&overlays.Count==vertices.Count&&(RGB(overlays[ia])-habitat).sqrMagnitude<.0001f)
                        diffuse=Vector3.Scale(diffuse,RGB(settings.habitatTint));
                    else
                        diffuse=Vector3.Scale(diffuse,RGB(settings.defaultTileTint));
                    triangleScratch.Add(new Triangle{a=a,b=b,c=c,normal=normal.normalized,albedo=diffuse,emission=Vector3.Scale(emission,tint),area=area});
                }
            }
            return true;
        }

        void UploadSources(ComputeBuffer buffer,int count)
        {
            // Copy cached source arrays directly into the upload mapping. Avoid
            // building a second scene-sized managed list/array on every relight.
            var mapped=buffer.BeginWrite<Triangle>(0,count);
            try {
                int offset=0;
                foreach(var source in sources) {
                    NativeArray<Triangle>.Copy(source.triangles,0,mapped,offset,source.triangles.Length);
                    offset+=source.triangles.Length;
                }
            } finally {buffer.EndWrite<Triangle>(count);}
        }
        static Vector3 RGB(Vector4 color)=>new Vector3(color.x,color.y,color.z);
    }
}
