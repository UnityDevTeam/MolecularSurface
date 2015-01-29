using UnityEngine;

[ExecuteInEditMode, AddComponentMenu("Image Effects/SSAO Pro")]
[RequireComponent(typeof(Camera))]
public class SSAOPro : MonoBehaviour
{
	public enum BlurMode
	{
		None,
		Gaussian,
		Bilateral
	}

	public enum SampleCount
	{
		VeryLow,
		Low,
		Medium,
		High
	}

	public enum AOMode
	{
		V11,
		V12
	}

	public AOMode Mode = AOMode.V12;
	public Texture2D NoiseTexture;
	public SampleCount Samples = SampleCount.Medium;

	[Range(1, 4)]
	public int Downsampling = 1;

	[Range(0.01f, 1.25f)]
	public float Radius = 0.125f;

	[Range(0f, 16f)]
	public float Intensity = 2f;

	[Range(0f, 10f)]
	public float Distance = 1f;

	[Range(0f, 1f)]
	public float Bias = 0.1f;

	[Range(0f, 1f)]
	public float LumContribution = 0.6f;

	public Color OcclusionColor = Color.black;

	public float CutoffDistance = 200f;
	public float CutoffFalloff = 50f;

	public BlurMode Blur = BlurMode.None;
	public bool BlurDownsampling = false;

	public bool DebugAO = false;

	protected Shader m_ShaderSSAO_v1;
	protected Shader m_ShaderSSAO_v2;
	protected Shader m_ShaderDepthNormal;
	protected Material m_Material_v1;
	protected Material m_Material_v2;
	protected Camera m_Camera;
	protected Camera m_RWSCamera;
	protected RenderTextureFormat m_RTFormat = RenderTextureFormat.RFloat;

	public Material Material
	{
		get
		{
			if (Mode == AOMode.V11)
			{
				if (m_Material_v1 == null)
				{
					m_Material_v1 = new Material(ShaderSSAO);
					m_Material_v1.hideFlags = HideFlags.HideAndDontSave;
				}

				return m_Material_v1;
			}
			else
			{
				if (m_Material_v2 == null)
				{
					m_Material_v2 = new Material(ShaderSSAO);
					m_Material_v2.hideFlags = HideFlags.HideAndDontSave;
				}

				return m_Material_v2;
			}
		}
	}

	public Shader ShaderSSAO
	{
		get
		{
			if (Mode == AOMode.V11)
			{
				if (m_ShaderSSAO_v1 == null)
					m_ShaderSSAO_v1 = Shader.Find("Hidden/SSAO Pro V1");

				return m_ShaderSSAO_v1;
			}
			else
			{
				if (m_ShaderSSAO_v2 == null)
					m_ShaderSSAO_v2 = Shader.Find("Hidden/SSAO Pro V2");

				return m_ShaderSSAO_v2;
			}
		}
	}

	public Shader ShaderDepthNormal
	{
		get
		{
			if (m_ShaderDepthNormal == null)
				m_ShaderDepthNormal = Shader.Find("Hidden/SSAO Pro - Depth Normal Map");

			return m_ShaderDepthNormal;
		}
	}

	void Start()
	{
		// Disable if we don't support image effects
		if (!SystemInfo.supportsImageEffects)
		{
			Debug.LogWarning("Image Effects are not supported on this platform.");
			enabled = false;
			return;
		}

		// Disable if we don't support render textures
		if (!SystemInfo.supportsRenderTextures || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RFloat))
		{
			if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.Depth))
			{
				Debug.LogWarning("RFloat && Depth RenderTextures are not supported on this platform.");
				enabled = false;
				return;
			}

			m_RTFormat = RenderTextureFormat.Depth;
		}

		// Disable the image effect if the shaders can't run on the users graphics card
		if (ShaderSSAO != null && !ShaderSSAO.isSupported)
		{
			Debug.LogWarning("Unsupported shader (SSAO).");
			enabled = false;
			return;
		}

		if (ShaderDepthNormal != null && !ShaderDepthNormal.isSupported)
		{
			Debug.LogWarning("Unsupported shader (DepthNormal).");
			enabled = false;
			return;
		}

		CheckShaderStates(true);
	}

	void OnEnable()
	{
		m_Camera = GetComponent<Camera>();
		m_Camera.depthTextureMode |= DepthTextureMode.DepthNormals;

		// Create the camera used to generate the high-precision normals & depth maps
		if (m_RWSCamera == null)
		{
			GameObject go = new GameObject("Depth Normal Camera", typeof(Camera));
			go.hideFlags = HideFlags.HideAndDontSave;
			m_RWSCamera = go.camera;
			m_RWSCamera.CopyFrom(m_Camera);
			m_RWSCamera.renderingPath = RenderingPath.Forward;
			m_RWSCamera.clearFlags = CameraClearFlags.Color;
			m_RWSCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
			m_RWSCamera.enabled = false;
		}
	}

	void OnDestroy()
	{
		if (m_Material_v1 != null)
			DestroyImmediate(m_Material_v1);

		if (m_Material_v2 != null)
			DestroyImmediate(m_Material_v2);

		if (m_RWSCamera != null)
			DestroyImmediate(m_RWSCamera.gameObject);
	}

	void OnPreRender()
	{
		// Render depth & normals to a custom Float RenderTexture
		m_RWSCamera.CopyFrom(m_Camera);
		m_RWSCamera.renderingPath = RenderingPath.Forward;
		m_RWSCamera.clearFlags = CameraClearFlags.Color;
		m_RWSCamera.backgroundColor = new Color(1f, 1f, 1f, 1f);

		RenderTexture rt = RenderTexture.GetTemporary((int)m_Camera.pixelWidth, (int)m_Camera.pixelHeight, 24, RenderTextureFormat.RFloat);
		rt.filterMode = FilterMode.Bilinear;
		m_RWSCamera.targetTexture = rt;
		m_RWSCamera.RenderWithShader(m_ShaderDepthNormal, "RenderType");
		rt.SetGlobalShaderProperty("_DepthNormalMapF32");
		m_RWSCamera.targetTexture = null;
		RenderTexture.ReleaseTemporary(rt);
	}

	[ImageEffectOpaque]
	void OnRenderImage(RenderTexture source, RenderTexture destination)
	{
		// Fail check
		if (ShaderSSAO == null || ShaderDepthNormal == null)
		{
			Graphics.Blit(source, destination);
			return;
		}

		// Shader keywords
		CheckShaderStates(false);

		// Uniforms
		if (NoiseTexture != null)
			Material.SetTexture("_NoiseTex", NoiseTexture);

		if (Mode == AOMode.V11)
		{
			Material.SetMatrix("_InverseViewProject", m_Camera.projectionMatrix.inverse);
		}
		else
		{
			Material.SetMatrix("_InverseViewProject", (m_Camera.projectionMatrix * m_Camera.worldToCameraMatrix).inverse);
			Material.SetMatrix("_CameraModelView", m_Camera.cameraToWorldMatrix);
		}

		Material.SetVector("_Params1", new Vector4(NoiseTexture == null ? 0f : NoiseTexture.width, Radius, Intensity, Distance));
		Material.SetVector("_Params2", new Vector4(Bias, LumContribution, CutoffDistance, CutoffFalloff));
		Material.SetColor("_OcclusionColor", OcclusionColor);

		// Render !
		if (Blur == BlurMode.None)
		{
			RenderTexture rt = RenderTexture.GetTemporary(source.width / Downsampling, source.height / Downsampling, 0);
			Graphics.Blit(rt, rt, Material, 0);

			if (DebugAO)
			{
				Graphics.Blit(source, rt, Material, 1);
				Graphics.Blit(rt, destination);
				RenderTexture.ReleaseTemporary(rt);
				return;
			}

			Graphics.Blit(source, rt, Material, 1);
			Material.SetTexture("_SSAOTex", rt);
			Graphics.Blit(source, destination, Material, 4);
			RenderTexture.ReleaseTemporary(rt);
		}
		else
		{
			int pass = (Blur == BlurMode.Bilateral) ? 3 : 2;

			int d = BlurDownsampling ? Downsampling : 1;
			RenderTexture rt1 = RenderTexture.GetTemporary(source.width / d, source.height / d, 0);
			RenderTexture rt2 = RenderTexture.GetTemporary(source.width / Downsampling, source.height / Downsampling, 0);
			Graphics.Blit(rt1, rt1, Material, 0);

			// SSAO
			Graphics.Blit(source, rt1, Material, 1);

			// Horizontal blur
			Material.SetVector("_Direction", new Vector2(1f / source.width, 0f));
			Graphics.Blit(rt1, rt2, Material, pass);

			// Vertical blur
			Material.SetVector("_Direction", new Vector2(0f, 1f / source.height));
			Graphics.Blit(rt2, DebugAO ? destination : rt1, Material, pass);

			if (!DebugAO)
			{
				Material.SetTexture("_SSAOTex", rt1);
				Graphics.Blit(source, destination, Material, 4);
			}

			RenderTexture.ReleaseTemporary(rt1);
			RenderTexture.ReleaseTemporary(rt2);
		}
	}

	// State switching... Gah, ugly but does the job until I refactor it.
	bool __useNoise = false;
	float __lumContribution = 0f;
	Color __occlusionColor = Color.black;
	SampleCount __samples = SampleCount.Medium;
	AOMode __aoMode = AOMode.V12;

	void CheckShaderStates(bool force)
	{
		if (!force)
		{
			if (__aoMode == Mode &&
				__useNoise == (NoiseTexture != null) &&
				__lumContribution == LumContribution &&
				__occlusionColor == OcclusionColor &&
				__samples == Samples)
			{
				return;
			}
		}

		Material.shaderKeywords = new string[]
		{
			(NoiseTexture != null) ? "NOISE_ON" : "NOISE_OFF",
			(LumContribution > 0.0001f) ? "LUM_CONTRIB_ON" : "LUM_CONTRIB_OFF",
			(OcclusionColor == Color.black) ? "CUSTOM_COLOR_OFF" : "CUSTOM_COLOR_ON",
			(Samples == SampleCount.Low) ? "SAMPLES_LOW"
				: (Samples == SampleCount.Medium) ? "SAMPLES_MEDIUM"
				: (Samples == SampleCount.High) ? "SAMPLES_HIGH"
				: "SAMPLES_VERY_LOW"
		};

		__useNoise = (NoiseTexture != null);
		__lumContribution = LumContribution;
		__occlusionColor = OcclusionColor;
		__samples = Samples;
		__aoMode = Mode;
	}
}
