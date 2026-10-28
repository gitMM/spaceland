using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DeathStar
{
    // The view from orbit (doc/close-up-detail-plan.md, far range): each 8 x 8 x 8 chunk of cells becomes one mesh of
    // its massing, the exposed faces of its solid cells merged into rectangles, with the tiles' relief baked into a
    // small atlas: world normal and occlusion, colour and glow, at half a metre a texel. Bake renders each rectangle
    // with an orthographic camera looking down onto it (the tiles drawn in a raw output mode of TileSurface, lighting
    // and screen effects off); occlusion comes from the baked heights. Beyond switchDistance a camera draws the chunk and
    // not its tiles.
    [ExecuteAlways]
    public sealed class StationChunks : MonoBehaviour
    {
        public float switchDistance=600;
        public const int Size=8;
        const float Texel=0.5f;
        const int Supersample=4;

        sealed class Rect3{public Vector3 origin,u,v,n;public Vector2Int texels;public RectInt atlas;}
        sealed class Chunk
        {
            public Vector3Int key;public Mesh mesh;public Material material;public Texture2D colour,normal;public Bounds bounds;
            public readonly List<Renderer> renderers=new List<Renderer>();public readonly List<Rect3> rects=new List<Rect3>();
            public int atlasSize;
        }
        readonly List<Chunk> chunks=new List<Chunk>();
        public bool Baked {get;private set;}
        public int ChunkCount=>chunks.Count;
        public int Triangles{get{int n=0;foreach(var c in chunks)if(c.mesh)n+=c.mesh.triangles.Length/3;return n;}}
        // Lab: >=0 forces every chunk on (1) or off (0) for every camera.
        public int force=-1;

        void OnEnable(){RenderPipelineManager.beginCameraRendering+=BeginCamera;RenderPipelineManager.endCameraRendering+=EndCamera;}
        void OnDisable(){RenderPipelineManager.beginCameraRendering-=BeginCamera;RenderPipelineManager.endCameraRendering-=EndCamera;Clear();}
        static void Release(Object o){if(!o)return;if(Application.isPlaying)Destroy(o);else DestroyImmediate(o);}
        void Clear(){foreach(var c in chunks){Release(c.mesh);Release(c.material);Release(c.colour);Release(c.normal);}chunks.Clear();Baked=false;}

        // The massing of every chunk of the structure, its tiles' renderers (children of tiles, positioned at their
        // cell's centre) and an atlas layout. paint: the material the chunks are drawn with (a copy is made).
        public void Build(TileStructure structure,Material paint)
        {
            Clear();
            var byKey=new Dictionary<Vector3Int,Chunk>();
            Chunk Of(Vector3Int cell)
            {
                var key=new Vector3Int(Mathf.FloorToInt(cell.x/(float)Size),Mathf.FloorToInt(cell.y/(float)Size),Mathf.FloorToInt(cell.z/(float)Size));
                if(!byKey.TryGetValue(key,out var c)){c=new Chunk{key=key};byKey.Add(key,c);chunks.Add(c);}
                return c;
            }
            // exposed faces of solid cells, per chunk, direction and layer
            var faces=new Dictionary<(Chunk,int dir,int layer),HashSet<Vector2Int>>();
            foreach(var pair in structure.cells)
            {
                var cell=pair.Key;if(!structure.Solid(cell))continue;
                var chunk=Of(cell);
                for(int dir=0;dir<6;dir++)
                {
                    int axis=dir>>1,sign=(dir&1)==0?1:-1;
                    var step=Vector3Int.zero;step[axis]=sign;
                    if(structure.Solid(cell+step))continue;
                    int a=(axis+1)%3,b=(axis+2)%3;
                    var k=(chunk,dir,cell[axis]);
                    if(!faces.TryGetValue(k,out var set))faces.Add(k,set=new HashSet<Vector2Int>());
                    set.Add(new Vector2Int(cell[a],cell[b]));
                }
            }
            // greedy rectangles
            foreach(var pair in faces)
            {
                var (chunk,dir,layer)=pair.Key;var set=pair.Value;
                int axis=dir>>1,sign=(dir&1)==0?1:-1,a=(axis+1)%3,b=(axis+2)%3;
                var left=new HashSet<Vector2Int>(set);
                var sorted=new List<Vector2Int>(set);sorted.Sort((p,q)=>p.y!=q.y?p.y.CompareTo(q.y):p.x.CompareTo(q.x));
                foreach(var start in sorted)
                {
                    if(!left.Contains(start))continue;
                    int w=1;while(left.Contains(start+new Vector2Int(w,0)))w++;
                    int h=1;
                    while(true){bool row=true;for(int i=0;i<w;i++)if(!left.Contains(start+new Vector2Int(i,h))){row=false;break;}if(!row)break;h++;}
                    for(int j=0;j<h;j++)for(int i=0;i<w;i++)left.Remove(start+new Vector2Int(i,j));
                    Vector3 Ax(int i){var v=Vector3.zero;v[i]=1;return v;}
                    var corner=Vector3.zero;corner[axis]=(layer+0.5f*sign)*TileBuilder.Cell;corner[a]=(start.x-0.5f)*TileBuilder.Cell;corner[b]=(start.y-0.5f)*TileBuilder.Cell;
                    var r=new Rect3{origin=corner,u=Ax(a)*(w*TileBuilder.Cell),v=Ax(b)*(h*TileBuilder.Cell),n=Ax(axis)*sign};
                    r.texels=new Vector2Int(Mathf.CeilToInt(w*TileBuilder.Cell/Texel),Mathf.CeilToInt(h*TileBuilder.Cell/Texel));
                    chunk.rects.Add(r);
                }
            }
            // atlas: shelves, a texel of padding round each rectangle
            foreach(var chunk in chunks)
            {
                chunk.rects.Sort((p,q)=>q.texels.y.CompareTo(p.texels.y));
                int size=64;
                while(true)
                {
                    int x=1,y=1,shelf=0;bool fits=true;
                    foreach(var r in chunk.rects)
                    {
                        if(x+r.texels.x+1>size){x=1;y+=shelf+2;shelf=0;}
                        if(y+r.texels.y+1>size||r.texels.x+2>size){fits=false;break;}
                        r.atlas=new RectInt(x,y,r.texels.x,r.texels.y);x+=r.texels.x+2;shelf=Mathf.Max(shelf,r.texels.y);
                    }
                    if(fits||size>=2048)break;
                    size*=2;
                }
                chunk.atlasSize=size;
                chunk.mesh=Mesh(chunk);
                chunk.bounds=chunk.mesh.bounds;
                chunk.material=new Material(paint){hideFlags=HideFlags.HideAndDontSave,name="Station chunk"};
                chunk.material.SetFloat("_TileChunk",1);
            }
            // tiles' renderers, by the chunk of their cell
            foreach(var renderer in GetComponentsInChildren<Renderer>(true))
            {
                var tile=renderer.GetComponentInParent<LODGroup>()?renderer.GetComponentInParent<LODGroup>().transform:renderer.transform;
                var cell=Vector3Int.RoundToInt(tile.localPosition/TileBuilder.Cell);
                if(!structure.cells.ContainsKey(cell))continue;
                var key=new Vector3Int(Mathf.FloorToInt(cell.x/(float)Size),Mathf.FloorToInt(cell.y/(float)Size),Mathf.FloorToInt(cell.z/(float)Size));
                if(byKey.TryGetValue(key,out var chunk))chunk.renderers.Add(renderer);
            }
        }

        static Mesh Mesh(Chunk c)
        {
            var positions=new List<Vector3>();var normals=new List<Vector3>();var atlas=new List<Vector2>();var tangents=new List<Vector4>();
            var colors=new List<Color>();var tri=new List<int>();var codes=new List<Vector4>();var far=new List<Vector4>();
            float s=1f/c.atlasSize;
            foreach(var r in c.rects)
            {
                int i=positions.Count;
                var p=new[]{r.origin,r.origin+r.u,r.origin+r.u+r.v,r.origin+r.v};
                var uv=new[]{new Vector2(r.atlas.xMin,r.atlas.yMin)*s,new Vector2(r.atlas.xMax,r.atlas.yMin)*s,new Vector2(r.atlas.xMax,r.atlas.yMax)*s,new Vector2(r.atlas.xMin,r.atlas.yMax)*s};
                for(int k=0;k<4;k++)
                {
                    positions.Add(p[k]);normals.Add(r.n);atlas.Add(uv[k]);var t=r.u.normalized;tangents.Add(new Vector4(t.x,t.y,t.z,1));
                    colors.Add(Color.white);codes.Add(Vector4.zero);far.Add(new Vector4(TileMesh.FarEdge,TileMesh.FarEdge,TileMesh.FarEdge,TileMesh.FarEdge));
                }
                bool flip=Vector3.Dot(Vector3.Cross(r.u,r.v),r.n)<0;
                // TileMesh's winding: (a, b, c), (a, c, d) when u x v points along the normal
                if(!flip){tri.Add(i);tri.Add(i+1);tri.Add(i+2);tri.Add(i);tri.Add(i+2);tri.Add(i+3);}
                else{tri.Add(i);tri.Add(i+2);tri.Add(i+1);tri.Add(i);tri.Add(i+3);tri.Add(i+2);}
            }
            var mesh=new Mesh{name="Station chunk "+c.key,hideFlags=HideFlags.HideAndDontSave,indexFormat=positions.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16};
            mesh.SetVertices(positions);mesh.SetNormals(normals);mesh.SetTangents(tangents);mesh.SetColors(colors);
            mesh.SetUVs(0,codes);mesh.SetUVs(1,far);mesh.SetUVs(2,far);mesh.SetUVs(3,new Vector4[positions.Count]);mesh.SetUVs(7,atlas);
            mesh.SetTriangles(tri,0);mesh.RecalculateBounds();
            return mesh;
        }

        // Renders every rectangle of every chunk into its atlas. Lighting and screen effects are switched off while it
        // runs; TileSurface's _TileBake mode 1 writes world normal and height over the rectangle, mode 2 colour and glow.
        public void Bake()
        {
            if(chunks.Count==0)return;
            var settings=StationLightingSettings.Current;
            var saved=(settings.screenSpaceGi,settings.screenSpaceAmbientOcclusion,settings.contactShadows,settings.kitProps);
            settings.screenSpaceGi=false;settings.screenSpaceAmbientOcclusion=false;settings.contactShadows=false;settings.kitProps=false;
            var go=new GameObject("Chunk bake camera"){hideFlags=HideFlags.HideAndDontSave};
            go.transform.SetParent(transform,false);
            var cam=go.AddComponent<Camera>();cam.enabled=false;cam.orthographic=true;cam.clearFlags=CameraClearFlags.SolidColor;
            cam.scene=gameObject.scene;cam.nearClipPlane=0.1f;cam.farClipPlane=60;cam.allowMSAA=false;cam.allowHDR=true;
            var data=cam.GetUniversalAdditionalCameraData();data.renderPostProcessing=false;data.antialiasing=AntialiasingMode.None;data.renderShadows=false;
            foreach(var chunk in chunks)foreach(var r in chunk.renderers)r.forceRenderingOff=false;
            try
            {
                foreach(var chunk in chunks)
                {
                    var normalPx=new Color[chunk.atlasSize*chunk.atlasSize];var colourPx=new Color[normalPx.Length];
                    for(int i=0;i<normalPx.Length;i++){normalPx[i]=new Color(0.5f,0.5f,1,1);colourPx[i]=new Color(0.3f,0.3f,0.3f,0);}
                    foreach(var r in chunk.rects)
                    {
                        var centre=r.origin+(r.u+r.v)*0.5f;
                        cam.transform.SetPositionAndRotation(centre+r.n*30,Quaternion.LookRotation(-r.n,r.v.normalized));
                        // looking along -n with up v, the camera's right is -u when u x v = n: then the picture runs backwards
                        bool mirror=Vector3.Dot(Vector3.Cross(r.u,r.v),r.n)>0;
                        cam.orthographicSize=r.v.magnitude*0.5f;cam.aspect=r.u.magnitude/r.v.magnitude;
                        Shader.SetGlobalVector("_TileBakePlane",new Vector4(r.n.x,r.n.y,r.n.z,-Vector3.Dot(r.n,r.origin)));
                        var normals=Render(cam,r,1,new Color(0.5f,0.5f,1,0));var colours=Render(cam,r,2,new Color(0,0,0,0));
                        var heights=new float[r.texels.x*r.texels.y];
                        for(int y=0;y<r.texels.y;y++)for(int x=0;x<r.texels.x;x++)
                        {
                            int sx=mirror?r.texels.x-1-x:x;
                            var n=normals[y*r.texels.x+sx];var c=colours[y*r.texels.x+sx];
                            heights[y*r.texels.x+x]=n.a;
                            colourPx[(r.atlas.y+y)*chunk.atlasSize+r.atlas.x+x]=c;
                            normalPx[(r.atlas.y+y)*chunk.atlasSize+r.atlas.x+x]=new Color(n.r,n.g,n.b,1);
                        }
                        // occlusion from the heights: how far neighbours within 2 m rise over each texel
                        for(int y=0;y<r.texels.y;y++)for(int x=0;x<r.texels.x;x++)
                        {
                            float h=heights[y*r.texels.x+x],rise=0;int count=0;
                            for(int dy=-4;dy<=4;dy++)for(int dx=-4;dx<=4;dx++)
                            {
                                int qx=x+dx,qy=y+dy;if((dx==0&&dy==0)||qx<0||qy<0||qx>=r.texels.x||qy>=r.texels.y)continue;
                                float d=Mathf.Sqrt(dx*dx+dy*dy)*Texel;rise+=Mathf.Clamp01((heights[qy*r.texels.x+qx]-h)/d);count++;
                            }
                            int at=(r.atlas.y+y)*chunk.atlasSize+r.atlas.x+x;
                            normalPx[at].a=1-0.6f*rise/Mathf.Max(count,1);
                        }
                        // padding: copy each edge texel outward one texel
                        for(int y=-1;y<=r.texels.y;y++)for(int x=-1;x<=r.texels.x;x++)
                        {
                            if(x>=0&&y>=0&&x<r.texels.x&&y<r.texels.y)continue;
                            int cx=Mathf.Clamp(x,0,r.texels.x-1),cy=Mathf.Clamp(y,0,r.texels.y-1);
                            int to=(r.atlas.y+y)*chunk.atlasSize+r.atlas.x+x,from=(r.atlas.y+cy)*chunk.atlasSize+r.atlas.x+cx;
                            if(to<0||to>=normalPx.Length)continue;
                            normalPx[to]=normalPx[from];colourPx[to]=colourPx[from];
                        }
                    }
                    Release(chunk.colour);Release(chunk.normal);
                    chunk.colour=new Texture2D(chunk.atlasSize,chunk.atlasSize,TextureFormat.RGBAHalf,true,true){name="Chunk colour",hideFlags=HideFlags.HideAndDontSave,wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Trilinear};
                    chunk.normal=new Texture2D(chunk.atlasSize,chunk.atlasSize,TextureFormat.RGBA32,true,true){name="Chunk normal",hideFlags=HideFlags.HideAndDontSave,wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Trilinear};
                    chunk.colour.SetPixels(colourPx);chunk.colour.Apply(true,false);chunk.normal.SetPixels(normalPx);chunk.normal.Apply(true,false);
                    chunk.material.SetTexture("_TileChunkColour",chunk.colour);chunk.material.SetTexture("_TileChunkNormal",chunk.normal);
                }
                Baked=true;
            }
            finally
            {
                Shader.SetGlobalFloat("_TileBake",0);
                (settings.screenSpaceGi,settings.screenSpaceAmbientOcclusion,settings.contactShadows,settings.kitProps)=saved;
                DestroyImmediate(go);
            }
        }
        // One rectangle in one bake mode, supersampled and averaged down to its texels (row 0 at the rectangle's v=0).
        static Color[] Render(Camera cam,Rect3 r,int mode,Color clear)
        {
            int w=r.texels.x*Supersample,h=r.texels.y*Supersample;
            var rt=RenderTexture.GetTemporary(w,h,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);
            cam.backgroundColor=clear;cam.targetTexture=rt;Shader.SetGlobalFloat("_TileBake",mode);
            cam.Render();cam.targetTexture=null;Shader.SetGlobalFloat("_TileBake",0);
            var prev=RenderTexture.active;RenderTexture.active=rt;
            var tex=new Texture2D(w,h,TextureFormat.RGBAHalf,false,true);tex.ReadPixels(new Rect(0,0,w,h),0,0);tex.Apply();
            RenderTexture.active=prev;RenderTexture.ReleaseTemporary(rt);
            var src=tex.GetPixels();Release(tex);
            var o=new Color[r.texels.x*r.texels.y];
            for(int y=0;y<r.texels.y;y++)for(int x=0;x<r.texels.x;x++)
            {
                Color sum=default;
                for(int j=0;j<Supersample;j++)for(int i=0;i<Supersample;i++)sum+=src[(y*Supersample+j)*w+x*Supersample+i];
                o[y*r.texels.x+x]=sum/(Supersample*Supersample);
            }
            return o;
        }

        // In play, bake once the main camera pulls back toward the switch (it takes a moment).
        void LateUpdate()
        {
            if(!Application.isPlaying||Baked||chunks.Count==0)return;
            var main=Camera.main;
            if(main&&Vector3.Distance(main.transform.position,transform.position)>switchDistance*0.6f)Bake();
        }
        bool Far(Chunk c,Camera camera)=>force>=0?force==1:Vector3.Distance(camera.transform.position,transform.TransformPoint(c.bounds.center))>switchDistance;
        void BeginCamera(ScriptableRenderContext context,Camera camera)
        {
            if(!Baked||camera.cameraType==CameraType.Preview||camera.cameraType==CameraType.Reflection||camera.name=="Chunk bake camera")return;
            foreach(var c in chunks)
            {
                if(!Far(c,camera))continue;
                foreach(var r in c.renderers)if(r)r.forceRenderingOff=true;
                var rp=new RenderParams(c.material){camera=camera,shadowCastingMode=ShadowCastingMode.On,receiveShadows=true,layer=gameObject.layer};
                Graphics.RenderMesh(rp,c.mesh,0,transform.localToWorldMatrix);
            }
        }
        void EndCamera(ScriptableRenderContext context,Camera camera)
        {
            foreach(var c in chunks)foreach(var r in c.renderers)if(r)r.forceRenderingOff=false;
        }
    }
}
