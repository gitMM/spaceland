using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

namespace DeathStar
{
    // A rig owns lights; the profile is shared by the gallery, foundry and lab.
    [ExecuteAlways]
    public sealed class StationLightingRig : MonoBehaviour
    {
        public Light sun, planet;
        readonly List<Volume> volumes=new();
        void Update()=>Apply();
        public void Apply() {
            var s=StationLightingSettings.Current;
            if(sun){sun.color=s.sunColor;sun.intensity=s.sunIntensity;sun.transform.rotation=Quaternion.LookRotation(-Direction(s.sunFrom));}
            if(planet){planet.color=s.planetColor;planet.intensity=s.planetIntensity;planet.transform.rotation=Quaternion.LookRotation(-Direction(s.planetFrom));}
            TileLook.ApplyLightingEnvironment();
            // Volumes may be created after the rig (Foundry and Gallery both do
            // this). Refill the reusable list instead of caching an empty array.
            (transform.parent?transform.parent:transform).GetComponentsInChildren(false,volumes);
            foreach(var v in volumes)if(v&&v.sharedProfile)s.ApplyPost(v.sharedProfile);
        }
        static Vector3 Direction(Vector3 v)=>v.sqrMagnitude>1e-8f?v.normalized:Vector3.forward;
    }
}
