using Godot;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Array = Godot.Collections.Array;
using System.Linq;
using Godot.Collections;


public partial class TerrainGeneration : Node
{
	[Export] int seed = 0;
	[Export] FastNoiseLite baseNoise;
	[Export] FastNoiseLite[] biomeNoises;
	[Export] Biome[] biomes;
	[Export] int chunkSizeX = 5, chunkSizeY = 5, chunkSizeZ = 5;
	[Export] float floorHeight = 50f;
	[Export] int tunnelWidth = 1;
	[Export] float pathStraightness = 0.6f; // How straight the paths should be, 1 is perfectly straight, 0 is completely random
	[Export] PointOfInterest[] pointsOfInterest;
	[Export] int chunkDistance = 2; // How many chunks away to generate
	[Export] ShaderMaterial biomeBaseMaterial;
	[Export] Player player;
	ConcurrentDictionary<Vector3I, float[,,]> chunks = new ConcurrentDictionary<Vector3I, float[,,]>();
	ConcurrentDictionary<Vector3I, bool> chunkGenerated = new ConcurrentDictionary<Vector3I, bool>();
	ConcurrentDictionary<Vector3I, bool> chunkScored = new ConcurrentDictionary<Vector3I, bool>();
	ConcurrentDictionary<Vector3I, MeshInstance3D> chunkMeshes = new ConcurrentDictionary<Vector3I, MeshInstance3D>();
	ConcurrentDictionary<Vector3I, int[,,]> cellBiomes = new ConcurrentDictionary<Vector3I, int[,,]>();
	RandomNumberGenerator rng = new RandomNumberGenerator();
	Vector3I currentChunkCoords = new Vector3I(int.MaxValue, int.MaxValue, int.MaxValue);
	bool generating = false;
	bool firstGeneration = true; // Flag to indicate if this is the first generation
	
	public override void _Ready()
	{
		PrepareTextures();
		Generate();
	}
	void PrepareTextures()
	{
		Array<Image> colorTextureArray = new Array<Image>();
		Array<Image> normalTextureArray = new Array<Image>();
		Array<Image> metalRoughTextureArray = new Array<Image>();
		foreach (Biome biome in biomes)
		{
			colorTextureArray.Add(biome.baseColorTexture?.GetImage());
			normalTextureArray.Add(biome.normalTexture?.GetImage());
			metalRoughTextureArray.Add(biome.metalRoughTexture?.GetImage());
		}
		Texture2DArray colorTexture = new Texture2DArray();
		colorTexture.CreateFromImages(colorTextureArray);
		Texture2DArray normalTexture = new Texture2DArray();
		normalTexture.CreateFromImages(normalTextureArray);
		Texture2DArray metalRoughTexture = new Texture2DArray();
		metalRoughTexture.CreateFromImages(metalRoughTextureArray);

		biomeBaseMaterial.SetShaderParameter("colorTexture", colorTexture);
		biomeBaseMaterial.SetShaderParameter("normalTexture", normalTexture);
		biomeBaseMaterial.SetShaderParameter("metalRoughTexture", metalRoughTexture);
		biomeBaseMaterial.SetShaderParameter("biomeAmount", biomes.Length);
	}
	void Generate()
	{
		if (seed == 0)
		{
			rng.Randomize();
			seed = (int)rng.Seed;
		}
		else
		{
			rng.Seed = (ulong)seed;
		}
		int currentSeed = seed;
		baseNoise.Seed = currentSeed++;
		for (int i = 0; i < biomeNoises.Length; i++)
		{
			biomeNoises[i].Seed = currentSeed++;
		}
		for (int i = 0; i < biomes.Length; i++)
		{
			currentSeed = biomes[i].initNoiseFunctions(currentSeed);
		}
		PreparePointsOfInterest();
	}
	List<Vector3I> GetAllCellsInRadiusRecursive(Vector3I position, int radius)
	{
		List<Vector3I> cells = new List<Vector3I>();

		if (radius <= 0)
		{
			cells.Add(position);
			return cells;
		}
		if (radius == 1)
		{
			for (int x = -1; x <= 1; x++)
			{
				for (int y = -1; y <= 1; y++)
				{
					for (int z = -1; z <= 1; z++)
					{
						cells.Add(new Vector3I(position.X + x, position.Y + y, position.Z + z));
					}
				}
			}
			return cells;
		}
		for (int i = -1; i <= 1; i++)
		{
			cells.AddRange(GetAllCellsInRadiusRecursive(new Vector3I(position.X + i, position.Y, position.Z), radius - 1));
			cells.AddRange(GetAllCellsInRadiusRecursive(new Vector3I(position.X, position.Y + i, position.Z), radius - 1));
			cells.AddRange(GetAllCellsInRadiusRecursive(new Vector3I(position.X, position.Y, position.Z + i), radius - 1));
		}
		return cells;
	}
	List<Vector3I> GetAllCellsInRadius(Vector3I position, int radius)
	{
		List<Vector3I> cells = GetAllCellsInRadiusRecursive(position, radius);
		cells = cells.Distinct().ToList(); // Remove duplicates
		return cells;
	}
	List<Vector3I> GetAllChunksFromCells(List<Vector3I> cells)
	{
		List<Vector3I> chunks = new List<Vector3I>();
		foreach (var cell in cells)
		{
			for(int x = -1; x <= 1; x++)
			{
				for(int y = -1; y <= 1; y++)
				{
					for(int z = -1; z <= 1; z++)
					{
						Vector3I chunk = new Vector3I(
							WorldToChunkIndex(cell.X + x, chunkSizeX),
							WorldToChunkIndex(cell.Y + y, chunkSizeY),
							WorldToChunkIndex(cell.Z + z, chunkSizeZ)
						);
						if (!chunks.Contains(chunk))
						{
							chunks.Add(chunk);
						}
					}
				}
			}
		}
		return chunks;
	}

	public void TerraformAt(Vector3 position, int TerraformRadius, float terraformPotency)
	{
		Vector3I roundedPosition = new Vector3I(
			Mathf.FloorToInt(position.X),
			Mathf.FloorToInt(position.Y),
			Mathf.FloorToInt(position.Z)
		);
		List<Vector3I> cells = GetAllCellsInRadius(roundedPosition, TerraformRadius);

		foreach (var cell in cells)
		{
			float cellValue = GetCellFromWorld(cell.X, cell.Y, cell.Z);
			cellValue += terraformPotency;
			cellValue = Mathf.Clamp(cellValue, -1f, 1f); // Ensure the value is within the valid range
			SetCellFromWorld(cell.X, cell.Y, cell.Z, cellValue + terraformPotency);
		}
		List<Vector3I> chunks = GetAllChunksFromCells(cells);
		foreach (var chunk in chunks)
		{
			List<Vector3> vertices = new List<Vector3>();
			List<Vector3> normals = new List<Vector3>();
			List<Color> biomeValues = new List<Color>();
			List<float> biomeIndices = new List<float>();
			MarchingCubesAlgorithm(chunk, vertices, normals, biomeValues, biomeIndices);
			//InterpolateNormals(vertices, normals);
			MeshInstance3D meshInstance = chunkMeshes.GetValueOrDefault(chunk, null);
			//GD.Print($"Updating chunk {chunk} mesh instance: {(meshInstance != null ? "Exists" : "Does not exist")}");
			if (meshInstance == null)
			{
				meshInstance = new MeshInstance3D();
				chunkMeshes[chunk] = meshInstance;
				meshInstance.Mesh = new ArrayMesh();
				AddChild(meshInstance);
			}
			(meshInstance.Mesh as ArrayMesh).ClearSurfaces();
			Array arrays = new Array();
			arrays.Resize((int)ArrayMesh.ArrayType.Max);
			arrays[(int)ArrayMesh.ArrayType.Vertex] = vertices.ToArray();
			arrays[(int)ArrayMesh.ArrayType.Normal] = normals.ToArray();
			arrays[(int)Mesh.ArrayType.Color] = biomeValues.ToArray();
			arrays[(int)Mesh.ArrayType.Tangent] = biomeIndices.ToArray();

			(meshInstance.Mesh as ArrayMesh).AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

			meshInstance.Mesh.SurfaceSetMaterial(0, biomeBaseMaterial);
			//(meshInstance.Mesh.SurfaceGetMaterial(0) as StandardMaterial3D).VertexColorUseAsAlbedo = true;

			StaticBody3D staticBody = meshInstance.GetChild<StaticBody3D>(0);
			if (staticBody == null)
			{
				staticBody = new StaticBody3D();
				meshInstance.AddChild(staticBody);
			}
			CollisionShape3D collisionShape = staticBody.GetChild<CollisionShape3D>(0);
			if (collisionShape == null)
			{
				collisionShape = new CollisionShape3D();
				staticBody.AddChild(collisionShape);
			}
			collisionShape.Shape = meshInstance.Mesh.CreateTrimeshShape();
		}
	}
	public void GenerateFrom(Vector3 position)
	{
		
		Vector3I chunkCoords = new Vector3I(
			WorldToChunkIndex((int)position.X, chunkSizeX),
			WorldToChunkIndex((int)position.Y, chunkSizeY),
			WorldToChunkIndex((int)position.Z, chunkSizeZ)
		);

		if (currentChunkCoords == chunkCoords || generating) return; // If the chunk is already generated or being generated, skip
		generating = true;
		currentChunkCoords = chunkCoords;

		GenerateFromAsync(chunkCoords);
	}
	async void GenerateFromAsync(Vector3I chunkCoords)
	{
		await Task.Run(() =>
		{
			//float currentTime = Time.GetTicksMsec();
			for (int x = -chunkDistance - 1; x <= chunkDistance + 1; x++)
			{
				for (int y = -chunkDistance - 1; y <= chunkDistance + 1; y++)
				{
					for (int z = -chunkDistance - 1; z <= chunkDistance + 1; z++)
					{
						Vector3I coords = new Vector3I(chunkCoords.X + x, chunkCoords.Y + y, chunkCoords.Z + z);
						if (chunkScored.TryGetValue(coords, out bool scored) && scored)
						{
							continue; // Skip already scored chunks
						}
						chunkScored[coords] = true; // Mark chunk as scored
						cellBiomes.TryAdd(coords, new int[chunkSizeX, chunkSizeY, chunkSizeZ]);
						DetermineBiomes(coords);
						GenerateNoiseCaves(coords);
					}
				}
			}
			//float elapsedTime = Time.GetTicksMsec() - currentTime;
			//GD.Print($"Noise caves generation took {elapsedTime} ms for chunk: {chunkCoords}");
			for (int x = -chunkDistance; x <= chunkDistance; x++)
			{
				for (int y = -chunkDistance; y <= chunkDistance; y++)
				{
					for (int z = -chunkDistance; z <= chunkDistance; z++)
					{
						Vector3I coords = new Vector3I(chunkCoords.X + x, chunkCoords.Y + y, chunkCoords.Z + z);
						if (chunkGenerated.TryGetValue(coords, out bool generated) && generated)
						{
							continue; // Skip already generated chunks
						}
						chunkGenerated[coords] = true; // Mark chunk as generated
						GenerateChunk(coords);
					}
				}
			}
		});
		generating = false; // Reset generating flag after generation is done
		if(firstGeneration)
		{
			player.ProcessMode = ProcessModeEnum.Inherit;
		}
		firstGeneration = false; // Reset first generation flag
	}
	void GenerateChunk(Vector3I chunkCoords)
	{
		List<Vector3> vertices = new List<Vector3>();
		List<Vector3> normals = new List<Vector3>();
		List<Color> biomeValues = new List<Color>();
		List<float> biomeIndices = new List<float>();
		MarchingCubesAlgorithm(chunkCoords, vertices, normals, biomeValues, biomeIndices);
		//InterpolateNormals(vertices, normals);
		GenerateGeometry(chunkCoords, vertices, normals, biomeValues, biomeIndices);
	}
	void PreparePointsOfInterest()
	{
		if (pointsOfInterest == null || pointsOfInterest.Length == 0) return;

		foreach (var poi in pointsOfInterest)
		{
			if (poi.leadsTo == null || poi.leadsTo.Length == 0) continue;
			string cells = "";
			foreach (int index in poi.leadsTo)
			{
				Vector3I start = poi.Position;
				float[,,] startChunk = GetChunkFromWorld(start.X, start.Y, start.Z);

				startChunk[WorldToChunkOffset(start.X, chunkSizeX), WorldToChunkOffset(start.Y, chunkSizeY), WorldToChunkOffset(start.Z, chunkSizeZ)] = -1; // Mark the start point

				if (index < 0 || index >= pointsOfInterest.Length) continue;
				Vector3I end = pointsOfInterest[index].Position;
				while (start != end)
				{
					start = NextCellInPath(start, end, false);
					while (GetCellFromWorld(start.X, start.Y, start.Z) == -1)
					{
						start = NextCellInPath(start, end, false); // If already marked, find next cell
					}
					for (int x = -tunnelWidth; x <= tunnelWidth; x++)
					{
						for (int y = -tunnelWidth; y <= tunnelWidth; y++)
						{
							for (int z = -tunnelWidth; z <= tunnelWidth; z++)
							{
								//if (x == 0 && y == 0 && z == 0) continue; // Skip the center cell
								cells += $"({start.X + x}, {start.Y + y}, {start.Z + z}): {-1f / (1 + Mathf.Max(Mathf.Abs(x), Mathf.Max(Mathf.Abs(y), Mathf.Abs(z))))}\n";
								SetCellFromWorld(start.X + x, start.Y + y, start.Z + z, -1f / (1 + Mathf.Max(Mathf.Abs(x), Mathf.Max(Mathf.Abs(y), Mathf.Abs(z))))); // Mark the surrounding cells as part of the path
							}
						}
					}
				}
			}
			//GD.Print($"Generated path for POI {poi.Name} with cells: {cells}");
		}
	}
	Vector3I NextCellInPath(Vector3I from, Vector3I to, bool turbulence = true)
	{
		int dx = Math.Abs(to.X - from.X);
		int dy = Math.Abs(to.Y - from.Y);
		int dz = Math.Abs(to.Z - from.Z);
		int xsing = from.X < to.X ? 1 : -1;
		int ysing = from.Y < to.Y ? 1 : -1;
		int zsing = from.Z < to.Z ? 1 : -1;
		float noiseValue = baseNoise.GetNoise3D(from.X, from.Y, from.Z);
		if (dx > dy && dx > dz)
		{
			//GD.Print($"Next cell in line from {from} to {to} is along X axis.");
			if (turbulence && noiseValue > pathStraightness)
			{
				int rand = rng.RandiRange(0, 3);
				//GD.Print($"Noise value at {from} is {noiseValue}, deforming path: {rand}");
				switch (rand)
				{
					case 0:
						return new Vector3I(from.X, from.Y + 1, from.Z);
					case 1:
						return new Vector3I(from.X, from.Y - 1, from.Z);
					case 2:
						return new Vector3I(from.X, from.Y, from.Z + 1);
					default:
						return new Vector3I(from.X, from.Y, from.Z - 1);
				}
			}
			else
			{
				return new Vector3I(from.X + xsing, from.Y, from.Z);
			}
		}
		else if (dy >= dx && dy >= dz)
		{
			//GD.Print($"Next cell in line from {from} to {to} is along Y axis.");
			if (turbulence && noiseValue > pathStraightness)
			{
				int rand = rng.RandiRange(0, 3);
				//GD.Print($"Noise value at {from} is {noiseValue}, deforming path: {rand}");
				switch (rand)
				{
					case 0:
						return new Vector3I(from.X + 1, from.Y, from.Z);
					case 1:
						return new Vector3I(from.X - 1, from.Y, from.Z);
					case 2:
						return new Vector3I(from.X, from.Y, from.Z + 1);
					default:
						return new Vector3I(from.X, from.Y, from.Z - 1);
				}
			}
			else
			{
				return new Vector3I(from.X, from.Y + ysing, from.Z);
			}
		}
		else
		{
			//GD.Print($"Next cell in line from {from} to {to} is along Z axis.");
			if (turbulence && noiseValue > pathStraightness)
			{
				int rand = rng.RandiRange(0, 3);
				//GD.Print($"Noise value at {from} is {noiseValue}, deforming path: {rand}");
				switch (rand)
				{
					case 0:
						return new Vector3I(from.X + 1, from.Y, from.Z);
					case 1:
						return new Vector3I(from.X - 1, from.Y, from.Z);
					case 2:
						return new Vector3I(from.X, from.Y + 1, from.Z);
					default:
						return new Vector3I(from.X, from.Y - 1, from.Z);
				}
			}
			else
			{
				return new Vector3I(from.X, from.Y, from.Z + zsing);
			}
		}
	}
	void DetermineBiomes(Vector3I chunkCoords)
	{
		//GD.Print($"Generating noise caves for chunk: {chunkCoords}");
		float[,,] cells = GetChunk(chunkCoords.X, chunkCoords.Y, chunkCoords.Z);
		int chunkPositionX = ChunkIndexToWorld(chunkCoords.X, chunkSizeX);
		int chunkPositionY = ChunkIndexToWorld(chunkCoords.Y, chunkSizeY);
		int chunkPositionZ = ChunkIndexToWorld(chunkCoords.Z, chunkSizeZ);
		for (int x = 0; x < cells.GetLength(0); x++)
		{
			for (int y = 0; y < cells.GetLength(1); y++)
			{
				for (int z = 0; z < cells.GetLength(2); z++)
				{
					float[] biomeValues = new float[biomeNoises.Length];
					for (int i = 0; i < biomeNoises.Length; i++)
					{
						biomeValues[i] = biomeNoises[i].GetNoise3D(x + chunkPositionX, y + chunkPositionY, z + chunkPositionZ);
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
					cellBiomes[chunkCoords][x, y, z] = bestBiomeIndex;
				}
			}
		}
	}
	void GenerateNoiseCaves(Vector3I chunkCoords)
	{
		//GD.Print($"Generating noise caves for chunk: {chunkCoords}");
		float[,,] cells = GetChunk(chunkCoords.X, chunkCoords.Y, chunkCoords.Z);
		int chunkPositionX = ChunkIndexToWorld(chunkCoords.X, chunkSizeX);
		int chunkPositionY = ChunkIndexToWorld(chunkCoords.Y, chunkSizeY);
		int chunkPositionZ = ChunkIndexToWorld(chunkCoords.Z, chunkSizeZ);
		for (int x = 0; x < cells.GetLength(0); x++)
		{
			for (int y = 0; y < cells.GetLength(1); y++)
			{
				for (int z = 0; z < cells.GetLength(2); z++)
				{
					if (cells[x, y, z] < 0)
					{
						//GD.Print($"Skipping cell ({x}, {y}, {z}) at chunk: {chunkCoords} as it is already part of a path or cave.");
						continue; // Skip if this cell is part of a path
					}
					
					cells[x, y, z] = biomes[cellBiomes[chunkCoords][x, y, z]].GetNoiseValue(x + chunkPositionX, y + chunkPositionY, z + chunkPositionZ);
				}
			}
		}
	}
	void MarchingCubesAlgorithm(Vector3I chunkCoords, List<Vector3> vertices, List<Vector3> normals, List<Color> biomeValues, List<float> biomeIndices)
	{
		int chunkPositionX = ChunkIndexToWorld(chunkCoords.X, chunkSizeX);
		int chunkPositionY = ChunkIndexToWorld(chunkCoords.Y, chunkSizeY);
		int chunkPositionZ = ChunkIndexToWorld(chunkCoords.Z, chunkSizeZ);

		float[,,] cells = new float[chunkSizeX + 2, chunkSizeY + 2, chunkSizeZ + 2];
		for (int x = 0; x < chunkSizeX + 2; x++)
		{
			for (int y = 0; y < chunkSizeY + 2; y++)
			{
				for (int z = 0; z < chunkSizeZ + 2; z++)
				{
					cells[x, y, z] = GetCellFromWorld(chunkPositionX + x - 1, chunkPositionY + y - 1, chunkPositionZ + z - 1);
				}
			}
		}

		//GD.Print($"Marching cubes algorithm started at chunk: {chunkCoords} with position: ({chunkPositionX}, {chunkPositionY}, {chunkPositionZ})");
		//GD.Print("Marching cubes algorithm");
		for (int i = 0; i < cells.GetLength(0) - 1; i++)
		{
			for (int j = 0; j < cells.GetLength(1) - 1; j++)
			{
				for (int k = 0; k < cells.GetLength(2) - 1; k++)
				{
					byte cubeIndex = 0;
					if (cells[i, j, k] < floorHeight)
						cubeIndex |= 1;
					if (cells[i + 1, j, k] < floorHeight)
						cubeIndex |= 2;
					if (cells[i + 1, j, k + 1] < floorHeight)
						cubeIndex |= 4;
					if (cells[i, j, k + 1] < floorHeight)
						cubeIndex |= 8;
					if (cells[i, j + 1, k] < floorHeight)
						cubeIndex |= 16;
					if (cells[i + 1, j + 1, k] < floorHeight)
						cubeIndex |= 32;
					if (cells[i + 1, j + 1, k + 1] < floorHeight)
						cubeIndex |= 64;
					if (cells[i, j + 1, k + 1] < floorHeight)
						cubeIndex |= 128;

					if (cubeIndex == 0 || cubeIndex == 255)
						continue;

					//GD.Print("Cube index: " + cubeIndex);

					if (vertices == null)
					{
						vertices = new List<Vector3>();
						normals = new List<Vector3>();
						biomeValues = new List<Color>();
						biomeIndices = new List<float>();
					}

					Vector3[] edgeVertices = new Vector3[12];
					if ((MarchTables.edges[cubeIndex] & 1) == 1)
					{
						edgeVertices[0] = VertexInterpolation(new Vector3(i + chunkPositionX, j + chunkPositionY, k + chunkPositionZ), new Vector3(i + chunkPositionX + 1, j + chunkPositionY, k + chunkPositionZ), cells[i, j, k], cells[i + 1, j, k]);
					}
					if ((MarchTables.edges[cubeIndex] & 2) == 2)
					{
						edgeVertices[1] = VertexInterpolation(new Vector3(i + chunkPositionX + 1, j + chunkPositionY, k + chunkPositionZ), new Vector3(i + chunkPositionX + 1, j + chunkPositionY, k + chunkPositionZ + 1), cells[i + 1, j, k], cells[i + 1, j, k + 1]);
					}
					if ((MarchTables.edges[cubeIndex] & 4) == 4)
					{
						edgeVertices[2] = VertexInterpolation(new Vector3(i + chunkPositionX + 1, j + chunkPositionY, k + chunkPositionZ + 1), new Vector3(i + chunkPositionX, j + chunkPositionY, k + chunkPositionZ + 1), cells[i + 1, j, k + 1], cells[i, j, k + 1]);
					}
					if ((MarchTables.edges[cubeIndex] & 8) == 8)
					{
						edgeVertices[3] = VertexInterpolation(new Vector3(i + chunkPositionX, j + chunkPositionY, k + chunkPositionZ + 1), new Vector3(i + chunkPositionX, j + chunkPositionY, k + chunkPositionZ), cells[i, j, k + 1], cells[i, j, k]);
					}
					if ((MarchTables.edges[cubeIndex] & 16) == 16)
					{
						edgeVertices[4] = VertexInterpolation(new Vector3(i + chunkPositionX, j + chunkPositionY + 1, k + chunkPositionZ), new Vector3(i + chunkPositionX + 1, j + chunkPositionY + 1, k + chunkPositionZ), cells[i, j + 1, k], cells[i + 1, j + 1, k]);
					}
					if ((MarchTables.edges[cubeIndex] & 32) == 32)
					{
						edgeVertices[5] = VertexInterpolation(new Vector3(i + chunkPositionX + 1, j + chunkPositionY + 1, k + chunkPositionZ), new Vector3(i + chunkPositionX + 1, j + chunkPositionY + 1, k + chunkPositionZ + 1), cells[i + 1, j + 1, k], cells[i + 1, j + 1, k + 1]);
					}
					if ((MarchTables.edges[cubeIndex] & 64) == 64)
					{
						edgeVertices[6] = VertexInterpolation(new Vector3(i + chunkPositionX + 1, j + chunkPositionY + 1, k + chunkPositionZ + 1), new Vector3(i + chunkPositionX, j + chunkPositionY + 1, k + chunkPositionZ + 1), cells[i + 1, j + 1, k + 1], cells[i, j + 1, k + 1]);
					}
					if ((MarchTables.edges[cubeIndex] & 128) == 128)
					{
						edgeVertices[7] = VertexInterpolation(new Vector3(i + chunkPositionX, j + chunkPositionY + 1, k + chunkPositionZ + 1), new Vector3(i + chunkPositionX, j + chunkPositionY + 1, k + chunkPositionZ), cells[i, j + 1, k + 1], cells[i, j + 1, k]);
					}
					if ((MarchTables.edges[cubeIndex] & 256) == 256)
					{
						edgeVertices[8] = VertexInterpolation(new Vector3(i + chunkPositionX, j + chunkPositionY, k + chunkPositionZ), new Vector3(i + chunkPositionX, j + chunkPositionY + 1, k + chunkPositionZ), cells[i, j, k], cells[i, j + 1, k]);
					}
					if ((MarchTables.edges[cubeIndex] & 512) == 512)
					{
						edgeVertices[9] = VertexInterpolation(new Vector3(i + chunkPositionX + 1, j + chunkPositionY, k + chunkPositionZ), new Vector3(i + chunkPositionX + 1, j + chunkPositionY + 1, k + chunkPositionZ), cells[i + 1, j, k], cells[i + 1, j + 1, k]);
					}
					if ((MarchTables.edges[cubeIndex] & 1024) == 1024)
					{
						edgeVertices[10] = VertexInterpolation(new Vector3(i + chunkPositionX + 1, j + chunkPositionY, k + chunkPositionZ + 1), new Vector3(i + chunkPositionX + 1, j + chunkPositionY + 1, k + chunkPositionZ + 1), cells[i + 1, j, k + 1], cells[i + 1, j + 1, k + 1]);
					}
					if ((MarchTables.edges[cubeIndex] & 2048) == 2048)
					{
						edgeVertices[11] = VertexInterpolation(new Vector3(i + chunkPositionX, j + chunkPositionY, k + chunkPositionZ + 1), new Vector3(i + chunkPositionX, j + chunkPositionY + 1, k + chunkPositionZ + 1), cells[i, j, k + 1], cells[i, j + 1, k + 1]);
					}

					for (int l = 0; MarchTables.triangles[cubeIndex, l] != -1; l += 3)
					{
						vertices.Add(edgeVertices[MarchTables.triangles[cubeIndex, l]]);
						vertices.Add(edgeVertices[MarchTables.triangles[cubeIndex, l + 1]]);
						vertices.Add(edgeVertices[MarchTables.triangles[cubeIndex, l + 2]]);
						Vector3 normal = (vertices[vertices.Count - 3] - vertices[vertices.Count - 2]).Cross(vertices[vertices.Count - 1] - vertices[vertices.Count - 2]);//.Normalized();
						normals.Add(normal);
						normals.Add(normal);
						normals.Add(normal);

						float vertex1Biome = GetCellBiomeFromWorld(Mathf.RoundToInt(vertices[vertices.Count - 3].X), Mathf.RoundToInt(vertices[vertices.Count - 3].Y), Mathf.RoundToInt(vertices[vertices.Count - 3].Z)) / (float)biomes.Length;
						float vertex2Biome = GetCellBiomeFromWorld(Mathf.RoundToInt(vertices[vertices.Count - 2].X), Mathf.RoundToInt(vertices[vertices.Count - 2].Y), Mathf.RoundToInt(vertices[vertices.Count - 2].Z)) / (float)biomes.Length;
						float vertex3Biome = GetCellBiomeFromWorld(Mathf.RoundToInt(vertices[vertices.Count - 1].X), Mathf.RoundToInt(vertices[vertices.Count - 1].Y), Mathf.RoundToInt(vertices[vertices.Count - 1].Z)) / (float)biomes.Length;

						biomeValues.Add(new Color(vertex1Biome, vertex2Biome, vertex3Biome, 0f));
						biomeValues.Add(new Color(vertex1Biome, vertex2Biome, vertex3Biome, 0f));
						biomeValues.Add(new Color(vertex1Biome, vertex2Biome, vertex3Biome, 0f));

						biomeIndices.Add(1f);
						biomeIndices.Add(0f);
						biomeIndices.Add(0f);
						biomeIndices.Add(0f);

						biomeIndices.Add(0f);
						biomeIndices.Add(1f);
						biomeIndices.Add(0f);
						biomeIndices.Add(0f);

						biomeIndices.Add(0f);
						biomeIndices.Add(0f);
						biomeIndices.Add(1f);
						biomeIndices.Add(0f);
					}
				}
			}
		}
		//GD.Print("Marching cubes algorithm done at chunk: " + chunk.index + vertices[0][0]);
	}

	Vector3 VertexInterpolation(Vector3 p1, Vector3 p2, float v1, float v2)
	{
		return p1 + (p2 - p1) * (floorHeight - v1) / (v2 - v1);
	}
	void GenerateGeometry(Vector3I chunkCoords, List<Vector3> vertices, List<Vector3> normals, List<Color> biomeValues, List<float> biomeIndices)
	{
		//if (vertices.Count == 0) return;
		//await Task.Delay(1); // Yield to the main thread to avoid blocking it
		

		Array arrays = new Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
		arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
		arrays[(int)Mesh.ArrayType.Color] = biomeValues.ToArray();
		arrays[(int)Mesh.ArrayType.Tangent] = biomeIndices.ToArray();

		MeshInstance3D meshInstance = new MeshInstance3D();
		meshInstance.Mesh = new ArrayMesh();
		(meshInstance.Mesh as ArrayMesh).AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

		meshInstance.Mesh.SurfaceSetMaterial(0, biomeBaseMaterial);

		//(meshInstance.Mesh.SurfaceGetMaterial(0) as StandardMaterial3D).VertexColorUseAsAlbedo = true;

		CollisionShape3D collisionShape = new CollisionShape3D();

		collisionShape.Shape = meshInstance.Mesh.CreateTrimeshShape();

		chunkMeshes[chunkCoords] = meshInstance;

		CallDeferred(nameof(ApplyGeometry), meshInstance, collisionShape);
	}
	void ApplyGeometry(MeshInstance3D meshInstance, CollisionShape3D collisionShape)
	{
		StaticBody3D chunkBody = new StaticBody3D();
		chunkBody.AddChild(collisionShape);
		meshInstance.AddChild(chunkBody);
		AddChild(meshInstance);
	}
	int WorldToChunkIndex(int worldCoord, int chunkSize)
	{
		if (worldCoord < 0)
		{
			return (worldCoord + 1) / chunkSize - 1; // Adjust for negative coordinates
		}
		else
		{
			return worldCoord / chunkSize;
		}
	}
	int WorldToChunkOffset(int worldCoord, int chunkSize)
	{
		if (worldCoord < 0)
		{
			return (worldCoord % chunkSize + chunkSize) % chunkSize; // Adjust for negative coordinates
		}
		else
		{
			return worldCoord % chunkSize;
		}
	}
	int ChunkIndexToWorld(int chunkIndex, int chunkSize)
	{
		if (chunkIndex < 0)
		{
			return chunkIndex * chunkSize; // Adjust for negative coordinates
		}
		else
		{
			return chunkIndex * chunkSize;
		}
	}
	/// <summary>
	/// Get the chunk at the specified chunk coordinates.
	/// </summary>
	/// <param name="x">X chunk coordinate</param>
	/// <param name="y">Y chunk coordinate</param>
	/// <param name="z">Z chunk coordinate</param>
	/// <returns></returns>
	float[,,] GetChunk(int x, int y, int z)
	{
		Vector3I coords = new Vector3I(x, y, z);
		if (chunks.TryGetValue(coords, out float[,,] chunk))
		{
			return chunk;
		}
		else
		{
			chunkGenerated[coords] = false;
			chunks[coords] = new float[chunkSizeX, chunkSizeY, chunkSizeZ];
			return chunks[coords];
		}
	}
	float[,,] GetChunkFromWorld(int x, int y, int z)
	{
		int chunkX = WorldToChunkIndex(x, chunkSizeX);
		int chunkY = WorldToChunkIndex(y, chunkSizeY);
		int chunkZ = WorldToChunkIndex(z, chunkSizeZ);
		return GetChunk(chunkX, chunkY, chunkZ);
	}
	float GetCellFromWorld(int x, int y, int z)
	{
		int chunkX = WorldToChunkIndex(x, chunkSizeX);
		int chunkY = WorldToChunkIndex(y, chunkSizeY);
		int chunkZ = WorldToChunkIndex(z, chunkSizeZ);
		float[,,] chunk = GetChunk(chunkX, chunkY, chunkZ);
		int offsetX = WorldToChunkOffset(x, chunkSizeX);
		int offsetY = WorldToChunkOffset(y, chunkSizeY);
		int offsetZ = WorldToChunkOffset(z, chunkSizeZ);
		try
		{
			return chunk[offsetX, offsetY, offsetZ];
		}
		catch (IndexOutOfRangeException)
		{
			//GD.PrintErr($"GetCellFromWorld: Out of range for chunk ({chunkX}, {chunkY}, {chunkZ}) at offset ({offsetX}, {offsetY}, {offsetZ}), coordinates ({x}, {y}, {z})\nWorld to chunk index z: {z} / )");
			return chunk[offsetX, offsetY, offsetZ];
		}
	}
	int GetCellBiomeFromWorld(int x, int y, int z)
	{
		int chunkX = WorldToChunkIndex(x, chunkSizeX);
		int chunkY = WorldToChunkIndex(y, chunkSizeY);
		int chunkZ = WorldToChunkIndex(z, chunkSizeZ);
		if (cellBiomes.TryGetValue(new Vector3I(chunkX, chunkY, chunkZ), out int[,,] biomes))
		{
			int offsetX = WorldToChunkOffset(x, chunkSizeX);
			int offsetY = WorldToChunkOffset(y, chunkSizeY);
			int offsetZ = WorldToChunkOffset(z, chunkSizeZ);
			return biomes[offsetX, offsetY, offsetZ];
		}
		GD.PrintErr($"GetCellBiomeFromWorld: Biome not found for chunk ({chunkX}, {chunkY}, {chunkZ}) at offset ({WorldToChunkOffset(x, chunkSizeX)}, {WorldToChunkOffset(y, chunkSizeY)}, {WorldToChunkOffset(z, chunkSizeZ)}), coordinates ({x}, {y}, {z})");
		return -1; // Return -1 if the biome is not found
	}
	void SetCellFromWorld(int x, int y, int z, float value)
	{
		int chunkX = WorldToChunkIndex(x, chunkSizeX);
		int chunkY = WorldToChunkIndex(y, chunkSizeY);
		int chunkZ = WorldToChunkIndex(z, chunkSizeZ);
		float[,,] chunk = GetChunk(chunkX, chunkY, chunkZ);
		int offsetX = WorldToChunkOffset(x, chunkSizeX);
		int offsetY = WorldToChunkOffset(y, chunkSizeY);
		int offsetZ = WorldToChunkOffset(z, chunkSizeZ);
		chunk[offsetX, offsetY, offsetZ] = value;
	}
	void SetChunk(int x, int y, int z, float[,,] chunk)
	{
		chunks[new Vector3I(x, y, z)] = chunk;
	}
	void SetChunkFromWorld(int x, int y, int z, float[,,] chunk)
	{
		int chunkX = WorldToChunkIndex(x, chunkSizeX);
		int chunkY = WorldToChunkIndex(y, chunkSizeY);
		int chunkZ = WorldToChunkIndex(z, chunkSizeZ);
		chunks[new Vector3I(chunkX, chunkY, chunkZ)] = chunk;
	}
}
