using UnityEditor;
using UnityEngine;

namespace DeathStar.EditorTools
{
    [CustomEditor(typeof(StationLightingSettings))]
    public sealed class StationLightingSettingsEditor:Editor
    {
        static readonly string[] TopFields={"m_Script","screenSpaceGi","screenGiStrength","screenGiRadius",
            "screenGiSlices","screenGiSliceSteps","screenGiSurfaceThickness","screenGiAoRadius",
            "giDebugView","habitatTintEnabled","habitatTint","voxelGi","voxelGiStrength","voxelGiDistance","voxelGiResolution","voxelGiSecondBounce"};
        public override void OnInspectorGUI() {
            serializedObject.Update();
            EditorGUILayout.LabelField("Distant and off-screen bounce",EditorStyles.boldLabel);
            Field("voxelGi","Enable voxel bounce");
            Slider("voxelGiDistance","Voxel Distance (m)",5,80);
            Slider("voxelGiStrength","Voxel Strength",0,2);
            Field("voxelGiSecondBounce","Second bounce");
            var resolution=serializedObject.FindProperty("voxelGiResolution");
            resolution.intValue=EditorGUILayout.IntPopup("Voxel Resolution",resolution.intValue,new[]{"64 (faster rebuilds)","128 (default)"},new[]{64,128});
            EditorGUILayout.HelpBox("Voxel bounce sees hidden and off-screen tiles. Screen-space bounce below retains nearby detail. The world-space volume is cached until construction or lighting changes.",MessageType.Info);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Bounce distance and quality",EditorStyles.boldLabel);
            Field("screenSpaceGi","Enable screen-space bounce");
            Slider("screenGiRadius","Bounce Distance (m)",.1f,30);
            Slider("screenGiStrength","Bounce Strength",0,1);
            Slider("screenGiAoRadius","AO Distance (m)",.05f,5);
            EditorGUILayout.IntSlider(serializedObject.FindProperty("screenGiSlices"),2,12,new GUIContent("Sampling Directions"));
            EditorGUILayout.IntSlider(serializedObject.FindProperty("screenGiSliceSteps"),4,32,new GUIContent("Samples Per Side"));
            Slider("screenGiSurfaceThickness","Surface Thickness (m)",.01f,3);
            Field("giDebugView","Lighting Debug View");
            EditorGUILayout.HelpBox("With voxel bounce enabled, keep screen-space bounce near 1 m for fine contacts. Longer screen-space distances move the handoff to voxels farther away. AO distance remains independent.",MessageType.Info);
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
