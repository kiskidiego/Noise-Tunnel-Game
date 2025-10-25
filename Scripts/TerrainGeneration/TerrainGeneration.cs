using Godot;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Array = Godot.Collections.Array;
using System.Linq;
using Godot.Collections;
using System.Collections.Concurrent;


public partial class TerrainGeneration : Node
{
	[Export] World world;
	[Export] int seed = 0;
	/* PATH STUFF
	[Export] FastNoiseLite baseNoise;
	[Export] int tunnelWidth = 1;
	[Export] float pathStraightness = 0.6f; // How straight the paths should be, 1 is perfectly straight, 0 is completely random
	[Export] PointOfInterest[] pointsOfInterest;
	*/
	[Export] int chunkDistance = 2; // How many chunks away to generate
	[Export] ShaderMaterial biomeBaseMaterial;
	RandomNumberGenerator rng = new RandomNumberGenerator();
	Vector3I currentChunkCoords = new Vector3I(int.MaxValue, int.MaxValue, int.MaxValue);
	ConcurrentBag<Vector3I> loadedChunks = new ConcurrentBag<Vector3I>();
	bool generating = false;
	bool firstGeneration = true; // Flag to indicate if this is the first generation
	float floorHeight = 0;

	public override void _Ready()
	{
		PrepareTextures();
		InitializeSeed();
	}

	public override void _EnterTree()
	{
		EventManager.Subscribe(EventKeys.PLAYER_MOVED, OnPlayerMoved);
	}

	public override void _ExitTree()
	{
		EventManager.Unsubscribe(EventKeys.PLAYER_MOVED, OnPlayerMoved);
	}
	
	void OnPlayerMoved(EventParameters parameters)
	{
		Vector3 playerPosition = parameters.Get<Vector3>(EventParameterKeys.POSITION);
		GenerateFromWorldPosition(playerPosition);
	}

	void PrepareTextures()
	{
		Array<Image> colorTextureArray = new Array<Image>();
		Array<Image> normalTextureArray = new Array<Image>();
		Array<Image> metalRoughTextureArray = new Array<Image>();
		foreach (Biome biome in world.biomes)
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
		biomeBaseMaterial.SetShaderParameter("biomeAmount", world.biomes.Length);
	}
	void InitializeSeed()
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
		world.SetSeed(seed);
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
						Vector3I chunk = world.WorldToChunkIndex(new Vector3I(cell.X + x, cell.Y + y, cell.Z + z));
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
			float cellValue = world.GetCellScoreFromWorld(cell);
			cellValue += terraformPotency;
			cellValue = Mathf.Clamp(cellValue, -1f, 1f); // Ensure the value is within the valid range
			world.SetCellScoreFromWorld(cell, cellValue);
		}
		List<Vector3I> chunkCoords = GetAllChunksFromCells(cells);
		foreach (var chunkCoord in chunkCoords)
		{
			List<Vector3> vertices = new List<Vector3>();
			List<Vector3> normals = new List<Vector3>();
			List<Color> biomeValues = new List<Color>();
			List<Vector2> biomeInfluences = new List<Vector2>();
			MarchingCubesAlgorithm(chunkCoord, vertices, normals, biomeValues, biomeInfluences);
			if (vertices.Count == 0) continue; // Skip if no vertices were generated
											   //InterpolateNormals(vertices, normals);
			Chunk chunk = world.GetChunk(chunkCoord);
			MeshInstance3D meshInstance = chunk.mesh;
			//GD.Print($"Updating chunk {chunk} mesh instance: {(meshInstance != null ? "Exists" : "Does not exist")}");
			if (meshInstance == null)
			{
				meshInstance = new MeshInstance3D();
				chunk.mesh = meshInstance;
				meshInstance.Mesh = new ArrayMesh();
				AddChild(meshInstance);
			}
			(meshInstance.Mesh as ArrayMesh).ClearSurfaces();
			Array arrays = new Array();
			arrays.Resize((int)ArrayMesh.ArrayType.Max);
			arrays[(int)ArrayMesh.ArrayType.Vertex] = vertices.ToArray();
			arrays[(int)ArrayMesh.ArrayType.Normal] = normals.ToArray();
			arrays[(int)Mesh.ArrayType.Color] = biomeValues.ToArray();
			arrays[(int)Mesh.ArrayType.TexUV] = biomeInfluences.ToArray();

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
	public bool GenerateFromWorldPosition(Vector3 position)
	{

		Vector3I chunkCoords = world.WorldToChunkIndex((Vector3I)position);

		if (currentChunkCoords == chunkCoords || generating) return false; // If the chunk is already generated or being generated, skip

		EventManager.Invoke(EventKeys.TERRAIN_GENERATION_REQUESTED);
		
		generating = true;
		currentChunkCoords = chunkCoords;

		CheckLoadedChunks(chunkCoords);

		GenerateFromAsync(chunkCoords);
		return true;
	}
	void CheckLoadedChunks(Vector3I chunkCoords)
	{
		if (loadedChunks.Count == 0) return; // No loaded chunks to check
		foreach (var loadedChunk in loadedChunks)
		{
			if (Math.Abs(loadedChunk.X - chunkCoords.X) > chunkDistance ||
				Math.Abs(loadedChunk.Y - chunkCoords.Y) > chunkDistance ||
				Math.Abs(loadedChunk.Z - chunkCoords.Z) > chunkDistance)
			{
				Chunk chunk = world.GetChunk(loadedChunk);
				chunk.mesh?.QueueFree(); // Free the mesh if it's outside the chunk distance
				chunk.mesh = null; // Set the mesh to null to avoid memory leaks
				chunk.generated = false; // Mark chunk as not generated
			}
		}
		loadedChunks.Clear(); // Clear the loaded chunks after checking
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
						loadedChunks.Add(coords);
						Chunk chunk = world.GetChunk(coords);
						if (chunk.scored)
						{
							continue; // Skip already scored chunks
						}
						Logger.Log($"Determining biomes and generating noise caves for chunk: {coords}");
						chunk.scored = true; // Mark chunk as scored
						world.DetermineBiomes(coords);
						world.GenerateNoiseCaves(coords);
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
						Chunk chunk = world.GetChunk(coords);
						if (chunk.generated)
						{
							continue; // Skip already generated chunks
						}
						Logger.Log($"Generating chunk geometry for chunk: {coords}");
						chunk.generated = true; // Mark chunk as generated
						GenerateChunk(coords);
					}
				}
			}
		});
		generating = false; // Reset generating flag after generation is done

		EventManager.Invoke(EventKeys.TERRAIN_GENERATION_COMPLETED);
	}
	void GenerateChunk(Vector3I chunkCoords)
	{
		List<Vector3> vertices = new List<Vector3>();
		List<Vector3> normals = new List<Vector3>();
		List<Color> biomeValues = new List<Color>();
		List<Vector2> biomeInfluences = new List<Vector2>();
		MarchingCubesAlgorithm(chunkCoords, vertices, normals, biomeValues, biomeInfluences);
		//InterpolateNormals(vertices, normals);
		GenerateGeometry(chunkCoords, vertices, normals, biomeValues, biomeInfluences);
	}
	/*
	void PreparePointsOfInterest()
	{
		if (pointsOfInterest == null || pointsOfInterest.Length == 0) return;

		foreach (var poi in pointsOfInterest)
		{
			if (poi.leadsTo == null || poi.leadsTo.Length == 0) continue;

			foreach (int index in poi.leadsTo)
			{
				Vector3I start = poi.Position;
				float[,,] startChunk = GetChunkFromWorld(start.X, start.Y, start.Z);

				startChunk[WorldToChunkOffset(start.X, chunkSizeX), WorldToChunkOffset(start.Y, chunkSizeY), WorldToChunkOffset(start.Z, chunkSizeZ)] = -1; // Mark the start point

				if (index < 0 || index >= pointsOfInterest.Length) continue;
				Vector3I end = pointsOfInterest[index].Position;
				while (start != end)
				{
					start = NextCellInPath(start, end);

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
								int absX = Mathf.Abs(x);
								int absY = Mathf.Abs(y);
								int absZ = Mathf.Abs(z);
								if (absX + absY + absZ > tunnelWidth + tunnelWidth / 2) continue; // Skip cells that are too far from the center

								SetCellFromWorld(start.X + x, start.Y + y, start.Z + z, -0.5f); // Mark the surrounding cells as part of the path
							}
						}
					}
					SetCellFromWorld(start.X, start.Y, start.Z, -1f); // Mark the surrounding cells as part of the path
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
	*/
	
	
	void MarchingCubesAlgorithm(Vector3I chunkCoords, List<Vector3> vertices, List<Vector3> normals, List<Color> biomeValues, List<Vector2> biomeInfluences)
	{
		Vector3I chunkPosition = world.ChunkIndexToWorld(chunkCoords);

		float[,,] cells = new float[world.chunkSize.X + 2, world.chunkSize.Y + 2, world.chunkSize.Z + 2];
		for (int x = 0; x < world.chunkSize.X + 2; x++)
		{
			for (int y = 0; y < world.chunkSize.Y + 2; y++)
			{
				for (int z = 0; z < world.chunkSize.Z + 2; z++)
				{
					cells[x, y, z] = world.GetCellScoreFromWorld(chunkPosition + new Vector3I(x - 1, y - 1, z - 1));
				}
			}
		}

		//GD.Print($"Marching cubes algorithm started at chunk: {chunkCoords} with position: ({chunkPosition.X}, {chunkPosition.Y}, {chunkPositionZ})");
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
						biomeInfluences = new List<Vector2>();
					}

					Vector3[] edgeVertices = new Vector3[12];
					if ((MarchTables.edges[cubeIndex] & 1) == 1)
					{
						edgeVertices[0] = VertexInterpolation(new Vector3(i + chunkPosition.X, j + chunkPosition.Y, k + chunkPosition.Z), new Vector3(i + chunkPosition.X + 1, j + chunkPosition.Y, k + chunkPosition.Z), cells[i, j, k], cells[i + 1, j, k]);
					}
					if ((MarchTables.edges[cubeIndex] & 2) == 2)
					{
						edgeVertices[1] = VertexInterpolation(new Vector3(i + chunkPosition.X + 1, j + chunkPosition.Y, k + chunkPosition.Z), new Vector3(i + chunkPosition.X + 1, j + chunkPosition.Y, k + chunkPosition.Z + 1), cells[i + 1, j, k], cells[i + 1, j, k + 1]);
					}
					if ((MarchTables.edges[cubeIndex] & 4) == 4)
					{
						edgeVertices[2] = VertexInterpolation(new Vector3(i + chunkPosition.X + 1, j + chunkPosition.Y, k + chunkPosition.Z + 1), new Vector3(i + chunkPosition.X, j + chunkPosition.Y, k + chunkPosition.Z + 1), cells[i + 1, j, k + 1], cells[i, j, k + 1]);
					}
					if ((MarchTables.edges[cubeIndex] & 8) == 8)
					{
						edgeVertices[3] = VertexInterpolation(new Vector3(i + chunkPosition.X, j + chunkPosition.Y, k + chunkPosition.Z + 1), new Vector3(i + chunkPosition.X, j + chunkPosition.Y, k + chunkPosition.Z), cells[i, j, k + 1], cells[i, j, k]);
					}
					if ((MarchTables.edges[cubeIndex] & 16) == 16)
					{
						edgeVertices[4] = VertexInterpolation(new Vector3(i + chunkPosition.X, j + chunkPosition.Y + 1, k + chunkPosition.Z), new Vector3(i + chunkPosition.X + 1, j + chunkPosition.Y + 1, k + chunkPosition.Z), cells[i, j + 1, k], cells[i + 1, j + 1, k]);
					}
					if ((MarchTables.edges[cubeIndex] & 32) == 32)
					{
						edgeVertices[5] = VertexInterpolation(new Vector3(i + chunkPosition.X + 1, j + chunkPosition.Y + 1, k + chunkPosition.Z), new Vector3(i + chunkPosition.X + 1, j + chunkPosition.Y + 1, k + chunkPosition.Z + 1), cells[i + 1, j + 1, k], cells[i + 1, j + 1, k + 1]);
					}
					if ((MarchTables.edges[cubeIndex] & 64) == 64)
					{
						edgeVertices[6] = VertexInterpolation(new Vector3(i + chunkPosition.X + 1, j + chunkPosition.Y + 1, k + chunkPosition.Z + 1), new Vector3(i + chunkPosition.X, j + chunkPosition.Y + 1, k + chunkPosition.Z + 1), cells[i + 1, j + 1, k + 1], cells[i, j + 1, k + 1]);
					}
					if ((MarchTables.edges[cubeIndex] & 128) == 128)
					{
						edgeVertices[7] = VertexInterpolation(new Vector3(i + chunkPosition.X, j + chunkPosition.Y + 1, k + chunkPosition.Z + 1), new Vector3(i + chunkPosition.X, j + chunkPosition.Y + 1, k + chunkPosition.Z), cells[i, j + 1, k + 1], cells[i, j + 1, k]);
					}
					if ((MarchTables.edges[cubeIndex] & 256) == 256)
					{
						edgeVertices[8] = VertexInterpolation(new Vector3(i + chunkPosition.X, j + chunkPosition.Y, k + chunkPosition.Z), new Vector3(i + chunkPosition.X, j + chunkPosition.Y + 1, k + chunkPosition.Z), cells[i, j, k], cells[i, j + 1, k]);
					}
					if ((MarchTables.edges[cubeIndex] & 512) == 512)
					{
						edgeVertices[9] = VertexInterpolation(new Vector3(i + chunkPosition.X + 1, j + chunkPosition.Y, k + chunkPosition.Z), new Vector3(i + chunkPosition.X + 1, j + chunkPosition.Y + 1, k + chunkPosition.Z), cells[i + 1, j, k], cells[i + 1, j + 1, k]);
					}
					if ((MarchTables.edges[cubeIndex] & 1024) == 1024)
					{
						edgeVertices[10] = VertexInterpolation(new Vector3(i + chunkPosition.X + 1, j + chunkPosition.Y, k + chunkPosition.Z + 1), new Vector3(i + chunkPosition.X + 1, j + chunkPosition.Y + 1, k + chunkPosition.Z + 1), cells[i + 1, j, k + 1], cells[i + 1, j + 1, k + 1]);
					}
					if ((MarchTables.edges[cubeIndex] & 2048) == 2048)
					{
						edgeVertices[11] = VertexInterpolation(new Vector3(i + chunkPosition.X, j + chunkPosition.Y, k + chunkPosition.Z + 1), new Vector3(i + chunkPosition.X, j + chunkPosition.Y + 1, k + chunkPosition.Z + 1), cells[i, j, k + 1], cells[i, j + 1, k + 1]);
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

						float vertex1Biome = world.GetCellBiomeFromWorld((Vector3I)vertices[vertices.Count - 3].Round()) / (float)world.biomes.Length;
						float vertex2Biome = world.GetCellBiomeFromWorld((Vector3I)vertices[vertices.Count - 2].Round()) / (float)world.biomes.Length;
						float vertex3Biome = world.GetCellBiomeFromWorld((Vector3I)vertices[vertices.Count - 1].Round()) / (float)world.biomes.Length;

						biomeValues.Add(new Color(vertex1Biome, vertex2Biome, vertex3Biome, 1f));
						biomeValues.Add(new Color(vertex1Biome, vertex2Biome, vertex3Biome, 0f));
						biomeValues.Add(new Color(vertex1Biome, vertex2Biome, vertex3Biome, 0f));

						biomeInfluences.Add(new Vector2(0, 0));
						biomeInfluences.Add(new Vector2(1f, 0f));
						biomeInfluences.Add(new Vector2(0f, 1f));
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
	void GenerateGeometry(Vector3I chunkCoords, List<Vector3> vertices, List<Vector3> normals, List<Color> biomeValues, List<Vector2> biomeInfluences)
	{
		if (vertices.Count == 0) return;
		//await Task.Delay(1); // Yield to the main thread to avoid blocking it
		

		Array arrays = new Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
		arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
		arrays[(int)Mesh.ArrayType.Color] = biomeValues.ToArray();
		arrays[(int)Mesh.ArrayType.TexUV] = biomeInfluences.ToArray();

		MeshInstance3D meshInstance = new MeshInstance3D();
		meshInstance.Mesh = new ArrayMesh();
		(meshInstance.Mesh as ArrayMesh).AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

		meshInstance.Mesh.SurfaceSetMaterial(0, biomeBaseMaterial);

		//(meshInstance.Mesh.SurfaceGetMaterial(0) as StandardMaterial3D).VertexColorUseAsAlbedo = true;

		CollisionShape3D collisionShape = new CollisionShape3D();

		collisionShape.Shape = meshInstance.Mesh.CreateTrimeshShape();

		world.GetChunk(chunkCoords).mesh = meshInstance;

		CallDeferred(nameof(ApplyGeometry), meshInstance, collisionShape);
	}
	void ApplyGeometry(MeshInstance3D meshInstance, CollisionShape3D collisionShape)
	{
		StaticBody3D chunkBody = new StaticBody3D();
		chunkBody.AddChild(collisionShape);
		meshInstance.AddChild(chunkBody);
		AddChild(meshInstance);
	}
}
