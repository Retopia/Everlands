using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

[CustomEditor (typeof (MapGenerator))]
public class MapGeneratorEditor : Editor {

  public override void OnInspectorGUI() {
    MapGenerator mapGen = (MapGenerator) target;

    // If any value is changed
    if (DrawDefaultInspector()) {
      if (mapGen.autoUpdate) {
        mapGen.DrawMapInEditor();
      }
    }

    if (GUILayout.Button("Generate")) {
      mapGen.DrawMapInEditor();
    }
  }
}
