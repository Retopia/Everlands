using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MapDisplay : MonoBehaviour {
  public Renderer textureRender;
  public MeshFilter meshFilter;
  public MeshRenderer meshRenderer;

  public void DrawTexture(Texture2D texture) {
    textureRender.sharedMaterial.mainTexture = texture;
    textureRender.transform.localScale = new Vector3(texture.width, 1, texture.height);
  }

  public void DrawMesh(MeshData meshData) {
    meshFilter.sharedMesh = meshData.CreateMesh();

    meshFilter.transform.localScale = Vector3.one * FindObjectOfType<MapGenerator>().meshSettings.meshScale;
  }
}
