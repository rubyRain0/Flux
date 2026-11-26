using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Препятствия, монеты, орбы мутаций и аптечки. Расстановка — чистая функция
/// от индекса чанка, поэтому повтор забега воспроизводит ту же трассу.
/// Препятствия вырезаны из самой трубы: сидят вровень, и хитбокс совпадает с
/// тем, что нарисовано. Коллизии считаются в координатах жёлоба
/// (s, theta, высота), без rigidbody.
/// </summary>
public class Props : MonoBehaviour
{
    public static Props I;

    public PlayerMotor player;
    public Material obstacleMat, coinMat, orbMat, healthMat;
    public int seed = 3;
    public float chunkLength = 24f;
    public int ahead = 14, behind = 1;
    [Tooltip("Свободный коридор между препятствием и любым пикапом.")]
    public float coinClearance = 11f;
    [Tooltip("Зазор между двумя пикапами, чтобы они никогда не пересекались.")]
    public float pickupGap = 3f;

    const int Obstacle = 0, Coin = 1, Orb = 2, Health = 3;
    static readonly float[] Lanes = { 0f, -0.5f, 0.5f, -0.88f, 0.88f };
    // Пикапы не висят выше, чем достаёт целеустремлённый подъём за один след.
    static readonly float[] PickupLanes = { 0f, -0.45f, 0.45f, -0.75f, 0.75f };

    const float PopTime = 0.14f;

    class Item
    {
        public int kind, mut;
        public float s, theta, halfArc, height, depth;
        public float pop;
        public Vector3 baseScale = Vector3.one;
        public GameObject go;
    }

    Track _track;
    readonly Dictionary<int, List<Item>> _chunks = new Dictionary<int, List<Item>>();
    readonly List<int> _stale = new List<int>();
    readonly Stack<GameObject> _obstacles = new Stack<GameObject>();
    readonly Stack<GameObject> _coins = new Stack<GameObject>();
    readonly Stack<GameObject> _orbs = new Stack<GameObject>();
    readonly Stack<GameObject> _health = new Stack<GameObject>();
    readonly Dictionary<string, Mesh> _patches = new Dictionary<string, Mesh>();
    MaterialPropertyBlock _mpb;

    void Awake()
    {
        I = this;
        _mpb = new MaterialPropertyBlock();
    }

    void Update()
    {
        if (!_track) { _track = Track.Main; if (!_track) return; }
        if (!player) { player = FindFirstObjectByType<PlayerMotor>(); if (!player) return; }

        int first = Mathf.FloorToInt(player.S / chunkLength) - behind;
        int last = first + behind + ahead;

        _stale.Clear();
        foreach (var k in _chunks.Keys) if (k < first) _stale.Add(k);
        foreach (var k in _stale) Unload(k);
        for (int c = first; c <= last; c++) if (!_chunks.ContainsKey(c)) Load(c);

        Test();
    }

    public void Reseed(int newSeed)
    {
        seed = newSeed;
        ClearAll();
    }

    public void ClearAll()
    {
        var keys = new List<int>(_chunks.Keys);
        for (int i = 0; i < keys.Count; i++) Unload(keys[i]);
    }

    // ------------------------------------------------------------- столкновения

    void Test()
    {
        var mut = Mutation.I;
        bool alive = Run.I != null && Run.I.Phase == RunState.Running;
        float ps = player.S, pth = player.Theta, air = player.Air;
        float body = player.bodyRadius;
        float bodyArc = body / _track.Radius;
        float spin = 180f * Time.deltaTime;

        foreach (var pair in _chunks)
        {
            var items = pair.Value;
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (it.go == null) continue;

                // Собранный пикап: короткий всплеск наружу, потом обратно в пул.
                if (it.pop > 0f)
                {
                    it.pop -= Time.deltaTime;
                    float k = Mathf.Clamp01(it.pop / PopTime);
                    it.go.transform.localScale = it.baseScale * (1f + 0.9f * (1f - k));
                    if (it.pop <= 0f) Return(it);
                    continue;
                }

                if (it.kind != Obstacle)
                {
                    it.go.transform.Rotate(0f, 0f, spin, Space.Self);
                    if (!alive) continue;

                    float reach = 1f;
                    if (it.kind == Orb) reach = 1.5f;
                    else if (it.kind == Coin && mut != null) reach = mut.CoinRad;

                    if (Mathf.Abs(it.s - ps) > 1.2f * reach) continue;
                    if (AngleBetween(it.theta, pth) > (it.halfArc + bodyArc) * reach) continue;
                    if (Mathf.Abs(it.height - (body + air)) > 1.1f * reach) continue;

                    if (it.kind == Orb) Run.I.CollectMut(it.mut);
                    else if (it.kind == Health) Run.I.Heal();
                    else Run.I.Collect();
                    it.pop = PopTime;
                    continue;
                }

                if (mut != null && mut.Ghost) continue;
                if (Mathf.Abs(it.s - ps) > it.depth * 0.5f + body) continue;
                if (AngleBetween(it.theta, pth) > it.halfArc + bodyArc) continue;
                if (air > it.height) continue;                 // перепрыгнул сверху
                if (!alive) continue;
                if (Run.I.Hit()) Return(it);
            }
        }
    }

    static float AngleBetween(float a, float b)
    {
        return Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, b * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
    }

    // --------------------------------------------------------------- расстановка

    void Load(int c)
    {
        var list = Generate(c);
        for (int i = 0; i < list.Count; i++) Spawn(list[i]);
        _chunks[c] = list;
    }

    void Unload(int c)
    {
        var list = _chunks[c];
        for (int i = 0; i < list.Count; i++) if (list[i].go != null) Return(list[i]);
        _chunks.Remove(c);
    }

    List<Item> Generate(int c)
    {
        var list = Obstacles(c);

        var nearObs = new List<Item>(list);
        nearObs.AddRange(Obstacles(c - 1));
        nearObs.AddRange(Obstacles(c + 1));

        var coins = Coins(c, nearObs);
        list.AddRange(coins);

        // Всё, что уже поставлено, — чтобы пикап не попал внутрь следа монет.
        var taken = new List<Item>(nearObs);
        taken.AddRange(coins);

        bool hurt = Run.I != null && Run.I.Phase == RunState.Running && Run.I.Hp <= 1;
        if (c > 2 && c % 6 == 1) AddPickup(list, taken, nearObs, c, Orb);
        if (c > 4 && (c % 9 == 4 || (hurt && c % 3 == 1))) AddPickup(list, taken, nearObs, c, Health);

        return list;
    }

    void AddPickup(List<Item> list, List<Item> taken, List<Item> obstacles, int c, int kind)
    {
        var rnd = new System.Random(c * 131 + kind * 7919 + seed * 13);
        int mut = kind == Orb ? rnd.Next(System.Enum.GetValues(typeof(Mut)).Length) : 0;

        for (int attempt = 0; attempt < 6; attempt++)
        {
            float s = c * chunkLength + 4f + (float)rnd.NextDouble() * (chunkLength - 8f);
            float theta = PickupLanes[rnd.Next(PickupLanes.Length)];

            if (Blocked(s, theta, obstacles)) continue;
            if (Near(s, taken)) continue;

            var it = new Item();
            it.kind = kind;
            it.mut = mut;
            it.s = s;
            it.theta = theta;
            it.halfArc = 0.26f;
            it.height = 0.95f;
            it.depth = 0.8f;
            list.Add(it);
            taken.Add(it);
            return;
        }
    }

    List<Item> Obstacles(int c)
    {
        var list = new List<Item>();
        if (c < 2) return list;                       // передышка на старте

        var rnd = new System.Random(c * 977 + seed * 31);
        // Плотность растёт на первых 1.5 км, дальше держится.
        float diff = Mathf.Clamp01((c - 2) * chunkLength / 1500f);
        int n = (float)rnd.NextDouble() < 0.12f + 0.55f * diff ? 2 : 1;
        float lastS = -999f;

        for (int i = 0; i < n; i++)
        {
            var it = new Item();
            it.kind = Obstacle;
            it.s = c * chunkLength + 3f + (float)rnd.NextDouble() * (chunkLength - 6f);
            if (it.s - lastS < 12f) it.s = lastS + 12f;
            if (it.s > (c + 1) * chunkLength - 1.5f) continue;

            it.theta = Lanes[rnd.Next(Lanes.Length)] + Mathf.Lerp(-0.09f, 0.09f, (float)rnd.NextDouble());
            it.halfArc = Mathf.Lerp(0.16f, 0.34f, (float)rnd.NextDouble());
            it.height = Mathf.Lerp(1.0f, 1.45f, (float)rnd.NextDouble());
            it.depth = Mathf.Lerp(1.2f, 1.8f, (float)rnd.NextDouble());
            lastS = it.s;
            list.Add(it);
        }
        return list;
    }

    List<Item> Coins(int c, List<Item> obstacles)
    {
        var list = new List<Item>();
        var rnd = new System.Random(c * 733 + seed * 17 + 991);
        int trails = 1 + rnd.Next(2);

        for (int t = 0; t < trails; t++)
        {
            float lane = PickupLanes[rnd.Next(PickupLanes.Length)];
            float start = c * chunkLength + (float)rnd.NextDouble() * chunkLength * 0.6f;
            int count = 4 + rnd.Next(4);

            var trail = new List<Item>();
            bool clear = true;
            for (int i = 0; i < count; i++)
            {
                var it = new Item();
                it.kind = Coin;
                it.s = start + i * 2.2f;
                it.theta = lane;
                it.halfArc = 0.22f;
                it.height = 0.95f;
                it.depth = 0.8f;
                if (Blocked(it.s, lane, obstacles)) { clear = false; break; }
                trail.Add(it);
            }
            if (clear) list.AddRange(trail);
        }
        return list;
    }

    bool Blocked(float s, float theta, List<Item> obstacles)
    {
        for (int i = 0; i < obstacles.Count; i++)
        {
            var o = obstacles[i];
            if (Mathf.Abs(o.s - s) > coinClearance + o.depth * 0.5f) continue;
            if (Mathf.Abs(o.theta - theta) < o.halfArc + 0.30f) return true;
        }
        return false;
    }

    bool Near(float s, List<Item> items)
    {
        for (int i = 0; i < items.Count; i++)
            if (Mathf.Abs(items[i].s - s) < pickupGap) return true;
        return false;
    }

    void Spawn(Item it)
    {
        if (_mpb == null) _mpb = new MaterialPropertyBlock();   // не полагаемся на порядок инициализации

        var go = Take(it.kind);
        it.go = go;              // ставим сразу: ранний return его уже не потеряет
        go.SetActive(true);
        it.baseScale = go.transform.localScale;

        float R = _track.Radius;
        if (it.kind == Obstacle)
        {
            // Меш-накладка живёт в координатах трубы, значит объект ставится на ось.
            Quaternion rot = _track.Rot(it.s);
            go.GetComponent<MeshFilter>().sharedMesh = Patch(it.halfArc, it.height, it.depth);
            go.transform.position = _track.Pos(it.s) + rot * (Vector3.up * R);
            go.transform.rotation = rot * Quaternion.AngleAxis(it.theta * Mathf.Rad2Deg, Vector3.forward);
            go.transform.localScale = Vector3.one;
            return;
        }

        Vector3 normal = _track.Normal(it.s, it.theta);
        Vector3 fwd = _track.Rot(it.s) * Vector3.forward;
        go.transform.position = _track.Surface(it.s, it.theta) + normal * it.height;
        go.transform.rotation = Quaternion.LookRotation(normal, fwd);

        if (it.kind == Orb)
        {
            Color c = Mutation.Tint[it.mut];
            _mpb.SetColor("_BaseColor", c);
            _mpb.SetColor("_EmissionColor", c * 2.2f);
            go.GetComponent<MeshRenderer>().SetPropertyBlock(_mpb);
        }
    }

    GameObject Take(int kind)
    {
        var pool = Pool(kind);
        return pool.Count > 0 ? pool.Pop() : Make(kind);
    }

    Stack<GameObject> Pool(int kind)
    {
        if (kind == Orb) return _orbs;
        if (kind == Health) return _health;
        if (kind == Coin) return _coins;
        return _obstacles;
    }

    void Return(Item it)
    {
        it.go.transform.localScale = it.baseScale;   // откатываем всплеск подбора
        it.go.SetActive(false);
        Pool(it.kind).Push(it.go);
        it.go = null;
    }

    GameObject Make(int kind)
    {
        if (kind == Obstacle)
        {
            var g = new GameObject("Obstacle");
            g.transform.SetParent(transform, false);
            g.AddComponent<MeshFilter>();
            g.AddComponent<MeshRenderer>().sharedMaterial = obstacleMat;
            return g;
        }

        if (kind == Health)
        {
            var root = new GameObject("Health");
            root.transform.SetParent(transform, false);
            for (int i = 0; i < 2; i++)
            {
                var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var col = bar.GetComponent<Collider>();
                if (col) Destroy(col);
                bar.transform.SetParent(root.transform, false);
                // Крест лежит в плоскости XZ и смотрит вдоль трассы — ровно
                // так же, как стоят монеты.
                bar.transform.localScale = i == 0
                    ? new Vector3(0.78f, 0.24f, 0.24f)
                    : new Vector3(0.24f, 0.24f, 0.78f);
                bar.GetComponent<MeshRenderer>().sharedMaterial = healthMat;
            }
            return root;
        }

        var p = GameObject.CreatePrimitive(kind == Orb ? PrimitiveType.Sphere : PrimitiveType.Cylinder);
        p.name = kind == Orb ? "Orb" : "Coin";
        var pc = p.GetComponent<Collider>();
        if (pc) Destroy(pc);
        p.transform.SetParent(transform, false);
        p.transform.localScale = kind == Orb
            ? new Vector3(0.7f, 0.7f, 0.7f)
            : new Vector3(0.9f, 0.05f, 0.9f);
        p.GetComponent<MeshRenderer>().sharedMaterial = kind == Orb ? orbMat : coinMat;
        return p;
    }

    // ------------------------------------------------------------ меш препятствия

    /// Кусок, вырезанный из трубы: он повторяет кривизну жёлоба, поэтому сидит
    /// вровень с поверхностью, а его силуэт и есть хитбокс.
    Mesh Patch(float halfArc, float height, float depth)
    {
        float R = _track.Radius;
        string key = Mathf.RoundToInt(R * 100f) + ":" + Mathf.RoundToInt(halfArc * 1000f) + ":"
                   + Mathf.RoundToInt(height * 100f) + ":" + Mathf.RoundToInt(depth * 100f);
        Mesh cached;
        if (_patches.TryGetValue(key, out cached)) return cached;

        const int N = 8;
        const float Sink = 0.08f;              // утоплено в трассу, чтобы не было шва
        float outer = R + Sink;
        float inner = R - height;

        var verts = new List<Vector3>(64);
        var tris = new List<int>(96);
        var center = new Vector3(0f, -(outer + inner) * 0.5f, 0f);

        // i: 0..1 вдоль s, j: 0..N по дуге, k: 0 = наружу, 1 = внутрь
        for (int i = 0; i < 2; i++)
        {
            float z = (i - 0.5f) * depth;
            for (int j = 0; j <= N; j++)
            {
                float th = Mathf.Lerp(-halfArc, halfArc, j / (float)N);
                for (int k = 0; k < 2; k++)
                {
                    float rad = k == 0 ? outer : inner;
                    verts.Add(new Vector3(Mathf.Sin(th) * rad, -Mathf.Cos(th) * rad, z));
                }
            }
        }
        int V(int i, int j, int k) { return ((i * (N + 1)) + j) * 2 + k; }

        for (int j = 0; j < N; j++)
        {
            Quad(verts, tris, center, V(0, j, 1), V(1, j, 1), V(1, j + 1, 1), V(0, j + 1, 1));
            Quad(verts, tris, center, V(0, j, 0), V(1, j, 0), V(1, j + 1, 0), V(0, j + 1, 0));
        }
        Quad(verts, tris, center, V(0, 0, 0), V(1, 0, 0), V(1, 0, 1), V(0, 0, 1));
        Quad(verts, tris, center, V(0, N, 0), V(1, N, 0), V(1, N, 1), V(0, N, 1));
        Quad(verts, tris, center, V(0, 0, 0), V(0, N, 0), V(0, N, 1), V(0, 0, 1));
        Quad(verts, tris, center, V(1, 0, 0), V(1, N, 0), V(1, N, 1), V(1, 0, 1));

        var m = new Mesh();
        m.name = "Patch";
        m.SetVertices(verts);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        _patches[key] = m;
        return m;
    }

    /// Добавляет четырёхугольник по индексам вершин, разворачивая обход, если
    /// он смотрит внутрь блока.
    static void Quad(List<Vector3> v, List<int> t, Vector3 center, int a, int b, int c, int d)
    {
        Vector3 n = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
        if (Vector3.Dot(n, (v[a] + v[b] + v[c] + v[d]) * 0.25f - center) < 0f)
        {
            int tmp = b; b = d; d = tmp;
        }
        t.Add(a); t.Add(b); t.Add(c);
        t.Add(a); t.Add(c); t.Add(d);
    }
}
