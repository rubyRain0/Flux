using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Скользит по жёлобу. Вперёд тянет уклон; боковое движение — маятник на дуге:
/// руль добавляет момент, гравитация отбирает, а высота оплачивается скоростью
/// движения вперёд.
/// </summary>
public class PlayerMotor : MonoBehaviour
{
    public Track track;
    public Transform visual;

    [Header("Вперёд")]
    public float startSpeed = 24f;
    public float minSpeed = 20f;
    public float maxSpeed = 54f;
    public float drag = 0.04f;
    public float slopeGain = 1f;

    [Header("Дуга")]
    public float gravityGain = 1f;
    public float steerAccel = 7f;
    public float steerRefSpeed = 26f;
    public float arcDamping = 0.8f;

    [Header("Энергия")]
    public float climbCost = 0.7f;
    public float descendGain = 1.12f;
    [Tooltip("Сколько скорости в секунду отнимает езда высоко по стене, у самой кромки.")]
    public float wallDrain = 3f;

    [Header("Прыжок")]
    public float jumpSpeed = 7.5f;
    [Tooltip("Множитель гравитации в воздухе. Больше — короче и резче прыжок.")]
    public float airGravity = 1.6f;
    [Tooltip("Доля управления в воздухе относительно наземного.")]
    public float airSteer = 0.55f;
    public float bodyRadius = 0.6f;

    [Header("Удар и визуал состояний")]
    [Tooltip("Подменяется на персонаже, пока активен GHOST.")]
    public Material ghostMat;
    [Tooltip("Цвет тела при инверсии управления — игрок должен видеть подмену.")]
    public Color invertTint = new Color(1f, 0.16f, 0.12f);
    public Color invertGlow = new Color(2.2f, 0.12f, 0.08f);
    [Tooltip("Доля скорости, остающаяся после столкновения с препятствием.")]
    public float hitSpeedLoss = 0.55f;

    [Tooltip("Служебная строка со скоростью и углом. Выключена в игре, включается для отладки.")]
    public bool debugOverlay = false;

    public float S { get { return _s; } }
    public float Theta { get { return _theta; } }
    public float Speed { get { return _v; } }
    public float Air { get { return _airH; } }

    float _s, _theta, _omega, _v, _airH, _airV, _spin;
    Renderer[] _renderers;
    Renderer _vis;
    Material _visMat;
    MaterialPropertyBlock _tint;
    int _visState = -1;

    void Start()
    {
        if (!track) track = Track.Main;
        _renderers = GetComponentsInChildren<Renderer>();
        _tint = new MaterialPropertyBlock();
        if (visual)
        {
            _vis = visual.GetComponent<Renderer>();
            if (_vis) _visMat = _vis.sharedMaterial;
        }
        ResetRun();
    }

    public void ResetRun()
    {
        _s = 0f; _theta = 0f; _omega = 0f; _v = startSpeed; _airH = 0f; _airV = 0f;
        if (_renderers != null)
            for (int i = 0; i < _renderers.Length; i++) _renderers[i].enabled = true;
    }

    void OnDisable()
    {
        if (_renderers != null)
            for (int i = 0; i < _renderers.Length; i++) _renderers[i].enabled = true;
    }

    void Update()
    {
        if (!track) { track = Track.Main; if (!track) return; }
        if (Run.I != null && Run.I.Phase != RunState.Running) return;
        float dt = Mathf.Min(Time.deltaTime, 0.05f);

        var kb = Keyboard.current;
        float axis = 0f;
        if (kb != null)
        {
            axis = (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1f : 0f)
                 - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1f : 0f);
            bool jumpLocked = Mutation.I != null && Mutation.I.JumpLocked;
            if (kb.spaceKey.wasPressedThisFrame && _airH <= 0f && !jumpLocked)
            {
                _airV = jumpSpeed;
                _airH = 0.0001f;
            }
        }

        var mut = Mutation.I;
        Quaternion rot = track.Rot(_s);
        Vector3 fwd = rot * Vector3.forward;
        Vector3 up = rot * Vector3.up;
        Vector3 right = rot * Vector3.right;
        Vector3 g = Physics.gravity * (mut != null ? mut.GravityScale : 1f);
        float gMag = g.magnitude;

        float steerSign = mut != null ? mut.SteerSign : 1f;
        float speedMul = mut != null ? mut.WorldSpeed : 1f;
        bool closed = track.Closed;
        float R = track.Radius;

        // Вперёд: тянет уклон, сверху ограничивает сопротивление. Здесь берём
        // обычную гравитацию, иначе при перевёрнутом небе игрока потащило бы
        // вверх по склону.
        _v += (Vector3.Dot(Physics.gravity, fwd) * slopeGain - drag * _v) * dt;

        // Дуга: маятник. Руль — момент, чья сила растёт как скорость в квадрате
        // и гаснет у кромки, поэтому оседлать край стены нельзя.
        bool airborne = _airH > 0f;
        float limit = track.HalfArc * 0.98f;
        Vector3 arcTangent = up * Mathf.Sin(_theta) + right * Mathf.Cos(_theta);
        float pendulum = Vector3.Dot(g, arcTangent) / R * gravityGain;

        float authority = Mathf.Clamp01(_v / steerRefSpeed);
        authority = authority * authority * (airborne ? airSteer : 1f);
        if (!closed) authority *= 1f - Mathf.InverseLerp(limit * 0.72f, limit, Mathf.Abs(_theta));

        _omega += (pendulum + axis * steerSign * steerAccel * authority
                   - _omega * arcDamping) * dt;

        float prev = _theta;
        _theta += _omega * dt;
        if (!closed && Mathf.Abs(_theta) > limit)
        {
            // зацепил кромку: сбрасывает вниз, и дальше сползаешь
            _theta = Mathf.Sign(_theta) * limit;
            if (_omega * Mathf.Sign(_theta) > 0f) _omega = -0.8f * Mathf.Sign(_theta);
        }

        // Подъём превращает скорость в высоту; спуск возвращает её с процентами.
        float climb = R * (Mathf.Cos(prev) - Mathf.Cos(_theta));
        float cost = gMag * Mathf.Abs(climb) / Mathf.Max(_v, 1f) * climbCost;
        _v += climb > 0f ? -cost : cost * descendGain;

        // Езда высоко съедает скорость: стена — место, куда заезжают, а не где
        // живут. У замкнутой трубы кромки нет, поэтому налог снимается.
        if (!closed)
        {
            float wall = Mathf.Abs(_theta) / track.HalfArc;
            _v -= wall * wall * wallDrain * dt;
        }

        _v = Mathf.Clamp(_v, minSpeed, maxSpeed);
        _s += _v * speedMul * dt;

        if (airborne)
        {
            _airV -= gMag * airGravity * dt;
            _airH += _airV * dt;
            if (_airH <= 0f) { _airH = 0f; _airV = 0f; }
        }

        Vector3 normal = track.Normal(_s, _theta);
        transform.position = track.Surface(_s, _theta) + normal * (bodyRadius + _airH);
        Vector3 flat = fwd - Vector3.Dot(fwd, normal) * normal;
        transform.rotation = Quaternion.LookRotation(
            flat.sqrMagnitude > 1e-4f ? flat : right, normal);

        if (visual)
        {
            _spin += _v * dt / bodyRadius * Mathf.Rad2Deg * 0.5f;
            visual.localRotation = Quaternion.Euler(_spin, 0f, 0f);
        }

        if (mut != null && _vis != null && ghostMat != null)
        {
            // GHOST и INVERT складываются, поэтому оба считаются из одного
            // состояния: красное просвечивающее тело, когда включены вместе.
            bool ghosting = mut.Ghost;
            bool inverted = mut.SteerSign < 0f;
            int state = (ghosting ? 1 : 0) | (inverted ? 2 : 0);

            if (state != _visState)
            {
                _visState = state;
                _vis.sharedMaterial = ghosting ? ghostMat : _visMat;

                if (inverted)
                {
                    Color body = invertTint;
                    body.a = ghosting ? 0.45f : 1f;
                    _tint.SetColor("_BaseColor", body);
                    _tint.SetColor("_EmissionColor", invertGlow);
                    _vis.SetPropertyBlock(_tint);
                }
                else _vis.SetPropertyBlock(null);
            }
        }

        if (_renderers != null && Run.I != null)
        {
            bool blink = Run.I.Invulnerable && Mathf.Repeat(Time.time * 10f, 1f) < 0.45f;
            for (int i = 0; i < _renderers.Length; i++) _renderers[i].enabled = !blink;
        }
    }

    /// Столкновение стоит не только здоровья, но и скорости.
    public void OnHit()
    {
        _v *= hitSpeedLoss;
    }

    void OnGUI()
    {
        if (!debugOverlay || !track) return;
        GUI.Label(new Rect(12, 40, 600, 24),
            string.Format("v {0:0.0} m/s    theta {1:0}°    h {2:0.0} m    air {3:0.00}",
                _v, _theta * Mathf.Rad2Deg, track.radius * (1f - Mathf.Cos(_theta)), _airH));
    }
}
