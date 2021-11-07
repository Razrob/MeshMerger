using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using System.Linq;
using UnityEditor;

public class MeshMerger : MonoBehaviour
{
    [SerializeField] private Transform[] _mergeObjects;
    [SerializeField] private Transform _meshPivot;

    [Header("Save properties")]
    [SerializeField] private bool _saveMesh;
    [SerializeField] private string _objectName = "MergedObject";
    [SerializeField] private string _savePath = "Assets/SavedMeshes";

    private MeshSaver _meshSaver = new MeshSaver();

    public void Merge()
    {
        Transform[] _meshesTransforms = GetTargetTranforms(_mergeObjects).Distinct().ToArray();
        Mesh[] meshes = new Mesh[_meshesTransforms.Length];
        meshes = _meshesTransforms.Select(transform => transform.GetComponent<MeshFilter>().sharedMesh).ToArray();

        Vector3[] vertices = new Vector3[0];
        int[] triangles = new int[0];
        Vector2[] uv = new Vector2[0];
        Vector4[] tangents = new Vector4[0];
        Vector3[] normals = new Vector3[0];

        List<SubMeshInfo> subMeshesInfo = new List<SubMeshInfo>();

        for (int meshIndex = 0; meshIndex < meshes.Length; meshIndex++)
        {
            Mesh currentMesh = meshes[meshIndex];
            Transform currentTransform = _meshesTransforms[meshIndex];
            Material[] meshMaterials = _meshesTransforms[meshIndex].GetComponent<MeshRenderer>().sharedMaterials;
            Vector3 meshLocalPosition = currentTransform.position - _meshPivot.position;

            int subMeshCount = currentMesh.subMeshCount;
            SubMeshDescriptor[] subMeshDescriptors = new SubMeshDescriptor[subMeshCount];
            for (int i = 0; i < subMeshCount; i++) subMeshDescriptors[i] = currentMesh.GetSubMesh(i);

            for (int i = 0; i < meshMaterials.Length; i++)
            {
                List<Vector3> addedVertices = currentMesh.vertices
                    .ToList()
                    .GetRange(subMeshDescriptors[i].firstVertex, subMeshDescriptors[i].vertexCount)
                    .Select(verticle => RotateVerticle(verticle + meshLocalPosition, currentTransform.eulerAngles, meshLocalPosition))
                    .ToList();
                List<Vector2> addedUV = currentMesh.uv
                    .ToList()
                    .GetRange(subMeshDescriptors[i].firstVertex, subMeshDescriptors[i].vertexCount);
                List<Vector4> addedTangents = currentMesh.tangents
                    .ToList()
                    .GetRange(subMeshDescriptors[i].firstVertex, subMeshDescriptors[i].vertexCount)
                    .Select(tangent =>
                    {
                        Vector4 result = RotateVerticle(new Vector3(tangent.x, tangent.y, tangent.z), currentTransform.eulerAngles, Vector3.zero);
                        result.w = tangent.w;
                        return result;
                    })
                    .ToList();
                List<Vector3> addedNormals = currentMesh.normals
                    .ToList()
                    .GetRange(subMeshDescriptors[i].firstVertex, subMeshDescriptors[i].vertexCount)
                    .Select(normal => RotateVerticle(normal, currentTransform.eulerAngles, Vector3.zero))
                    .ToList();
                List<int> addedTriangleIndexes = currentMesh.triangles
                    .Where(index => index >= subMeshDescriptors[i].firstVertex && index < subMeshDescriptors[i].firstVertex + subMeshDescriptors[i].vertexCount)
                    .ToList();

                int subMeshInfoIndex = subMeshesInfo
                    .FindIndex(subMeshInfo => subMeshInfo.VerticesMaterial == meshMaterials[i]);
                if (subMeshInfoIndex == -1)
                {
                    subMeshesInfo.Add(new SubMeshInfo
                    {
                        Vertices = addedVertices,
                        UV = addedUV,
                        Tangents = addedTangents,
                        Normals = addedNormals,
                        TriangleIndexes = addedTriangleIndexes,
                        VerticesMaterial = meshMaterials[i]
                    });
                }
                else
                {
                    subMeshesInfo[subMeshInfoIndex].TriangleIndexes
                        .AddRange(addedTriangleIndexes
                        .Select(index => index + subMeshesInfo[subMeshInfoIndex].Vertices.Count));
                    subMeshesInfo[subMeshInfoIndex].Vertices.AddRange(addedVertices);
                    subMeshesInfo[subMeshInfoIndex].UV.AddRange(addedUV);
                    subMeshesInfo[subMeshInfoIndex].Tangents.AddRange(addedTangents);
                    subMeshesInfo[subMeshInfoIndex].Normals.AddRange(addedNormals);
                }
            }
        }

        Material[] materials = new Material[subMeshesInfo.Count];

        SubMeshDescriptor[] finalSubMeshDescriptors = new SubMeshDescriptor[subMeshesInfo.Count];

        for(int i = 0; i < subMeshesInfo.Count; i++)
        {
            materials[i] = subMeshesInfo[i].VerticesMaterial;
            int startVerticleIndex = vertices.Length;
            int startTrianglesIndex = triangles.Length;

            triangles = triangles
                .Concat(subMeshesInfo[i].TriangleIndexes
                .Select(index => index + vertices.Length))
                .ToArray();
            vertices = vertices
                .Concat(subMeshesInfo[i].Vertices)
                .ToArray();
            uv = uv
                .Concat(subMeshesInfo[i].UV)
                .ToArray();
            tangents = tangents
                .Concat(subMeshesInfo[i].Tangents)
                .ToArray();
            normals = normals
                .Concat(subMeshesInfo[i].Normals)
                .ToArray();

            finalSubMeshDescriptors[i] = new SubMeshDescriptor
            {
                firstVertex = startVerticleIndex,
                indexStart = startTrianglesIndex,
                vertexCount = subMeshesInfo[i].Vertices.Count,
                indexCount = subMeshesInfo[i].TriangleIndexes.Count,
            };
        }

        Mesh resultMesh = CreateMesh(vertices, triangles, uv, tangents, normals, finalSubMeshDescriptors);
        GameObject house = CreateGameObject(resultMesh, materials);
        if (_saveMesh) _meshSaver.SaveMesh(resultMesh, _savePath);
    }

    private Vector3 RotateVerticle(Vector3 verticle, Vector3 rotation, Vector3 rotatePoint) => Quaternion.Euler(rotation) * (verticle - rotatePoint) + rotatePoint;

    private Transform[] GetTargetTranforms(Transform[] transforms)
    {
        List<Transform> result = new List<Transform>();
        for(int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i].TryGetComponent(out MeshFilter meshFilter)) result.Add(transforms[i]);

            Transform[] children = new Transform[transforms[i].childCount];
            for (int t = 0; t < transforms[i].childCount; t++) children[t] = transforms[i].GetChild(t);
            result.AddRange(GetTargetTranforms(children));
        }

        return result.ToArray();
    }

    private Mesh CreateMesh(Vector3[] vertices, int[] triangles, Vector2[] uv, Vector4[] tangents, Vector3[] normals, SubMeshDescriptor[] subMeshDescriptors)
    {
        Mesh resultMesh = new Mesh();
        
        resultMesh.vertices = vertices;
        resultMesh.triangles = triangles;
        resultMesh.uv = uv;
        resultMesh.tangents = tangents;
        resultMesh.normals = normals;
        resultMesh.name = $"{_objectName}Mesh";
        resultMesh.subMeshCount = subMeshDescriptors.Length;
        for (int i = 0; i < subMeshDescriptors.Length; i++) resultMesh.SetSubMesh(i, subMeshDescriptors[i]);

        resultMesh.RecalculateBounds();

        return resultMesh;
    }

    private GameObject CreateGameObject(Mesh mesh, Material[] materials)
    {
        GameObject house = new GameObject(_objectName);
        house.AddComponent<MeshFilter>().sharedMesh = mesh;
        house.AddComponent<MeshRenderer>().sharedMaterials = materials;

        return house;
    }

    private struct SubMeshInfo
    {
        public List<Vector3> Vertices;
        public List<Vector2> UV;
        public List<Vector4> Tangents;
        public List<Vector3> Normals;
        public List<int> TriangleIndexes;
        public Material VerticesMaterial;
    }
}