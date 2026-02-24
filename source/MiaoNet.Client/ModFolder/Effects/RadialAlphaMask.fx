#define DECLARE_TEXTURE(Name, index) \
    texture Name: register(t##index); \
    sampler Name##Sampler: register(s##index)

#define SAMPLE_TEXTURE(Name, texCoord) tex2D(Name##Sampler, texCoord)

uniform float Time; 
uniform float2 CamPos; 
uniform float2 Dimensions; 

uniform float2 CenterPos;      
uniform float FadeRadiusInner; 
uniform float FadeRadiusOuter; 
uniform float MinAlpha;        

DECLARE_TEXTURE(text, 0);

float4 SpritePixelShader(float2 uv : TEXCOORD0) : COLOR0
{
    float2 worldPos = (uv * Dimensions) + CamPos;
    float4 color = SAMPLE_TEXTURE(text, uv);
    
    float dist = distance(worldPos, CenterPos);
    float fadeFactor = smoothstep(FadeRadiusInner, FadeRadiusOuter, dist);
    float finalAlphaMult = lerp(MinAlpha, 1.0, fadeFactor);
    
    color.rgba *= finalAlphaMult;
    
    return color;
}

technique RadialFade
{
    pass pass0
    {
        PixelShader = compile ps_3_0 SpritePixelShader();
    }
}