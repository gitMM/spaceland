using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DeathStar.EditorTools
{
    public static class SettlementValidation
    {
        static void Require(bool value,string message){if(!value)throw new Exception(message);}
        public static string Run()
        {
            int checks=0;
            var seed=new BlockConstruction();var before=JsonUtility.ToJson(seed.Capture());
            Require(seed.TryEdit(Vector3Int.right,false,out var preview,TileFamily.Habitat),"Family preview failed");
            Require(before==JsonUtility.ToJson(seed.Capture()),"Preview changed live family data");checks++;
            var restored=new BlockConstruction();restored.Restore(preview.Capture());
            Require(restored.Family(Vector3Int.right)==TileFamily.Habitat,"Family save round trip failed");checks++;
            var legacy=preview.Capture();legacy.version=5;legacy.families=null;restored.Restore(legacy);
            Require(restored.Cells.All(c=>restored.Family(c)==TileFamily.Hull),"Legacy cells must migrate as Hull");checks++;
            var bad=preview.Capture();bad.families[0]=(TileFamily)999;string stable=JsonUtility.ToJson(restored.Capture());
            bool rejected=false;try{restored.Restore(bad);}catch(ArgumentException){rejected=true;}
            Require(rejected&&stable==JsonUtility.ToJson(restored.Capture()),"Invalid families are not atomic");checks++;

            // Every face, all 24 orientations, negative coordinates and insertion-order determinism.
            var rotations=new HashSet<(Vector3Int,Vector3Int)>();
            foreach(var n in TileStructure.Directions)foreach(var side in TileStructure.Directions)
            {
                if(Vector3.Dot(n,side)!=0)continue;
                var third=TileStructure.Cross(side,n);var cells=new Dictionary<Vector3Int,TileFamily>();
                var origin=new Vector3Int(-4,-4,-4);
                for(int a=0;a<2;a++)for(int b=0;b<2;b++)
                {var c=origin+side*a+third*b;cells[c]=TileFamily.Pad;cells[c-n]=TileFamily.Hull;}
                var model=BlockConstruction.FromFamilies(cells);var s=SettlementSolver.Solve(model);
                foreach(var c in cells.Keys.Where(c=>cells[c]==TileFamily.Pad))
                {Require(s.cells[c].axis==n,"Pad ignored its supporting face: "+n);Require(s.cells[c].SameAs(s.cells[origin]),"Pad quarters disagree");}
                var reversed=BlockConstruction.FromFamilies(cells.Reverse().ToDictionary(p=>p.Key,p=>p.Value));
                var s2=SettlementSolver.Solve(reversed);
                foreach(var c in cells.Keys)Require(s.cells[c].SameAs(s2.cells[c]),"Solver depends on insertion order");
                rotations.Add((n,side));checks++;
            }
            Require(rotations.Count==24,"Rotation coverage missing");
            var connectors=new Dictionary<Vector3Int,TileFamily>{{Vector3Int.zero,TileFamily.Corridor},{Vector3Int.right,TileFamily.Hull},{Vector3Int.up,TileFamily.Corridor},{Vector3Int.forward,TileFamily.Conduit}};
            var open=BlockConstruction.FromFamilies(connectors);var solved=SettlementSolver.Solve(open);
            Require(!solved.Solid(Vector3Int.zero)&&!solved.Solid(Vector3Int.forward),"Connector became solid");
            Require(solved.CorridorLinks(Vector3Int.zero,solved.cells[Vector3Int.zero]).Count==3,"Corridor junction lost a branch");
            Require(BlockMeshData.Build(open,new[]{Vector3Int.right}).faces.Any(f=>f.side==1),"Open connector hid its neighbour's wall");checks++;
            var pipeCells=new Dictionary<Vector3Int,TileFamily>{{Vector3Int.zero,TileFamily.Conduit}};
            foreach(var d in TileStructure.Directions)pipeCells[d]=TileFamily.Conduit;
            var pipes=SettlementSolver.Solve(BlockConstruction.FromFamilies(pipeCells));
            Require(pipes.cells.Values.All(p=>p.spatial),"Nonplanar pipe group lost its spatial junctions");
            // A 2 x 2 conduit loop beside a hull and a habitat (four elbows, no run ends) used to float beside them,
            // plugged into nothing. Tethered, the elbows touching the ship branch into it and the ship builds ports.
            var loop=new Dictionary<Vector3Int,TileFamily>{{new Vector3Int(0,0,0),TileFamily.Hull},{new Vector3Int(-1,0,0),TileFamily.Habitat}};
            foreach(var c in new[]{new Vector3Int(0,0,-1),new Vector3Int(-1,0,-1),new Vector3Int(0,0,-2),new Vector3Int(-1,0,-2)})loop[c]=TileFamily.Conduit;
            var looped=SettlementSolver.Solve(BlockConstruction.FromFamilies(loop));
            Require(looped.cells.Values.Where(p=>p.family==TileFamily.Conduit).All(p=>p.tether),"Unplugged conduit loop not tethered");
            var loopBuilder=new TileBuilder(looped);
            foreach(var ship in new[]{new Vector3Int(0,0,0),new Vector3Int(-1,0,0)})
                Require(loopBuilder.Build(ship).fixedParts.Exists(p=>p.name=="port"),"Conduit loop did not plug into "+ship);
            Require(SettlementSolver.Solve(SettlementExamples.Foundry()).cells.Values.All(p=>!p.tether),"A conduit group that plugs in was tethered");
            var pipeMesh=new TileBuilder(pipes).Build(Vector3Int.zero).ToMesh("spatial junction validation");
            for(int i=0;i<3;i++)Require(pipeMesh.bounds.min[i]<=-2.49f&&pipeMesh.bounds.max[i]>=2.49f,"Spatial conduit dropped a face link");
            UnityEngine.Object.DestroyImmediate(pipeMesh);checks++;

            var box=new Dictionary<Vector3Int,TileFamily>();
            for(int x=-2;x<=2;x++)for(int y=-2;y<=2;y++)for(int z=-2;z<=2;z++)box[new Vector3Int(x,y,z)]=TileFamily.Hull;
            var globe=BlockConstruction.FromFamilies(box);var warp=new CurvedTileMesh(globe);
            foreach(var c in globe.Cells)for(int k=0;k<8;k++)
            {
                var corner=c+BlockConstruction.CornerOffset(k);
                Require((warp.Map(((Vector3)corner-Vector3.one*0.5f)*5)-globe.Corner(corner)*5).magnitude<0.0001f,"Warp missed a stored corner");
            }
            checks++;
            // The same relief sample generated by two owners agrees, and its depth remains in physical metres.
            var point=new Vector3(2.5f,12.5f,1.2f);var frame=new Vector4(0,1,0,12.5f);
            foreach(float h in new[]{0.1f,0.8f})
                Require(Mathf.Abs((warp.Position(point+Vector3.up*h,frame)-warp.Map(point)).magnitude-h)<0.0001f,"Curvature changed relief height");
            var left=TileStructure.Center(new Vector3Int(0,2,0))+new Vector3(2.5f,2.5f,1.2f);
            var right=TileStructure.Center(new Vector3Int(1,2,0))+new Vector3(-2.5f,2.5f,1.2f);
            Require((warp.Position(left+Vector3.up*0.8f,frame)-warp.Position(right+Vector3.up*0.8f,frame)).magnitude<0.00001f,"Relief seam split");checks++;

            var old=SettlementSolver.Solve(globe);Require(globe.TryEdit(new Vector3Int(3,0,0),false,out var edited,TileFamily.Pad),"Edit failed");
            var next=SettlementSolver.Solve(edited);var dirty=SettlementSolver.Dirty(old,next,edited.ChangedCells);
            Require(dirty.Contains(new Vector3Int(2,1,1)),"Corner neighbour not invalidated");checks++;
            // Pad/hatch tangent metric removes local squash and shear while retaining curvature.
            var anchor=new Vector4(2.5f,12.5f,1.2f,1);var metric=warp.Metric(anchor,frame);
            var pa=warp.Position(anchor,frame);
            foreach(var direction in new[]{Vector3.right,Vector3.forward})
            {
                float distance=(warp.Position((Vector3)anchor+metric.MultiplyVector(direction*0.1f),frame)-pa).magnitude;
                Require(Mathf.Abs(distance-0.1f)<0.01f,"Human-scale metric compensation failed");
            }
            checks++;
            ShapeClasses();checks++;
            return $"PASS: {checks} settlement checks; family preview/save/migration/atomicity; 24 rotations; insertion order; open connectors and junctions; exact eight-corner warp; seamless metric relief; diagonal invalidation; shape classes and exposures; every family placeable in every cell, valid cells out to the build limit.";
        }
        // On a ball grown from the seed, cells on the cube's edge diagonals (two coordinates tie for the largest
        // magnitude) are wedges along the third axis, the corner diagonals apexes, everything else boxes.
        static void ShapeClasses()
        {
            var ball=new List<Vector3Int>();
            for(int x=-3;x<=3;x++)for(int y=-3;y<=3;y++)for(int z=-3;z<=3;z++)ball.Add(new Vector3Int(x,y,z));
            var grown=BlockConstruction.FromCells(ball,BlockConstruction.SeedGuide);
            foreach(var c in ball)
            {
                int m=Mathf.Max(Mathf.Abs(c.x),Mathf.Max(Mathf.Abs(c.y),Mathf.Abs(c.z))),ties=0,odd=-1;
                for(int a=0;a<3;a++)if(Mathf.Abs(c[a])==m)ties++;else odd=a;
                var expected=m==0||ties==1?CellShape.Box:ties==2?CellShape.Wedge:CellShape.Apex;
                var s=grown.Shape(c);
                Require(s.kind==expected,$"Cell {c} is a {s.kind}, expected a {expected}");
                if(expected==CellShape.Wedge)Require(s.spine==odd,$"Wedge {c} runs along {s.spine}, expected {odd}");
            }
            Require(grown.Exposure(new Vector3Int(3,3,0),out bool gable)==CellExposure.Ridge&&!gable,"Outer wedge is not a ridge");
            Require(grown.Exposure(new Vector3Int(3,3,3),out _)==CellExposure.Crown,"Outer apex is not a crown");
            Require(grown.Exposure(new Vector3Int(2,2,0),out _)==CellExposure.Buried,"Inner wedge is exposed");
            Require(grown.Exposure(new Vector3Int(1,0,0),out _)==CellExposure.Box,"Box reported an exposure");
            Require(grown.TryEdit(new Vector3Int(4,3,0),false,out var walled),"Wall placement failed");
            Require(walled.Exposure(new Vector3Int(3,3,0),out _)==CellExposure.Half,"Half-covered roof not reported");
            Require(grown.TryEdit(new Vector3Int(3,3,1),true,out var cut),"Ridge cut failed");
            Require(cut.Exposure(new Vector3Int(3,3,0),out gable)==CellExposure.Ridge&&gable,"Ridge end lost its gable");
            Require(walled.Shape(new Vector3Int(3,3,0)).kind==CellShape.Wedge,"An edit changed a neighbour's shape");
            // Every family can be placed in every cell: each surface cell of the ball (boxes, small boxes, wedges and
            // apexes) is taken out and put back in each family.
            var families=(TileFamily[])Enum.GetValues(typeof(TileFamily));int small=0;
            foreach(var c in ball)
            {
                if(Mathf.Max(Mathf.Abs(c.x),Mathf.Max(Mathf.Abs(c.y),Mathf.Abs(c.z)))!=3)continue;
                if(grown.Shape(c).Small)small++;
                Require(grown.TryEdit(c,true,out var without),"Removal failed at "+c);
                foreach(var f in families)Require(without.TryEdit(c,false,out _,f),$"{f} refused at {c}: {without.LastError}");
            }
            Require(small>0,"No small boxes on the ball");
            // Out to the build limit the cube's corners and the ridges beside them stay valid: their roofs flatten, they
            // don't fold. Stored corners that do fold a cell (two swapped) or collapse it (one on another) are refused.
            foreach(int k in new[]{13,40,63})
                Require(Builds(new[]{new Vector3Int(k,k,k),new Vector3Int(k,k,k-1),new Vector3Int(-k,k,0),new Vector3Int(0,-k,-k+1)},BlockConstruction.SeedGuide),"Shell "+k+" corner or ridge refused");
            Require(Builds(new[]{new Vector3Int(-64,-64,-64),new Vector3Int(63,-64,0)},new BlockGuide{shape=BlockShape.Rounded,strength=1,center=Vector3.zero,extents=Vector3.one*4}),"Corner-centred guide refused its corners");
            foreach(bool collapse in new[]{false,true})
            {
                var save=new BlockConstruction().Capture();var a=save.corners[0].position;
                save.corners[0].position=save.corners[1].position;if(!collapse)save.corners[1].position=a;
                bool refused=false;try{new BlockConstruction().Restore(save);}catch(ArgumentException){refused=true;}
                Require(refused,collapse?"A collapsed cell was accepted":"A folded cell was accepted");
            }
            var ridges=SettlementExamples.Ridges();
            const int R=SettlementExamples.RidgeRadius;
            Require(ridges.Exposure(SettlementExamples.RidgeCell(R-1,R-1,1),out _)==CellExposure.Keel,"Ridges example lost its keel");
            Require(ridges.Exposure(SettlementExamples.RidgeCell(R,SettlementExamples.RidgeLow,R),out _)==CellExposure.Half,"Ridges example lost its half-covered roof");
            Require(ridges.Exposure(SettlementExamples.RidgeCell(R,R,R),out _)==CellExposure.Crown,"Ridges example lost its crown");
            // The truss example has a truss crown, a truss ridge with a gable at the cut edge, and a frame on a Habitat wall.
            var truss=SettlementExamples.Truss();
            Require(truss.Family(SettlementExamples.RidgeCell(R,R,R))==TileFamily.Truss&&truss.Exposure(SettlementExamples.RidgeCell(R,R,R),out _)==CellExposure.Crown,"Truss example lost its crown");
            Require(truss.Exposure(SettlementExamples.RidgeCell(SettlementExamples.RidgeLow,R,R),out bool cutEnd)==CellExposure.Ridge&&cutEnd,"Truss example's ridge lost its gable");
            var trussStructure=SettlementSolver.Solve(truss);var habitat=SettlementExamples.RidgeCell(3,1,R+1);
            Require(trussStructure.Solid(habitat)&&!trussStructure.Solid(SettlementExamples.RidgeCell(2,1,R+1))&&trussStructure.Covered(SettlementExamples.RidgeCell(2,1,R+1)),"A truss is solid, or doesn't cover what it stands on");
            // The station example has every shape and exposure, every family on a ridge and every solid family on a crown.
            var station=SettlementExamples.Station();
            var seen=new HashSet<string>();var onRidge=new HashSet<TileFamily>();var onCrown=new HashSet<TileFamily>();
            foreach(var c in station.Cells)
            {
                var s=station.Shape(c);var e=station.Exposure(c,out bool open);var f=station.Family(c);
                seen.Add(s.kind==CellShape.Box?(s.Small?"small box":"box"):e.ToString());if(open)seen.Add("gable");
                if(e==CellExposure.Ridge)onRidge.Add(f);if(e==CellExposure.Crown)onCrown.Add(f);
            }
            foreach(var name in new[]{"box","small box","Ridge","Half","Keel","Crown","Spike","Buried","gable"})Require(seen.Contains(name),"Station example lacks "+name);
            foreach(TileFamily f in Enum.GetValues(typeof(TileFamily)))
            {
                Require(onRidge.Contains(f),"Station example lacks "+f+" on a ridge");
                if(!TileStructure.IsOpen(f))Require(onCrown.Contains(f),"Station example lacks a "+f+" crown");
            }
        }
        static bool Builds(Vector3Int[] cells,BlockGuide guide)
        {
            try{BlockConstruction.FromCells(cells,guide);return true;}
            catch(ArgumentException){return false;}
        }
        public static string FoundryInteraction(TileLibrary library)
        {
            var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var root=new GameObject("Foundry interaction validation");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);
            var cam=new GameObject("Camera");cam.transform.SetParent(root.transform);var camera=cam.AddComponent<Camera>();camera.enabled=false;
            var preview=new Material(Shader.Find("DeathStar/Block Preview"));
            var f=root.AddComponent<BlockFoundry>();f.viewer=camera;f.tileLibrary=library;f.hullMaterial=library.materials[0];f.previewMaterial=preview;
            try
            {
                var cells=new Dictionary<Vector3Int,TileFamily>();
                for(int x=0;x<2;x++)for(int z=0;z<2;z++)cells[new Vector3Int(x,0,z)]=TileFamily.Hull;
                f.Initialize(BlockConstruction.FromFamilies(cells));f.SelectedFamily=TileFamily.Pad;
                float globalOverlay=Shader.GetGlobalFloat("_TileOverlay");
                f.ColorCoding=true;
                Require(Shader.GetGlobalFloat("_TileOverlay")==globalOverlay,"Foundry colour switch leaked into global shader state");
                foreach(var c in cells.Keys)Require(f.EditFace(new BlockFace(c,2),false),"Foundry EditFace failed");
                CheckDebugRenderers(root,true);
                var complete=JsonUtility.ToJson(f.Model.Capture());
                var builder=new TileBuilder(f.Settlement.Structure);
                foreach(var c in cells.Keys)
                {
                    var mesh=builder.Build(c+Vector3Int.up).ToMesh("pad validation");var anchors=new List<Vector4>();mesh.GetUVs(5,anchors);
                    Require(anchors.Any(a=>a.w>0.5f&&((Vector3)a-new Vector3(2.5f,7.5f,2.5f)).magnitude<0.001f),"2x2 did not produce shared pad anchor");
                    UnityEngine.Object.DestroyImmediate(mesh);
                }
                Require(f.EditFace(new BlockFace(new Vector3Int(1,1,1),0),true),"Foundry removal failed");
                Require(f.Model.Count==7,"Removal left a cell");
                f.Undo();Require(JsonUtility.ToJson(f.Model.Capture())==complete,"Undo failed to restore family assembly");
                f.Redo();Require(f.Model.Count==7&&!f.Settlement.Structure.cells.ContainsKey(new Vector3Int(1,1,1)),"Redo left stale architecture");
                CheckDebugRenderers(root,true);
                var meshIds=root.GetComponentsInChildren<MeshFilter>().Select(m=>m.sharedMesh.GetInstanceID()).OrderBy(i=>i).ToArray();
                f.ColorCoding=false;CheckDebugRenderers(root,false);
                Require(meshIds.SequenceEqual(root.GetComponentsInChildren<MeshFilter>().Select(m=>m.sharedMesh.GetInstanceID()).OrderBy(i=>i)),"Debug toggle rebuilt geometry");
                Require(Shader.GetGlobalFloat("_TileOverlay")==globalOverlay,"Debug toggle changed another view's global colours");
                var c0=Vector3Int.zero;var p=f.Model.FacePoint(c0,5,0.5f,0.5f)*5;
                var a=f.Model.FacePoint(c0,5,0.6f,0.5f)*5-p;var b=f.Model.FacePoint(c0,5,0.5f,0.6f)*5-p;var normal=Vector3.Cross(a,b).normalized;
                Physics.SyncTransforms();Require(f.Pick(new Ray(p+normal*2,-normal),out var hit,out _)&&hit.cell==c0,"Curved picking lost cell ownership");
                return "PASS: Foundry family picker/EditFace, 2x2 assembly, removal, undo/redo, curved picking and independent debug colours through rebuilds on both LODs.";
            }
            finally{UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(preview);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
        }
        public static int CheckDebugRenderers(GameObject root,bool enabled)
        {
            int count=0;var block=new MaterialPropertyBlock();var colors=new List<Vector4>();
            foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>())
            {
                var mesh=renderer.GetComponent<MeshFilter>().sharedMesh;colors.Clear();mesh.GetUVs(3,colors);
                if(colors.Count==0)continue; // Picking proxies have no family overlay data.
                Require(colors.Count==mesh.vertexCount,"Curved mesh lost family colours");
                var family=(TileFamily)Enum.Parse(typeof(TileFamily),mesh.name.Split(' ')[0]);
                var expected=(Vector4)TileLook.FamilyColor(family);
                Require(colors.All(c=>(c-expected).sqrMagnitude<0.000001f),"Wrong family colour on "+mesh.name);
                renderer.GetPropertyBlock(block);
                Require(block.HasFloat(Shader.PropertyToID("_TileOverlay"))&&block.GetFloat("_TileOverlay")== (enabled?1:0),"Wrong debug state on "+mesh.name);
                count++;
            }
            Require(count>0,"No settlement renderers checked");return count;
        }
    }
}
