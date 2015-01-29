Shader "Hidden/SSAO Pro V2"
{
	Properties
	{
		_MainTex ("Base (RGB)", 2D) = "white" {}
	}

	CGINCLUDE

		#include "UnityCG.cginc"

		sampler2D _MainTex;
		sampler2D_float _CameraDepthTexture;
		sampler2D_float _CameraDepthNormalsTexture;
		
		float4x4 _InverseViewProject;
		float4x4 _CameraModelView;

		sampler2D _NoiseTex;
		float4 _Params1; // Noise Size / Sample Radius / Intensity / Distance
		float4 _Params2; // Bias / Luminosity Contribution / Distance Cutoff / Cutoff Falloff

		inline float invlerp(float from, float to, float value)
		{
			return (value - from) / (to - from);
		}

		inline float3 getWSPosition(float2 uv, float depth)
		{
			// Compute world space position from the view depth
			float4 pos = float4(uv.xy * 2.0 - 1.0, depth, 1.0);
			float4 ray = mul(_InverseViewProject, pos);
			return ray.xyz / ray.w;
		}

		inline float3 getWSNormal(float2 uv)
		{
			// Get the view space normal and convert it to world space
			float3 nn = tex2D(_CameraDepthNormalsTexture, uv).xyz * float3(3.5554, 3.5554, 0) + float3(-1.7777, -1.7777, 1.0);
			float g = 2.0 / dot(nn.xyz, nn.xyz);
			float3 vsnormal = float3(g * nn.xy, g - 1.0); // View space
			float3 wsnormal = mul((float3x3)_CameraModelView, vsnormal); // World space
			return wsnormal;
		}

		inline float calcAO(float2 tcoord, float2 uv, float3 p, float3 cnorm)
		{
			float2 t = tcoord + uv;
			float depth = tex2D(_CameraDepthTexture, t).x;
			float3 diff = getWSPosition(t, depth) - p; // World space
			float3 v = normalize(diff);
			float d = length(diff) * _Params1.w;
			return max(0.0, dot(cnorm, v) - _Params2.x) * (1.0 / (1.0 + d)) * _Params1.z;
		}

		float ssao(float2 uv)
		{
			const float2 vec[4] = { float2(1.0, 0.0), float2(-1.0, 0.0), float2(0.0, 1.0), float2(0.0, -1.0) };
			
			float depth = tex2D(_CameraDepthTexture, uv).x;
			float3 position = getWSPosition(uv, depth); // World space
			float3 normal = getWSNormal(uv); // World space

			#if NOISE_ON
			float2 random = normalize(tex2D(_NoiseTex, _ScreenParams.xy * uv / _Params1.x).rg * 2.0 - 1.0);
			#endif

			float ao = 0.0;
			float eyeDepth = LinearEyeDepth(depth);
			float radius = _Params1.y / eyeDepth;

			// Skip out of range pixels
			clip(_Params2.z - eyeDepth);

			for (int j = 0; j < 4; j++)
			{
				float2 coord1;

				#if NOISE_ON
				coord1 = reflect(vec[j], random) * radius;
				#elif NOISE_OFF
				coord1 = vec[j] * radius;
				#endif

				#if !SAMPLES_VERY_LOW
				float2 coord2 = coord1 * 0.707;
				coord2 = float2(coord2.x - coord2.y, coord2.x + coord2.y);
				#endif
  
				#if SAMPLES_HIGH			// 16
				ao += calcAO(uv, coord1 * 0.25, position, normal);
				ao += calcAO(uv, coord2 * 0.50, position, normal);
				ao += calcAO(uv, coord1 * 0.75, position, normal);
				ao += calcAO(uv, coord2, position, normal);
				#elif SAMPLES_MEDIUM		// 12
				ao += calcAO(uv, coord1 * 0.25, position, normal);
				ao += calcAO(uv, coord2 * 0.50, position, normal);
				ao += calcAO(uv, coord1 * 0.75, position, normal);
				#elif SAMPLES_LOW			// 8
				ao += calcAO(uv, coord1 * 0.25, position, normal);
				ao += calcAO(uv, coord2 * 0.75, position, normal);
				#elif SAMPLES_VERY_LOW		// 4
				ao += calcAO(uv, coord1 * 0.50, position, normal);
				#endif
			}
			
			#if SAMPLES_HIGH
			ao /= 16.0;
			#elif SAMPLES_MEDIUM
			ao /= 12.0;
			#elif SAMPLES_LOW
			ao /= 8.0;
			#elif SAMPLES_VERY_LOW
			ao /= 4.0;
			#endif

			// Distance cutoff
			ao = lerp(1.0 - ao, 1.0, saturate(invlerp(_Params2.z - _Params2.w, _Params2.z, eyeDepth)));

			return ao;
		}

	ENDCG

	SubShader
	{
		ZTest Always Cull Off ZWrite Off
		Fog { Mode off }

		// (0) Clear
		Pass
		{
			CGPROGRAM

				#pragma vertex vert
				#pragma fragment frag
				#pragma fragmentoption ARB_precision_hint_fastest
				#pragma exclude_renderers flash gles gles3
				#pragma glsl
				
				struct v_data 
				{
					float4 pos : SV_POSITION; 
					float2 uv : TEXCOORD0;
				};

				v_data vert(appdata_img v)
				{
					v_data o;
					o.pos = mul(UNITY_MATRIX_MVP, v.vertex);
					o.uv = v.texcoord;        	        	
					return o; 
				}

				float4 frag(v_data i) : COLOR
				{
					return float4(1.0, 1.0, 1.0, 1.0);
				}

			ENDCG
		}

		// (1) SSAO
		Pass
		{
			CGPROGRAM

				#pragma vertex vert
				#pragma fragment frag
				#pragma fragmentoption ARB_precision_hint_fastest
				#pragma exclude_renderers flash gles gles3
				#pragma target 3.0
				#pragma glsl

				#pragma multi_compile SAMPLES_VERY_LOW  SAMPLES_LOW  SAMPLES_MEDIUM  SAMPLES_HIGH
				#pragma multi_compile LUM_CONTRIB_ON  LUM_CONTRIB_OFF
				#pragma multi_compile CUSTOM_COLOR_ON  CUSTOM_COLOR_OFF
				#pragma multi_compile NOISE_ON  NOISE_OFF
				
				float4 _MainTex_TexelSize;
				float4 _OcclusionColor;

				struct v_data 
				{
					float4 pos : SV_POSITION; 
					float2 uv : TEXCOORD0;
					
					#if UNITY_UV_STARTS_AT_TOP
					float2 uv2 : TEXCOORD1;
					#endif
				};

				v_data vert(appdata_img v)
				{
					v_data o;
					o.pos = mul(UNITY_MATRIX_MVP, v.vertex);
					o.uv = v.texcoord;
        	
					#if UNITY_UV_STARTS_AT_TOP
					o.uv2 = v.texcoord;
					if (_MainTex_TexelSize.y < 0.0)
						o.uv.y = 1.0 - o.uv.y;
					#endif
        	        	
					return o; 
				}

				float4 getAOColor(float ao, float2 uv)
				{
					#if LUM_CONTRIB_ON

					// Luminance for the current pixel, used to reduce the AO amount in bright areas
					float3 color = tex2D(_MainTex, uv).rgb;
					float luminance = dot(color, float3(0.299, 0.587, 0.114));
					float aofinal = lerp(ao, 1.0, luminance * _Params2.y);
					return float4(aofinal, aofinal, aofinal, 1.0);

					#elif LUM_CONTRIB_OFF

					return float4(ao, ao, ao, 1.0);

					#endif
				}

				float4 frag(v_data i) : COLOR
				{
					#if UNITY_UV_STARTS_AT_TOP

					#if CUSTOM_COLOR_ON
					return saturate(getAOColor(ssao(i.uv), i.uv2) + _OcclusionColor);
					#elif CUSTOM_COLOR_OFF
					return getAOColor(ssao(i.uv), i.uv2);
					#endif

					#else

					#if CUSTOM_COLOR_ON
					return saturate(getAOColor(ssao(i.uv), i.uv) + _OcclusionColor);
					#elif CUSTOM_COLOR_OFF
					return getAOColor(ssao(i.uv), i.uv);
					#endif
					
					#endif
				}

			ENDCG
		}

		// (2) Gaussian Blur
		Pass
		{
			CGPROGRAM

				#pragma vertex vert
				#pragma fragment frag
				#pragma fragmentoption ARB_precision_hint_fastest
				#pragma exclude_renderers flash gles gles3
				#pragma glsl

				#pragma multi_compile CUSTOM_COLOR_ON  CUSTOM_COLOR_OFF

				float2 _Direction;

				struct v2f
				{
					float4 pos : SV_POSITION;
					float2 uv : TEXCOORD0;
					float4 uv1 : TEXCOORD1;
					float4 uv2 : TEXCOORD2;
				};

				v2f vert(appdata_img v)
				{
					v2f o;
					o.pos = mul(UNITY_MATRIX_MVP, v.vertex);
					o.uv = MultiplyUV(UNITY_MATRIX_TEXTURE0, v.texcoord);
					float2 d1 = 1.3846153846 * _Direction;
					float2 d2 = 3.2307692308 * _Direction;
					o.uv1 = float4(o.uv + d1, o.uv - d1);
					o.uv2 = float4(o.uv + d2, o.uv - d2);
					return o;
				}

				float4 frag(v2f i) : COLOR
				{
					#if CUSTOM_COLOR_ON

					float3 c = tex2D(_MainTex, i.uv).rgb * 0.2270270270;
					c += tex2D(_MainTex, i.uv1.xy).rgb * 0.3162162162;
					c += tex2D(_MainTex, i.uv1.zw).rgb * 0.3162162162;
					c += tex2D(_MainTex, i.uv2.xy).rgb * 0.0702702703;
					c += tex2D(_MainTex, i.uv2.zw).rgb * 0.0702702703;
					return float4(c, 1.0);

					#elif CUSTOM_COLOR_OFF

					float c = tex2D(_MainTex, i.uv).r * 0.2270270270;
					c += tex2D(_MainTex, i.uv1.xy).r * 0.3162162162;
					c += tex2D(_MainTex, i.uv1.zw).r * 0.3162162162;
					c += tex2D(_MainTex, i.uv2.xy).r * 0.0702702703;
					c += tex2D(_MainTex, i.uv2.zw).r * 0.0702702703;
					return float4(c, c, c, 1.0);

					#endif
				}

			ENDCG
		}

		// (3) Bilateral Gaussian Blur
		Pass
		{
			CGPROGRAM

				#pragma vertex vert
				#pragma fragment frag
				#pragma fragmentoption ARB_precision_hint_fastest
				#pragma exclude_renderers flash gles gles3
				#pragma glsl

				#pragma multi_compile CUSTOM_COLOR_ON  CUSTOM_COLOR_OFF

				float2 _Direction;
				float _BilateralThreshold;

				struct v2f
				{
					float4 pos : SV_POSITION;
					float2 uv : TEXCOORD0;
					float4 uv1 : TEXCOORD1;
					float4 uv2 : TEXCOORD2;
				};

				v2f vert(appdata_img v)
				{
					v2f o;
					o.pos = mul(UNITY_MATRIX_MVP, v.vertex);
					o.uv = MultiplyUV(UNITY_MATRIX_TEXTURE0, v.texcoord);
					float2 d2 = 2.0 * _Direction;
					o.uv1 = float4(o.uv + _Direction, o.uv - _Direction);
					o.uv2 = float4(o.uv + d2, o.uv - d2);
					return o;
				}

				float4 frag(v2f i) : COLOR
				{
					float4 depthTmp, coeff;
					float depth = Linear01Depth(tex2D(_CameraDepthTexture, i.uv).x);

					#if CUSTOM_COLOR_ON
					
					float3 c = tex2D(_MainTex, i.uv).rgb * 0.2270270270;
					
					depthTmp.x = Linear01Depth(tex2D(_CameraDepthTexture, i.uv1.xy).x);
					depthTmp.y = Linear01Depth(tex2D(_CameraDepthTexture, i.uv1.zw).x);
					depthTmp.z = Linear01Depth(tex2D(_CameraDepthTexture, i.uv2.xy).x);
					depthTmp.w = Linear01Depth(tex2D(_CameraDepthTexture, i.uv2.zw).x);
					coeff = 1.0 / (1e-05 + abs(depth - depthTmp));
					c += tex2D(_MainTex, i.uv1.xy).rgb * coeff.x;
					c += tex2D(_MainTex, i.uv1.zw).rgb * coeff.y;
					c += tex2D(_MainTex, i.uv2.xy).rgb * coeff.z;
					c += tex2D(_MainTex, i.uv2.zw).rgb * coeff.w;

					c /= (coeff.x + coeff.y + coeff.z + coeff.w);
					return float4(c, 1);

					#elif CUSTOM_COLOR_OFF
					
					float c = tex2D(_MainTex, i.uv).r * 0.2270270270;

					depthTmp.x = Linear01Depth(tex2D(_CameraDepthTexture, i.uv1.xy).x);
					depthTmp.y = Linear01Depth(tex2D(_CameraDepthTexture, i.uv1.zw).x);
					depthTmp.z = Linear01Depth(tex2D(_CameraDepthTexture, i.uv2.xy).x);
					depthTmp.w = Linear01Depth(tex2D(_CameraDepthTexture, i.uv2.zw).x);
					coeff = 1.0 / (1e-05 + abs(depth - depthTmp));
					c += tex2D(_MainTex, i.uv1.xy).r * coeff.x;
					c += tex2D(_MainTex, i.uv1.zw).r * coeff.y;
					c += tex2D(_MainTex, i.uv2.xy).r * coeff.z;
					c += tex2D(_MainTex, i.uv2.zw).r * coeff.w;

					c /= (coeff.x + coeff.y + coeff.z + coeff.w);
					return float4(c, c, c, 1);

					#endif
				}

			ENDCG
		}

		// (4) Composite
		Pass
		{
			CGPROGRAM

				#pragma vertex vert
				#pragma fragment frag
				#pragma fragmentoption ARB_precision_hint_fastest
				#pragma exclude_renderers flash gles gles3
				#pragma glsl

				#pragma multi_compile CUSTOM_COLOR_ON  CUSTOM_COLOR_OFF
				
				float4 _MainTex_TexelSize;
				sampler2D _SSAOTex;
				
				struct v_data 
				{
					float4 pos : SV_POSITION; 
					float2 uv : TEXCOORD0;
					
					#if UNITY_UV_STARTS_AT_TOP
					float2 uv2 : TEXCOORD1;
					#endif
				};

				v_data vert(appdata_img v)
				{
					v_data o;
					o.pos = mul(UNITY_MATRIX_MVP, v.vertex);
					o.uv = v.texcoord;
        	
					#if UNITY_UV_STARTS_AT_TOP
					o.uv2 = v.texcoord;
					if (_MainTex_TexelSize.y < 0.0)
						o.uv.y = 1.0 - o.uv.y;
					#endif
        	        	
					return o; 
				}

				float4 frag(v_data i) : COLOR
				{
					#if UNITY_UV_STARTS_AT_TOP
					float3 color = tex2D(_MainTex, i.uv2).rgb;
					#else
					float3 color = tex2D(_MainTex, i.uv).rgb;
					#endif
					
					#if CUSTOM_COLOR_ON
					return float4(color * tex2D(_SSAOTex, i.uv).rgb, 1.0);
					#elif CUSTOM_COLOR_OFF
					return float4(color * tex2D(_SSAOTex, i.uv).r, 1.0);
					#endif
				}

			ENDCG
		}
	}

	FallBack off
}
