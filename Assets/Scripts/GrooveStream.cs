using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Поток чанков жёлоба вокруг игрока. Меш чанка — чистая функция от его
/// индекса, поэтому чанки не хранят состояния и свободно переиспользуются.
/// Стыки не видны, потому что обе стороны берут одну и ту же непрерывную
/// трассу.
/// </summary>
public class GrooveStream : MonoBehaviour
{
    public PlayerMotor player;
    public Material body, edge;
    public float chunkLength = 24f;
    public float ringStep = 1f;
    public int arcSegments = 40;
    public int edgeBands = 2;
    public int ahead = 7;
    public int behind = 1;

    public static GrooveStream I;

    Track _track;
    readonly Dictionary<int, GameObject> _live = new Dictionary<int, GameObject>();
    readonly Stack<Mesh> _pool = new Stack<Mesh>();
    readonly List<int> _stale = new List<int>();

    void Awake()
    {
        I = this;
        if (!body) body = MakeMaterial("Groove", new Color(.055f, .075f, .16f), Color.black);
        if (!edge) edge = MakeMaterial("GrooveEdge", new Color(.05f, .8f, 1f), new Color(0f, 2f, 2.6f));
    }

    static Material MakeMaterial(string n, Color c, Color e)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.name = n;
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", 0.6f);
        if (e.maxColorComponent > 0f)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", e);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        return m;
    }

    void Update()
    {
        if (!_track) { _track = Track.Main; if (!_track) return; }
        if (!player) { player = FindFirstObjectByType<PlayerMotor>(); if (!player) return; }

        int first = Mathf.FloorToInt(player.S / chunkLength) - behind;
        int last = first + behind + ahead;

        _stale.Clear();
        foreach (var k in _live.Keys)
            if (k < first || k > last) _stale.Add(k);
        foreach (var k in _stale) Recycle(k);

        for (int c = first; c <= last; c++)
            if (!_live.ContainsKey(c)) Spawn(c);
    }

    /// Сбрасывает все чанки, чтобы на следующем кадре они пересобрались
    /// по новой геометрии трассы.
    public void ClearAll()
    {
        var keys = new List<int>(_live.Keys);
        for (int i = 0; i < keys.Count; i++) Recycle(keys[i]);
    }

    void Spawn(int c)
    {
        var m = _pool.Count > 0 ? _pool.Pop() : new Mesh();
        Build(m, c);

        var go = new GameObject("Chunk " + c);
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = m;
        go.AddComponent<MeshRenderer>().sharedMaterials = new[] { body, edge };
        _live[c] = go;
    }

    void Recycle(int c)
    {
        var go = _live[c];
        _pool.Push(go.GetComponent<MeshFilter>().sharedMesh);
        _live.Remove(c);
        Destroy(go);
    }

    void Build(Mesh m, int c)
    {
        m.Clear();
        float s0 = c * chunkLength;
        int rings = Mathf.RoundToInt(chunkLength / ringStep);
        float half = _track.HalfArc;
        float r = _track.Radius;
        // Держим грани одного размера, когда сектор раскрывается в полную трубу.
        int segs = Mathf.Max(arcSegments, Mathf.RoundToInt(arcSegments * half / 1.658f));
        int cols = segs + 1;

        var verts = new Vector3[(rings + 1) * cols];
        var norms = new Vector3[verts.Length];
        var uvs = new Vector2[verts.Length];

        for (int i = 0; i <= rings; i++)
        {
            float s = s0 + i * ringStep;
            Quaternion rot = _track.Rot(s);
            Vector3 p = _track.Pos(s);
            for (int j = 0; j <= segs; j++)
            {
                float t = (float)j / segs;
                float th = Mathf.Lerp(-half, half, t);
                int v = i * cols + j;
                verts[v] = p + rot * new Vector3(Mathf.Sin(th) * r, (1f - Mathf.Cos(th)) * r, 0f);
                norms[v] = rot * new Vector3(-Mathf.Sin(th), Mathf.Cos(th), 0f);
                uvs[v] = new Vector2(t * 4f, s * 0.1f);
            }
        }

        var face = new List<int>(rings * segs * 6);
        var glow = new List<int>(rings * edgeBands * 12);
        for (int i = 0; i < rings; i++)
        {
            for (int j = 0; j < segs; j++)
            {
                int a = i * cols + j, b = a + 1, c2 = a + cols, d = c2 + 1;
                var list = (j < edgeBands || j >= segs - edgeBands) ? glow : face;
                list.Add(a); list.Add(c2); list.Add(b);
                list.Add(b); list.Add(c2); list.Add(d);
            }
        }

        m.SetVertices(verts);
        m.SetNormals(norms);
        m.SetUVs(0, uvs);
        m.subMeshCount = 2;
        m.SetTriangles(face, 0);
        m.SetTriangles(glow, 1);
        m.RecalculateBounds();
    }
}
