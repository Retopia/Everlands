using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WaterWaveUV : MonoBehaviour {
    public float scrollSpeed = 0.05f;
    private Material mat;

    void Start() {
        mat = GetComponent<Renderer>().material;
    }

    void Update() {
        float offset = Time.time * scrollSpeed;
        mat.mainTextureOffset = new Vector2(offset, offset);
    }
}
