using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EndlessTerrain : MonoBehaviour {
  const float viewerMoveThresholdForChunkUpdate = 25f;
  const float squareViewerMoveThresholdForChunkUpdate = viewerMoveThresholdForChunkUpdate * viewerMoveThresholdForChunkUpdate;
  const float colliderGenerationDistanceThreshold = 5;
  public int colliderLODIndex;
  public LODInfo[] detailLevels;
  public static float maxViewDistance;
  public Transform viewer;
  public Material mapMaterial;
  public static Vector2 viewerPosition;
  Vector2 viewerPositionOld;
  static MapGenerator mapGenerator;
  int chunkSize;
  int chunksVisibleInViewDistance;
  Dictionary<Vector2, TerrainChunk> terrainChunkDictionary = new Dictionary<Vector2, TerrainChunk>();
  static List<TerrainChunk> visibleTerrainChunks = new List<TerrainChunk>();
  
  void Start() {
    mapGenerator = FindObjectOfType<MapGenerator>();

    maxViewDistance = detailLevels[detailLevels.Length - 1].visibleDistanceThreshold;
    chunkSize = mapGenerator.mapChunkSize - 1;
    chunksVisibleInViewDistance = Mathf.RoundToInt(maxViewDistance / chunkSize);

    UpdateVisibleChunks();
  }

  void Update() {
    viewerPosition = new Vector2(viewer.position.x, viewer.position.z) / mapGenerator.terrainData.uniformScale;

    if (viewerPosition != viewerPositionOld) {
      foreach (TerrainChunk chunk in visibleTerrainChunks) {
        chunk.UpdateCollisionMesh();
      }
    }

    if ((viewerPositionOld - viewerPosition).sqrMagnitude > squareViewerMoveThresholdForChunkUpdate) {
      viewerPositionOld = viewerPosition;
      UpdateVisibleChunks();
    }
  }

  void UpdateVisibleChunks() {
    HashSet<Vector2> alreadyUpdatedChunkCoords = new HashSet<Vector2>();
    for (int i = visibleTerrainChunks.Count - 1; i > -1; i--) {
      alreadyUpdatedChunkCoords.Add(visibleTerrainChunks[i].coord);
      visibleTerrainChunks[i].UpdateTerrainChunk();
    }

    int currentChunkCoordX = Mathf.RoundToInt(viewerPosition.x / chunkSize);
    int currentChunkCoordY = Mathf.RoundToInt(viewerPosition.y / chunkSize);

    for (int yOffset = -chunksVisibleInViewDistance; yOffset <= chunksVisibleInViewDistance; yOffset += 1) {
      for (int xOffset = -chunksVisibleInViewDistance; xOffset <= chunksVisibleInViewDistance; xOffset += 1) {
        Vector2 viewedChunkCoord = new Vector2(currentChunkCoordX + xOffset, currentChunkCoordY + yOffset);
        if (!alreadyUpdatedChunkCoords.Contains(viewedChunkCoord)) {
          if (terrainChunkDictionary.ContainsKey(viewedChunkCoord)) {
            terrainChunkDictionary[viewedChunkCoord].UpdateTerrainChunk();
          } else {
            terrainChunkDictionary.Add(viewedChunkCoord, new TerrainChunk(viewedChunkCoord, chunkSize, detailLevels, colliderLODIndex, transform, mapMaterial));
          }
        }
      }
    }
  }

  public class TerrainChunk {
    public Vector2 coord;
    GameObject meshObject;
    Vector2 position;
    Bounds bounds;

    MapData mapData;
    MeshRenderer meshRenderer;
    MeshFilter meshFilter;
    MeshCollider meshCollider;
    LODInfo[] detailLevels;
    LODMesh[] lodMeshes;
    int colliderLODIndex;
    bool mapDataReceived;
    int previousLODIndex = -1;
    bool hasSetCollider;
    public TerrainChunk(Vector2 coord, int size, LODInfo[] detailLevels, int colliderLODIndex, Transform parent, Material material) {
      this.coord = coord;
      this.detailLevels = detailLevels;
      this.colliderLODIndex = colliderLODIndex;

      position = coord * size;
      bounds = new Bounds(position, Vector2.one * size);
      Vector3 positionV3 = new Vector3(position.x, 0, position.y);

      meshObject = new GameObject("Terrain Chunk");
      meshRenderer = meshObject.AddComponent<MeshRenderer>();
      meshFilter = meshObject.AddComponent<MeshFilter>();
      meshCollider = meshObject.AddComponent<MeshCollider>();
      meshRenderer.material = material;
      meshObject.transform.position = positionV3 * mapGenerator.terrainData.uniformScale;
      meshObject.transform.parent = parent;
      meshObject.transform.localScale = Vector3.one * mapGenerator.terrainData.uniformScale;
      SetVisible(false);

      lodMeshes = new LODMesh[detailLevels.Length];

      for (int i = 0; i < detailLevels.Length; i++) {
        lodMeshes[i] = new LODMesh(detailLevels[i].lod);
        lodMeshes[i].updateCallback += UpdateTerrainChunk;
        if (i == colliderLODIndex) {
          lodMeshes[i].updateCallback += UpdateCollisionMesh;
        }
      }

      mapGenerator.RequestMapData(position, OnMapDataReceived);
    }

    void OnMapDataReceived(MapData mapData) {
      this.mapData = mapData;
      mapDataReceived = true;

      UpdateTerrainChunk();
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

          if (lodIndex != previousLODIndex) {
            LODMesh lodMesh = lodMeshes[lodIndex];
            if (lodMesh.hasMesh) {
              previousLODIndex = lodIndex;
              meshFilter.mesh = lodMesh.mesh;
            } else if (!lodMesh.hasRequestedMesh) {
              lodMesh.RequestMesh(mapData);
            }
          }
          visibleTerrainChunks.Add(this);
        }

        if (wasVisible != visible) {
          if (visible) {
            visibleTerrainChunks.Add(this);
          } else {
            visibleTerrainChunks.Remove(this);
          }
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

    public void RequestMesh(MapData mapData) {
      hasRequestedMesh = true;
      mapGenerator.RequestMeshData(mapData, lod, OnMeshDataReceived);
    }
  }

  [System.Serializable]
  public struct LODInfo {
    [Range(0, MeshGenerator.numSupportedLODs - 1)]
    public int lod;
    public float visibleDistanceThreshold;

    public float sqrVisibleDistanceThreshold {
      get {
        return visibleDistanceThreshold * visibleDistanceThreshold;
      }
    }
  }
}