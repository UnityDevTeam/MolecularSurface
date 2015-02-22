using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

[RequireComponent(typeof(Camera))]
public class RayMarching : MonoBehaviour
{
    public Shader compositeShader;
    public Shader renderBackDepthShader;
    public Shader rayMarchSurfaceShader;

    public ComputeShader InitVolume;
    public ComputeShader FillVolume;
    public ComputeShader BlitVolume;

    public Mesh CubeMesh;
    public Color SurfaceColor;

    [Range(1, 16)]
    public float Scale = 1;

    [Range(0, 1)]
    public float Opacity = 1;
    
    [Range(32, 512)]
    public int NumSteps = 256;

    [Range(128, 1024)]
    public int VolumeSize = 128;

    [Range(0.5f, 10)]
    public float SurfaceSmoothness = 0.8f;

    [Range(0.0f, 10)]
    public float IntensityThreshold = 0.8f;

	private Material _rayMarchMaterial;
	private Material _compositeMaterial;
    private Material _backDepthMaterial;

    private ComputeBuffer _voxelBuffer;
    private ComputeBuffer _atomBuffer;
	private ComputeBuffer _normalBuffer;
    private RenderTexture _volumeTexture;
	private RenderTexture _normalTexture;

	private void OnEnable()
	{
		_rayMarchMaterial = new Material(rayMarchSurfaceShader);
		_compositeMaterial = new Material(compositeShader);
        _backDepthMaterial = new Material(renderBackDepthShader);
    }

    private void OnDisable()
    {
        if (_atomBuffer != null) _atomBuffer.Release(); _atomBuffer = null;
        if (_voxelBuffer != null) _voxelBuffer.Release(); _voxelBuffer = null;
		if (_normalBuffer != null) _normalBuffer.Release(); _normalBuffer = null;
        if (_volumeTexture != null) _volumeTexture.Release(); _volumeTexture = null;
		if (_normalTexture != null) _normalTexture.Release(); _normalTexture = null;
    }

	private void Start()
	{
		CreateResources();
	}
    
	private void CreateResources()
	{
        string pdbPath = Application.dataPath + "/Molecules/1.pdb";
        var atoms = PdbReader.ReadPdbFile(pdbPath);
        _atomBuffer = new ComputeBuffer(atoms.Count, sizeof(float) * 4, ComputeBufferType.Default);
        _atomBuffer.SetData(atoms.ToArray());

        _voxelBuffer = new ComputeBuffer(VolumeSize * VolumeSize * VolumeSize, sizeof(float), ComputeBufferType.Default);
		_normalBuffer = new ComputeBuffer(VolumeSize * VolumeSize * VolumeSize, sizeof(float), ComputeBufferType.Default);

        _volumeTexture = new RenderTexture(VolumeSize, VolumeSize, 0, RenderTextureFormat.RFloat);
        _volumeTexture.volumeDepth = VolumeSize;
        _volumeTexture.isVolume = true;
        _volumeTexture.enableRandomWrite = true;
        _volumeTexture.filterMode = FilterMode.Trilinear;
        _volumeTexture.Create();

		_normalTexture = new RenderTexture(VolumeSize, VolumeSize, 0, RenderTextureFormat.RFloat);
		_normalTexture.volumeDepth = VolumeSize;
		_normalTexture.isVolume = true;
		_normalTexture.enableRandomWrite = true;
		_normalTexture.filterMode = FilterMode.Trilinear;
		_normalTexture.Create();
	}
    
    [ImageEffectOpaque]
    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        // Init the volume data with zeros 
        InitVolume.SetInt("_VolumeSize", VolumeSize);
        InitVolume.SetTexture(0, "_VolumeTexture", _volumeTexture);
        InitVolume.SetBuffer(0, "_VoxelBuffer", _voxelBuffer);
        InitVolume.Dispatch(0, VolumeSize / 8, VolumeSize / 8, VolumeSize / 8);
        
        // Fill the volume data with atom values
        FillVolume.SetInt("_VolumeSize", VolumeSize);
        FillVolume.SetInt("_AtomCount", _atomBuffer.count);
        FillVolume.SetFloat("_Scale", Scale);
        FillVolume.SetFloat("_SurfaceSmoothness", SurfaceSmoothness);
        FillVolume.SetBuffer(0, "_AtomBuffer", _atomBuffer);
		FillVolume.SetBuffer(0, "_NormalBuffer", _normalBuffer);
        FillVolume.SetBuffer(0, "_VoxelBuffer", _voxelBuffer);
        FillVolume.Dispatch(0, (int)Mathf.Ceil((_atomBuffer.count) / 64.0f), 1, 1);

        // Blit linear buffer in 3D texture
        BlitVolume.SetInt("_VolumeSize", VolumeSize);
        BlitVolume.SetBuffer(0, "_VoxelBuffer", _voxelBuffer);
        BlitVolume.SetTexture(0, "_VolumeTexture", _volumeTexture);
        BlitVolume.Dispatch(0, VolumeSize / 8, VolumeSize / 8, VolumeSize / 8);

        // Render cube back depth
        var backDepth = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGBFloat);
        Graphics.SetRenderTarget(backDepth);
        GL.Clear(true, true, new Color(0, 0, 0, 0));

        _backDepthMaterial.SetPass(0);
        Graphics.DrawMeshNow(CubeMesh, GetComponent<MouseOrbit>().target, Quaternion.identity);
        
        // Render volume
        var cameraDepthBuffer = RenderTexture.GetTemporary(source.width, source.height, 24, RenderTextureFormat.Depth);
        var volumeTarget = RenderTexture.GetTemporary(source.width, source.height, 0, source.format);
        Graphics.SetRenderTarget(volumeTarget.colorBuffer, cameraDepthBuffer.depthBuffer);
        GL.Clear(true, true, new Color(0, 0, 0, 0));

        _rayMarchMaterial.SetInt("_VolumeSize", VolumeSize);
        _rayMarchMaterial.SetFloat("_Opacity", Opacity);
        _rayMarchMaterial.SetFloat("_StepSize", 1.0f / NumSteps);
        _rayMarchMaterial.SetFloat("_IntensityThreshold", IntensityThreshold);
        _rayMarchMaterial.SetColor("_SurfaceColor", SurfaceColor);
        _rayMarchMaterial.SetTexture("_CubeBackTex", backDepth);
        _rayMarchMaterial.SetTexture("_VolumeTex", _volumeTexture);
        _rayMarchMaterial.SetPass(0);

        Graphics.DrawMeshNow(CubeMesh, Vector3.zero, Quaternion.identity);

        // Change unity's camera depth texture with our own depth buffer
        Shader.SetGlobalTexture("_CameraDepthTexture", cameraDepthBuffer);

        // Composite pass
        _compositeMaterial.SetTexture("_BlendTex", volumeTarget);
        Graphics.Blit(source, destination, _compositeMaterial);

        RenderTexture.ReleaseTemporary(cameraDepthBuffer);
        RenderTexture.ReleaseTemporary(volumeTarget);
        RenderTexture.ReleaseTemporary(backDepth);
    }
}