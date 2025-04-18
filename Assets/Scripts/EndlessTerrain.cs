using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

public class EndlessTerrain : MonoBehaviour {
  NavMeshData               navMeshData;
  NavMeshDataInstance       navMeshInstance;
  List<NavMeshBuildSource>  navSources = new List<NavMeshBuildSource>();
  Bounds                    navBounds;

  Queue<Vector2>        chunksToCreate = new Queue<Vector2>();
  HashSet<Vector2>      queuedCoords   = new HashSet<Vector2>();
  NavMeshSurface surface;
  bool navMeshDirty = false;
  float rebuildCooldown = 0.5f;    // IMPROVED: rebuild twice per second
  float timeSinceRebuild = 0f;
  
  // ADDED: Tracking variables for chunk loading
  float timeSinceLastChunkCreation = 0f;
  bool isProcessingChunks = false;
  int failedNavMeshSamples = 0;
  
  public GameObject[] treePrefabs;
  public GameObject[] grassPrefabs;
  public GameObject[] rockPrefabs;
  public GameObject robotPrefab;

  // IMPROVED: Reduced threshold for more responsive updates
  const float viewerMoveThresholdForChunkUpdate = 15f;
  const float squareViewerMoveThresholdForChunkUpdate = viewerMoveThresholdForChunkUpdate * viewerMoveThresholdForChunkUpdate;
  const float colliderGenerationDistanceThreshold = 5;
  public int colliderLODIndex;
  public LODInfo[] detailLevels;
  public static float maxViewDistance;
  public Transform viewer;
  public Material mapMaterial;
  public static Vector2 viewerPosition;
  public GameObject waterPrefab;
  Vector2 viewerPositionOld;
  static MapGenerator mapGenerator;
  float meshWorldSize;
  int chunksVisibleInViewDistance;
  Dictionary<Vector2, TerrainChunk> terrainChunkDictionary = new Dictionary<Vector2, TerrainChunk>();
  static List<TerrainChunk> visibleTerrainChunks = new List<TerrainChunk>();
  
  // ADDED: Configuration parameters
  [Header("Chunk Loading Settings")]
  [Tooltip("Maximum chunks to create per frame")]
  public int maxChunksPerFrame = 4; // IMPROVED: Increased from 2
  
  [Tooltip("How long to wait (in seconds) before trying to load more chunks if we hit the limit")]
  public float chunkCreationCooldown = 0.05f;
  
  [Tooltip("If true, prioritizes chunks closer to the player")]
  public bool prioritizeNearbyChunks = true;
  
  void Start() {
    mapGenerator = FindObjectOfType<MapGenerator>();
    surface      = GetComponent<NavMeshSurface>();

    meshWorldSize = mapGenerator.meshSettings.meshWorldSize - 1;
    maxViewDistance = detailLevels.Last().visibleDistanceThreshold;
    chunksVisibleInViewDistance = 
        Mathf.RoundToInt(maxViewDistance / meshWorldSize);

    float worldSize = meshWorldSize * (chunksVisibleInViewDistance * 2 + 1);
    navBounds = new Bounds(Vector3.zero, Vector3.one * worldSize);

    navMeshData     = new NavMeshData();
    navMeshInstance = NavMesh.AddNavMeshData(navMeshData);
    UpdateVisibleChunks();
    
    // ADDED: Initial state
    viewerPositionOld = viewerPosition;
    
    // IMPROVED: Schedule immediate NavMesh rebuild
    navMeshDirty = true;
    timeSinceRebuild = rebuildCooldown;
  }

  void Update() {
    viewerPosition = new Vector2(viewer.position.x, viewer.position.z);

    // IMPROVED: Update collision meshes more eagerly
    if ((viewerPositionOld - viewerPosition).sqrMagnitude > 1f) {
      foreach (TerrainChunk chunk in visibleTerrainChunks) {
        chunk.UpdateCollisionMesh();
      }
    }

    // IMPROVED: More responsive chunk updates
    if ((viewerPositionOld - viewerPosition).sqrMagnitude > squareViewerMoveThresholdForChunkUpdate) {
      viewerPositionOld = viewerPosition;
      UpdateVisibleChunks();
    }

    // IMPROVED: More efficient chunk creation with cooldown
    timeSinceLastChunkCreation += Time.deltaTime;
    
    // Process chunks in batches with cooldown between batches
    if (chunksToCreate.Count > 0 && timeSinceLastChunkCreation >= chunkCreationCooldown) {
      isProcessingChunks = true;
      ProcessChunkQueue();
      timeSinceLastChunkCreation = 0f;
    } else {
      isProcessingChunks = false;
    }

    // NavMesh throttle logic
    timeSinceRebuild += Time.deltaTime;
    if (navMeshDirty && timeSinceRebuild >= rebuildCooldown) {
      RebuildNavMeshAsync();
    }
    
    // ADDED: Reset failed nav mesh counter periodically
    if (failedNavMeshSamples > 10) {
      failedNavMeshSamples = 0;
      // Force full NavMesh rebuild if we have too many failures
      RebuildNavMeshComplete();
    }
  }
  
  // ADDED: New method to process chunks with priority
  void ProcessChunkQueue() {
    if (chunksToCreate.Count == 0) return;
    
    // Process more chunks when far away from camera
    int chunksToProcess = Mathf.Min(chunksToCreate.Count, maxChunksPerFrame);
    
    if (prioritizeNearbyChunks && chunksToCreate.Count > chunksToProcess) {
      // Get all chunks and sort by distance to player
      List<Vector2> sortedCoords = new List<Vector2>(chunksToCreate);
      chunksToCreate.Clear();
      
      // Sort by distance to player
      sortedCoords.Sort((a, b) => {
        float distA = Vector2.Distance(a * meshWorldSize, viewerPosition);
        float distB = Vector2.Distance(b * meshWorldSize, viewerPosition);
        return distA.CompareTo(distB);
      });
      
      // Process closest chunks first
      for (int i = 0; i < chunksToProcess; i++) {
        Vector2 coord = sortedCoords[i];
        InstantiateChunk(coord);
      }
      
      // Re-queue remaining chunks
      for (int i = chunksToProcess; i < sortedCoords.Count; i++) {
        chunksToCreate.Enqueue(sortedCoords[i]);
      }
    } else {
      // Process in queue order
      for (int i = 0; i < chunksToProcess; i++) {
        Vector2 coord = chunksToCreate.Dequeue();
        InstantiateChunk(coord);
      }
    }
  }
  
  // ADDED: Refactored chunk instantiation
  void InstantiateChunk(Vector2 coord) {
    queuedCoords.Remove(coord);
    
    // Instantiate the chunk and immediately do its first LOD & decorations pass
    TerrainChunk newChunk = new TerrainChunk(
        coord, meshWorldSize, detailLevels, colliderLODIndex,
        transform, mapMaterial, waterPrefab,
        treePrefabs, grassPrefabs, rockPrefabs,
        robotPrefab          
    );
    terrainChunkDictionary.Add(coord, newChunk);
    newChunk.UpdateTerrainChunk();
    
    // Mark NavMesh for rebuild
    navMeshDirty = true;
  }

  void RebuildNavMeshAsync() {
    // 1) collect all your geometry into navSources
    navSources.Clear();
    NavMeshBuilder.CollectSources(
      navBounds,
      surface.layerMask, 
      surface.useGeometry,
      surface.defaultArea,
      new List<NavMeshBuildMarkup>(),
      navSources
    );

    // 2) kick off the bake on a background thread
    NavMeshBuilder.UpdateNavMeshDataAsync(
      navMeshData,
      surface.GetBuildSettings(),
      navSources,
      navBounds
    );

    navMeshDirty = false;
    timeSinceRebuild = 0f;
  }
  
  // ADDED: Method for complete NavMesh rebuild
  void RebuildNavMeshComplete() {
    // For when we need a full rebuild
    Debug.Log("Performing complete NavMesh rebuild");
    
    navSources.Clear();
    NavMeshBuilder.CollectSources(
      navBounds,
      surface.layerMask, 
      surface.useGeometry,
      surface.defaultArea,
      new List<NavMeshBuildMarkup>(),
      navSources
    );
    
    // Remove old data
    NavMesh.RemoveNavMeshData(navMeshInstance);
    
    // Create new data with complete bake
    navMeshData = NavMeshBuilder.BuildNavMeshData(
      surface.GetBuildSettings(),
      navSources,
      navBounds,
      transform.position,
      transform.rotation
    );
    
    // Add back to navigation
    navMeshInstance = NavMesh.AddNavMeshData(navMeshData);
    
    navMeshDirty = false;
    timeSinceRebuild = 0f;
  }

  void UpdateVisibleChunks() {
    int currentX = Mathf.RoundToInt(viewerPosition.x / meshWorldSize);
    int currentY = Mathf.RoundToInt(viewerPosition.y / meshWorldSize);
    
    // IMPROVED: Allow keeping chunks a bit longer for smoother experience
    float deleteDistance = maxViewDistance * 1.5f;

    // 1) Cull & destroy any chunk too far away
    var removeList = new List<Vector2>();
    foreach (var kv in terrainChunkDictionary) {
        Vector2 coord = kv.Key;
        TerrainChunk chunk = kv.Value;
        float dist = Vector2.Distance(coord * meshWorldSize, viewerPosition);
        if (dist > deleteDistance) {
            chunk.Destroy();
            removeList.Add(coord);
        }
    }
    foreach (var coord in removeList) {
        terrainChunkDictionary.Remove(coord);
        visibleTerrainChunks.RemoveAll(c => c.coord == coord);
        queuedCoords.Remove(coord);     // so we can re‑enqueue if player returns
    }

    // 2) Enqueue new coords, update existing
    var newVisible = new List<TerrainChunk>();
    
    // IMPROVED: Load chunks in spiral pattern from center outward
    List<Vector2> coordsToCheck = new List<Vector2>();
    
    // First add the immediate surroundings
    for (int y = -1; y <= 1; y++) {
        for (int x = -1; x <= 1; x++) {
            coordsToCheck.Add(new Vector2(currentX + x, currentY + y));
        }
    }
    
    // Then add rest in spiral order
    for (int layer = 2; layer <= chunksVisibleInViewDistance; layer++) {
        // Add the four sides of this "layer" square
        // Top edge
        for (int x = -layer; x <= layer; x++) {
            coordsToCheck.Add(new Vector2(currentX + x, currentY - layer));
        }
        // Right edge
        for (int y = -layer + 1; y <= layer; y++) {
            coordsToCheck.Add(new Vector2(currentX + layer, currentY + y));
        }
        // Bottom edge
        for (int x = layer - 1; x >= -layer; x--) {
            coordsToCheck.Add(new Vector2(currentX + x, currentY + layer));
        }
        // Left edge
        for (int y = layer - 1; y >= -layer + 1; y--) {
            coordsToCheck.Add(new Vector2(currentX - layer, currentY + y));
        }
    }
    
    // Process all coords in spiral order
    foreach (Vector2 coord in coordsToCheck) {
        float distance = Vector2.Distance(coord * meshWorldSize, viewerPosition);
        if (distance <= maxViewDistance) {
            if (terrainChunkDictionary.TryGetValue(coord, out TerrainChunk chunk)) {
                // existing chunk: just update
                chunk.UpdateTerrainChunk();
                if (chunk.IsVisible())
                    newVisible.Add(chunk);
            } else {
                // brand‑new: enqueue once
                if (!queuedCoords.Contains(coord)) {
                    queuedCoords.Add(coord);
                    chunksToCreate.Enqueue(coord);
                }
            }
        }
    }

    // 3) Swap in our fresh visible list
    visibleTerrainChunks = newVisible;

    // 4) mark navmesh rebuild for later
    navMeshDirty = true;
  }

  public class TerrainChunk {
    bool hasSpawnedDecorations = false;
    public Vector2 coord;
    GameObject meshObject;
    Vector2 sampleCenter;
    Bounds bounds;

    HeightMap mapData;
    MeshRenderer meshRenderer;
    MeshFilter meshFilter;
    MeshCollider meshCollider;
    LODInfo[] detailLevels;
    LODMesh[] lodMeshes;
    int colliderLODIndex;
    bool mapDataReceived;
    int previousLODIndex = -1;
    bool hasSetCollider;

    GameObject waterObject;
    GameObject waterPrefab;
    GameObject robotPrefab;
    bool hasSpawnedRobot = false;
    bool hasScheduledRobotSpawn = false;
    GameObject[] treePrefabs, grassPrefabs, rockPrefabs;
    float waterHeight = 0.2f;
    float meshSize;
    
    // ADDED: Cache for height evaluation
    float cachedWaterWorldHeight = -1f;

    public TerrainChunk(Vector2 coord, float meshWorldSize, LODInfo[] detailLevels, int colliderLODIndex, Transform parent, Material material, GameObject waterPrefab,
                    GameObject[] treePrefabs, GameObject[] grassPrefabs, GameObject[] rockPrefabs, GameObject robotPrefab) {
      this.treePrefabs = treePrefabs;
      this.grassPrefabs = grassPrefabs;
      this.rockPrefabs = rockPrefabs;
      this.waterPrefab = waterPrefab;
      this.robotPrefab = robotPrefab;
      this.coord = coord;
      this.detailLevels = detailLevels;
      this.colliderLODIndex = colliderLODIndex;
      meshSize = meshWorldSize;
      
      // We'll use TerrainChunkBehaviour for coroutines

      sampleCenter = coord * meshWorldSize / mapGenerator.meshSettings.meshScale;
      Vector2 position = coord * meshWorldSize;
      bounds = new Bounds(position, Vector2.one * meshWorldSize);

      meshObject = new GameObject("Terrain Chunk");
      // Add this MonoBehaviour component so we can use coroutines
      meshObject.AddComponent<TerrainChunkBehaviour>();

      // Layer setting
      int terrainLayer = LayerMask.NameToLayer("Terrain");
      SetLayerRecursively(meshObject, terrainLayer);

      meshRenderer = meshObject.AddComponent<MeshRenderer>();
      meshFilter = meshObject.AddComponent<MeshFilter>();
      meshCollider = meshObject.AddComponent<MeshCollider>();
      meshRenderer.material = material;
      meshObject.transform.position = new Vector3(position.x, 0, position.y);
      meshObject.transform.parent = parent;
      SetVisible(false);

      lodMeshes = new LODMesh[detailLevels.Length];

      for (int i = 0; i < detailLevels.Length; i++) {
        lodMeshes[i] = new LODMesh(detailLevels[i].lod);
        lodMeshes[i].updateCallback += UpdateTerrainChunk;
        if (i == colliderLODIndex) {
          lodMeshes[i].updateCallback += UpdateCollisionMesh;
        }
      }

      mapGenerator.RequestHeightMap(sampleCenter, OnMapDataReceived);
    }

    public void Destroy() {
      GameObject.Destroy(meshObject);
      if (waterObject != null)     GameObject.Destroy(waterObject);
    }

    void OnMapDataReceived(HeightMap mapData) {
      this.mapData = mapData;
      mapDataReceived = true;

      UpdateTerrainChunk();
    }

    void SetLayerRecursively(GameObject go, int layer) {
      go.layer = layer;
      foreach (Transform t in go.transform) {
        SetLayerRecursively(t.gameObject, layer);
      }
    }

    // IMPROVED: More efficient decoration placement
    void SpawnDecorations(Mesh mesh, Vector3 chunkPos) {
      Vector3[] verts = mesh.vertices;

      // compute world‐space water height
      if (cachedWaterWorldHeight < 0) {
        float hMult = mapGenerator.heightMapSettings.heightMultiplier;
        cachedWaterWorldHeight = mapGenerator
            .heightMapSettings
            .heightCurve
            .Evaluate(waterHeight)
          * hMult;
      }
      float waterWorld = cachedWaterWorldHeight;

      const int MAX_DECOR = 10;
      int decorCount = 0;

      // interior padding so nothing spawns at the very edge
      float pad = meshSize * 0.1f;
      bool InInterior(Vector3 w) {
        var local = w - chunkPos;
        return local.x > pad && local.x < meshSize - pad
            && local.z > pad && local.z < meshSize - pad;
      }

      // helper to get prefab's base radius from its mesh/renderer bounds
      float GetPrefabRadius(GameObject prefab) {
        var rend = prefab.GetComponentInChildren<Renderer>();
        if (rend == null) return 1f; // fallback
        Vector3 size = rend.bounds.size;
        return Mathf.Max(size.x, size.z) * 0.5f;
      }

      // track all placed decor as (position, radius)
      var placed = new List<(Vector3 pos, float radius)>();
      float extraGap = 0.5f;

      // 1) Trees in clusters
      int clusters = 3;
      float clusterRadius = 5f;
      for (int c = 0; c < clusters; c++) {
        // pick a valid center
        Vector3 center = Vector3.zero;
        for (int t = 0; t < 20; t++) {
          int idx = Random.Range(0, verts.Length);
          Vector3 w = chunkPos + verts[idx];
          if (w.y > waterWorld + 0.1f
           && w.y < mapGenerator.heightMapSettings.heightMultiplier * 0.6f
           && InInterior(w)) {
            center = w;
            break;
          }
        }
        if (center == Vector3.zero) continue;

        int treeCount = Random.Range(3, 7);
        for (int i = 0; i < treeCount && decorCount < MAX_DECOR; i++) {
          Vector2 offs = Random.insideUnitCircle * clusterRadius;
          Vector3 pos = center + new Vector3(offs.x, 0, offs.y);
          if (!InInterior(pos)) continue;
          pos.y = SampleHeightAtPosition(mesh, chunkPos, pos);
          if (pos.y <= waterWorld) continue;

          // choose prefab & scale
          var prefab = treePrefabs[Random.Range(0, treePrefabs.Length)];
          float scale = Random.Range(8f, 14f);
          float baseRadius = GetPrefabRadius(prefab) * scale;
          // overlap check
          if (placed.Any(d => Vector3.Distance(d.pos, pos) < d.radius + baseRadius + extraGap))
            continue;

          // instantiate
          var go = GameObject.Instantiate(prefab, pos, Quaternion.Euler(0, Random.Range(0,360),0), meshObject.transform);
          go.transform.localScale = Vector3.one * scale;

          placed.Add((pos, baseRadius));
          decorCount++;
        }
      }

      // 2) Grass & Rocks with noise thinning
      float noiseScale = 0.1f;
      int spacing = 8;
      for (int i = 0; i < verts.Length && decorCount < MAX_DECOR; i += spacing) {
        Vector3 w = chunkPos + verts[i];
        if (w.y <= waterWorld || !InInterior(w)) continue;
        float n = Mathf.PerlinNoise((w.x+1000)*noiseScale, (w.z+2000)*noiseScale);

        // grass
        if (w.y < waterWorld + 5f && n < 0.6f && grassPrefabs.Length > 0) {
          var prefab = grassPrefabs[Random.Range(0, grassPrefabs.Length)];
          float scale = Random.Range(3f, 6f);
          float baseRadius = GetPrefabRadius(prefab) * scale;
          if (placed.Any(d => Vector3.Distance(d.pos, w) < d.radius + baseRadius + extraGap))
            continue;

          var g = GameObject.Instantiate(prefab, w + Vector3.up*0.02f,
                              Quaternion.Euler(0, Random.Range(0,360),0),
                              meshObject.transform);
          g.transform.localScale = Vector3.one * scale;

          placed.Add((w, baseRadius));
          decorCount++;
        }
        // rocks
        else if (w.y > waterWorld + 2f && n > 0.5f && rockPrefabs.Length > 0) {
          var prefab = rockPrefabs[Random.Range(0, rockPrefabs.Length)];
          float scale = Random.Range(15f, 25f);  // no more tiny rocks
          float baseRadius = GetPrefabRadius(prefab) * scale;
          if (placed.Any(d => Vector3.Distance(d.pos, w) < d.radius + baseRadius + extraGap))
            continue;

          var r = GameObject.Instantiate(prefab, w, Quaternion.Euler(0, Random.Range(0,360),0), meshObject.transform);
          r.transform.localScale = Vector3.one * scale;

          placed.Add((w, baseRadius));
          decorCount++;
        }
      }
    }


    // Helper: roughly sample height by projecting onto mesh terrain
    float SampleHeightAtPosition(Mesh mesh, Vector3 chunkPos, Vector3 worldPos) {
      // IMPROVED: More accurate height sampling
      Vector3 local = worldPos - chunkPos;
      
      // Use raycast for better precision (if possible)
      Ray ray = new Ray(new Vector3(worldPos.x, 1000, worldPos.z), Vector3.down);
      RaycastHit hit;
      int terrainLayer = LayerMask.GetMask("Terrain");
      if (Physics.Raycast(ray, out hit, 2000f, terrainLayer)) {
        return hit.point.y;
      }
      
      // Fallback to vertex sampling
      float bestDist = float.MaxValue;
      float bestY = chunkPos.y;
      foreach (var v in mesh.vertices) {
        float d = Vector2.SqrMagnitude(new Vector2(v.x - local.x, v.z - local.z));
        if (d < bestDist) {
          bestDist = d;
          bestY = v.y + chunkPos.y;
        }
      }
      return bestY;
    }


    public void UpdateTerrainChunk() {
      if (mapDataReceived) {
        float viewerDistanceFromNearestEdge = Mathf.Sqrt(bounds.SqrDistance(viewerPosition));
        bool wasVisible = IsVisible();
        bool visible = viewerDistanceFromNearestEdge <= maxViewDistance;

        if (visible) {
          int lodIndex = 0;

          for (int i = 0; i < detailLevels.Length - 1; i++) {
            if (viewerDistanceFromNearestEdge > detailLevels[i].visibleDistanceThreshold) {
              lodIndex = i + 1;
            } else {
              break;
            }
          }
          Vector3 chunkPos = meshObject.transform.position;

          if (lodIndex != previousLODIndex) {
            LODMesh lodMesh = lodMeshes[lodIndex];
            if (lodMesh.hasMesh) {
              previousLODIndex = lodIndex;
              meshFilter.mesh = lodMesh.mesh;

              if (!hasSpawnedDecorations) {
                SpawnDecorations(lodMesh.mesh, chunkPos);
                hasSpawnedDecorations = true;

                // Handle robot spawning - separated from decoration spawning
                if (!hasSpawnedRobot && !hasScheduledRobotSpawn && robotPrefab != null) {
                  // Increased chance to 80% per chunk
                  if (Random.value < 0.8f) {
                    // Schedule robot spawn for later when NavMesh is ready
                    hasScheduledRobotSpawn = true;
                    hasSpawnedRobot = true; // Mark as spawned to prevent further attempts
                    
                    // Get water height if needed
                    if (cachedWaterWorldHeight < 0) {
                      float hMult = mapGenerator.heightMapSettings.heightMultiplier;
                      cachedWaterWorldHeight = mapGenerator
                        .heightMapSettings
                        .heightCurve
                        .Evaluate(waterHeight)
                        * hMult;
                    }
                    
                    // Start coroutine on the behaviour component
                    TerrainChunkBehaviour behaviour = meshObject.GetComponent<TerrainChunkBehaviour>();
                    if (behaviour != null) {
                      behaviour.SetParentChunk(this);
                      behaviour.StartRobotSpawnProcess(
                        lodMesh.mesh, 
                        chunkPos, 
                        cachedWaterWorldHeight, 
                        meshSize,
                        robotPrefab
                      );
                      Debug.Log("Scheduled robot spawn for later");
                    } else {
                      Debug.LogError("Missing TerrainChunkBehaviour component!");
                    }
                  } else {
                    hasSpawnedRobot = true; // Don't attempt again for this chunk
                  }
                }
              }

              // IMPROVED: Create water with layer-aware collision
              if (waterObject == null && waterPrefab != null) {
                waterObject = GameObject.Instantiate(waterPrefab);

                int waterLayer = LayerMask.NameToLayer("Water");
                SetLayerRecursively(waterObject, waterLayer);

                // Use cached water height
                if (cachedWaterWorldHeight < 0) {
                  float hMult = mapGenerator.heightMapSettings.heightMultiplier;
                  cachedWaterWorldHeight = mapGenerator
                    .heightMapSettings
                    .heightCurve
                    .Evaluate(waterHeight)
                  * hMult;
                }
                float y = cachedWaterWorldHeight;

                Vector3 chunkPosition = meshObject.transform.position;
                waterObject.transform.position = new Vector3(chunkPosition.x, y, chunkPosition.z);

                float scale = mapGenerator.meshSettings.meshWorldSize / 10f;
                waterObject.transform.localScale = new Vector3(scale, 1, scale);
                waterObject.transform.parent = meshObject.transform;
              }
            }
            else if (!lodMesh.hasRequestedMesh) {
              lodMesh.RequestMesh(mapData);
            }
          }
          
          // Ensure chunk is in the visible list if it should be
          if (!visibleTerrainChunks.Contains(this)) {
            visibleTerrainChunks.Add(this);
          }
        }

        if (wasVisible != visible) {
          SetVisible(visible);
        }
      }
    }
  
    public void UpdateCollisionMesh() {
      if (!hasSetCollider) {
        float sqrDstFromViewerToEdge = bounds.SqrDistance(viewerPosition);

        if (sqrDstFromViewerToEdge < detailLevels[colliderLODIndex].sqrVisibleDistanceThreshold) {
          if (!lodMeshes[colliderLODIndex].hasRequestedMesh) {
            lodMeshes[colliderLODIndex].RequestMesh(mapData);
          }
        }

        if (sqrDstFromViewerToEdge < colliderGenerationDistanceThreshold * colliderGenerationDistanceThreshold) {
          if (lodMeshes[colliderLODIndex].hasMesh) {
            meshCollider.sharedMesh = lodMeshes[colliderLODIndex].mesh;
            hasSetCollider = true;
          }
        }
      }
    }

    public void SetVisible(bool visible) {
      meshObject.SetActive(visible);
    }

    public bool IsVisible() {
      return meshObject.activeSelf;
    }
  }

  class LODMesh {
    public Mesh mesh;
    public bool hasRequestedMesh;
    public bool hasMesh;
    int lod;
    public event System.Action updateCallback;

    public LODMesh(int lod) {
      this.lod = lod;
    }

    void OnMeshDataReceived(MeshData meshData) {
      mesh = meshData.CreateMesh();
      hasMesh = true;

      updateCallback();
    }

    public void RequestMesh(HeightMap mapData) {
      hasRequestedMesh = true;
      mapGenerator.RequestMeshData(mapData, lod, OnMeshDataReceived);
    }
  }

  [System.Serializable]
  public struct LODInfo {
    [Range(0, MeshSettings.numSupportedLODs - 1)]
    public int lod;
    public float visibleDistanceThreshold;

    public float sqrVisibleDistanceThreshold {
      get {
        return visibleDistanceThreshold * visibleDistanceThreshold;
      }
    }
  }
  
  // Helper class to enable coroutines for terrain chunks
  public class TerrainChunkBehaviour : MonoBehaviour {
    private TerrainChunk parentChunk;
    
    public void SetParentChunk(TerrainChunk chunk) {
      parentChunk = chunk;
    }
    
    public void StartRobotSpawnProcess(Mesh mesh, Vector3 chunkPos, float waterWorld, float meshSize, GameObject robotPrefab) {
      // Start the coroutine properly from the MonoBehaviour
      StartCoroutine(TrySpawnRobotWithDelay(mesh, chunkPos, waterWorld, meshSize, robotPrefab));
    }
    
    private IEnumerator TrySpawnRobotWithDelay(Mesh mesh, Vector3 chunkPos, float waterWorld, float meshSize, GameObject robotPrefab) {
      // Wait a moment for NavMesh to build
      yield return new WaitForSeconds(1.0f);
      
      float pad = meshSize * 0.1f;
      bool InInterior(Vector3 w) {
        var local = w - chunkPos;
        return local.x > pad && local.x < meshSize - pad
            && local.z > pad && local.z < meshSize - pad;
      }
      
      bool spawnSuccessful = false;
      int attempts = 0;
      int maxAttempts = 5;
      
      while (!spawnSuccessful && attempts < maxAttempts) {
        attempts++;
        
        // Randomly select positions to try
        var verts = mesh.vertices;
        for (int i = 0; i < 10; i++) {
          var v = verts[Random.Range(0, verts.Length)];
          Vector3 w = chunkPos + v;
          
          if (w.y <= waterWorld + 0.1f || !InInterior(w)) continue;
          
          // Check NavMesh
          NavMeshHit hit;
          if (NavMesh.SamplePosition(w, out hit, 5f, NavMesh.AllAreas)) {
            GameObject robot = Instantiate(
              robotPrefab, 
              hit.position, 
              Quaternion.identity, 
              transform
            );
            spawnSuccessful = true;
            Debug.Log("Robot spawned successfully at " + hit.position);
            break;
          }
        }
        
        if (!spawnSuccessful) {
          Debug.Log($"Robot spawn attempt {attempts} failed, retrying in 2 seconds...");
          yield return new WaitForSeconds(2.0f); // Wait longer between attempts
        }
      }
      
      if (!spawnSuccessful) {
        Debug.LogWarning("Failed to spawn robot after maximum attempts.");
      }
    }
  }
}