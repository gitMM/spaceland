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
        readonly List<Material> materials=new List<Material>();
        void OnEnable(){filter=GetComponent<MeshFilter>();surface=GetComponent<MeshRenderer>();StationLocalLights.Collect+=Gather;}
        void OnDisable(){StationLocalLights.Collect-=Gather;}
        void Gather(Camera camera,List<StationLocalLights.LightData> lights)
        {
            if(!StationLocalLights.VisibleTo(camera,gameObject)||!surface||!surface.enabled||!filter)return;
            var settings=StationLightingSettings.Current;var scale=transform.lossyScale;
            float reach=settings.localLightDistance+1.5f*settings.localLightRange*Mathf.Max(Mathf.Abs(scale.x),Mathf.Max(Mathf.Abs(scale.y),Mathf.Abs(scale.z)));
            if(surface.bounds.SqrDistance(camera.transform.position)>reach*reach)return;
            surface.GetSharedMaterials(materials);
            if(materialSlot<0||materialSlot>=materials.Count||!materials[materialSlot]||!materials[materialSlot].HasProperty("_Emission"))return;
            var plane=Vector4.zero;
            if(clipToMountingPlane){var n=transform.TransformDirection(mountingNormal).normalized;plane=new Vector4(n.x,n.y,n.z,-Vector3.Dot(transform.position,n));}
            StationLocalLights.Append(lights,filter.sharedMesh,materialSlot,transform.localToWorldMatrix,materials[materialSlot].GetVector("_Emission"),1,plane);
        }
    }
}

