// Cached board layers (DESIGN 2c, Two layers): rows, the hero and the footer, rasterized by Direct2D only when their data
// changes, composited at animated places, faded, tinted (a value's warm flash) and clipped to the ranking list's frame.

Texture2D layers : register(t2);

struct LayerIn
{
    float4 rect : INSTANCE0;   // x, y (top-left), width, height in points
    float4 uv : INSTANCE1;     // atlas u0, v0, u1, v1
    float4 tint : INSTANCE2;   // rgb (straight), how much of it replaces the layer's color
    float4 params : INSTANCE3; // opacity, unused x3
    float4 clip : INSTANCE4;   // min x, min y, max x, max y; none when max x <= min x
    uint vid : SV_VertexID;
};

struct LayerOut
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
    float2 canvas : TEXCOORD1;
    nointerpolation float4 tint : TEXCOORD2;
    nointerpolation float4 params : TEXCOORD3;
    nointerpolation float4 clip : TEXCOORD4;
};

LayerOut VsLayer(LayerIn input)
{
    float2 t = corners[input.vid] * 0.5 + 0.5;
    // Layers land on whole device pixels, so a resting layer's texels map one to one.
    float2 origin = round(input.rect.xy * viewport.z) / viewport.z;
    float2 p = origin + t * input.rect.zw;
    LayerOut output;
    output.position = orb_clip_position(p);
    output.uv = lerp(input.uv.xy, input.uv.zw, t);
    output.canvas = p;
    output.tint = input.tint;
    output.params = input.params;
    output.clip = input.clip;
    return output;
}

float4 PsLayer(LayerOut input) : SV_Target
{
    float keep = 1.0;
    if (input.clip.z > input.clip.x)
    {
        float aa = 1.0 / viewport.z;
        float2 lo = input.clip.xy - input.canvas;
        float2 hi = input.canvas - input.clip.zw;
        keep = saturate(0.5 - max(max(lo.x, lo.y), max(hi.x, hi.y)) / aa);
    }

    float4 texel = layers.SampleLevel(linear_clamp, input.uv, 0);
    float3 rgb = lerp(texel.rgb, input.tint.rgb * texel.a, input.tint.a);
    float k = input.params.x * keep * orb_clip_coverage(input.canvas);
    if (texel.a * k <= 0.0005)
    {
        discard;
    }

    return orb_output(rgb * k, texel.a * k);
}
