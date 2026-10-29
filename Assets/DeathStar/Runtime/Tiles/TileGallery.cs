using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DeathStar
{
    // Review stage for the settlement tiles, phase 1 (massing): a floating station fragment and nine specimens in
    // space, no floor and no up. Built in edit mode too; nothing generated is saved with the scene.
    [ExecuteAlways,DisallowMultipleComponent]
    public sealed class TileGallery : MonoBehaviour
    {
        public TileLibrary library;
        public Camera viewer;

        public static readonly TileFamily[] Families=(TileFamily[])Enum.GetValues(typeof(TileFamily));
        public static string Title(TileFamily f)=>f switch
        {
            TileFamily.Power=>"Power & Thermal",TileFamily.LifeSupport=>"Life Support",TileFamily.Storage=>"Storage & Logistics",
            TileFamily.Defense=>"Defense & Security",TileFamily.Command=>"Command & Comms",TileFamily.Hull=>"Hull & Structure",
            TileFamily.Pad=>"Landing Pad",_=>f.ToString()
        };

        public readonly struct Label{public readonly string text;public readonly Vector3 position;public Label(string t,Vector3 p){text=t;position=p;}}
        public readonly struct View
        {
            public readonly Vector3 position,target,up;public readonly float fov;
            public View(Vector3 position,Vector3 target,Vector3 up,float fov=38){this.position=position;this.target=target;this.up=up;this.fov=fov;}
        }
        public sealed class Stage
        {
            public GameObject root;
            public TileStructure structure;
            public readonly List<TileStructure.Specimen> specimens=new List<TileStructure.Specimen>();
            public readonly List<Mesh> meshes=new List<Mesh>();
            public readonly List<Label> labels=new List<Label>();
            public readonly Dictionary<TileFamily,(int triangles,Bounds bounds)> stats=new Dictionary<TileFamily,(int,Bounds)>();
            // Each cell's tile renderers (all levels of detail), and the rooms built near the viewer.
            public readonly Dictionary<Vector3Int,MeshRenderer[]> renderers=new Dictionary<Vector3Int,MeshRenderer[]>();
            public StationInteriors interiors;
            public int triangles,farTriangles,tiles;
            public double buildMs;
            public void Destroy()
            {
                StationVoxelGi.Invalidate();
                foreach(var mesh in meshes)Release(mesh);
                meshes.Clear();
                if(root!=null)Release(root);
            }
        }
        static void Release(UnityEngine.Object o){if(Application.isPlaying)Destroy(o);else DestroyImmediate(o);}

        public static Stage Build(Transform parent,TileLibrary lib,bool dontSave)
        {
            var sw=System.Diagnostics.Stopwatch.StartNew();
            var stage=new Stage();
            stage.structure=TileStructure.Showcase(stage.specimens);
            var builder=new TileBuilder(stage.structure);
            var root=Node("Tile Gallery Stage",parent,dontSave);stage.root=root;
            root.AddComponent<StationKit>();   // props near the camera on the tiles' plates
            var station=Node("Station",root.transform,dontSave);
            var specimenCells=new Dictionary<Vector3Int,TileFamily>();
            foreach(var sp in stage.specimens)specimenCells[sp.TileCell]=sp.family;
            foreach(var pair in stage.structure.cells)
            {
                var t=builder.Build(pair.Key);
                if(t.VertexCount==0)continue;
                string name=pair.Value.family+" "+pair.Key;
                var (mesh,far)=t.ToMeshes(name);var mid=t.Mid;
                mesh.hideFlags=HideFlags.DontSave;stage.meshes.Add(mesh);
                if(far!=null){far.hideFlags=HideFlags.DontSave;stage.meshes.Add(far);}
                if(mid!=null){mid.hideFlags=HideFlags.DontSave;stage.meshes.Add(mid);}
                var tile=AddTile(station,name,mesh,mid,far,TileStructure.Center(pair.Key),lib,dontSave);
                stage.renderers[pair.Key]=tile.GetComponentsInChildren<MeshRenderer>();
                stage.triangles+=t.TriangleCount;stage.farTriangles+=far!=null?Triangles(far):t.TriangleCount;stage.tiles++;
                if(specimenCells.TryGetValue(pair.Key,out var fam)&&fam==pair.Value.family)stage.stats[fam]=(t.TriangleCount,t.Bounds);
            }
            foreach(var sp in stage.specimens)stage.labels.Add(new Label(Title(sp.family),TileStructure.Center(sp.TileCell)+(Vector3)sp.up*5f));

            // Reference people, each standing on whatever surface is under their feet.
            var figure=TileBuilder.Figure(0).ToMesh("Figure");figure.hideFlags=HideFlags.DontSave;stage.meshes.Add(figure);
            var figure2=TileBuilder.Figure(1).ToMesh("Figure 2");figure2.hideFlags=HideFlags.DontSave;stage.meshes.Add(figure2);
            var people=Node("People",root.transform,dontSave);
            void Person(Mesh m,Vector3 at,Vector3 up,Vector3 facing)=>Add(people,"Person",m,at,Quaternion.LookRotation(Vector3.ProjectOnPlane(facing,up),up),lib,dontSave);
            Vector3 Cell(int x,int y,int z)=>TileStructure.Center(new Vector3Int(x,y,z));
            Person(figure,Cell(5,1,2)+new Vector3(0.3f,1.4f,0),Vector3.up,new Vector3(-1,0,-0.4f));
            Person(figure2,Cell(11,1,3)+new Vector3(-0.2f,1.4f,0),Vector3.up,new Vector3(-1,0,0.3f));
            Person(figure,Cell(0,1,7)+new Vector3(-3.35f,-1.9f,0.4f),Vector3.left,new Vector3(0,0,-1));
            Person(figure2,Cell(7,6,7)+new Vector3(1.9f,3.6f,0.3f),Vector3.up,new Vector3(-1,0,-1));
            Person(figure,Cell(8,2,10)+new Vector3(0.6f,2.95f,2.0f),Vector3.up,new Vector3(-1,0,-0.3f));
            Person(figure2,Cell(4,-1,3)+new Vector3(-2.5f,-2.7f,-1.667f),Vector3.down,new Vector3(-1,0,0));

            // Craft for scale: pods on a single pad, on the underside pad and nose-in at a hangar mouth, a shuttle on
            // the 2 x 2 pad, a pod on the pad specimen. Each sits with its belly hatch over the pad's airlock.
            var pod=Craft.Pod().ToMesh("Pod");pod.hideFlags=HideFlags.DontSave;stage.meshes.Add(pod);
            var shuttle=Craft.Shuttle().ToMesh("Shuttle");shuttle.hideFlags=HideFlags.DontSave;stage.meshes.Add(shuttle);
            var craft=Node("Craft",root.transform,dontSave);
            void Ship(Mesh m,Vector3 at,Vector3 up,Vector3 forward)=>Add(craft,"Craft",m,at,Quaternion.LookRotation(Vector3.ProjectOnPlane(forward,up),up),lib,dontSave);
            const float padTop=TileBuilder.Half+0.99f;
            Ship(pod,Cell(4,1,0)+Vector3.up*padTop,Vector3.up,new Vector3(1,0,-0.3f));
            Ship(pod,Cell(12,0,10)+Vector3.down*padTop,Vector3.down,new Vector3(0,0,1));
            Ship(pod,Cell(0,0,6)+new Vector3(-1.5f,0,2.5f),Vector3.up,Vector3.right);
            Ship(shuttle,Cell(16,2,6)+new Vector3(2.5f,padTop,2.5f),Vector3.up,Vector3.right);
            foreach(var sp in stage.specimens)
                if(sp.family==TileFamily.Pad)Ship(pod,TileStructure.Center(sp.TileCell)+(Vector3)sp.up*padTop,sp.up,sp.side);

            var lights=Node("Lights",root.transform,dontSave);
            TileLook.AddLights(lights.transform);
            if(dontSave)foreach(Transform t in lights.transform)t.gameObject.hideFlags=HideFlags.DontSave;
            if(lib!=null&&lib.post!=null)
            {
                var grading=Node("Grading",root.transform,dontSave).AddComponent<Volume>();
                grading.isGlobal=true;grading.sharedProfile=lib.post;
            }
            // the view from orbit: chunk massing, baked when first needed
            if(lib!=null)root.AddComponent<StationChunks>().Build(stage.structure,lib.materials[(int)TileSlot.Paint]);
            // rooms round the viewer: in play they follow the camera, the lab places them
            if(lib!=null){stage.interiors=root.AddComponent<StationInteriors>();stage.interiors.Init(stage.structure,builder,lib.materials,lib.clearGlass,stage.renderers);}
            stage.buildMs=sw.Elapsed.TotalMilliseconds;
            StationVoxelGi.Invalidate();
            return stage;
        }
        static GameObject Node(string name,Transform parent,bool dontSave)
        {
            var go=new GameObject(name);if(dontSave)go.hideFlags=HideFlags.DontSave;
            go.transform.SetParent(parent,false);return go;
        }
        // A tile seen from further than about 30 m (a 5 m cube over ~170 px at 1080p) hands its armour carving over to
        // the relief trace on plain armour (the mid level); from about 110 m (~45 px) it takes the far level, with coarse
        // forms and kits.
        public const float MidDetail=0.6f,NearDetail=0.16f;
        static GameObject AddTile(GameObject parent,string name,Mesh near,Mesh mid,Mesh far,Vector3 position,TileLibrary lib,bool dontSave)
        {
            if(far==null)return Add(parent,name,near,position,Quaternion.identity,lib,dontSave).gameObject;
            var go=Node(name,parent.transform,dontSave);go.transform.localPosition=position;
            var r0=Add(go,"Near",near,Vector3.zero,Quaternion.identity,lib,dontSave);
            var r1=mid!=null?Add(go,"Mid",mid,Vector3.zero,Quaternion.identity,lib,dontSave):null;
            var r2=Add(go,"Far",far,Vector3.zero,Quaternion.identity,lib,dontSave);
            SetLods(go.AddComponent<LODGroup>(),r0,r1,r2);
            return go;
        }
        // Near, mid (may be null) and far. Through a narrow band at each handover both levels draw, whole: they show the
        // same plates (the traced ones sit a little behind real ones, so geometry wins), so nothing pops and nothing is
        // dithered. TileSurface takes no LOD cross-fade keyword.
        public static void SetLods(LODGroup group,Renderer near,Renderer mid,Renderer far)
        {
            group.fadeMode=LODFadeMode.CrossFade;group.animateCrossFading=false;
            var lods=mid!=null?new[]{new LOD(MidDetail,new[]{near}),new LOD(NearDetail,new[]{mid}),new LOD(0f,new[]{far})}:
                new[]{new LOD(NearDetail,new[]{near}),new LOD(0f,new[]{far})};
            for(int i=0;i<lods.Length;i++)lods[i].fadeTransitionWidth=0.1f;
            group.SetLODs(lods);group.RecalculateBounds();
        }
        public static int Triangles(Mesh m){long n=0;for(int i=0;i<m.subMeshCount;i++)n+=m.GetIndexCount(i);return (int)(n/3);}
        static MeshRenderer Add(GameObject parent,string name,Mesh mesh,Vector3 position,Quaternion rotation,TileLibrary lib,bool dontSave)
        {
            var go=Node(name,parent.transform,dontSave);go.transform.SetLocalPositionAndRotation(position,rotation);
            go.AddComponent<MeshFilter>().sharedMesh=mesh;
            var r=go.AddComponent<MeshRenderer>();r.sharedMaterials=lib!=null?lib.materials:new Material[0];
            return r;
        }

        // ---------------------------------------------------------------- views shared with the lab
        static Vector3 C(float x,float y,float z)=>new Vector3(x,y,z)*TileBuilder.Cell;
        static readonly Vector3 FragmentCenter=C(8f,2f,5.5f);
        public static View Station=>new View(new Vector3(-100,125,-195),new Vector3(55,0,-18),Vector3.up,40);
        public static View Fragment=>new View(FragmentCenter+new Vector3(-62,52,-70),FragmentCenter+new Vector3(2,-3,0),Vector3.up,40);
        public static View Trench=>new View(C(-1.6f,1.3f,2.5f),C(10f,0.6f,2.6f),Vector3.up,58);
        public static View Under=>new View(FragmentCenter+new Vector3(-45,-58,-55),FragmentCenter+new Vector3(4,-9,2),Vector3.up,40);
        public static View Surface=>new View(C(-2f,4.5f,-3f),C(8f,3.5f,7f),Vector3.up,46);
        public static View EndFace=>new View(C(23f,4.5f,1f),C(15f,2f,6.5f),Vector3.up,46);
        public static View Dish=>new View(C(14.5f,2.8f,-6.5f),C(10.5f,0.6f,-0.9f),Vector3.up,44);
        public static View HangarEnd=>new View(C(-8f,2.4f,4f),C(-0.4f,0.5f,7.5f),Vector3.up,44);
        public static View CraftView=>new View(new Vector3(110,27,10),new Vector3(80,12,33),Vector3.up,42);
        public static View PodView=>new View(new Vector3(4,17,-17),new Vector3(21,8,1),Vector3.up,40);
        public static View Seams=>new View(C(2.6f,0.9f,-4.6f),C(6.2f,0.4f,0f),Vector3.up,44);
        // Inside a home in the trench's far wall (greybox interiors), standing on its deck at eye height: its windows and
        // the hatch to the next home, which the corridor bridging the trench opens into.
        public static View Room=>new View(new Vector3(31.8f,4.25f,21.9f),new Vector3(27.6f,4.1f,18.2f),Vector3.up,75);
        // Close-up of a specimen from the diagonal the sun lights best (or the opposite one), framed with its own up.
        public static View SpecimenView(in TileStructure.Specimen sp,bool back)
        {
            Vector3 up=sp.up,side=sp.side,third=TileStructure.Cross(sp.side,sp.up);
            var target=TileStructure.Center(sp.TileCell)+up*1.2f;
            Vector3 best=Vector3.zero;float score=-9;
            foreach(float a in new[]{-1f,1f})foreach(float b in new[]{-1f,1f})
            {
                var d=(side*a*0.55f+third*b*0.75f+up*0.55f).normalized;
                float sc=Vector3.Dot(d,TileLook.KeyFrom);if(sc>score){score=sc;best=d;}
            }
            if(back)best=Vector3.Reflect(-best,up);
            return new View(target+best*19f,target,up,34);
        }

        // ---------------------------------------------------------------- scene behaviour
        Stage stage;
        Volume grayscale;
        VolumeProfile grayProfile;
        bool showHud=true,showLabels,gray,coded=true;
        // Fly: the camera moves itself (WASD along its own axes) instead of orbiting a focus, to go inside.
        bool fly;Vector3 eye;float flySpeed=2.5f;
        Vector3 focus,targetFocus;
        Quaternion rot=Quaternion.identity,targetRot=Quaternion.identity;
        float distance=80,targetDistance=80,fov=38;
        GUIStyle hudStyle,labelStyle;

        void OnEnable()
        {
            if(library==null)return;
            stage=Build(transform,library,!Application.isPlaying);
            TileLook.ApplyEnvironment(library);
            Shader.SetGlobalFloat("_TileClay",0);
            Shader.SetGlobalFloat("_TileOverlay",coded?1:0);
            if(!Application.isPlaying)return;
            if(stage.interiors!=null&&viewer!=null)stage.interiors.ringCentre=viewer.transform;
            grayProfile=ScriptableObject.CreateInstance<VolumeProfile>();
            grayProfile.Add<ColorAdjustments>(true).saturation.Override(-100f);
            grayscale=new GameObject("Grayscale check").AddComponent<Volume>();grayscale.transform.SetParent(stage.root.transform,false);
            grayscale.isGlobal=true;grayscale.priority=10;grayscale.weight=0;grayscale.sharedProfile=grayProfile;
            Go(Fragment,true);
        }
        void OnDisable()
        {
            stage?.Destroy();stage=null;
            Shader.SetGlobalFloat("_TileOverlay",0);
            if(grayProfile!=null){Release(grayProfile);grayProfile=null;}
        }

        void Go(View v,bool snap)
        {
            targetFocus=v.target;targetDistance=(v.position-v.target).magnitude;
            targetRot=Quaternion.LookRotation(v.target-v.position,v.up);fov=v.fov;
            if(snap){focus=targetFocus;distance=targetDistance;rot=targetRot;}
        }
        void Fly(bool on)
        {
            if(on)eye=focus-rot*Vector3.forward*distance;
            else{focus=targetFocus=eye+rot*Vector3.forward*distance;targetDistance=distance;}
            fly=on;
        }
        void Update()
        {
            if(!Application.isPlaying||viewer==null||stage==null)return;
            var kb=Keyboard.current;var mouse=Mouse.current;float dt=Time.unscaledDeltaTime;
            if(kb!=null)
            {
                if(kb.tabKey.wasPressedThisFrame)showHud=!showHud;
                if(kb.lKey.wasPressedThisFrame)showLabels=!showLabels;
                if(kb.gKey.wasPressedThisFrame){gray=!gray;grayscale.weight=gray?1:0;}
                if(kb.cKey.wasPressedThisFrame){coded=!coded;Shader.SetGlobalFloat("_TileOverlay",coded?1:0);}
                for(int i=0;i<stage.specimens.Count&&i<9;i++)if(kb[Key.Digit1+i].wasPressedThisFrame)Go(SpecimenView(stage.specimens[i],false),false);
                if(kb.minusKey.wasPressedThisFrame&&stage.specimens.Count>9)Go(SpecimenView(stage.specimens[9],false),false);
                if(kb.equalsKey.wasPressedThisFrame&&stage.specimens.Count>10)Go(SpecimenView(stage.specimens[10],false),false);
                if(kb.backspaceKey.wasPressedThisFrame&&stage.specimens.Count>11)Go(SpecimenView(stage.specimens[11],false),false);
                if(kb.digit0Key.wasPressedThisFrame||kb.homeKey.wasPressedThisFrame)Go(Fragment,false);
                if(kb.oKey.wasPressedThisFrame)Go(Station,false);
                if(kb.tKey.wasPressedThisFrame)Go(Trench,false);
                if(kb.uKey.wasPressedThisFrame)Go(Under,false);
                if(kb.qKey.isPressed)targetRot*=Quaternion.Euler(0,0,70*dt);
                if(kb.eKey.isPressed)targetRot*=Quaternion.Euler(0,0,-70*dt);
                if(kb.fKey.wasPressedThisFrame)Fly(!fly);
                if(kb.iKey.wasPressedThisFrame){Go(Room,true);Fly(true);}
                if(fly)
                {
                    var move=Vector3.zero;
                    if(kb.wKey.isPressed)move.z+=1;if(kb.sKey.isPressed)move.z-=1;
                    if(kb.dKey.isPressed)move.x+=1;if(kb.aKey.isPressed)move.x-=1;
                    if(kb.spaceKey.isPressed)move.y+=1;if(kb.leftCtrlKey.isPressed)move.y-=1;
                    eye+=rot*move*(flySpeed*(kb.leftShiftKey.isPressed?4:1)*dt);
                }
            }
            if(mouse!=null)
            {
                Vector2 d=mouse.delta.ReadValue();
                // Trackball: turn about the camera's own axes, so there is no world up to snap to.
                if(mouse.rightButton.isPressed)targetRot*=Quaternion.Euler(-d.y*0.25f,d.x*0.25f,0);
                if(!fly&&mouse.middleButton.isPressed)targetFocus-=(rot*Vector3.right*d.x+rot*Vector3.up*d.y)*distance*0.0015f;
                float scroll=mouse.scroll.ReadValue().y;
                if(Mathf.Abs(scroll)>0.01f)
                {
                    if(fly)flySpeed=Mathf.Clamp(flySpeed*Mathf.Pow(1.25f,Mathf.Sign(scroll)),0.5f,40f);
                    else targetDistance=Mathf.Clamp(targetDistance*Mathf.Pow(0.9f,Mathf.Sign(scroll)),3f,400f);
                }
            }
            float k=1-Mathf.Exp(-dt*10f);
            focus=Vector3.Lerp(focus,targetFocus,k);distance=Mathf.Lerp(distance,targetDistance,k);rot=Quaternion.Slerp(rot,targetRot,k);
            viewer.fieldOfView=Mathf.Lerp(viewer.fieldOfView,fov,k);
            viewer.farClipPlane=fly?400f:distance+260f;
            viewer.transform.SetPositionAndRotation(fly?eye:focus-rot*Vector3.forward*distance,rot);
        }

        void OnGUI()
        {
            if(!Application.isPlaying||viewer==null||stage==null)return;
            float s=Mathf.Clamp(Screen.height/900f,0.8f,1.6f);
            hudStyle??=new GUIStyle(GUI.skin.label){richText=true};
            labelStyle??=new GUIStyle(GUI.skin.label){alignment=TextAnchor.MiddleCenter,fontStyle=FontStyle.Bold};
            hudStyle.fontSize=Mathf.RoundToInt(13*s);labelStyle.fontSize=Mathf.RoundToInt(14*s);
            if(showLabels)foreach(var label in stage.labels)
            {
                Vector3 p=viewer.WorldToScreenPoint(label.position);if(p.z<0)continue;
                var rect=new Rect(p.x-120*s,Screen.height-p.y-12*s,240*s,24*s);
                GUI.color=new Color(0,0,0,0.7f);GUI.Label(new Rect(rect.x+1,rect.y+1,rect.width,rect.height),label.text,labelStyle);
                GUI.color=Color.white;GUI.Label(rect,label.text,labelStyle);
            }
            if(!showHud)return;
            int legend=coded?Families.Length:0;
            GUI.color=new Color(0,0,0,0.55f);GUI.DrawTexture(new Rect(12*s,12*s,360*s,(170+legend*19)*s),Texture2D.whiteTexture);GUI.color=Color.white;
            for(int i=0;i<legend;i++)
            {
                var c=TileLook.FamilyColor(Families[i]);float y=(180+i*19)*s;
                GUI.color=c.a>0?c.gamma:new Color(0.55f,0.55f,0.55f);GUI.DrawTexture(new Rect(24*s,y+3*s,14*s,14*s),Texture2D.whiteTexture);
                GUI.color=Color.white;GUI.Label(new Rect(46*s,y,300*s,20*s),Title(Families[i]),hudStyle);
            }
            GUI.Label(new Rect(22*s,18*s,350*s,160*s),
                "<b>Settlement tiles — phase 1: massing</b>\n"+
                "1–9 - = Backspace specimens · 0 fragment · O station · T trench · U underside\n"+
                "Right drag tumble · Q/E roll · middle drag pan · scroll zoom\n"+
                $"F fly {(fly?"on":"off")}: WASD Space Ctrl, Shift fast, scroll speed · I inside a home ({stage.interiors?.Count??0} rooms)\n"+
                $"C colour code {(coded?"on":"off")} · L labels {(showLabels?"on":"off")} · G greyscale {(gray?"on":"off")}\n"+
                $"{stage.tiles} tiles, {stage.triangles/1000}k triangles · Tab hides this",hudStyle);
        }
    }
}
