using System.Collections.Generic;
using UnityEngine;
namespace DeathStar {
    // Attach once to the near mesh; its sources persist through visual LOD transitions.
    [ExecuteAlways,DisallowMultipleComponent,RequireComponent(typeof(MeshFilter),typeof(MeshRenderer))]
    public sealed class StationLocalLightSource:MonoBehaviour
    {
        public int materialSlot=6;
        public bool clipToMountingPlane;
        public Vector3 mountingNormal=Vector3.up;
        MeshFilter filter;MeshRenderer surface;
        Transform sourceTransform;GameObject owner;
        readonly List<Material> materials=new List<Material>();
        readonly List<StationLocalLights.LightData> worldLights=new();
        Mesh lastMesh;
        Matrix4x4 lastMatrix;
        Vector4 lastPlane;
        Color lastEmission;
        int lastSlot=-1,lastRevision=-1;
        bool cached;
        void OnEnable(){filter=GetComponent<MeshFilter>();surface=GetComponent<MeshRenderer>();sourceTransform=transform;owner=gameObject;cached=false;StationLocalLights.Collect+=Gather;}
        void OnDisable(){StationLocalLights.Collect-=Gather;}
        void Gather(Camera camera,List<StationLocalLights.LightData> lights)
        {
            var query=StationLocalLights.CameraQuery(camera);
            if(!surface||!surface.enabled||!filter||(query.mask&(1<<owner.layer))==0)return;
            var scene=owner.scene;
            if(query.sceneValid&&query.scene!=scene)return;
            #if UNITY_EDITOR
            if(UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(scene)&&query.scene!=scene)return;
            #endif
            var scale=sourceTransform.lossyScale;
            float reach=query.distance+1.5f*query.range*Mathf.Max(Mathf.Abs(scale.x),Mathf.Max(Mathf.Abs(scale.y),Mathf.Abs(scale.z)));
            if(surface.bounds.SqrDistance(query.eye)>reach*reach)return;
            surface.GetSharedMaterials(materials);
            if(materialSlot<0||materialSlot>=materials.Count||!StationLocalLights.ReadEmission(materials[materialSlot],out var emission))return;
            var mesh=filter.sharedMesh;if(!mesh||!mesh.isReadable)return;
            var matrix=sourceTransform.localToWorldMatrix;
            var plane=Vector4.zero;
            if(clipToMountingPlane){var n=sourceTransform.TransformDirection(mountingNormal).normalized;plane=new Vector4(n.x,n.y,n.z,-Vector3.Dot(sourceTransform.position,n));}
            if(!cached||lastMesh!=mesh||!lastMatrix.Equals(matrix)||!lastPlane.Equals(plane)||!lastEmission.Equals(emission)||
                lastSlot!=materialSlot||lastRevision!=StationLocalLights.GeometryRevision) {
                worldLights.Clear();StationLocalLights.Append(worldLights,mesh,materialSlot,matrix,emission,1,plane);
                lastMesh=mesh;lastMatrix=matrix;lastPlane=plane;lastEmission=emission;lastSlot=materialSlot;
                lastRevision=StationLocalLights.GeometryRevision;cached=true;
            }
            lights.AddRange(worldLights);
        }
    }
}

