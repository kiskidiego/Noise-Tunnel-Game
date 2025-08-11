using Godot;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

[GlobalClass]
public partial class World : Resource
{
	[Export] public Vector3I chunkSize = new Vector3I(20, 20, 20);
	[Export] FastNoiseLite[] biomeNoises;
	[Export] public Biome[] biomes;
	[Export] int ceiling = 0;
	public ConcurrentDictionary<Vector3, Chunk> chunks = new ConcurrentDictionary<Vector3, Chunk>();
	RandomNumberGenerator rng = new RandomNumberGenerator();
	int Seed;
	public void SetSeed(int seed)
	{
		Seed = seed++;
		rng.Seed = (ulong)Seed;
		foreach (var noise in biomeNoises)
		{
			noise.Seed = seed++;
		}
		foreach (var biome in biomes)
		{
			biome.initNoiseFunctions(seed++);
		}
	}
	public Vector3I WorldToChunkOffset(Vector3I worldPosition)
	{
		return new Vector3I(
			worldPosition.X < 0 ? (worldPosition.X % chunkSize.X + chunkSize.X) % chunkSize.X : worldPosition.X % chunkSize.X,
			worldPosition.Y < 0 ? (worldPosition.Y % chunkSize.Y + chunkSize.Y) % chunkSize.Y : worldPosition.Y % chunkSize.Y,
			worldPosition.Z < 0 ? (worldPosition.Z % chunkSize.Z + chunkSize.Z) % chunkSize.Z : worldPosition.Z % chunkSize.Z
		);
	}
	public Vector3I WorldToChunkIndex(Vector3I worldPosition)
	{
		return new Vector3I(
			worldPosition.X < 0 ? (worldPosition.X + 1) / chunkSize.X - 1 : worldPosition.X / chunkSize.X,
			worldPosition.Y < 0 ? (worldPosition.Y + 1) / chunkSize.Y - 1 : worldPosition.Y / chunkSize.Y,
			worldPosition.Z < 0 ? (worldPosition.Z + 1) / chunkSize.Z - 1 : worldPosition.Z / chunkSize.Z
		);
	}
	public Vector3I ChunkIndexToWorld(Vector3I chunkIndex)
	{
		return new Vector3I(
			chunkIndex.X * chunkSize.X,
			chunkIndex.Y * chunkSize.Y,
			chunkIndex.Z * chunkSize.Z
		);
	}
	public Chunk GetChunk(Vector3I chunkIndex)
	{
		if (chunks.TryGetValue(chunkIndex, out Chunk chunk))
		{
			return chunk;
		}
		chunk = new Chunk();
		chunk.cellsScore = new float[chunkSize.X, chunkSize.Y, chunkSize.Z];
		chunk.cellsBiome = new int[chunkSize.X, chunkSize.Y, chunkSize.Z];
		chunks[chunkIndex] = chunk;
		return chunk;
	}
	public Chunk GetChunkFromWorld(Vector3I worldPosition)
	{
		Vector3I chunkIndex = WorldToChunkIndex(worldPosition);
		return GetChunk(chunkIndex);
	}
	public float GetCellScoreFromWorld(Vector3I worldPosition)
	{
		Chunk chunk = GetChunkFromWorld(worldPosition);
		Vector3I chunkOffset = WorldToChunkOffset(worldPosition);
		return chunk.cellsScore[chunkOffset.X, chunkOffset.Y, chunkOffset.Z];
	}
	public int GetCellBiomeFromWorld(Vector3I worldPosition)
	{
		Chunk chunk = GetChunkFromWorld(worldPosition);
		Vector3I chunkOffset = WorldToChunkOffset(worldPosition);
		return chunk.cellsBiome[chunkOffset.X, chunkOffset.Y, chunkOffset.Z];
	}
	public void SetCellScoreFromWorld(Vector3I worldPosition, float score)
	{
		Chunk chunk = GetChunkFromWorld(worldPosition);
		Vector3I chunkOffset = WorldToChunkOffset(worldPosition);
		chunk.cellsScore[chunkOffset.X, chunkOffset.Y, chunkOffset.Z] = score;
	}
	public void DetermineBiomes(Vector3I chunkCoords)
	{
		Chunk chunk = GetChunk(chunkCoords);
		Vector3I chunkPosition = ChunkIndexToWorld(chunkCoords);
		for (int x = 0; x < chunkSize.X; x++)
		{
			for (int y = 0; y < chunkSize.Y; y++)
			{
				for (int z = 0; z < chunkSize.Z; z++)
				{
					float[] biomeValues = new float[biomeNoises.Length];
					for (int i = 0; i < biomeNoises.Length; i++)
					{
						biomeValues[i] = biomeNoises[i].GetNoise3D(x + chunkPosition.X, y + chunkPosition.Y, z + chunkPosition.Z);
					}
					int bestBiomeIndex = -1;
					float minBiomeDeviation = float.MaxValue;
					for (int i = 0; i < biomes.Length; i++)
					{
						float deviation = 0;
						for (int j = 0; j < biomes[i].biomeWeights.Length; j++)
						{
							deviation += Mathf.Abs(biomeValues[j] - biomes[i].biomeWeights[j]);
						}
						if (deviation < minBiomeDeviation)
						{
							minBiomeDeviation = deviation;
							bestBiomeIndex = i;
						}
					}
					chunk.cellsBiome[x, y, z] = bestBiomeIndex;
				}
			}
		}
	}
	public void GenerateNoiseCaves(Vector3I chunkCoords)
	{
		//GD.Print($"Generating noise caves for chunk: {chunkCoords}");
		Chunk chunk = GetChunk(chunkCoords);

		if (chunkCoords.Y >= ceiling)
		{
			for (int x = 0; x < chunkSize.X; x++)
			{
				for (int y = 0; y < chunkSize.Y; y++)
				{
					for (int z = 0; z < chunkSize.Z; z++)
					{
						float cellValue = chunk.cellsScore[x, 0, z];
						if (cellValue > 0.0000001f && cellValue < -0.0000001f)
						{
							continue;
						}
						chunk.cellsScore[x, y, z] = -1;
						chunk.cellsBiome[x, y, z] = 0;
					}
				}
			}
			return;
		}

		Vector3I chunkPosition = ChunkIndexToWorld(chunkCoords);

		if (chunkCoords.Y == ceiling - 1)
		{
			for (int x = 0; x < chunkSize.X; x++)
			{
				for (int y = 0; y < chunkSize.Y; y++)
				{
					for (int z = 0; z < chunkSize.Z; z++)
					{
						float cellValue = chunk.cellsScore[x, y, z];
						if (cellValue > 0.0000001f && cellValue < -0.0000001f)
						{
							continue;
						}
						chunk.cellsScore[x, y, z] = biomes[chunk.cellsBiome[x, y, z]].GetNoiseValue(x + chunkPosition.X, y + chunkPosition.Y, z + chunkPosition.Z);
					}
				}
			}
			SetCellScoreFromWorld(new Vector3I(-1, -1, -1), 1);
			return;
		}
		
		for (int x = 0; x < chunkSize.X; x++)
		{
			for (int y = 0; y < chunkSize.Y; y++)
			{
				for (int z = 0; z < chunkSize.Z; z++)
				{
					float cellValue = chunk.cellsScore[x, y, z];
					if (cellValue > 0.0000001f && cellValue < -0.0000001f)
					{
						continue;
					}
					chunk.cellsScore[x, y, z] = biomes[chunk.cellsBiome[x, y, z]].GetNoiseValue(x + chunkPosition.X, y + chunkPosition.Y, z + chunkPosition.Z);
				}
			}
		}
	}
}
