using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DeathStar
{
    [DisallowMultipleComponent]
    public sealed class BlockFoundry : MonoBehaviour
    {
        public Camera viewer;
        public Material hullMaterial, previewMaterial;
        public TileLibrary tileLibrary;
        public TileFamily SelectedFamily {get;set;}=TileFamily.Habitat;
        [SerializeField,UnityEngine.Serialization.FormerlySerializedAs("debugColors")] DebugView debugView;
        public DebugView DebugMode
        {
            get=>debugView;
            set{debugView=value;Settlement?.SetDebugView(value);}
        }
        public bool ColorCoding
        {
            get=>debugView!=DebugView.Off;
            set=>DebugMode=value?DebugView.Family:DebugView.Off;
        }
        public FoundrySettlement Settlement {get;private set;}
        Material[] generatedMaterials;
        GameObject settlementLights;
        TileLook.Saved savedEnvironment;
        public BlockConstruction Model { get; private set; }
        public int Revision { get; private set; }
        public int ExposedFaces { get; private set; }
        public int ChunkCount => chunks.Count;
        public string SavePath => Path.GetFullPath(Path.Combine(Application.dataPath,"../DeathStarRuns/BlockFoundry.json"));
        const int ChunkSize = 8;
        sealed class Chunk
        {
            public GameObject root;
            public Mesh mesh;
            public MeshCollider collider;
            public MeshRenderer renderer;
            public BlockMeshData data;
        }
        readonly Dictionary<Vector3Int,Chunk> chunks = new Dictionary<Vector3Int,Chunk>();
        readonly Dictionary<Vector3Int,HashSet<Vector3Int>> groups = new Dictionary<Vector3Int,HashSet<Vector3Int>>();
        readonly List<BlockSave> undo = new List<BlockSave>(), redo = new List<BlockSave>();
        readonly LineRenderer[] outline = new LineRenderer[12];
        static readonly int[,] Edges = { {0,1},{2,3},{4,5},{6,7},{0,2},{1,3},{4,6},{5,7},{0,4},{1,5},{2,6},{3,7} };
        Material lineMaterial;
        BlockConstruction previewModel;
        BlockFace previewFace;
        bool previewRemove;
        TileFamily previewFamily;
        string previewText;
        int previewRevision=-1;
        readonly Dictionary<Vector3Int,Vector3> transitionFrom=new Dictionary<Vector3Int,Vector3>();
        readonly HashSet<Vector3Int> animatedChunks=new HashSet<Vector3Int>();
        float transitionStarted;
        public const float MorphDuration=0.1f;
        public bool IsMorphing=>transitionFrom.Count>0;
        public bool IsSettling=>Model!=null&&Model.RelaxationPending;
        Vector3 focus, targetFocus;
        float distance=6,targetDistance=6,yaw=35,pitch=25;
        bool showHud=true,removeMode,hasHover;
        BlockFace hovered;
        string message="Click a face to attach a block.", failure;
        float messageUntil;
        GUIStyle title,kicker,body,button,active,small;
        float UiScale => Mathf.Clamp(Screen.height/850f,0.72f,1.4f);

        void OnEnable()
        {
            if (!Application.isPlaying) return;
            Initialize();
        }
        public void Initialize(BlockConstruction construction=null)
        {
            if(Model!=null)return;
            if (viewer==null || hullMaterial==null || previewMaterial==null)
            { failure="Open Death Star / Open Block Foundry to use the configured scene."; return; }
            failure=null;
            Model = construction??new BlockConstruction();
            InitializeSettlement();
            lineMaterial = new Material(previewMaterial);
            for(int i=0;i<outline.Length;i++)
            {
                var go=new GameObject("Face preview "+i); go.transform.SetParent(transform,false);
                var line=go.AddComponent<LineRenderer>(); line.sharedMaterial=lineMaterial;
                line.useWorldSpace=false; line.positionCount=2; line.numCapVertices=3;
                line.enabled=false; outline[i]=line;
            }
            RebuildAll(); FrameBuild(true);
        }

        void InitializeSettlement()
        {
            var materials=tileLibrary!=null?tileLibrary.materials:null;
            if(materials==null||materials.Length!=TileMesh.SlotCount)
            {
                generatedMaterials=new Material[TileMesh.SlotCount];
                for(int i=0;i<generatedMaterials.Length;i++)
                {
                    var slot=(TileSlot)i;
                    var m=new Material(Shader.Find(slot==TileSlot.Glass?"DeathStar/Tile Glass":"DeathStar/Tile Surface"));
                    TileLook.Configure(m,slot);generatedMaterials[i]=m;
                }
                materials=generatedMaterials;
            }
            Settlement=new FoundrySettlement(transform,materials);
            Settlement.SetDebugView(debugView);
            settlementLights=new GameObject("Settlement lighting");settlementLights.transform.SetParent(transform,false);
            TileLook.AddLights(settlementLights.transform);savedEnvironment=TileLook.ApplyEnvironment(tileLibrary);
            if(tileLibrary!=null&&tileLibrary.post!=null)
            {
                var volume=settlementLights.AddComponent<UnityEngine.Rendering.Volume>();volume.isGlobal=true;volume.sharedProfile=tileLibrary.post;
            }
        }

        static Vector3Int ChunkKey(Vector3Int p) => new Vector3Int(Mathf.FloorToInt(p.x/(float)ChunkSize),Mathf.FloorToInt(p.y/(float)ChunkSize),Mathf.FloorToInt(p.z/(float)ChunkSize));
        void AddGroup(Vector3Int cell)
        {
            var key=ChunkKey(cell);
            if(!groups.TryGetValue(key,out var group)){group=new HashSet<Vector3Int>();groups.Add(key,group);}
            group.Add(cell);
        }
        void RebuildAll()
        {
            transitionFrom.Clear();animatedChunks.Clear();
            groups.Clear(); foreach(var cell in Model.Cells)AddGroup(cell);
            var keys=new HashSet<Vector3Int>(chunks.Keys); keys.UnionWith(groups.Keys);
            foreach(var key in keys)RebuildChunk(key);
            Settlement.Rebuild(Model);
            FinishRebuild();
        }
        public Vector3 DisplayCorner(Vector3Int key)
        {
            if(!transitionFrom.TryGetValue(key,out var from))return Model.Corner(key);
            float t=Mathf.Clamp01((Time.unscaledTime-transitionStarted)/MorphDuration);t=t*t*(3-2*t);
            return Vector3.Lerp(from,Model.Corner(key),t);
        }
        public void CompleteMorph()
        {
            transitionFrom.Clear();
            foreach(var key in animatedChunks)RebuildChunk(key);
            animatedChunks.Clear();
        }
        void AdvanceMorph()
        {
            if(!IsMorphing)return;
            if(Time.unscaledTime-transitionStarted>=MorphDuration){CompleteMorph();return;}
            foreach(var key in animatedChunks)RebuildChunk(key);
        }
        void Commit(BlockConstruction next)
        {
            foreach(var cell in next.ChangedCells)animatedChunks.Add(ChunkKey(cell));
            Model=next;
            // Architectural joints appear together. A transient collapsed picking cell must not disagree
            // with the finished pad, tube or hangar that the player sees.
            transitionFrom.Clear();
            transitionStarted=Time.unscaledTime;
            groups.Clear();foreach(var cell in Model.Cells)AddGroup(cell);
            foreach(var key in animatedChunks)RebuildChunk(key);
            if(!IsMorphing)animatedChunks.Clear();
            Settlement.Rebuild(Model,next.ChangedCells);
            FinishRebuild();
        }
        void FinishRebuild()
        {
            Revision++; ExposedFaces=0;
            foreach(var chunk in chunks.Values)ExposedFaces+=chunk.data.faces.Count;
            hasHover=false;previewRevision=-1;previewModel=null;HidePreview();
        }
        void RebuildChunk(Vector3Int key)
        {
            if(!groups.TryGetValue(key,out var group)||group.Count==0)
            {
                if(chunks.TryGetValue(key,out var old)){Release(old.root);Release(old.mesh);chunks.Remove(key);}
                return;
            }
            var data=BlockMeshData.Build(Model,group,DisplayCorner);
            for(int i=0;i<data.vertices.Count;i++)data.vertices[i]*=TileBuilder.Cell;
            if(!chunks.TryGetValue(key,out var chunk))
            {
                chunk=new Chunk{root=new GameObject("Hull chunk "+key),mesh=new Mesh{name="Adaptive hull "+key}};
                chunk.root.transform.SetParent(transform,false);
                chunk.collider=chunk.root.AddComponent<MeshCollider>();
                // Preserve triangle ordering: the collider triangle index is the face-picking contract.
                chunk.collider.cookingOptions=MeshColliderCookingOptions.CookForFasterSimulation | MeshColliderCookingOptions.UseFastMidphase;
                chunk.root.AddComponent<MeshFilter>().sharedMesh=chunk.mesh;
                chunk.renderer=chunk.root.AddComponent<MeshRenderer>();chunk.renderer.sharedMaterial=hullMaterial;
                chunks.Add(key,chunk);
            }
            chunk.collider.sharedMesh=null; data.Apply(chunk.mesh); chunk.data=data;
            if(data.triangles.Count>0)chunk.collider.sharedMesh=chunk.mesh;
            chunk.renderer.enabled=false; // Stable low-detail cell surfaces are picking proxies only.
        }

        void Remember(BlockSave before)
        {
            undo.Add(before); if(undo.Count>48)undo.RemoveAt(0);redo.Clear();
        }
        public bool EditFace(BlockFace face,bool remove)
        {
            if(Model==null || !Model.Contains(face.cell) || face.side<0 || face.side>=6)return false;
            var target=remove?face.cell:face.cell+BlockConstruction.Steps[face.side];
            if(remove && Model.Count==1){Notice("Keep one seed block so you can continue building.");return false;}
            if(!remove && !Model.CanAdd(target)){Notice("That space is occupied or outside the prototype's build limit.");return false;}
            PreparePreview(face,remove);
            if(previewModel==null){Notice(Model.LastError??"This face cannot be extended safely.");return false;}
            var before=Model.Capture();
            var next=previewModel;Remember(before);Commit(next);return true;
        }
        public void Undo()
        {
            if(undo.Count==0)return;
            redo.Add(Model.Capture());var state=undo[undo.Count-1];undo.RemoveAt(undo.Count-1);Model.Restore(state);RebuildAll();
        }
        public void Redo()
        {
            if(redo.Count==0)return;
            undo.Add(Model.Capture());var state=redo[redo.Count-1];redo.RemoveAt(redo.Count-1);Model.Restore(state);RebuildAll();
        }
        public void NewBuild()
        {
            Remember(Model.Capture());Model=new BlockConstruction();
            RebuildAll();FrameBuild();Notice("One block. Six directions. Start building.");
        }
        public void LoadExample(int example)
        {
            Remember(Model.Capture());
            if(example==4){Model=SettlementExamples.Foundry();RebuildAll();FrameBuild();Notice("Families grow into a curved settlement. Add or remove cells to reshape it.");return;}
            if(example==5){Model=SettlementExamples.Ridges();RebuildAll();FrameBuild();Notice("Three ridges meet at a crown above a cube corner. Press C twice for the shape view.");return;}
            if(example==6){Model=SettlementExamples.Station();RebuildAll();FrameBuild();Notice("A whole station: every family on every shape. C cycles family and shape colours.");return;}
            if(example==7){Model=SettlementExamples.Truss();RebuildAll();FrameBuild();Notice("Space frames: a girder ridge to a lattice tower, a boom, a frame, a pit and a bridge.");return;}
            var cells=new List<Vector3Int>();
            int n=example==2?12:8, height=example==0?2:example==1?6:12;
            for(int z=0;z<n;z++)for(int y=0;y<height;y++)for(int x=0;x<n;x++)
            {
                // A hollow shell with an open docking bay demonstrates actual empty space.
                if(example==2)
                {
                    if(x>0&&x<n-1&&y>0&&y<height-1&&z>0&&z<n-1)continue;
                    if(z==0&&x>=4&&x<=7&&y>=4&&y<=7)continue;
                }
                if(example==1)
                {int inset=y/2;if(x<inset||x>=n-inset||z<inset||z>=n-inset)continue;}
                cells.Add(new Vector3Int(x-n/2,y-height/2,z-n/2));
            }
            if(example==3)
            {
                cells.Clear();
                for(int z=-4;z<4;z++)for(int y=-4;y<4;y++)for(int x=-4;x<4;x++)cells.Add(new Vector3Int(x,y,z));
            }
            Model=BlockConstruction.FromCells(cells);
            RebuildAll();FrameBuild();Notice("Build outward on the original sphere grid.");
        }
        public void SaveBuild()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SavePath));
                string temp=SavePath+".tmp";File.WriteAllText(temp,JsonUtility.ToJson(Model.Capture(),true));
                if(File.Exists(SavePath))File.Replace(temp,SavePath,null);else File.Move(temp,SavePath);
                Notice("Saved to DeathStarRuns/BlockFoundry.json");
            }
            catch(Exception error){Notice("Save failed: "+error.Message);}
        }
        public void LoadBuild()
        {
            try
            {
                var saved=JsonUtility.FromJson<BlockSave>(File.ReadAllText(SavePath));
                var before=Model.Capture();Model.Restore(saved);Remember(before);RebuildAll();FrameBuild();Notice("Saved construction loaded.");
            }
            catch(Exception error){Notice("Load failed: "+error.Message);}
        }
        void Notice(string value){message=value;messageUntil=Time.unscaledTime+7;}

        public bool Pick(Ray ray,out BlockFace face,out float distanceToFace)
        {
            face=default;distanceToFace=float.PositiveInfinity;bool found=false;
            foreach(var chunk in chunks.Values)
            {
                if(chunk.collider.sharedMesh==null || !chunk.collider.Raycast(ray,out var hit,distanceToFace))continue;
                int index=hit.triangleIndex/BlockMeshData.TrianglesPerFace;
                if(index<0 || index>=chunk.data.faces.Count)continue;
                face=chunk.data.faces[index];distanceToFace=hit.distance;found=true;
            }
            return found;
        }
        public Bounds BuildBounds
        {
            get
            {
                if(Settlement!=null)return Settlement.Bounds;
                bool first=true;Bounds result=default;
                foreach(var chunk in chunks.Values)
                {
                    if(!chunk.renderer.enabled)continue;
                    if(first){result=chunk.renderer.bounds;first=false;}else result.Encapsulate(chunk.renderer.bounds);
                }
                return result;
            }
        }
        public void FrameBuild(bool snap=false)
        {
            Bounds bounds=BuildBounds;targetFocus=bounds.center;
            targetDistance=Mathf.Max(4,bounds.extents.magnitude/Mathf.Sin(viewer.fieldOfView*Mathf.Deg2Rad*0.5f)*1.25f);
            if(snap){focus=targetFocus;distance=targetDistance;UpdateCamera(true);}
        }
        void Update()
        {
            if(Model==null||failure!=null)return;
            AdvanceMorph();
            var mouse=Mouse.current;var keys=Keyboard.current;
            bool shift=keys!=null&&(keys.leftShiftKey.isPressed||keys.rightShiftKey.isPressed);
            if(keys!=null)
            {
                if(keys.tabKey.wasPressedThisFrame)showHud=!showHud;
                if(keys.homeKey.wasPressedThisFrame)FrameBuild();
                bool ctrl=keys.leftCtrlKey.isPressed||keys.rightCtrlKey.isPressed;
                if(!ctrl&&keys.cKey.wasPressedThisFrame)CycleDebugView();
                if(ctrl&&keys.zKey.wasPressedThisFrame){if(shift)Redo();else Undo();}
                if(ctrl&&keys.yKey.wasPressedThisFrame)Redo();
                if(ctrl&&keys.sKey.wasPressedThisFrame)SaveBuild();
            }
            hasHover=false;HidePreview();
            if(mouse!=null)
            {
                Vector2 position=mouse.position.ReadValue();
                Vector2 gui=new Vector2(position.x,Screen.height-position.y)/UiScale;
                bool overHud=showHud&&new Rect(18,18,286,716).Contains(gui);
                if(!overHud)
                {
                    Vector2 delta=mouse.delta.ReadValue();
                    if(mouse.rightButton.isPressed){yaw+=delta.x*0.2f;pitch=Mathf.Clamp(pitch-delta.y*0.2f,-85,85);}
                    if(mouse.middleButton.isPressed)
                    {float scale=distance*0.0015f;targetFocus-=viewer.transform.right*delta.x*scale+viewer.transform.up*delta.y*scale;}
                    targetDistance=Mathf.Clamp(targetDistance*Mathf.Exp(-mouse.scroll.ReadValue().y*0.07f),1,500);
                    if(!mouse.rightButton.isPressed&&!mouse.middleButton.isPressed&&Pick(viewer.ScreenPointToRay(position),out hovered,out _))
                    {
                        hasHover=true;ShowPreview(hovered,removeMode||shift);
                        if(mouse.leftButton.wasPressedThisFrame)EditFace(hovered,removeMode||shift);
                    }
                }
            }
            UpdateCamera(false);
        }
        void UpdateCamera(bool snap)
        {
            float t=snap?1:1-Mathf.Exp(-Time.unscaledDeltaTime*12);
            focus=Vector3.Lerp(focus,targetFocus,t);distance=Mathf.Lerp(distance,targetDistance,t);
            Quaternion rotation=Quaternion.Euler(pitch,yaw,0);
            viewer.transform.SetPositionAndRotation(focus+rotation*new Vector3(0,0,-distance),rotation);
            // Shift the construction into the open area beside the tool panel.
            viewer.lensShift=new Vector2(showHud?-Mathf.Min(0.15f,155*UiScale/Mathf.Max(1,Screen.width)):0,0);
            viewer.nearClipPlane=0.03f;viewer.farClipPlane=Mathf.Max(1000,distance*4);
        }
        void HidePreview()
        {foreach(var line in outline)if(line!=null)line.enabled=false;}
        void PreparePreview(BlockFace face,bool remove)
        {
            if(previewRevision==Revision&&previewFace.cell==face.cell&&previewFace.side==face.side&&previewRemove==remove&&previewFamily==SelectedFamily)return;
            previewRevision=Revision;previewFace=face;previewRemove=remove;
            previewFamily=SelectedFamily;
            var cell=remove?face.cell:face.cell+BlockConstruction.Steps[face.side];
            Model.TryEdit(cell,remove,out previewModel,SelectedFamily);
            if(previewModel==null){previewText=Model.LastError??"This face cannot be extended safely.";return;}
            previewModel=previewModel.SettleForEdit();
            previewText=remove?"Click to remove the highlighted block":
                $"{FamilyName(SelectedFamily)} · {previewModel.Describe(cell,face.side)} · click to attach, Shift + click to remove";
        }
        void ShowPreview(BlockFace face,bool remove)
        {
            PreparePreview(face,remove);
            Vector3Int cell=remove?face.cell:face.cell+BlockConstruction.Steps[face.side];
            bool valid=previewModel!=null;
            Color color=remove||!valid?new Color(1,0.38f,0.22f):new Color(0.3f,0.95f,1);
            lineMaterial.SetColor("_Color",color);
            var shown=valid&&!remove?previewModel:Model;
            for(int i=0;i<outline.Length;i++)
            {
                var line=outline[i];line.enabled=true;
                line.startWidth=line.endWidth=Mathf.Clamp(distance*0.0018f,0.012f,0.065f);
                line.SetPosition(0,shown.Corner(cell+BlockConstruction.CornerOffset(Edges[i,0]))*TileBuilder.Cell);
                line.SetPosition(1,shown.Corner(cell+BlockConstruction.CornerOffset(Edges[i,1]))*TileBuilder.Cell);
            }
        }
        void Styles()
        {
            if(title!=null)return;
            title=new GUIStyle(GUI.skin.label){fontSize=27,fontStyle=FontStyle.Bold,normal={textColor=new Color(0.91f,0.95f,0.97f)}};
            kicker=new GUIStyle(GUI.skin.label){fontSize=10,normal={textColor=new Color(0.34f,0.8f,0.86f)}};
            body=new GUIStyle(GUI.skin.label){fontSize=12,wordWrap=true,normal={textColor=new Color(0.68f,0.75f,0.8f)}};
            small=new GUIStyle(body){fontSize=10};
            button=new GUIStyle(GUI.skin.button){fontSize=12,alignment=TextAnchor.MiddleCenter};
            active=new GUIStyle(button){normal={textColor=new Color(0.3f,1,1)},fontStyle=FontStyle.Bold};
        }
        static void Panel(Rect rect,Color color){Color old=GUI.color;GUI.color=color;GUI.DrawTexture(rect,Texture2D.whiteTexture);GUI.color=old;}
        void OnGUI()
        {
            if(!showHud)return;Styles();var previous=GUI.matrix;GUI.matrix=Matrix4x4.Scale(Vector3.one*UiScale);
            Panel(new Rect(18,18,286,716),new Color(0.025f,0.04f,0.055f,0.97f));
            Panel(new Rect(18,18,3,716),new Color(0.22f,0.7f,0.76f));
            GUI.Label(new Rect(36,32,250,20),"ORBITAL FOUNDRY  /  CONSTRUCTION",kicker);
            GUI.Label(new Rect(34,53,260,40),"Block by block",title);
            GUI.Label(new Rect(36,98,246,38),"Choose a family. Place a cell.\nNeighbours shape the architecture.",body);
            if(Model==null){GUI.Label(new Rect(36,145,246,100),failure??"Starting…",body);GUI.matrix=previous;return;}
            if(GUI.Button(new Rect(36,148,119,32),"+  Build",removeMode?button:active))removeMode=false;
            if(GUI.Button(new Rect(163,148,119,32),"−  Remove",removeMode?active:button))removeMode=true;
            GUI.Label(new Rect(36,193,246,20),"SETTLEMENT FAMILY",kicker);
            var families=TileGallery.Families;
            for(int i=0;i<families.Length;i++)
            {
                float x=36+(i%2)*127,y=218+(i/2)*24;
                var color=TileLook.FamilyColor(families[i]);
                Panel(new Rect(x+1,y+6,10,10),color.a>0?color.gamma:new Color(0.55f,0.55f,0.55f));
                if(GUI.Button(new Rect(x+15,y,104,22),FamilyName(families[i]),SelectedFamily==families[i]?active:button))SelectedFamily=families[i];
            }
            if(GUI.Button(new Rect(36,371,246,28),"Debug view [C]: "+debugView,ColorCoding?active:button))CycleDebugView();
            GUI.enabled=undo.Count>0;
            if(GUI.Button(new Rect(36,414,119,28),"Undo",button))Undo();
            GUI.enabled=redo.Count>0;
            if(GUI.Button(new Rect(163,414,119,28),"Redo",button))Redo();GUI.enabled=true;
            GUI.Label(new Rect(36,452,246,20),"TRY A CONSTRUCTION",kicker);
            string[] examples={"Slab","Wedge","Orbital shell"};
            for(int i=0;i<3;i++)if(GUI.Button(new Rect(36+i*84,478,78,28),examples[i],button))LoadExample(i);
            if(GUI.Button(new Rect(36,515,78,28),"Sphere",button))LoadExample(3);
            if(GUI.Button(new Rect(120,515,78,28),"Settlement",button))LoadExample(4);
            if(GUI.Button(new Rect(204,515,78,28),"Ridges",button))LoadExample(5);
            if(GUI.Button(new Rect(36,552,78,28),"Station",button))LoadExample(6);
            if(GUI.Button(new Rect(120,552,78,28),"Truss",button))LoadExample(7);
            if(GUI.Button(new Rect(36,595,78,27),"New build",button))NewBuild();
            if(GUI.Button(new Rect(120,595,78,27),"Save build",button))SaveBuild();
            if(GUI.Button(new Rect(204,595,78,27),"Load build",button))LoadBuild();
            GUI.Label(new Rect(36,632,246,52),"Click: build · Shift + click: remove\nRight drag: orbit · Middle drag: pan\nScroll: zoom · Home: frame · C: colours\nTab: hide panel",small);
            GUI.Label(new Rect(36,690,246,36),$"{Model.Count:N0} blocks  /  {ExposedFaces:N0} exposed faces\nCtrl Z / Y: undo / redo · Ctrl S: save",kicker);
            float width=Screen.width/UiScale;
            string status=Time.unscaledTime<messageUntil?message:hasHover?previewText??"Click to attach a block · Shift + click to remove":"Choose a face to begin · Right drag to orbit";
            if(debugView==DebugView.Family&&hasHover&&Settlement.Structure.TryGet(hovered.cell,out var placed))
                status=$"{TileGallery.Title(placed.family)} · cell {hovered.cell} · axis {placed.axis} · main face {Settlement.Structure.Primary(hovered.cell,placed)}";
            if(debugView==DebugView.Shape)
            {
                status="Grey box · red box under "+ShapeInfo.SmallEdge+" m · orange wedge · magenta apex";
                if(hasHover&&Model.Contains(hovered.cell))
                {
                    var shape=Model.Shape(hovered.cell);var exposure=Model.Exposure(hovered.cell,out bool gable);
                    string kind=shape.kind==CellShape.Box?(shape.Small?"Small box":"Box"):$"{shape.kind} {exposure}{(gable?" + gable":"")}{(shape.spine>=0?" · spine "+"xyz"[shape.spine]:"")}";
                    status=$"{kind} · edges {shape.minEdge:0.0}–{shape.maxEdge:0.0} m · cell {hovered.cell}";
                }
            }
            Panel(new Rect(325,Screen.height/UiScale-65,Mathf.Max(200,width-345),42),new Color(0.025f,0.04f,0.055f,0.87f));
            GUI.Label(new Rect(341,Screen.height/UiScale-56,Mathf.Max(180,width-375),32),status,body);
            GUI.matrix=previous;
        }
        void OnDisable()
        {
            if(Settlement!=null){Settlement.Dispose();Settlement=null;TileLook.Restore(savedEnvironment);}
            if(settlementLights!=null)Release(settlementLights);
            if(generatedMaterials!=null){foreach(var m in generatedMaterials)Release(m);generatedMaterials=null;}
            foreach(var chunk in chunks.Values){if(chunk.root!=null)Release(chunk.root);if(chunk.mesh!=null)Release(chunk.mesh);}
            chunks.Clear();groups.Clear();
            foreach(var line in outline)if(line!=null)Release(line.gameObject);
            if(lineMaterial!=null)Release(lineMaterial);
            transitionFrom.Clear();animatedChunks.Clear();previewModel=null;previewRevision=-1;
            undo.Clear();redo.Clear();Model=null;
        }
        void CycleDebugView()=>DebugMode=(DebugView)(((int)debugView+1)%3);
        static string FamilyName(TileFamily f)=>f==TileFamily.LifeSupport?"Life Support":f==TileFamily.Pad?"Landing Pad":f.ToString();
        static void Release(UnityEngine.Object o){if(o==null)return;if(Application.isPlaying)Destroy(o);else DestroyImmediate(o);}
    }
}
