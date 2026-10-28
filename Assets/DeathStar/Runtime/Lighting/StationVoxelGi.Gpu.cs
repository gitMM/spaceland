using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeathStar
{
    public sealed partial class StationVoxelGi
    {
        // Only Radiance survives the solve. One non-mipped scratch volume fits
        // every occupancy/directional mip and is reused for both bounces.
        sealed class SolveResources : IDisposable
        {
            public RenderTexture Radiance,Direct,Occupancy,Scratch;
            public ComputeBuffer Owners,Triangles;
            public SolveResources(int resolution,int count)
            {
                try {
                    Radiance=Volume(resolution/2,true,"Station directional voxel radiance",true);
                    Direct=Volume(resolution,false,"Station voxel direct radiance");
                    Occupancy=Volume(resolution,true,"Station voxel occupancy / second bounce");
                    Scratch=Volume(resolution/2,false,"Station voxel mip scratch",true);
                    Owners=new ComputeBuffer(resolution*resolution*resolution,4);
                    Triangles=new ComputeBuffer(count,Marshal.SizeOf<Triangle>(),ComputeBufferType.Structured,ComputeBufferMode.SubUpdates);
                } catch {Dispose();throw;}
            }
            public RenderTexture TakeRadiance(){var texture=Radiance;Radiance=null;return texture;}
            public void Dispose()
            {
                Owners?.Dispose();Triangles?.Dispose();Owners=Triangles=null;
                Release(ref Radiance);Release(ref Direct);Release(ref Occupancy);Release(ref Scratch);
            }
        }

        void BindGrid()
        {
            UpdateSteps();
            compute.SetVector(MinimumId,minimum);compute.SetVector(ParametersId,parameters);compute.SetVector(GridId,grid);
            compute.SetVectorArray(StepsId,steps);compute.SetInt(StepCountId,stepCount);
        }
        void BindGeometry(int kernel,SolveResources solve)
        {
            compute.SetBuffer(kernel,"_Owners",solve.Owners);compute.SetBuffer(kernel,"_Triangles",solve.Triangles);
        }
        void Solve(StationLightingSettings settings,SolveResources solve,int resolution,int count)
        {
            BindGrid();compute.SetInt("_Resolution",resolution);compute.SetInt("_TriangleCount",count);
            int clear=compute.FindKernel("Clear"),voxelize=compute.FindKernel("Voxelize"),resolve=compute.FindKernel("Resolve");
            compute.SetBuffer(clear,"_Owners",solve.Owners);DispatchVolume(clear,resolution);
            BindGeometry(voxelize,solve);compute.Dispatch(voxelize,(count+63)/64,1,1);
            compute.SetBuffer(resolve,"_Owners",solve.Owners);compute.SetTexture(resolve,"_Output",solve.Occupancy);DispatchVolume(resolve,resolution);
            FilterMips(solve.Occupancy,solve.Scratch,resolution,false);

            int inject=compute.FindKernel("Inject");BindGeometry(inject,solve);
            compute.SetVector("_SunDirection",settings.sunFrom.normalized);compute.SetVector("_SunColor",(Vector4)(settings.sunColor.linear*settings.sunIntensity));
            compute.SetVector("_PlanetDirection",settings.planetFrom.normalized);compute.SetVector("_PlanetColor",(Vector4)(settings.planetColor.linear*settings.planetIntensity));
            compute.SetFloat("_ShadowStrength",settings.receivedShadowStrength);
            compute.SetTexture(inject,"_Occupancy",solve.Occupancy);compute.SetTexture(inject,"_Output",solve.Direct);DispatchVolume(inject,resolution);
            DirectionalMips(solve.Direct,solve,resolution);
            if(settings.voxelGiSecondBounce) {
                int bounce=compute.FindKernel("Bounce");BindGeometry(bounce,solve);
                compute.SetTexture(bounce,RadianceId,solve.Radiance);compute.SetTexture(bounce,"_Direct",solve.Direct);
                compute.SetTexture(bounce,"_Output",solve.Occupancy);DispatchVolume(bounce,resolution);
                DirectionalMips(solve.Occupancy,solve,resolution);
            }
        }
        void DirectionalMips(RenderTexture source,SolveResources solve,int resolution)
        {
            int kernel=compute.FindKernel("DirectionalBase"),size=resolution/2;
            BindGeometry(kernel,solve);compute.SetInt("_MipSize",size);
            compute.SetTexture(kernel,"_Input",source);compute.SetTexture(kernel,"_Output",solve.Radiance);
            DispatchVolume(kernel,size,6);
            FilterMips(solve.Radiance,solve.Scratch,size,true);
        }
        void FilterMips(RenderTexture texture,RenderTexture scratch,int resolution,bool directional)
        {
            int filter=compute.FindKernel(directional?"DirectionalFilter":"Filter"),copy=compute.FindKernel("CopyMip");
            int lobes=directional?6:1;
            for(int mip=1,size=resolution/2;size>0;mip++,size/=2) {
                compute.SetInt("_MipSize",size);compute.SetInt("_SourceMip",mip-1);
                compute.SetTexture(filter,"_Input",texture);compute.SetTexture(filter,"_Output",scratch);
                DispatchVolume(filter,size,lobes);
                // Copy the complete 3D region. The texture element CopyTexture
                // overload did not transfer all depth slices on this D3D11 path.
                compute.SetInt("_SourceMip",0);compute.SetInt("_CopyWidth",size*lobes);
                compute.SetTexture(copy,"_Input",scratch);compute.SetTexture(copy,"_Output",texture,mip);
                DispatchVolume(copy,size,lobes);
            }
        }
        void DispatchVolume(int kernel,int size,int lobes=1)=>compute.Dispatch(kernel,(size*lobes+3)/4,(size+3)/4,(size+3)/4);

        static RenderTexture Volume(int size,bool mips,string name,bool directional=false)
        {
            var texture=new RenderTexture(directional?size*6:size,size,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear) {
                name=name,dimension=TextureDimension.Tex3D,volumeDepth=size,enableRandomWrite=true,
                useMipMap=mips,autoGenerateMips=false,filterMode=FilterMode.Trilinear,wrapMode=TextureWrapMode.Clamp,
                hideFlags=HideFlags.HideAndDontSave};
            if(!texture.Create()){CoreUtils.Destroy(texture);throw new InvalidOperationException("Could not allocate "+name);}
            return texture;
        }
    }
}
