using UnityEngine;
using System.Collections.Generic;
using System.Runtime.InteropServices;

// ExecuteInEditMode 方便在编辑器里看到效果
[ExecuteInEditMode]
public class DrawMeshInstancedIndirectDemo : MonoBehaviour
{
    [Header("设置")] public Mesh instanceMesh;
    public Material instanceMaterial;
    [Range(1, 400000)] public int instanceCount = 10000;
    public float areaSize = 100f;
    public ComputeShader cullingComputeShader;
    public float Radius = 1;


    private Camera mainCamera;

    private int cullingKernelID;

    // 存储所有实例的原始数据
    private ComputeBuffer allInstancesDataBuffer;

    // 只存储通过剔除后可见的实例数据
    private ComputeBuffer visibleInstancesDataBuffer;

    // 存储渲染指令参数
    private ComputeBuffer argsBuffer;

    private uint[] args = new uint[5] { 0, 0, 0, 0, 0 };
    private float instanceRadius;
    private bool isInitialized = false;

    // 与Shader匹配的数据结构
    private struct InstanceData
    {
        public Vector4 position;
        public Vector4 color;
    }

    void Start()
    {
        Initialize();
    }

    void OnEnable()
    {
        Initialize();
    }

    // 当在编辑器中修改参数时，重新初始化
    void OnValidate()
    {
        if (!Application.isPlaying)
        {
            Initialize();
        }
    }


    void Update()
    {
        if (!isInitialized)
        {
            Initialize();
            // 如果初始化失败，则不执行后续逻辑
            if (!isInitialized) return;
        }

        // --- GPU Culling ---
        Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(mainCamera);
        cullingComputeShader.SetVectorArray("_FrustumPlanes", PlanesToVector4(frustumPlanes));
        cullingComputeShader.SetFloat("_MaxDistance", mainCamera.farClipPlane);
        cullingComputeShader.SetMatrix("_CameraLocalToWorld", mainCamera.transform.localToWorldMatrix);
        cullingComputeShader.SetFloat("_InstanceRadius", instanceRadius);

        visibleInstancesDataBuffer.SetCounterValue(0);

        // 修正: 使用浮点数除法以确保正确计算线程组数量
        cullingComputeShader.Dispatch(cullingKernelID, Mathf.CeilToInt(instanceCount / 64f), 1, 1);

        // 从可见缓冲区中获取计数，并设置到渲染参数中
        ComputeBuffer.CopyCount(visibleInstancesDataBuffer, argsBuffer, sizeof(uint));

        // --- Rendering ---
        Bounds renderBounds = new Bounds(Vector3.zero, new Vector3(areaSize * 2, 20, areaSize * 2));
        Graphics.DrawMeshInstancedIndirect(
            instanceMesh,
            0,
            instanceMaterial,
            renderBounds,
            argsBuffer
        );
    }

    void OnDisable()
    {
        ReleaseBuffers();
    }

    void Initialize()
    {
        // 确保在重新初始化前释放旧资源
        ReleaseBuffers();
        if (mainCamera == null)
            mainCamera = Camera.main;


        if (instanceMesh == null || instanceMaterial == null || cullingComputeShader == null || mainCamera == null ||
            !SystemInfo.supportsComputeShaders)
        {
            isInitialized = false;
            return;
        }

        this.instanceRadius = instanceMesh.bounds.extents.magnitude * Radius;

        // 初始化所有实例数据
        List<InstanceData> allInstances = new List<InstanceData>(instanceCount);
        for (int i = 0; i < instanceCount; i++)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float distance = Random.Range(0f, areaSize / 2f);
            float x = Mathf.Cos(angle) * distance;
            float z = Mathf.Sin(angle) * distance;
            Vector4 pos = new Vector4(x, 0, z, 1);
            Color randomColor = new Color(Random.value, Random.value, Random.value, 1);

            allInstances.Add(new InstanceData { position = pos, color = randomColor });
        }

// 告诉 GPU 在读取缓冲区时，每个数据元素占多少字节，
// 主要作用是为了 精确计算出每个实例数据的大小，确保 GPU 能正确地、无误地读取每一个实例的信息。
        int stride = Marshal.SizeOf(typeof(InstanceData));
        // 告诉GPU开辟一块需要能容纳 instanceCount 个元素的内存空间。
        allInstancesDataBuffer = new ComputeBuffer(instanceCount, stride);

        // 将在CPU 端创建的 allInstances 列表（包含了所有实例的位置和颜色），一次性地拷贝到 GPU 上的 allInstancesDataBuffer 中。
        // 将所有实例的原始数据（无论可见与否）交给 GPU，作为后续剔除计算的“数据源”。
        allInstancesDataBuffer.SetData(allInstances);

        visibleInstancesDataBuffer = new ComputeBuffer(instanceCount, stride, ComputeBufferType.Append);

        argsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        args[0] = (uint)instanceMesh.GetIndexCount(0);
        args[1] = 0; // 关键: 初始实例数为0，由GPU填充
        args[2] = (uint)instanceMesh.GetIndexStart(0);
        args[3] = (uint)instanceMesh.GetBaseVertex(0);
        args[4] = 0;
        argsBuffer.SetData(args);


        cullingKernelID = cullingComputeShader.FindKernel("CullInstances");
        // 绑定缓冲区
        cullingComputeShader.SetBuffer(cullingKernelID, "_AllInstancesData", allInstancesDataBuffer);
        cullingComputeShader.SetBuffer(cullingKernelID, "_VisibleInstancesData", visibleInstancesDataBuffer);
        // 关键: 将*可见*实例的缓冲区绑定到渲染材质
        instanceMaterial.SetBuffer("_VisibleInstancesData", visibleInstancesDataBuffer);

        isInitialized = true;
    }

    private Vector4[] PlanesToVector4(Plane[] planes)
    {
        Vector4[] planeVectors = new Vector4[planes.Length];
        for (int i = 0; i < planes.Length; i++)
        {
            planeVectors[i] = new Vector4(planes[i].normal.x, planes[i].normal.y, planes[i].normal.z,
                planes[i].distance);
        }

        return planeVectors;
    }

    private void ReleaseBuffers()
    {
        allInstancesDataBuffer?.Release();
        allInstancesDataBuffer = null;

        argsBuffer?.Release();
        argsBuffer = null;

        visibleInstancesDataBuffer?.Release();
        visibleInstancesDataBuffer = null;

        // isInitialized 在这里设置为false，确保下次Update时能重新初始化
        isInitialized = false;
    }
}