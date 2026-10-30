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
        int signature;
        bool applied;
        Light lastSun,lastPlanet;
        Transform lastScope;
        double reconcileAt;
        static readonly Unity.Profiling.ProfilerMarker applyMarker=new("Station lighting rig apply");
        void OnEnable()=>applied=false;
        void OnValidate()=>applied=false;
        void OnTransformChildrenChanged()=>applied=false;
        void OnTransformParentChanged()=>applied=false;
        void Update()=>Apply(false);
        // Explicit authoring/lab calls force immediate reconciliation of external changes.
        public void Apply()=>Apply(true);
        void Apply(bool force) {
            var s=StationLightingSettings.Current;
            int next=s.RigSignature();double now=Time.realtimeSinceStartupAsDouble;
            var scope=transform.parent?transform.parent:transform;
            var sunRotation=Quaternion.LookRotation(-Direction(s.sunFrom));var planetRotation=Quaternion.LookRotation(-Direction(s.planetFrom));
            bool directionsMatch=(!sun||Mathf.Abs(Quaternion.Dot(sun.transform.rotation,sunRotation))>0.999999f)&&
                (!planet||Mathf.Abs(Quaternion.Dot(planet.transform.rotation,planetRotation))>0.999999f);
            if(!force&&applied&&signature==next&&sun==lastSun&&planet==lastPlanet&&scope==lastScope&&directionsMatch&&now<reconcileAt)return;
            using var sample=applyMarker.Auto();
            if(sun){sun.color=s.sunColor;sun.intensity=s.sunIntensity;sun.transform.rotation=sunRotation;}
            if(planet){planet.color=s.planetColor;planet.intensity=s.planetIntensity;planet.transform.rotation=planetRotation;}
            TileLook.ApplyLightingEnvironment();
            // Bounded fallback for new volumes, replaced profiles/components, or external
            // RenderSettings/light edits. The rig owns these values, even if its settings
            // haven't changed; do not trust a cached volume list forever.
            scope.GetComponentsInChildren(false,volumes);
            foreach(var v in volumes)if(v&&v.sharedProfile)s.ApplyPost(v.sharedProfile);
            signature=next;lastSun=sun;lastPlanet=planet;lastScope=scope;applied=true;reconcileAt=now+.5;
        }
        static Vector3 Direction(Vector3 v)=>v.sqrMagnitude>1e-8f?v.normalized:Vector3.forward;
    }
}
