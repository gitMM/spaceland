using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using Debug=UnityEngine.Debug;

namespace DeathStar
{
    [DisallowMultipleComponent]
    public sealed class DeathStarExperiment : MonoBehaviour
    {
        [Header("Independent experiment assets")]
        public ComputeShader geometry;
        public Material surface;
        public Camera viewer;
        [Header("Whole-planet generation")]
        [Range(16,256)] public int resolution=128;
        [Min(20)] public float radius=200;
        public int seed=1977;
        [Range(0.25f,3)] public float heightStep=1.25f;
        [Range(0.5f,2)] public float detailScale=1;
        public Vector3 sunDirection=new Vector3(-0.6f,0.65f,0.8f);
        [Header("Editing")]
        public PaintTool tool=PaintTool.Raise;
        [Range(1,10)] public float brushSize=2.5f;

        PlanetRenderer renderer;
        NativeArray<CellEdit> edits,buildEdits;
        NativeArray<CellPlan> current,building;
        JobHandle buildJob;
        bool buildingNow,requested,hasPlan,stroke;
        float requestedAt,nextPaint;
        int version,buildingVersion,gridResolution;
        float planetRadius;
        Stopwatch buildTimer;
        readonly Queue<int> brushQueue=new Queue<int>();
        readonly HashSet<int> visited=new HashSet<int>(),touched=new HashSet<int>();
        readonly Dictionary<int,CellEdit> strokeBefore=new Dictionary<int,CellEdit>();
        readonly List<Change[]> undo=new List<Change[]>(),redo=new List<Change[]>();
        struct Change {public int cell;public CellEdit before,after;}
        float yaw=27,pitch=20,altitude,targetAltitude,frameMs;
        Vector4 brush=new Vector4(0,0,0,-1);
        bool showHud=true;
        string failure,seedText;
        GUIStyle title,caption,body,button,selected,field;
        static readonly string[] ToolNames={"1   Armor plate","2   Raise surface","3   Lower surface","4   Cut trench","5   Machinery","6   Erase edits"};
        float UiScale=>Mathf.Clamp(Screen.height/900f,1,1.65f);
        public double LastGenerationMs {get;private set;}
        public double LastUploadMs {get;private set;}
        public int Generation => renderer?.Revision??0;
        public bool IsBuilding => buildingNow||requested;
        public int CellCount => current.IsCreated?current.Length:0;
        public long GpuBytes => renderer?.ResidentBytes??0;
        public uint VisibleTops => renderer?.VisibleTops??0;
        public uint VisibleWalls => renderer?.VisibleWalls??0;
        public uint VisibleBoxes => renderer?.VisibleBoxes??0;

        void OnEnable()
        {
            failure=null;
            try
            {
                if(!SystemInfo.supportsComputeShaders) throw new NotSupportedException("This experiment requires compute shader support.");
                if(geometry==null || surface==null || viewer==null) throw new InvalidOperationException("Use Death Star / Open Experiment to create the configured scene.");
                gridResolution=Mathf.Clamp(resolution,16,256);planetRadius=Mathf.Max(20,radius);
                int count=CubeSphere.Count(gridResolution);
                edits=new NativeArray<CellEdit>(count,Allocator.Persistent);
                buildEdits=new NativeArray<CellEdit>(count,Allocator.Persistent);
                current=new NativeArray<CellPlan>(count,Allocator.Persistent);
                building=new NativeArray<CellPlan>(count,Allocator.Persistent);
                renderer=new PlanetRenderer(geometry,surface,gridResolution,planetRadius);
                altitude=targetAltitude=planetRadius*2.05f;
                seedText=seed.ToString();RequestRegeneration();UpdateCamera(true);
            }
            catch(Exception error){failure=error.Message;Debug.LogException(error,this);}
        }

        public void RequestRegeneration()
        {if(!requested)requestedAt=Time.unscaledTime;requested=true;version++;}

        void Update()
        {
            frameMs=Mathf.Lerp(frameMs,Time.unscaledDeltaTime*1000,0.05f);
            if(failure!=null)return;
            if(buildingNow && buildJob.IsCompleted)
            {
                buildJob.Complete();buildingNow=false;
                // Coalesce strokes while retaining the last complete visible generation.
                if(buildingVersion==version || !hasPlan || stroke)
                {
                    try
                    {
                        var upload=Stopwatch.StartNew();renderer.Publish(building);LastUploadMs=upload.Elapsed.TotalMilliseconds;
                        var swap=current;current=building;building=swap;hasPlan=true;
                        LastGenerationMs=buildTimer.Elapsed.TotalMilliseconds;
                    }
                    catch(Exception error){failure=error.Message;Debug.LogException(error,this);return;}
                }
            }
            HandleInput();
            if(requested && !buildingNow && (Time.unscaledTime-requestedAt>=0.08f || !hasPlan || !stroke))
            {
                edits.CopyTo(buildEdits);buildingVersion=version;requested=false;buildingNow=true;
                buildTimer=Stopwatch.StartNew();
                buildJob=new BuildPlanetPlan{resolution=gridResolution,radius=planetRadius,seed=seed,heightStep=heightStep,
                    edits=buildEdits,plans=building}.Schedule(building.Length,128);
            }
            UpdateCamera(false);
        }

        void LateUpdate()
        {
            if(failure==null && renderer!=null)
                renderer.Draw(viewer,sunDirection.normalized,brush,tool==PaintTool.Trench?new Color(1,0.55f,0.3f):new Color(0.35f,0.9f,1),detailScale);
        }

        void HandleInput()
        {
            var mouse=Mouse.current;var keyboard=Keyboard.current;
            if(keyboard!=null)
            {
                if(keyboard.tabKey.wasPressedThisFrame)showHud=!showHud;
                bool typing=GUI.GetNameOfFocusedControl()=="PlanetSeed";
                if(!typing)
                {
                    if(keyboard.digit1Key.wasPressedThisFrame)tool=PaintTool.Armor;
                    if(keyboard.digit2Key.wasPressedThisFrame)tool=PaintTool.Raise;
                    if(keyboard.digit3Key.wasPressedThisFrame)tool=PaintTool.Lower;
                    if(keyboard.digit4Key.wasPressedThisFrame)tool=PaintTool.Trench;
                    if(keyboard.digit5Key.wasPressedThisFrame)tool=PaintTool.Machinery;
                    if(keyboard.digit6Key.wasPressedThisFrame)tool=PaintTool.Erase;
                    if(keyboard.rKey.wasPressedThisFrame)NewSeed();
                    if(keyboard.homeKey.wasPressedThisFrame)Home();
                    if(keyboard.leftBracketKey.wasPressedThisFrame)brushSize=Mathf.Max(1,brushSize-0.5f);
                    if(keyboard.rightBracketKey.wasPressedThisFrame)brushSize=Mathf.Min(10,brushSize+0.5f);
                    if((keyboard.leftCtrlKey.isPressed||keyboard.rightCtrlKey.isPressed)&&keyboard.zKey.wasPressedThisFrame)UndoStroke();
                    if((keyboard.leftCtrlKey.isPressed||keyboard.rightCtrlKey.isPressed)&&keyboard.yKey.wasPressedThisFrame)RedoStroke();
                }
            }
            brush.w=-1;
            if(mouse==null)return;
            Vector2 position=mouse.position.ReadValue();
            bool overHud=showHud && position.x<326*UiScale && position.y>Screen.height-548*UiScale;
            if(mouse.leftButton.wasReleasedThisFrame)EndStroke();
            if(overHud)return;
            if(mouse.rightButton.isPressed || mouse.middleButton.isPressed)
            {
                Vector2 delta=mouse.delta.ReadValue();
                float sensitivity=0.14f*Mathf.Clamp(targetAltitude/planetRadius,0.12f,1);
                yaw+=delta.x*sensitivity;pitch=Mathf.Clamp(pitch-delta.y*sensitivity,-88,88);
            }
            float wheel=mouse.scroll.ReadValue().y;
            if(Mathf.Abs(wheel)>0.01f)
            {
                if(keyboard!=null && (keyboard.leftShiftKey.isPressed||keyboard.rightShiftKey.isPressed))
                    brushSize=Mathf.Clamp(brushSize+Mathf.Sign(wheel)*0.5f,1,10);
                else targetAltitude=Mathf.Clamp(targetAltitude*Mathf.Exp(-wheel*0.075f),planetRadius*0.105f,planetRadius*5);
            }
            if(hasPlan && Pick(viewer.ScreenPointToRay(position),out Vector3 direction))
            {
                brush=new Vector4(direction.x,direction.y,direction.z,BrushAngle);
                if(mouse.leftButton.isPressed && !mouse.rightButton.isPressed && Time.unscaledTime>=nextPaint)
                {nextPaint=Time.unscaledTime+0.045f;PaintDirection(direction,tool,brushSize);}
            }
        }

        public float BrushAngle => brushSize*1.6f/gridResolution;
        public void Home(){yaw=27;pitch=20;targetAltitude=planetRadius*2.05f;}
        public void SetView(float azimuth,float elevation,float height)
        {yaw=azimuth;pitch=Mathf.Clamp(elevation,-88,88);altitude=targetAltitude=Mathf.Clamp(height,planetRadius*0.105f,planetRadius*5);UpdateCamera(true);}
        void UpdateCamera(bool snap)
        {
            altitude=snap?targetAltitude:Mathf.Lerp(altitude,targetAltitude,1-Mathf.Exp(-Time.unscaledDeltaTime*10));
            Vector3 direction=Quaternion.Euler(-pitch,yaw,0)*Vector3.forward;
            viewer.transform.SetPositionAndRotation(direction*(planetRadius+altitude),Quaternion.LookRotation(-direction,Vector3.up));
            viewer.nearClipPlane=Mathf.Max(0.08f,altitude*0.002f);viewer.farClipPlane=planetRadius*9;
        }

        // Pick the actual stepped skin rather than allocating colliders or selecting the unraised reference sphere.
        public bool Pick(Ray ray,out Vector3 direction)
        {
            direction=default;if(!hasPlan)return false;
            float outer=planetRadius*1.10f;
            float b=Vector3.Dot(ray.origin,ray.direction),c=ray.origin.sqrMagnitude-outer*outer;
            float disc=b*b-c;if(disc<0)return false;
            float begin=Mathf.Max(0,-b-Mathf.Sqrt(disc)),end=-b+Mathf.Sqrt(disc),previous=begin;
            float step=planetRadius/gridResolution*0.45f;
            for(float t=begin;t<=end;t+=step)
            {
                Vector3 p=ray.GetPoint(t);float distance=p.magnitude;if(distance<0.001f)break;
                int id=CubeSphere.FromDirection(p,gridResolution);
                if(distance<=planetRadius+current[id].height)
                {
                    float lo=previous,hi=t;
                    for(int j=0;j<10;j++)
                    {
                        float mid=(lo+hi)*0.5f;Vector3 m=ray.GetPoint(mid);
                        if(m.magnitude<=planetRadius+current[CubeSphere.FromDirection(m,gridResolution)].height)hi=mid;else lo=mid;
                    }
                    direction=ray.GetPoint(hi).normalized;return true;
                }
                previous=t;
            }
            return false;
        }

        public int PaintDirection(Vector3 direction,PaintTool paint,float cells)
        {
            if(!hasPlan)return 0;
            if(!stroke){stroke=true;touched.Clear();strokeBefore.Clear();}
            direction.Normalize();float threshold=Mathf.Cos(cells*1.6f/gridResolution);
            brushQueue.Clear();visited.Clear();
            int start=CubeSphere.FromDirection(direction,gridResolution);brushQueue.Enqueue(start);visited.Add(start);
            int changed=0;
            while(brushQueue.Count>0)
            {
                int id=brushQueue.Dequeue();
                if(id!=start && math.dot(CubeSphere.Direction(id,gridResolution),(float3)direction)<threshold)continue;
                if(touched.Add(id))
                {
                    CellEdit before=edits[id],after=before;
                    switch(paint)
                    {
                        case PaintTool.Armor:after.kind=(int)SurfaceKind.Armor+1;after.steps=0;break;
                        case PaintTool.Raise:after.steps=Mathf.Min(12,after.steps+1);break;
                        case PaintTool.Lower:after.steps=Mathf.Max(-12,after.steps-1);break;
                        case PaintTool.Trench:after.kind=(int)SurfaceKind.Trench+1;after.steps=0;break;
                        case PaintTool.Machinery:after.kind=(int)SurfaceKind.Machinery+1;break;
                        case PaintTool.Erase:after=default;break;
                    }
                    if(after.kind!=before.kind || after.steps!=before.steps)
                    {strokeBefore[id]=before;edits[id]=after;changed++;}
                }
                for(int side=0;side<4;side++)
                {int next=CubeSphere.Neighbour(id,side,gridResolution);if(visited.Add(next))brushQueue.Enqueue(next);}
            }
            if(changed>0)RequestRegeneration();return changed;
        }

        public void EndStroke()
        {
            if(!stroke)return;stroke=false;
            if(strokeBefore.Count>0)
            {
                var changes=new Change[strokeBefore.Count];int i=0;
                foreach(var pair in strokeBefore)changes[i++]=new Change{cell=pair.Key,before=pair.Value,after=edits[pair.Key]};
                undo.Add(changes);if(undo.Count>32)undo.RemoveAt(0);redo.Clear();
            }
            touched.Clear();strokeBefore.Clear();
        }
        public void UndoStroke()
        {EndStroke();if(undo.Count==0)return;var c=undo[undo.Count-1];undo.RemoveAt(undo.Count-1);foreach(var e in c)edits[e.cell]=e.before;redo.Add(c);RequestRegeneration();}
        public void RedoStroke()
        {EndStroke();if(redo.Count==0)return;var c=redo[redo.Count-1];redo.RemoveAt(redo.Count-1);foreach(var e in c)edits[e.cell]=e.after;undo.Add(c);RequestRegeneration();}
        public void NewSeed()
        {EndStroke();seed=unchecked(seed+1);seedText=seed.ToString();RequestRegeneration();}
        public void ResetPaint()
        {EndStroke();for(int i=0;i<edits.Length;i++)edits[i]=default;undo.Clear();redo.Clear();RequestRegeneration();}
        public CellPlan GetCell(int id)=>current[id];
        public CellEdit GetEdit(int id)=>edits[id];

        void OnDisable()
        {
            if(buildingNow)buildJob.Complete();buildingNow=false;hasPlan=false;
            renderer?.Dispose();renderer=null;
            if(edits.IsCreated)edits.Dispose();if(buildEdits.IsCreated)buildEdits.Dispose();
            if(current.IsCreated)current.Dispose();if(building.IsCreated)building.Dispose();
            undo.Clear();redo.Clear();stroke=false;
        }

        void Styles()
        {
            if(title!=null)return;
            title=new GUIStyle(GUI.skin.label){fontSize=25,fontStyle=FontStyle.Bold,normal={textColor=new Color(0.87f,0.92f,0.94f)}};
            caption=new GUIStyle(GUI.skin.label){fontSize=10,normal={textColor=new Color(0.38f,0.73f,0.81f)}};
            body=new GUIStyle(GUI.skin.label){fontSize=12,wordWrap=true,normal={textColor=new Color(0.66f,0.73f,0.78f)}};
            button=new GUIStyle(GUI.skin.button){fontSize=12,alignment=TextAnchor.MiddleLeft,padding=new RectOffset(10,6,4,4)};
            selected=new GUIStyle(button){normal={textColor=new Color(0.4f,0.95f,1)}};
            field=new GUIStyle(GUI.skin.textField){fontSize=13,padding=new RectOffset(8,8,5,5)};
        }
        static void Rect(Rect rect,Color color){Color old=GUI.color;GUI.color=color;GUI.DrawTexture(rect,Texture2D.whiteTexture);GUI.color=old;}
        void OnGUI()
        {
            if(!showHud)return;Styles();
            Matrix4x4 previousMatrix=GUI.matrix;
            GUI.matrix=Matrix4x4.Scale(Vector3.one*UiScale);
            Rect(new Rect(18,18,292,520),new Color(0.025f,0.04f,0.055f,0.96f));
            Rect(new Rect(18,18,3,520),new Color(0.24f,0.62f,0.7f,1));
            GUI.Label(new Rect(36,32,260,20),"EXPERIMENT  /  CUBE SPHERE",caption);
            GUI.Label(new Rect(34,54,264,38),"ORBITAL FOUNDRY",title);
            GUI.Label(new Rect(36,94,250,35),"A procedural mechanical world.\nPaint its skin. Shape its skyline.",body);
            for(int i=0;i<6;i++)
                if(GUI.Button(new Rect(36+(i%2)*132,145+(i/2)*34,126,29),ToolNames[i],(int)tool==i?selected:button)){EndStroke();tool=(PaintTool)i;GUI.FocusControl(null);}
            GUI.Label(new Rect(36,252,250,22),$"Brush   {brushSize:0.0} cells",body);
            brushSize=GUI.HorizontalSlider(new Rect(36,280,250,18),brushSize,1,10);
            GUI.Label(new Rect(36,307,50,22),"SEED",caption);
            GUI.SetNextControlName("PlanetSeed");seedText=GUI.TextField(new Rect(82,303,116,28),seedText??seed.ToString(),field);
            if(GUI.Button(new Rect(204,303,82,28),"Generate",button))
            {if(int.TryParse(seedText,out int value)){seed=value;EndStroke();RequestRegeneration();GUI.FocusControl(null);}}
            if(GUI.Button(new Rect(36,341,79,27),"Undo",button))UndoStroke();
            if(GUI.Button(new Rect(121,341,79,27),"Redo",button))RedoStroke();
            if(GUI.Button(new Rect(206,341,80,27),"Reset paint",button))ResetPaint();
            GUI.Label(new Rect(36,383,252,56),"Drag LMB to paint · RMB to orbit\nScroll to zoom · Shift + scroll: brush\nR: next seed · Home: orbit · Tab: hide",body);
            string state=failure!=null?failure:!hasPlan?"Constructing planet…":IsBuilding?"Regenerating whole planet…":$"{CellCount:N0} cells  /  generation {Generation}";
            GUI.Label(new Rect(36,447,253,34),state,caption);
            GUI.Label(new Rect(36,481,250,47),$"{frameMs:0.0} ms/frame  ·  {GpuBytes/1048576f:0.0} MiB GPU\nLast rebuild {LastGenerationMs:0} ms  ·  upload {LastUploadMs:0.0} ms\nVisible: {VisibleTops:N0} panels / {VisibleBoxes:N0} details",body);
            GUI.matrix=previousMatrix;
        }
    }
}
