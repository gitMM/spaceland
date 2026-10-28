using UnityEditor;
using UnityEngine;

namespace DeathStar.EditorTools
{
    [CustomEditor(typeof(StationLightingSettings))]
    public sealed class StationLightingSettingsEditor:Editor
    {
        static readonly string[] TopFields={"m_Script","screenSpaceGi","screenGiStrength","screenGiRadius",
            "screenGiSlices","screenGiSliceSteps","screenGiSurfaceThickness","screenGiAoRadius",
            "giDebugView","habitatTintEnabled","habitatTint"};
        public override void OnInspectorGUI() {
            serializedObject.Update();
            EditorGUILayout.LabelField("Bounce distance and quality",EditorStyles.boldLabel);
            Field("screenSpaceGi","Enable screen-space bounce");
            Slider("screenGiRadius","Bounce Distance (m)",.1f,30);
            Slider("screenGiStrength","Bounce Strength",0,1);
            Slider("screenGiAoRadius","AO Distance (m)",.05f,5);
            EditorGUILayout.IntSlider(serializedObject.FindProperty("screenGiSlices"),2,12,new GUIContent("Sampling Directions"));
            EditorGUILayout.IntSlider(serializedObject.FindProperty("screenGiSliceSteps"),4,32,new GUIContent("Samples Per Side"));
            Slider("screenGiSurfaceThickness","Surface Thickness (m)",.01f,3);
            Field("giDebugView","Lighting Debug View");
            EditorGUILayout.HelpBox("Try 5 m bounce, 6 directions and 24 samples per side. Keep AO and thickness unchanged initially. More samples cost GPU time; off-screen sources remain unavailable.",MessageType.Info);
            EditorGUILayout.Space();
            Field("habitatTintEnabled","Red Habitat paint");Field("habitatTint","Habitat Tint (linear)");
            EditorGUILayout.Space();
            DrawPropertiesExcluding(serializedObject,TopFields);
            serializedObject.ApplyModifiedProperties();
        }
        void Field(string property,string label)=>EditorGUILayout.PropertyField(serializedObject.FindProperty(property),new GUIContent(label),true);
        void Slider(string property,string label,float min,float max)=>EditorGUILayout.Slider(serializedObject.FindProperty(property),min,max,new GUIContent(label));
    }

    // A dedicated window keeps controls accessible even with a locked Inspector.
    public sealed class StationLightingWindow:EditorWindow
    {
        Editor inspector;Vector2 scroll;
        public static void Open(){var window=GetWindow<StationLightingWindow>("Station Lighting");window.minSize=new Vector2(390,500);window.Show();window.Focus();}
        void OnGUI(){
            var settings=StationLightingSettings.Current;
            if(!inspector||inspector.target!=settings){if(inspector)DestroyImmediate(inspector);inspector=Editor.CreateEditor(settings);}
            scroll=EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Shared by Foundry, Gallery and Tile Lab",EditorStyles.miniLabel);
            inspector.OnInspectorGUI();EditorGUILayout.EndScrollView();
        }
        void OnDisable(){if(inspector)DestroyImmediate(inspector);}
    }
}
