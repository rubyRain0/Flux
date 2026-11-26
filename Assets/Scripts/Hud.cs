using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Сама собирает канвас в рантайме и рисует весь интерфейс по данным Run:
/// счёт и таймер сверху, HP слева, монеты справа, активные мутации под ними,
/// карточка по центру для меню / паузы / конца забега и полноэкранный фейд.
/// </summary>
public class Hud : MonoBehaviour
{
    Run _run;
    Text _score, _time, _hp, _coins, _mut, _banner, _card, _inv;
    Image _flashImg, _fadeImg;

    float _flash, _bannerLife, _fade = 1f;
    int _shapeSeen, _bannerSeen;
    RunState _phase = (RunState)(-1);   // невозможное значение: гарантирует первую отрисовку

    void Start()
    {
        _run = Run.I;
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var canvasGo = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler));
        canvasGo.transform.SetParent(transform, false);
        canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        Transform root = canvasGo.transform;

        // Лежит под текстом и прикрывает кадр, в котором пересобирается жёлоб.
        _flashImg = MakeImage(root, "Flash", new Color(0.75f, 0.92f, 1f, 0f));

        _score  = Make(root, font, 64, TextAnchor.UpperCenter, new Vector2(0f, -26f), new Vector2(0.5f, 1f), Color.white);
        _time   = Make(root, font, 26, TextAnchor.UpperCenter, new Vector2(0f, -100f), new Vector2(0.5f, 1f), new Color(0.55f, 0.72f, 1f));
        _hp     = Make(root, font, 30, TextAnchor.UpperLeft, new Vector2(44f, -28f), new Vector2(0f, 1f), new Color(1f, 0.42f, 0.55f));
        _coins  = Make(root, font, 30, TextAnchor.UpperRight, new Vector2(-44f, -28f), new Vector2(1f, 1f), new Color(1f, 0.85f, 0.3f));
        _mut    = Make(root, font, 24, TextAnchor.UpperLeft, new Vector2(44f, -76f), new Vector2(0f, 1f), Color.white);
        _banner = Make(root, font, 52, TextAnchor.MiddleCenter, new Vector2(0f, 150f), new Vector2(0.5f, 0.5f), Color.white);
        _card   = Make(root, font, 44, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(0.5f, 0.5f), Color.white);
        _card.rectTransform.sizeDelta = new Vector2(1100f, 560f);

        _inv = Make(root, font, 34, TextAnchor.LowerCenter, new Vector2(0f, 92f), new Vector2(0.5f, 0f), new Color(1f, 0.3f, 0.3f));
        _inv.text = "ИНВЕРСИЯ";
        _inv.gameObject.SetActive(false);

        _fadeImg = MakeImage(root, "Fade", new Color(0f, 0f, 0f, 1f));
    }

    static Image MakeImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var img = go.GetComponent<Image>();
        img.raycastTarget = false;
        img.color = color;
        return img;
    }

    static Text Make(Transform parent, Font font, int size, TextAnchor align, Vector2 pos, Vector2 anchor, Color color)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.font = font;
        t.fontSize = size;
        t.alignment = align;
        t.color = color;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(900f, 200f);
        return t;
    }

    static string Clock(float t)
    {
        return string.Format("{0:00}:{1:00}", (int)(t / 60f), (int)(t % 60f));
    }

    void Update()
    {
        if (!_run) _run = Run.I;
        if (!_run) return;

        float dt = Time.unscaledDeltaTime;
        bool live = _run.Phase == RunState.Running;
        float fxDt = live ? dt : 0f;                                  // эффекты живут только с забегом
        float uiDt = _run.Phase == RunState.Dead ? 0f : dt;           // на экране смерти всё стоит
        var mut = Mutation.I;

        if (_run.Phase != _phase) OnPhase(_run.Phase);

        _score.text = _run.Score.ToString();
        _time.text = Clock(_run.Elapsed);
        _hp.text = "ЗДОРОВЬЕ " + Mathf.Max(0, _run.Hp);
        _coins.text = "МОНЕТЫ " + _run.Coins;

        PaintMutations(mut);
        PaintBanner(mut, fxDt);
        PaintFlash(mut, fxDt);

        _fade = Mathf.Max(0f, _fade - uiDt * 2.2f);
        var f = _fadeImg.color;
        f.a = Mathf.Clamp01(_fade);
        _fadeImg.color = f;

        bool inverted = _run.Phase == RunState.Running && mut != null && mut.SteerSign < 0f;
        if (_inv.gameObject.activeSelf != inverted) _inv.gameObject.SetActive(inverted);
        if (inverted)
        {
            var ic = _inv.color;
            ic.a = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 8f);
            _inv.color = ic;
        }
    }

    void OnPhase(RunState phase)
    {
        _phase = phase;
        _card.gameObject.SetActive(phase != RunState.Running);

        switch (phase)
        {
            case RunState.Menu:
                _card.text = string.Format("FLUX\n\nРЕКОРД  {0}\n\nНАЖМИ ЛЮБУЮ КЛАВИШУ\n\nA / D  движение      ПРОБЕЛ  прыжок",
                                           _run.Best);
                break;
            case RunState.Paused:
                _card.text = "ПАУЗА\n\nESC  продолжить";
                break;
            case RunState.Dead:
                _flash = 0f;
                _bannerLife = 0f;
                _card.text = string.Format("ЗАБЕГ ОКОНЧЕН\n\nВРЕМЯ   {0}\nМОНЕТЫ  {1}\nСЧЁТ    {2}\nРЕКОРД  {3}\n\nНАЖМИ ЛЮБУЮ КЛАВИШУ",
                                           Clock(_run.Elapsed), _run.Coins, _run.Score, _run.Best);
                break;
            case RunState.Running:
                _fade = 1f;
                _bannerLife = 0f;
                break;
        }
    }

    void PaintMutations(Mutation mut)
    {
        string m = "";
        if (mut != null)
        {
            for (int i = 0; i < mut.Count; i++)
            {
                int k = (int)mut.KindAt(i);
                m += "<color=#" + ColorUtility.ToHtmlStringRGB(Mutation.Tint[k]) + ">"
                   + Mutation.Name[k] + "  " + Mathf.CeilToInt(mut.TimeAt(i)) + "с</color>\n";
            }
        }
        _mut.text = m;
    }

    void PaintBanner(Mutation mut, float dt)
    {
        if (mut != null && mut.AddVersion != _bannerSeen)
        {
            _bannerSeen = mut.AddVersion;
            int k = (int)mut.LastAdded;
            _banner.text = Mutation.Name[k] + "   +" + Mathf.RoundToInt(mut.LastDuration) + " с";
            _banner.color = Mutation.Tint[k];
            _bannerLife = 1f;
        }

        _bannerLife = Mathf.Max(0f, _bannerLife - dt * 0.8f);
        var c = _banner.color;
        c.a = Mathf.Clamp01(_bannerLife);
        _banner.color = c;
    }

    void PaintFlash(Mutation mut, float dt)
    {
        if (mut != null && mut.ShapeVersion != _shapeSeen)
        {
            _shapeSeen = mut.ShapeVersion;
            _flash = 1f;
        }
        _flash = Mathf.Max(0f, _flash - dt * 3.2f);
        var fc = _flashImg.color;
        fc.a = _flash * _flash * 0.45f;
        _flashImg.color = fc;
    }
}
