using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Камера сопровождения едет по трассе, а не по игроку, поэтому горизонт
/// остаётся ровным, пока персонаж лезет по стенам. Боком она повторяет лишь
/// часть угла дуги — достаточно, чтобы читать происходящее, но не настолько,
/// чтобы путать.
/// </summary>
public class CameraRig : MonoBehaviour
{
    public static CameraRig I;

    public PlayerMotor target;
    public float back = 9f;
    public float height = 4.5f;
    public float lateral = 0.55f;
    public float lookAhead = 14f;
    public float lookHeight = 1.5f;
    public float followLag = 0.12f;
    public float baseFov = 58f;
    public float fovPerSpeed = 0.45f;
    [Tooltip("Насколько камера поднимается вместе с игроком. Держит его в кадре на стене и на потолке.")]
    public float heightFollow = 0.55f;
    [Tooltip("Global Volume, на котором висит виньетка скорости.")]
    public Volume volume;
    [Tooltip("Прибавка виньетки на максимальной скорости.")]
    public float vignetteSpeed = 0.16f;
    [Tooltip("На крутых спусках туман поджимается, чтобы дальняя трасса не читалась длинной лентой.")]
    public float fogFlat = 250f;
    public float fogSteep = 175f;

    Track _track;
    Camera _cam;
    Vector3 _camPos, _vel;
    float _shake;
    bool _snap, _init;
    Vignette _vignette;
    float _vignetteBase = 0.2f;

    void Awake() { I = this; }

    void Start()
    {
        if (volume != null && volume.profile != null)
        {
            Vignette v;
            if (volume.profile.TryGet(out v))
            {
                _vignette = v;
                _vignetteBase = v.intensity.value;
            }
        }
    }

    public void Shake(float amount) { _shake = Mathf.Max(_shake, amount); }
    public void Snap() { _snap = true; }

    void LateUpdate()
    {
        if (!target) { target = FindFirstObjectByType<PlayerMotor>(); if (!target) return; }
        if (!_track) _track = Track.Main;
        if (!_track) return;

        float s = target.S;
        float dt = Time.deltaTime;

        float R = _track.Radius;
        float side = Mathf.Sin(target.Theta) * R * lateral;
        float rise = R * (1f - Mathf.Cos(target.Theta)) * heightFollow;

        Vector3 anchor = _track.Pos(Mathf.Max(0f, s - back));
        Vector3 want = anchor + Vector3.up * (height + rise) + _track.Rot(s) * (Vector3.right * side);
        if (!_init) { _camPos = want; _init = true; }

        _camPos = Vector3.SmoothDamp(_camPos, want, ref _vel, followLag);
        if (_snap) { _camPos = want; _vel = Vector3.zero; _snap = false; }
        if (_track.Closed) _camPos = ClampInside(_camPos, Mathf.Max(0f, s - back));

        Vector3 look = _track.Pos(s + lookAhead) + Vector3.up * (lookHeight + rise);
        Quaternion wantRot = Quaternion.LookRotation(look - _camPos, Vector3.up);

        Vector3 offset = Vector3.zero;
        if (_shake > 0f)
        {
            _shake = Mathf.Max(0f, _shake - dt * 2.2f);
            offset = Random.insideUnitSphere * _shake * 0.35f;
        }

        transform.position = _camPos + offset;
        transform.rotation = Quaternion.Slerp(transform.rotation, wantRot, 1f - Mathf.Exp(-14f * dt));

        if (!_cam) _cam = GetComponent<Camera>();
        if (_cam)
            _cam.fieldOfView = Mathf.Lerp(_cam.fieldOfView, baseFov + target.Speed * fovPerSpeed,
                                          1f - Mathf.Exp(-3f * dt));

        if (_vignette != null)
            _vignette.intensity.value = Mathf.Lerp(_vignetteBase, _vignetteBase + vignetteSpeed,
                                                   Mathf.InverseLerp(22f, 44f, target.Speed));

        // На провале видно сотни метров трассы; поджимаем туман, чтобы даль
        // растворялась, а не читалась как чертёж маршрута.
        Vector3 ahead = _track.Rot(s) * Vector3.forward;
        RenderSettings.fogEndDistance = Mathf.Lerp(fogFlat, fogSteep,
                                                   Mathf.InverseLerp(0.2f, 0.7f, -ahead.y));
    }

    /// Удерживает камеру внутри замкнутого жёлоба. Без этого она уходит сквозь
    /// потолок на высокой стене и смотрит на трубу снаружи.
    Vector3 ClampInside(Vector3 p, float sCam)
    {
        float R = _track.Radius;
        Quaternion rot = _track.Rot(sCam);
        Vector3 axis = _track.Pos(sCam) + rot * (Vector3.up * R);
        Vector3 local = Quaternion.Inverse(rot) * (p - axis);

        float radial = Mathf.Sqrt(local.x * local.x + local.y * local.y);
        float limit = R - 1.3f;
        if (radial <= limit || radial < 0.0001f) return p;

        float k = limit / radial;
        local.x *= k;
        local.y *= k;
        return axis + rot * local;
    }
}
