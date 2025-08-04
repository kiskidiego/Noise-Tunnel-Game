using Godot;
using System;
using Godot.Collections;

[GlobalClass]
public partial class Biome : Resource
{
	public enum CombineModes
	{
		Min,
		Max,
		Average
	}
	[Export] public string name;
	[Export] BiomeNoiseFunction[] noiseFunctions;
	[Export] public float[] biomeWeights;
	[ExportGroup("BiomeTextures")]
	[Export] public Texture2D baseColorTexture;
	[Export] public Texture2D normalTexture;
	[Export] public Texture2D roughnessTexture;
	[Export] public Texture2D metallicTexture;
	public int initNoiseFunctions(int seed)
	{
		int currentSeed = seed + 1;
		for (int i = 0; i < noiseFunctions.Length; i++)
		{
			noiseFunctions[i].noise.Seed = currentSeed + i;
			currentSeed++; // Increment seed for each noise function
		}
		return currentSeed; // Return the next seed value
	}
	public float GetNoiseValue(float x, float y, float z)
	{
		float noiseValue = 0;
		if (noiseFunctions[0].combineMode == CombineModes.Average)
		{
			noiseValue = 0f; // Reset to 0 for averaging
		}
		else if (noiseFunctions[0].combineMode == CombineModes.Min)
		{
			noiseValue = float.MaxValue; // Start with the maximum value for min comparison
		}
		else if (noiseFunctions[0].combineMode == CombineModes.Max)
		{
			noiseValue = float.MinValue; // Start with the minimum value for max comparison
		}

		for (int i = 0; i < noiseFunctions.Length; i++)
		{
			float noise = noiseFunctions[i].GetNoiseValue(x, y, z);
			if (noiseFunctions[i].combineMode == CombineModes.Min)
			{
				noiseValue = Math.Min(noiseValue, noise);
			}
			else if (noiseFunctions[i].combineMode == CombineModes.Max)
			{
				noiseValue = Math.Max(noiseValue, noise);
			}
			else if (noiseFunctions[i].combineMode == CombineModes.Average)
			{
				noiseValue += noise / noiseFunctions.Length; // Average the noise values
			}
		}
		//noiseValue -= 1f; // Adjust the noise value to be in the range of -1 to 1
		//noiseValue *= scalar; // Apply the scalar
		//noiseValue += offset; // Apply the offset
		//GD.Print($"Biome: {Name}, Noise Value: {noiseValue}, Scalar: {scalar}, Offset: {offset}");
		return noiseValue;
	}
}
