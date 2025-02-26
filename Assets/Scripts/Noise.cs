using System.Collections;
using UnityEngine;

public static class Noise {

  public enum NormalizeMode {Local, Global};
  
  public static float[,] GenerateNoiseMap(int mapWidth, int mapHeight, int seed, float scale, int octaves, float persistance, float lacunarity, Vector2 offset, NormalizeMode normalizeMode) {
    float[,] noiseMap = new float[mapWidth, mapHeight];

    System.Random prng = new System.Random(seed);
    Vector2[] octaveOffsests = new Vector2[octaves];

    float maxPossibleHeight = 0;
    float amplitude = 1;
    float frequency = 1;

    for (int i = 0; i < octaves; i++) {
      float offsetX = prng.Next(-100000, 100000) + offset.x;
      float offsetY = prng.Next(-100000, 100000) - offset.y;
      octaveOffsests[i] = new Vector2(offsetX, offsetY);

      maxPossibleHeight += amplitude;
      amplitude *= persistance;
    }

    if (scale <= 0) {
      scale = 0.0001f;
    }

    float maxLocalNoiseHeight = float.MinValue;
    float minLocalNoiseHeight = float.MaxValue;

    // This is so what we change noise scale, it zooms in or out from the center, and not from the top right
    float halfWidth = mapWidth / 2f;
    float halfHeight = mapHeight / 2f;

    for (int y = 0; y < mapHeight; y++) {
      for (int x = 0; x < mapWidth; x++) {
        amplitude = 1;
        frequency = 1;
        float noiseHeight = 0;

        for (int i = 0; i < octaves; i++) {
          // We don't want integer values, so we divide by scale
          float sampleX = (x  - halfWidth + octaveOffsests[i].x) / scale * frequency;
          float sampleY = (y - halfHeight + octaveOffsests[i].y) / scale * frequency;

          // We mult by 2 then sub 1 so we can get negative numbers
          float perlinValue = Mathf.PerlinNoise(sampleX, sampleY) * 2 - 1;
          noiseHeight += perlinValue * amplitude;

          amplitude *= persistance;
          frequency *= lacunarity;
        }

        // Keep track of max and min
        if (noiseHeight > maxLocalNoiseHeight) {
          maxLocalNoiseHeight = noiseHeight;
        } else if (noiseHeight < minLocalNoiseHeight) {
          minLocalNoiseHeight = noiseHeight;
        }

        noiseMap[x, y] = noiseHeight;
      }
    }
    
    for (int y = 0; y < mapHeight; y++) {
      for (int x = 0; x < mapWidth; x++) {
        // Normalize our noise map
        if (normalizeMode == NormalizeMode.Local) {
          noiseMap[x, y] = Mathf.InverseLerp(minLocalNoiseHeight, maxLocalNoiseHeight, noiseMap[x, y]);
        } else {
          float normalizedHeight = (noiseMap[x, y] + 1) / (2f * maxPossibleHeight / 1.8f);
          noiseMap[x, y] = Mathf.Clamp(normalizedHeight, 0, int.MaxValue);
        }
      }
    }
    
    return noiseMap;
  }
}
