using UnityEngine;
using UnityEngine.InputSystem;

public enum RunState { Menu, Running, Paused, Dead }

/// <summary>
/// Забег: счёт, здоровье, рекорд и текущий экран игры. Все остальные системы
/// спрашивают здесь, прежде чем что-то менять на экране.
/// </summary>
public class Run : MonoBehaviour
{
    public static Run I;

    public PlayerMotor player;
    public int maxHp = 3;
    public float invulnTime = 1.3f;
    [Tooltip("Каждый забег получает свежесгенерированную трассу, а не ту же самую.")]
    public bool varyEachRun = true;

    public RunState Phase { get; private set; }
    public int Hp { get; private set; }
    public int Coins { get; private set; }
    public float Elapsed { get; private set; }
    public int Best { get; private set; }
    public int Score { get { return Mathf.FloorToInt(Elapsed) + Coins * 10; } }
    public bool Invulnerable { get { return _inv > 0f; } }

    float _inv;

    void Awake()
    {
        I = this;
        Time.timeScale = 1f;
        Best = PlayerPrefs.GetInt("FluxBest", 0);
        Phase = RunState.Menu;
        Hp = maxHp;
        if (Mutation.I) Mutation.I.Clear();
        if (player) player.enabled = false;
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        switch (Phase)
        {
            case RunState.Menu:
                if (kb.anyKey.wasPressedThisFrame) StartRun();
                break;

            case RunState.Running:
                if (kb.escapeKey.wasPressedThisFrame) { Phase = RunState.Paused; Time.timeScale = 0f; break; }
                Elapsed += Time.deltaTime;
                if (_inv > 0f) _inv -= Time.deltaTime;
                break;

            case RunState.Paused:
                if (kb.escapeKey.wasPressedThisFrame) { Phase = RunState.Running; Time.timeScale = 1f; }
                break;

            case RunState.Dead:
                if (!kb.escapeKey.wasPressedThisFrame && kb.anyKey.wasPressedThisFrame) StartRun();
                break;
        }
    }

    public void StartRun()
    {
        Time.timeScale = 1f;
        Hp = maxHp;
        Coins = 0;
        Elapsed = 0f;
        _inv = 0f;

        if (Mutation.I) Mutation.I.Clear();
        if (varyEachRun) Shuffle();
        else if (Props.I) Props.I.ClearAll();

        if (player) { player.enabled = true; player.ResetRun(); }
        if (CameraRig.I) CameraRig.I.Snap();

        Phase = RunState.Running;
    }

    void Shuffle()
    {
        int sd = Random.Range(1, 999999);
        if (Track.Main) Track.Main.Reseed(sd);
        if (GrooveStream.I) GrooveStream.I.ClearAll();
        if (Props.I) Props.I.Reseed(sd);
    }

    public bool Hit()
    {
        if (Phase != RunState.Running || _inv > 0f) return false;
        Hp--;
        _inv = invulnTime;
        if (player) player.OnHit();
        if (CameraRig.I) CameraRig.I.Shake(0.6f);
        if (Hp <= 0) Die();
        return true;
    }

    public void Collect() { if (Phase == RunState.Running) Coins++; }

    public void Heal() { if (Phase == RunState.Running) Hp = Mathf.Min(Hp + 1, maxHp); }

    public void CollectMut(int kind)
    {
        if (Phase != RunState.Running || Mutation.I == null) return;
        Mutation.I.Add((Mut)kind, Random.Range(11f, 19f));
    }

    void Die()
    {
        Phase = RunState.Dead;
        if (Score > Best) { Best = Score; PlayerPrefs.SetInt("FluxBest", Best); PlayerPrefs.Save(); }
        if (player) player.enabled = false;
    }
}
