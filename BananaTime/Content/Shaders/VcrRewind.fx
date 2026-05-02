#if OPENGL
    #define SV_POSITION POSITION
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0_level_9_1
    #define PS_SHADERMODEL ps_4_0_level_9_1
#endif

Texture2D SpriteTexture;
sampler2D SpriteTextureSampler = sampler_state
{
    Texture = <SpriteTexture>;
};

float Time;

struct VertexShaderOutput
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR0;
    float2 TextureCoordinates : TEXCOORD0;
};

float hash(float2 p)
{
    return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
}

float4 MainPS(VertexShaderOutput input) : COLOR
{
    float2 uv = input.TextureCoordinates;

    // Per-row horizontal jitter: a tiny offset that varies with row + time.
    float rowJitter = (hash(float2(floor(uv.y * 360.0), floor(Time * 30.0))) - 0.5) * 0.004;

    // Scrolling tracking band: ~6% tall band sweeping bottom-to-top, with a heavier shear inside.
    float bandPos = frac(Time * 0.35);
    float bandHeight = 0.06;
    float bandMask = smoothstep(bandHeight, 0.0, abs(uv.y - bandPos));
    float bandShear = bandMask * (hash(float2(uv.y * 50.0, Time)) - 0.5) * 0.05;

    float2 jitteredUv = float2(uv.x + rowJitter + bandShear, uv.y);

    // Chromatic aberration: ~1.5px at 640 wide.
    float chroma = 0.0024;
    float r = tex2D(SpriteTextureSampler, jitteredUv + float2(chroma, 0.0)).r;
    float g = tex2D(SpriteTextureSampler, jitteredUv).g;
    float b = tex2D(SpriteTextureSampler, jitteredUv - float2(chroma, 0.0)).b;
    float a = tex2D(SpriteTextureSampler, jitteredUv).a;

    float3 color = float3(r, g, b);

    // Brighten and slightly desaturate the band for a "playback head" smear.
    float luma = dot(color, float3(0.299, 0.587, 0.114));
    color = lerp(color, float3(luma, luma, luma) + 0.15, bandMask * 0.55);

    // Scanlines: subtle multiplicative attenuation; doubled lines per source row at 360 px tall.
    float scan = 0.92 + 0.08 * sin(uv.y * 360.0 * 3.14159);
    color *= scan;

    // Static / noise sprinkled lightly.
    float noise = (hash(float2(uv.x * 320.0, uv.y * 180.0 + Time * 60.0)) - 0.5) * 0.12;
    color += noise;

    return float4(color, a) * input.Color;
}

technique VcrRewind
{
    pass P0
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
};
