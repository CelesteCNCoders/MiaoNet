#define DECLARE_TEXTURE(Name, index) \
    texture Name: register(t##index); \
    sampler Name##Sampler: register(s##index)

#define SAMPLE_TEXTURE(Name, texCoord) tex2D(Name##Sampler, texCoord)

uniform float2 Dimensions;

uniform float2 CenterPos;
uniform float MinAlpha;

DECLARE_TEXTURE(text, 0);

float4 SpritePixelShader(float4 inColor : COLOR0, float2 uv : TEXCOORD0) : COLOR0
{
    float2 worldPos = uv * Dimensions;
    float4 color = SAMPLE_TEXTURE(text, uv);
    
    float dist = distance(worldPos, CenterPos);
    float fadeFactor = smoothstep(32.0f, 96.0f, dist);
    float finalAlphaMult = lerp(MinAlpha, 1.0f, fadeFactor);
    
    color *= finalAlphaMult;
    color *= inColor;
    
    return color;
}

technique RadialFade
{
    pass pass0
    {
        PixelShader = compile ps_3_0 SpritePixelShader();
    }
}