using UnityEngine;
using UnityEngine.InputSystem;

namespace DeathStar
{
    // Walk the interiors (Death Star / Open Interior Walk, then Play): in magnetic boots (MagBootWalker), through the
    // flat review station's homes and the corridor bridging its trench (1), or the Foundry example's homes and junction
    // on the curved grid (2). The rooms near the walker are built as it goes (StationInteriors, with colliders).
    [DisallowMultipleComponent]
    public sealed class InteriorWalk : MonoBehaviour
    {
        public TileLibrary library;
        public Camera viewer;

        int place=-1;bool hud=true;
        TileGallery.Stage stage;
        FoundrySettlement foundry;GameObject foundryRoot;
        StationInteriors interiors;
        MagBootWalker walker;
        GUIStyle style;
        TileLook.Saved saved;

        void OnEnable()
        {
            if(!Application.isPlaying||library==null||viewer==null)return;
            saved=TileLook.ApplyEnvironment(library);
            Shader.SetGlobalFloat("_TileClay",0);Shader.SetGlobalFloat("_TileOverlay",0);
            viewer.nearClipPlane=0.05f;viewer.farClipPlane=400;viewer.fieldOfView=75;
            walker=new GameObject("Walker").AddComponent<MagBootWalker>();walker.eye=viewer;
            Load(0);
        }
        void OnDisable()
        {
            Unload();
            if(walker!=null)Destroy(walker.gameObject);
            Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
            if(library!=null)TileLook.Restore(saved);
        }

        void Load(int which)
        {
            Unload();place=which;
            if(which==0){stage=TileGallery.Build(transform,library,false);interiors=stage.interiors;}
            else
            {
                foundryRoot=new GameObject("Foundry example");foundryRoot.transform.SetParent(transform,false);
                TileLook.AddLights(foundryRoot.transform);
                foundry=new FoundrySettlement(foundryRoot.transform,library.materials,library.clearGlass);
                foundry.Rebuild(SettlementExamples.Foundry());interiors=foundry.Interiors;
            }
            interiors.colliders=true;interiors.ringCentre=walker.transform;
            Respawn();
        }
        void Unload()
        {
            stage?.Destroy();stage=null;
            if(foundry!=null){foundry.Dispose();foundry=null;}
            if(foundryRoot!=null){Destroy(foundryRoot);foundryRoot=null;}
            interiors=null;
        }
        void Respawn()
        {
            var near=place==0?TileGallery.Room.position:Vector3.zero;
            if(interiors==null||!interiors.Spawn(near,out var feet,out var up,out var forward,out _,out _))return;
            interiors.ShowAround(feet,interiors.radius);Physics.SyncTransforms();
            walker.Place(feet,up,forward);
        }

        void Update()
        {
            var kb=Keyboard.current;var mouse=Mouse.current;
            if(kb!=null)
            {
                if(kb.digit1Key.wasPressedThisFrame)Load(0);
                if(kb.digit2Key.wasPressedThisFrame)Load(1);
                if(kb.rKey.wasPressedThisFrame)Respawn();
                if(kb.tabKey.wasPressedThisFrame)hud=!hud;
                if(kb.escapeKey.wasPressedThisFrame){Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}
            }
            if(mouse!=null&&mouse.leftButton.wasPressedThisFrame&&Cursor.lockState!=CursorLockMode.Locked){Cursor.lockState=CursorLockMode.Locked;Cursor.visible=false;}
        }

        void OnGUI()
        {
            if(!hud||walker==null)return;
            float s=Mathf.Clamp(Screen.height/900f,0.8f,1.6f);
            style??=new GUIStyle(GUI.skin.label){richText=true};style.fontSize=Mathf.RoundToInt(13*s);
            GUI.color=new Color(0,0,0,0.55f);GUI.DrawTexture(new Rect(12*s,12*s,390*s,128*s),Texture2D.whiteTexture);GUI.color=Color.white;
            string where=interiors!=null&&interiors.CellAt(walker.EyePosition,out var cell)?interiors.Structure.cells[cell].family+" "+cell:"outside";
            GUI.Label(new Rect(22*s,18*s,380*s,120*s),
                $"<b>Interior walk — {(place==0?"flat station":"Foundry example (curved)")}</b>\n"+
                "Click: capture mouse · Esc: release · WASD walk · Shift run\n"+
                "Walk into a wall to step onto it · Space: push off where you look\n"+
                "R respawn · 1 flat station · 2 curved Foundry · Tab hides this\n"+
                $"{(walker.Floating?"drifting":"boots on")} · {where} · {interiors?.Count??0} rooms built",style);
        }
    }
}
