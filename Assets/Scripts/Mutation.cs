using System.Collections.Generic;
using UnityEngine;

public enum Mut { Tube, LowGravity, NoJump, Speed, Ghost, Magnet, Narrow, Invert }

/// <summary>
/// Временные правила забега. Одновременно живут до трёх; каждая сводится к
/// набору множителей, которые игрок, объекты и камера читают каждый кадр.
/// Мутации, меняющие форму, пересобирают жёлоб — поэтому применяются один раз
/// при переходе, а не покадрово.
/// </summary>
public class Mutation : MonoBehaviour
{
    public static Mutation I;

    public static readonly string[] Name =
    {
        "ТРУБА", "НИЗКАЯ G", "БЕЗ ПРЫЖКА", "СКОРОСТЬ x1.5",
        "ПРИЗРАК", "МАГНИТ", "СУЖЕНИЕ", "ИНВЕРСИЯ"
    };

    public static readonly Color[] Tint =
    {
        new Color(0.25f, 0.85f, 1.00f), new Color(0.45f, 0.85f, 1.00f),
        new Color(1.00f, 0.42f, 0.25f), new Color(1.00f, 0.88f, 0.30f),
        new Color(0.60f, 1.00f, 0.70f), new Color(1.00f, 0.82f, 0.40f),
        new Color(1.00f, 0.42f, 0.62f), new Color(1.00f, 0.36f, 0.48f)
    };

    class Slot { public Mut kind; public float time; }

    public int MaxActive = 3;

    // Живые множители — пересчитываются при любом изменении набора.
    public float GravityScale = 1f;
    public float WorldSpeed = 1f;
    public float SteerSign = 1f;
    public bool JumpLocked;
    public bool Ghost;
    public float CoinRad = 1f;
    public bool Closed;
    public float Radius = 1f;

    /// Увеличивается при каждой пересборке жёлоба, чтобы HUD мог мигнуть.
    public int ShapeVersion { get; private set; }

    /// Увеличивается при каждом подборе, чтобы HUD объявил мутацию.
    public int AddVersion { get; private set; }
    public Mut LastAdded { get; private set; }
    public float LastDuration { get; private set; }

    public int Count { get { return _slots.Count; } }
    public Mut KindAt(int i) { return _slots[i].kind; }
    public float TimeAt(int i) { return _slots[i].time; }

    readonly List<Slot> _slots = new List<Slot>();
    bool _closed, _dirtyShape;
    float _radius = 1f;

    void Awake()
    {
        I = this;
        _slots.Clear();
        Recompute();          // не доверяем значениям, сохранённым в редакторе
    }

    public void Clear()
    {
        _slots.Clear();
        Recompute();
    }

    public void Add(Mut kind, float duration)
    {
        LastAdded = kind;
        LastDuration = duration;
        AddVersion++;

        for (int i = 0; i < _slots.Count; i++)
            if (_slots[i].kind == kind) { _slots[i].time = duration; Recompute(); return; }

        if (_slots.Count >= MaxActive) _slots.RemoveAt(0);   // освобождает место самый старый

        var s = new Slot();
        s.kind = kind;
        s.time = duration;
        _slots.Add(s);
        Recompute();
    }

    void Update()
    {
        // Таймеры идут только во время забега: на экране Game Over всё замирает.
        if (Run.I != null && Run.I.Phase != RunState.Running) return;

        bool expired = false;
        for (int i = _slots.Count - 1; i >= 0; i--)
        {
            _slots[i].time -= Time.deltaTime;
            if (_slots[i].time <= 0f) { _slots.RemoveAt(i); expired = true; }
        }
        if (expired) Recompute();
    }

    void Recompute()
    {
        GravityScale = 1f;
        WorldSpeed = 1f;
        SteerSign = 1f;
        JumpLocked = false;
        Ghost = false;
        CoinRad = 1f;
        Closed = false;
        Radius = 1f;

        for (int i = 0; i < _slots.Count; i++)
        {
            switch (_slots[i].kind)
            {
                case Mut.Tube: Closed = true; break;
                case Mut.LowGravity: GravityScale = 0.55f; break;
                case Mut.NoJump: JumpLocked = true; break;
                case Mut.Speed: WorldSpeed = 1.5f; break;
                case Mut.Ghost: Ghost = true; break;
                case Mut.Magnet: CoinRad = 2.8f; break;
                case Mut.Narrow: Radius = 0.66f; break;
                case Mut.Invert: SteerSign = -1f; break;
            }
        }

        if (Closed != _closed || Mathf.Abs(Radius - _radius) >= 0.001f) _dirtyShape = true;
    }

    void LateUpdate()
    {
        if (!_dirtyShape) return;
        _dirtyShape = false;
        _closed = Closed;
        _radius = Radius;

        // Отложено намеренно: подбор может вызвать это изнутри цикла по
        // объектам, а выдёргивание чанков на ходу бросит исключение.
        if (GrooveStream.I) GrooveStream.I.ClearAll();
        if (Props.I) Props.I.ClearAll();

        ShapeVersion++;
        if (CameraRig.I) CameraRig.I.Shake(0.35f);
    }
}
