using Godot;
using System;

[GlobalClass]
public partial class Chunk : Resource
{
    public Cell[,,] cells;
    public bool scored = false;
    public bool generated = false;
    public MeshInstance3D mesh = null;
    public float GetCellScore(int x, int y, int z)
    {
        return cells[x, y, z].score;
    }
    public int GetCellBiome(int x, int y, int z)
    {
        return cells[x, y, z].biome;
    }
    public void SetCellScore(int x, int y, int z, float score)
    {
        cells[x, y, z].score = score;
    }
    public void SetCellBiome(int x, int y, int z, int biome)
    {
        cells[x, y, z].biome = biome;
    }
}
