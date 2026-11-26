using UnityEngine;

/// <summary>
/// Трасса. Одна гладкая аналитическая кривая: направление — сумма синусов,
/// осевая линия интегрируется по метру в массив узлов. И построение меша, и
/// физика читают один и тот же массив, поэтому видно всегда ровно то,
/// по чему едешь.
/// </summary>
public class Track : MonoBehaviour
{
    public static Track Main;

    [Header("Сечение")]
    public float radius = 6f;
    public float halfArcDeg = 95f;

    [Header("Кривая")]
    [Tooltip("Базовый угол спуска — нижняя граница «саночного» ощущения.")]
    public float slopeDeg = 9f;
    public float slopeWaveDeg = 4f;
    public float slopeWaveFreq = 0.045f;
    [Tooltip("Дополнительная крутизна в самой низкой точке провала: рывок свободного падения.")]
    public float dropDeg = 20f;
    public float dropFreq = 0.019f;
    public float drop2Deg = 9f;
    public float drop2Freq = 0.043f;
    public float turnDeg = 26f;
    public float turnFreq = 0.028f;
    public float turn2Deg = 11f;
    public float turn2Freq = 0.073f;
    public int seed = 7;
    public Vector3 start = Vector3.zero;

    /// Половина видимого сектора. Мутации могут замкнуть жёлоб в полную трубу.
    public float HalfArc
    {
        get { return Closed ? Mathf.PI : halfArcDeg * Mathf.Deg2Rad; }
    }

    /// Истина, пока жёлоб замкнут в полный круг.
    public bool Closed
    {
        get { return Mutation.I != null && Mutation.I.Closed; }
    }

    /// Текущий радиус сечения — мутации могут сжать трубу.
    public float Radius
    {
        get { return radius * (Mutation.I != null ? Mutation.I.Radius : 1f); }
    }

    const float Step = 1f;
    Vector3[] _knots = new Vector3[1024];
    int _count = 1;
    float _p1, _p2, _p3, _p4, _p5;

    void Awake()
    {
        Main = this;
        Reset();
    }

    public void Reset()
    {
        var rnd = new System.Random(seed);
        _p1 = (float)rnd.NextDouble() * 6.2832f;
        _p2 = (float)rnd.NextDouble() * 6.2832f;
        _p3 = (float)rnd.NextDouble() * 6.2832f;
        _p4 = (float)rnd.NextDouble() * 6.2832f;
        _p5 = (float)rnd.NextDouble() * 6.2832f;
        _knots[0] = start;
        _count = 1;
    }

    public Vector3 Forward(float s)
    {
        // Повороты и провалы плавно вводятся на первом отрезке, чтобы любой сид
        // открывался спокойной прямой, а не случайной шпилькой.
        float ease = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(s / 150f));

        float yaw = (Mathf.Sin(s * turnFreq + _p1) * turnDeg
                   + Mathf.Sin(s * turn2Freq + _p2) * turn2Deg) * ease;
        float pitch = slopeDeg
                    + Mathf.Sin(s * slopeWaveFreq + _p3) * slopeWaveDeg
                    + (dropDeg * Pulse(s * dropFreq + _p4)
                     + drop2Deg * Pulse(s * drop2Freq + _p5)) * ease;
        return Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;
    }

    /// Выдаёт забегу совершенно новую трассу. Вызывающий обязан также обновить
    /// всё, что уже было построено по старой.
    public void Reseed(int newSeed)
    {
        seed = newSeed;
        Reset();
    }

    /// Редкое резкое углубление. Высокая степень положительной полуволны даёт
    /// долгие мягкие участки, разбитые внезапным обрывом.
    public static float Pulse(float phase)
    {
        float w = Mathf.Sin(phase);
        if (w <= 0f) return 0f;
        w *= w;
        return w * w * w;
    }

    public Quaternion Rot(float s)
    {
        return Quaternion.LookRotation(Forward(s), Vector3.up);
    }

    public Vector3 Pos(float s)
    {
        if (s <= 0f) return _knots[0] + Forward(0f) * s;
        int i = (int)(s / Step);
        Ensure(i + 2);
        return Vector3.LerpUnclamped(_knots[i], _knots[i + 1], s / Step - i);
    }

    void Ensure(int need)
    {
        if (need <= _count) return;
        if (need > _knots.Length) System.Array.Resize(ref _knots, Mathf.NextPowerOfTwo(need));
        for (int i = _count; i < need; i++)
            _knots[i] = _knots[i - 1] + Forward((i - 1) * Step) * Step;
        _count = need;
    }

    /// Точка на поверхности жёлоба. theta = 0 — дно, «+» — вправо.
    public Vector3 Surface(float s, float theta)
    {
        float r = Radius;
        return Pos(s) + Rot(s) * new Vector3(Mathf.Sin(theta) * r,
                                            (1f - Mathf.Cos(theta)) * r, 0f);
    }

    /// Нормаль поверхности, направленная внутрь жёлоба (на дне — вверх).
    public Vector3 Normal(float s, float theta)
    {
        return Rot(s) * new Vector3(-Mathf.Sin(theta), Mathf.Cos(theta), 0f);
    }
}
