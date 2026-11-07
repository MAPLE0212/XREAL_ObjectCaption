using UnityEngine;
using UnityEngine.UI;

namespace XrealCaption
{
    /// <summary>
    /// 眼鏡側の表示（頭に固定、2 m 先）。
    /// ・中央：認識する範囲の目安の枠（四隅のかぎ）。認識すると明るくなる
    /// ・枠の右：キャプションのカード（物の名前＋説明）
    /// ・上：案内（撮影中の手順など）
    /// 光学シースルーなので黒は透明。既存のラベルアプリと同じく、白の半透明の背景に黒い文字で出す。
    /// </summary>
    public class GlassesView : MonoBehaviour
    {
        const float k_Distance = 2.0f;
        /// <summary>Canvas の 1 単位 = 0.5 mm（2 m 先で 1° ≒ 70 単位）。</summary>
        const float k_MetersPerUnit = 0.0005f;
        const float k_CornerLength = 90f;
        const float k_CornerThickness = 10f;
        const float k_CardWidth = 860f;
        const float k_CardGap = 50f;

        RectTransform m_Root;
        RectTransform m_Frame;
        readonly Image[] m_Corners = new Image[8];
        Image m_CardBg;
        Text m_Title;
        Text m_Body;
        Text m_Score;
        Image m_GuideBg;
        Text m_Guide;
        Image m_ToastBg;
        Text m_Toast;
        float m_ToastUntil;
        float m_FrameDeg;
        bool m_FrameActive;
        CanvasGroup m_CardGroup;
        float m_CardAlpha;
        bool m_CardVisible;

        public static readonly Color FrameIdle = new Color(1f, 1f, 1f, 0.45f);
        public static readonly Color FrameActive = new Color(0.55f, 1f, 0.65f, 0.95f);

        /// <summary>今キャプションに出している名前（テスト用）。出していなければ null。</summary>
        public string ShownTitle => m_CardVisible ? m_Title.text : null;
        public string ShownBody => m_CardVisible ? m_Body.text : null;
        public string GuideText => m_GuideBg.gameObject.activeSelf ? m_Guide.text : null;

        public static GlassesView Create(Camera camera)
        {
            var go = new GameObject("GlassesView");
            go.transform.SetParent(camera.transform, false);
            go.transform.localPosition = new Vector3(0, 0, k_Distance);
            go.transform.localRotation = Quaternion.identity;
            var view = go.AddComponent<GlassesView>();
            view.Build();
            return view;
        }

        void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 10;
            m_Root = (RectTransform)transform;
            m_Root.sizeDelta = new Vector2(4000, 4000);
            m_Root.localScale = Vector3.one * k_MetersPerUnit;

            // 認識範囲の枠：四隅に L 字のかぎ（2 本ずつ）
            m_Frame = UiFactory.CreateRect("Frame", m_Root);
            for (int i = 0; i < 4; i++)
            {
                float sx = (i & 1) == 0 ? -1f : 1f;
                float sy = (i & 2) == 0 ? -1f : 1f;
                var anchor = new Vector2((sx + 1f) * 0.5f, (sy + 1f) * 0.5f);
                for (int k = 0; k < 2; k++)
                {
                    var img = UiFactory.CreateImage($"Corner{i}_{k}", m_Frame, null, FrameIdle);
                    var rt = img.rectTransform;
                    rt.anchorMin = rt.anchorMax = anchor;
                    rt.pivot = anchor;
                    rt.anchoredPosition = Vector2.zero;
                    rt.sizeDelta = k == 0 ? new Vector2(k_CornerLength, k_CornerThickness) : new Vector2(k_CornerThickness, k_CornerLength);
                    m_Corners[i * 2 + k] = img;
                }
            }

            // キャプションのカード（枠の右）。高さは文字の量に合わせる
            m_CardBg = UiFactory.CreateImage("Card", m_Root, UiFactory.RoundedFill, new Color(1f, 1f, 1f, 0.55f), sliced: true);
            var card = m_CardBg.rectTransform;
            card.pivot = new Vector2(0f, 0.5f);
            m_CardGroup = m_CardBg.gameObject.AddComponent<CanvasGroup>();
            var layout = m_CardBg.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(44, 44, 34, 34);
            layout.spacing = 14;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = m_CardBg.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            m_Title = UiFactory.CreateText("Title", card, "", 64, Color.black, TextAnchor.UpperLeft);
            m_Title.fontStyle = FontStyle.Bold;
            m_Body = UiFactory.CreateText("Body", card, "", 46, Color.black, TextAnchor.UpperLeft);
            m_Body.lineSpacing = 1.1f;
            m_Score = UiFactory.CreateText("Score", card, "", 34, new Color(0f, 0f, 0f, 0.6f), TextAnchor.UpperRight);
            m_CardBg.gameObject.SetActive(false);

            // 案内（上）と通知（下）
            m_GuideBg = CreateBanner("Guide", 0.55f, out m_Guide, 56);
            m_ToastBg = CreateBanner("Toast", 0.7f, out m_Toast, 56);
            m_GuideBg.gameObject.SetActive(false);
            m_ToastBg.gameObject.SetActive(false);

            SetFrameSize(10f);
        }

        Image CreateBanner(string name, float alpha, out Text text, int fontSize)
        {
            var bg = UiFactory.CreateImage(name + "Bg", m_Root, UiFactory.RoundedFill, new Color(1f, 1f, 1f, alpha), sliced: true);
            var fitter = bg.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var layout = bg.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(36, 36, 20, 20);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            text = UiFactory.CreateText(name, bg.rectTransform, "", fontSize, Color.black);
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            return bg;
        }

        /// <summary>認識範囲の枠の大きさ（一辺の視角、度）。カメラの切り抜きの範囲に合わせる。</summary>
        public void SetFrameSize(float degrees)
        {
            m_FrameDeg = degrees;
            float side = 2f * k_Distance * Mathf.Tan(degrees * 0.5f * Mathf.Deg2Rad) / k_MetersPerUnit;
            m_Frame.sizeDelta = new Vector2(side, side);
            var card = m_CardBg.rectTransform;
            card.anchoredPosition = new Vector2(side * 0.5f + k_CardGap, 0f);
            card.sizeDelta = new Vector2(k_CardWidth, card.sizeDelta.y);
            m_GuideBg.rectTransform.anchoredPosition = new Vector2(0f, side * 0.5f + 110f);
            m_ToastBg.rectTransform.anchoredPosition = new Vector2(0f, -side * 0.5f - 110f);
        }

        public float FrameDegrees => m_FrameDeg;

        public void SetFrameActive(bool active)
        {
            if (m_FrameActive == active) return;
            m_FrameActive = active;
            foreach (var c in m_Corners) c.color = active ? FrameActive : FrameIdle;
        }

        /// <summary>キャプションを出す。title が null なら消す。</summary>
        public void ShowCaption(string title, string body, string score)
        {
            if (string.IsNullOrEmpty(title))
            {
                m_CardVisible = false;
                return;
            }
            bool changed = m_Title.text != title;
            m_Title.text = title;
            m_Body.text = body ?? "";
            m_Body.gameObject.SetActive(!string.IsNullOrEmpty(body));
            m_Score.text = score ?? "";
            m_Score.gameObject.SetActive(!string.IsNullOrEmpty(score));
            if (!m_CardVisible || changed) m_CardAlpha = 0f; // 新しく出すときはフェードイン
            m_CardVisible = true;
            m_CardBg.gameObject.SetActive(true);
        }

        public void SetGuide(string text)
        {
            bool on = !string.IsNullOrEmpty(text);
            m_GuideBg.gameObject.SetActive(on);
            if (on) m_Guide.text = text;
        }

        public void ShowToast(string text, float seconds = 2f)
        {
            m_Toast.text = text;
            m_ToastBg.gameObject.SetActive(true);
            m_ToastUntil = Time.unscaledTime + seconds;
        }

        void Update()
        {
            if (m_ToastBg.gameObject.activeSelf && Time.unscaledTime > m_ToastUntil)
                m_ToastBg.gameObject.SetActive(false);

            // カードは 0.15 秒でフェードイン、0.3 秒でフェードアウト
            float target = m_CardVisible ? 1f : 0f;
            float speed = m_CardVisible ? 1f / 0.15f : 1f / 0.3f;
            m_CardAlpha = Mathf.MoveTowards(m_CardAlpha, target, speed * Time.unscaledDeltaTime);
            m_CardGroup.alpha = m_CardAlpha;
            if (!m_CardVisible && m_CardAlpha <= 0f && m_CardBg.gameObject.activeSelf)
                m_CardBg.gameObject.SetActive(false);
        }
    }
}
