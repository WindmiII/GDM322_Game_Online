using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[DisallowMultipleComponent]
public class Outline : MonoBehaviour {
  private static HashSet<Mesh> registeredMeshes = new HashSet<Mesh>();

  public Color OutlineColor {
    get { return outlineColor; }
    set {
      outlineColor = value;
      needsUpdate = true;
    }
  }

  public float OutlineWidth {
    get { return outlineWidth; }
    set {
      outlineWidth = value;
      needsUpdate = true;
    }
  }

  [Serializable]
  private class ListVector3 {
    public List<Vector3> data;
  }

  [SerializeField]
  private Color outlineColor = Color.white;

  [SerializeField, Range(0f, 10f)]
  private float outlineWidth = 0f; // เริ่มต้นเป็น 0 (ซ่อน)

  [Header("HDRP Settings")]
  [SerializeField]
  private Material outlineMaterial; 

  [Header("Optional")]
  [SerializeField]
  private bool precomputeOutline = true; 

  [SerializeField, HideInInspector]
  private List<Mesh> bakeKeys = new List<Mesh>();

  [SerializeField, HideInInspector]
  private List<ListVector3> bakeValues = new List<ListVector3>();

  private Renderer[] renderers;
  private Material outlineInstanceMat;
  private bool needsUpdate;

  void Awake() {
    renderers = GetComponentsInChildren<Renderer>();

    if (outlineMaterial == null) {
        enabled = false; 
        return;
    }

    outlineInstanceMat = Instantiate(outlineMaterial);
    outlineInstanceMat.name = "Outline (Instance)";
    
    outlineInstanceMat.SetColor("_OutlineColor", outlineColor);
    outlineInstanceMat.SetFloat("_OutlineWidth", 0f); // เริ่มมาซ่อนไว้ก่อน

    LoadSmoothNormals();
  }

  void Start() {
      foreach (var renderer in renderers) {
        var materials = renderer.sharedMaterials.ToList();
        materials.Add(outlineInstanceMat);
        renderer.materials = materials.ToArray();
      }
  }

  // --- 👇 ฟังก์ชันที่ Error ถามหาอยู่ตรงนี้ครับ 👇 ---
  public void ShowOutline(bool show) {
      if (outlineInstanceMat != null) {
          // ถ้า true ให้ใช้ความหนา 0.05 (หรือตามที่ตั้ง), ถ้า false ให้เป็น 0
          float widthToShow = (outlineWidth > 0) ? outlineWidth : 0.05f; 
          outlineInstanceMat.SetFloat("_OutlineWidth", show ? widthToShow : 0f);
      }
  }
  // ----------------------------------------------

  void OnValidate() {
    needsUpdate = true;
    if (!precomputeOutline && bakeKeys.Count != 0 || bakeKeys.Count != bakeValues.Count) {
      bakeKeys.Clear();
      bakeValues.Clear();
    }
    if (precomputeOutline && bakeKeys.Count == 0) {
      Bake();
    }
  }

  void Update() {
    if (needsUpdate) {
      needsUpdate = false;
      UpdateMaterialProperties();
    }
  }

  void OnDestroy() {
    if(outlineInstanceMat != null) Destroy(outlineInstanceMat);
  }

  void Bake() {
    var bakedMeshes = new HashSet<Mesh>();
    foreach (var meshFilter in GetComponentsInChildren<MeshFilter>()) {
      if (!bakedMeshes.Add(meshFilter.sharedMesh)) continue;
      var smoothNormals = SmoothNormals(meshFilter.sharedMesh);
      bakeKeys.Add(meshFilter.sharedMesh);
      bakeValues.Add(new ListVector3() { data = smoothNormals });
    }
  }

  void LoadSmoothNormals() {
    foreach (var meshFilter in GetComponentsInChildren<MeshFilter>()) {
      if (!registeredMeshes.Add(meshFilter.sharedMesh)) continue;
      var index = bakeKeys.IndexOf(meshFilter.sharedMesh);
      var smoothNormals = (index >= 0) ? bakeValues[index].data : SmoothNormals(meshFilter.sharedMesh);
      meshFilter.sharedMesh.SetUVs(3, smoothNormals);
    }
    foreach (var skinnedMeshRenderer in GetComponentsInChildren<SkinnedMeshRenderer>()) {
      if (!registeredMeshes.Add(skinnedMeshRenderer.sharedMesh)) continue;
      skinnedMeshRenderer.sharedMesh.uv4 = new Vector2[skinnedMeshRenderer.sharedMesh.vertexCount];
    }
  }

  List<Vector3> SmoothNormals(Mesh mesh) {
    var groups = mesh.vertices.Select((vertex, index) => new KeyValuePair<Vector3, int>(vertex, index)).GroupBy(pair => pair.Key);
    var smoothNormals = new List<Vector3>(mesh.normals);
    foreach (var group in groups) {
      if (group.Count() == 1) continue;
      var smoothNormal = Vector3.zero;
      foreach (var pair in group) smoothNormal += smoothNormals[pair.Value];
      smoothNormal.Normalize();
      foreach (var pair in group) smoothNormals[pair.Value] = smoothNormal;
    }
    return smoothNormals;
  }

  void UpdateMaterialProperties() {
    if(outlineInstanceMat != null) {
        outlineInstanceMat.SetColor("_OutlineColor", outlineColor);
    }
  }
}