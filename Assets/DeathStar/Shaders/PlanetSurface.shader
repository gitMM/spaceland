Shader "DeathStar/Industrial Surface"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "Planet"
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Back ZWrite On
            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawArgs
            #include "UnityIndirect.cginc"
            #include "PlanetCommon.hlsl"
            StructuredBuffer<Primitive> _Primitives;
            StructuredBuffer<uint> _Visible;
            float3 _SunDirection;
            float4 _Brush; // direction.xyz, angular radius; negative w hides brush
            float4 _BrushColor;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                nointerpolation float4 data : TEXCOORD3;
                nointerpolation uint flags : TEXCOORD4;
            };

            float2 Quad(uint vertex)
            {
                const float2 corners[6]={float2(0,0),float2(1,0),float2(1,1),float2(0,0),float2(1,1),float2(0,1)};
                return corners[vertex%6];
            }

            Varyings Vert(uint vertex : SV_VertexID, uint instance : SV_InstanceID)
            {
                InitIndirectDrawArgs(0);
                vertex=GetIndirectVertexID(vertex);
                instance=GetIndirectInstanceID(instance);
                Primitive p=_Primitives[_Visible[instance]];
                CellPlan plan=_Plans[p.cell];
                uint kind=p.flags&3;
                float2 q=Quad(vertex);
                float3 pos,normal,u,v;
                float2 uv=q;
                if(kind==0)
                {
                    float3 cube=CubePoint(p.cell,q,u,v);
                    normal=normalize(cube); pos=normal*(_Radius+p.upper);
                }
                else if(kind==1)
                {
                    uint side=(p.flags>>2)&3;
                    float2 edge=side==0 ? float2(q.x,0) : side==1 ? float2(1,q.x) :
                        side==2 ? float2(1-q.x,1) : float2(0,1-q.x);
                    float3 direction=normalize(CubePoint(p.cell,edge,u,v));
                    float3 along=side==0?u:side==1?v:side==2?-u:-v;
                    normal=normalize(cross(along,direction));
                    pos=direction*(_Radius+lerp(p.lower,p.upper,q.y));
                }
                else
                {
                    float2 anchor=(p.rect.xy+p.rect.zw)*0.5;
                    float3 cube=CubePoint(p.cell,anchor,u,v);
                    float3 radial=normalize(cube);
                    float3 tu=normalize(u-radial*dot(u,radial));
                    float3 tv=normalize(cross(radial,tu));
                    float cellScale=2.0/_Resolution;
                    float w=length(u-radial*dot(u,radial))/length(cube)*cellScale*(_Radius+p.lower)*(p.rect.z-p.rect.x);
                    float depth=length(v-radial*dot(v,radial))/length(cube)*cellScale*(_Radius+p.lower)*(p.rect.w-p.rect.y);
                    float h=p.upper-p.lower;
                    uint face=vertex/6;
                    float3 local;
                    // Cross products for every face point out of the box.
                    if(face==0) {local=float3(q.x-0.5,1,q.y-0.5); normal=radial;}
                    else if(face==1) {local=float3(0.5-q.x,0,q.y-0.5);normal=-radial;}
                    else if(face==2) {local=float3(q.x-0.5,q.y,-0.5);normal=-tv;}
                    else if(face==3) {local=float3(0.5,q.y,q.x-0.5);normal=tu;}
                    else if(face==4) {local=float3(0.5-q.x,q.y,0.5);normal=tv;}
                    else {local=float3(-0.5,q.y,0.5-q.x);normal=-tu;}
                    // tu,tv,radial form the surface frame; side winding follows the same surface convention.
                    pos=radial*(_Radius+p.lower)+tu*(local.x*w)+tv*(local.z*depth)+radial*(local.y*h);
                }
                Varyings o;
                o.positionWS=pos; o.positionCS=TransformWorldToHClip(pos); o.normalWS=normal;
                o.uv=uv; o.data=float4(plan.tone,plan.kind,plan.feature,PlanetRandom(plan.seed)); o.flags=p.flags;
                return o;
            }

            float Lines(float2 uv,float2 repeats,float width)
            {
                float2 p=uv*repeats;
                float2 dist=min(frac(p),1-frac(p));
                float2 aa=max(fwidth(p),0.0001);
                float2 seams=1-smoothstep(width,width+aa,dist);
                return max(seams.x,seams.y)*saturate(1-max(aa.x,aa.y)*0.65);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 n=normalize(i.normalWS), radial=normalize(i.positionWS);
                uint geometry=i.flags&3;
                float tone=i.data.x;
                float panel=Lines(i.uv,float2(1,1),0.018);
                float fine=Lines(i.uv,float2(3+(uint)(i.data.w*3),2),0.025);
                float albedo=tone*(1-panel*0.55-fine*0.16);
                float ao=1;
                if(geometry==1)
                {
                    albedo*=0.62;
                    albedo*=1-Lines(i.uv,float2(7,3),0.06)*0.23;
                    ao=lerp(0.46,1,smoothstep(0,0.65,i.uv.y));
                }
                if(geometry==2) {albedo*=0.8;ao=0.9;}
                if(i.data.y==2) {albedo*=0.46;ao*=0.65;}
                if(i.data.y==4)
                {
                    float rings=abs(frac(i.data.z*13)-0.5);
                    albedo*=i.data.z<1 ? 0.57+0.15*smoothstep(0.04,0.15,rings) : 1.18;
                    ao*=lerp(0.45,1,saturate(i.data.z));
                }
                float3 sun=normalize(_SunDirection);
                float diffuse=saturate(dot(n,sun));
                float horizon=smoothstep(-0.02,0.08,dot(radial,sun));
                float3 view=normalize(_WorldSpaceCameraPos-i.positionWS);
                float3 halfVector=normalize(view+sun);
                float spec=pow(saturate(dot(n,halfVector)),40)*0.15*diffuse*horizon;
                float3 color=albedo*(float3(0.12,0.145,0.18)*ao + float3(1.0,0.93,0.82)*diffuse*horizon*1.35)+spec;
                // Restrained service lights; only machinery/trench cells and a tiny part of their panel perimeter.
                float lights=(i.data.y==2 || i.data.y==3) ? step(0.96,i.uv.x)*step(0.94,frac(i.uv.y*5+i.data.w)) : 0;
                color+=float3(0.3,0.6,0.75)*lights*0.8;
                if(_Brush.w>0)
                {
                    float angle=acos(clamp(dot(radial,_Brush.xyz),-1,1));
                    float aa=max(fwidth(angle),0.0001);
                    float edge=1-smoothstep(aa,aa*2.5,abs(angle-_Brush.w));
                    float fill=(1-smoothstep(_Brush.w-aa,_Brush.w+aa,angle))*0.08;
                    color=lerp(color,_BrushColor.rgb,saturate(edge*0.85+fill));
                }
                return half4(color,1);
            }
            ENDHLSL
        }
    }
}
