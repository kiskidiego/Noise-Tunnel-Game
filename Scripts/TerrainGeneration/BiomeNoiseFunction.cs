using Godot;
using System;

[GlobalClass]
public partial class BiomeNoiseFunction : Resource
{
    [Export] public FastNoiseLite noise;
    [Export] float scalar = 1.0f;
    [Export] float offset = 0.0f;
    [Export] public Biome.CombineModes combineMode = Biome.CombineModes.Min;
    public float GetNoiseValue(float x, float y, float z)
    {
        float noiseValue = noise.GetNoise3D(x, y, z) * scalar + offset;
        noiseValue = Math.Clamp(noiseValue, -1f, 1f); // Ensure noise is within -1 to 1 range

        return noiseValue;
    }
}
