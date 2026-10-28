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
        sealed class Source {public int hash;public Triangle[] triangles;}
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
        readonly List<Source> sources=new();
        static readonly int BaseColorId=Shader.PropertyToID("_BaseColor"),MetallicId=Shader.PropertyToID("_Metallic"),EmissionId=Shader.PropertyToID("_Emission");

        // (a material that casts no shadow is no occluder either: the far level's relief shells, which as boxes would
        // fill the space over the relief)
        static bool Supported(Material material)=>material && material.shader &&
            (material.shader.name=="DeathStar/Tile Surface" || material.shader.name=="DeathStar/Tile Glass") &&
            material.GetShaderPassEnabled("ShadowCaster");

        int Collect(Scene scene,int cullingMask,StationLightingSettings settings)
        {
            renderers.Clear();roots.Clear();excluded.Clear();sourceKeys.Clear();
            if(scene.IsValid()&&scene.isLoaded)scene.GetRootGameObjects(roots);
            foreach(var root in roots) {
                lodScratch.Clear();root.GetComponentsInChildren(false,lodScratch);
                foreach(var group in lodScratch) {
                    var lods=group.GetLODs();
                    for(int i=0;i<lods.Length-1;i++)foreach(var r in lods[i].renderers)excluded.Add(r);
                }
            }
            var sceneKey=new HashCode();
            foreach(var root in roots) {
                rendererScratch.Clear();root.GetComponentsInChildren(false,rendererScratch);
                foreach(var r in rendererScratch) {
                    if(!r.enabled||!r.gameObject.activeInHierarchy||excluded.Contains(r)||(cullingMask&(1<<r.gameObject.layer))==0)continue;
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
                        var material=materials[slot];key.Add(material?material.GetInstanceID():0);
                        key.Add(material&&material.shader?material.shader.GetInstanceID():0);
                        if(slot>=mesh.subMeshCount || !Supported(material))continue;
                        supported=true;
                        if(material.HasProperty(BaseColorId))key.Add(material.GetVector(BaseColorId));
                        if(material.HasProperty(MetallicId))key.Add(material.GetFloat(MetallicId));
                        if(material.HasProperty(EmissionId))key.Add(material.GetVector(EmissionId));
                    }
                    if(!supported)continue;
                    int rendererId=r.GetInstanceID(),hash=key.ToHashCode();
                    sourceKeys[rendererId]=hash;renderers.Add(r);sceneKey.Add(rendererId);sceneKey.Add(hash);
                }
            }
            staleSources.Clear();foreach(int key in sourceCache.Keys)if(!sourceKeys.ContainsKey(key))staleSources.Add(key);
            foreach(int key in staleSources)sourceCache.Remove(key);
            return sceneKey.ToHashCode();
        }

        bool PrepareSources(StationLightingSettings settings,out int count,out int refreshed)
        {
            sources.Clear();count=0;refreshed=0;
            foreach(var renderer in renderers) {
                int id=renderer.GetInstanceID(),hash=sourceKeys[id];
                if(!sourceCache.TryGetValue(id,out var source)||source.hash!=hash) {
                    if(!ExtractSource(renderer,settings,MaxTriangles-count))return false;
                    source=new Source{hash=hash,triangles=triangleScratch.ToArray()};
                    sourceCache[id]=source;refreshed++;
                }
                if(source.triangles.Length>MaxTriangles-count)return false;
                sources.Add(source);count+=source.triangles.Length;
            }
            return true;
        }

        bool ExtractSource(MeshRenderer renderer,StationLightingSettings settings,int remaining)
        {
            triangleScratch.Clear();
            var mesh=renderer.GetComponent<MeshFilter>().sharedMesh;
            materials.Clear();renderer.GetSharedMaterials(materials);
            // Reject oversized inputs before expanding managed triangle arrays.
            long upperBound=0;
            for(int sub=0;sub<mesh.subMeshCount&&sub<materials.Count;sub++)if(Supported(materials[sub])) {
                upperBound+=mesh.GetIndexCount(sub)/3;
                if(upperBound>remaining)return false;
            }
            vertices.Clear();mesh.GetVertices(vertices);colors.Clear();mesh.GetColors(colors);
            overlays.Clear();mesh.GetUVs(3,overlays);
            var matrix=renderer.localToWorldMatrix;
            Vector3 habitat=RGB(TileLook.FamilyColor(TileFamily.Habitat));
            for(int sub=0;sub<mesh.subMeshCount&&sub<materials.Count;sub++) {
                var material=materials[sub];if(!Supported(material))continue;
                bool surface=material.shader.name=="DeathStar/Tile Surface";
                Vector3 albedo=surface?RGB(material.GetVector(BaseColorId))*(1-material.GetFloat(MetallicId))*.96f:Vector3.zero;
                Vector3 emission=surface?RGB(material.GetVector(EmissionId))*settings.emission:Vector3.zero;
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
