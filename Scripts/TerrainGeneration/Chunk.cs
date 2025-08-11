using Godot;
using System;

[GlobalClass]
public partial class Chunk : Resource
{
    public float[,,] cellsScore;
    public int[,,] cellsBiome;
    public bool scored = false;
    public bool generated = false;
    public MeshInstance3D mesh = null;
    public float GetCellScore(int x, int y, int z)
    {
        return cellsScore[x, y, z];
    }
    public int GetCellBiome(int x, int y, int z)
    {
        return cellsBiome[x, y, z];
    }
    public void SetCellScore(int x, int y, int z, float score)
    {
        cellsScore[x, y, z] = score;
    }
    public void SetCellBiome(int x, int y, int z, int biome)
    {
        cellsBiome[x, y, z] = biome;
    }
}
