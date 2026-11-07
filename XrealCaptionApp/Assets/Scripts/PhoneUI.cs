using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace XrealCaption
{
    /// <summary>
    /// Beam Pro 画面のUI（Screen Space Overlay。眼鏡には出ない）。
    /// 1080×2340 の縦長で作り、実機では画面全体、エディタでは Game ビューの右側に縮小して置く。
    /// ・カメラの映像（認識に使う正方形の範囲そのもの）と認識結果
    /// ・物の登録（名前・キャプションの入力 → 撮影）と、登録した物の一覧（撮り足し・編集・削除）
    /// ・設定（しきい値・認識範囲・カメラ・映像の回転・同梱の写真に戻す）
    /// </summary>
    public class PhoneUI : MonoBehaviour
    {
        const float k_W = 1080f;
        const float k_H = 2340f;
        /// <summary>エディタで Game ビューの右側に使う幅の割合（残りは眼鏡ビュー）。</summary>
        public const float EditorPanelFraction = 0.36f;
        const float k_RowHeight = 150f;

        static readonly Color k_Bg = new Color(0.10f, 0.11f, 0.13f, 1f);
        static readonly Color k_Card = new Color(0.17f, 0.18f, 0.21f, 1f);
        static readonly Color k_Primary = new Color(0.22f, 0.47f, 0.95f, 1f);
        static readonly Color k_Danger = new Color(0.85f, 0.27f, 0.27f, 1f);
        static readonly Color k_Neutral = new Color(0.30f, 0.32f, 0.37f, 1f);
        static readonly Color k_Good = new Color(0.30f, 0.75f, 0.45f, 1f);
        static readonly Color k_TextMain = new Color(0.95f, 0.95f, 0.97f, 1f);
        static readonly Color k_TextSub = new Color(0.70f, 0.72f, 0.78f, 1f);

        public event Action CapturePressed;
        public event Action FinishCapturePressed;
        public event Action NewObjectPressed;
        public event Action BackgroundPressed;
        public event Action<RegisteredObject> AddShotsPressed;
        public event Action<RegisteredObject> EditPressed;
        public event Action<RegisteredObject> DeletePressed;
        public event Action<int> ThresholdStep;
        public event Action<int> CropStep;
        public event Action SourceTogglePressed;
        public event Action RotatePressed;
        public event Action ResetPresetsPressed;
        public event Action<string, string> InputSubmitted;
        public event Action InputCancelled;

        RectTransform m_Phone;
        Text m_Source;
        Text m_LastKey;
        RawImage m_Preview;
        Image m_PreviewFrame;
        Text m_Result;
        RectTransform m_NormalArea;
        RectTransform m_CaptureArea;
        Text m_CaptureLabel;
        Button m_FinishButton;
        RectTransform m_ListContent;
        readonly List<GameObject> m_Rows = new List<GameObject>();
        Text m_ThresholdLabel;
        Text m_CropLabel;
        Text m_SourceLabel;
        Text m_RotateLabel;

        RectTransform m_InputPanel;
        Text m_InputTitle;
        InputField m_NameField;
        InputField m_CaptionField;

        RectTransform m_Dialog;
        Text m_DialogText;
        Action m_DialogOk;

        RectTransform m_ToastRoot;
        Text m_Toast;
        float m_ToastUntil;
        Vector2Int m_LastScreen;

        public bool IsInputOpen => m_InputPanel.gameObject.activeSelf;
        public bool IsDialogOpen => m_Dialog.gameObject.activeSelf;
        public string ResultText => m_Result.text;

        public static PhoneUI Create()
        {
            var go = new GameObject("PhoneUI");
            var ui = go.AddComponent<PhoneUI>();
            ui.Build();
            return ui;
        }

        void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100; // SDK の仮想コントローラーより手前
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            gameObject.AddComponent<GraphicRaycaster>();

            m_Phone = UiFactory.CreateRect("Phone", transform);
            m_Phone.sizeDelta = new Vector2(k_W, k_H);
            m_Phone.gameObject.AddComponent<Image>().color = k_Bg;

            float y = 40f;
            var title = UiFactory.CreateText("Title", m_Phone, "物体キャプション", 54, k_TextMain, TextAnchor.MiddleLeft);
            UiFactory.PlaceTop(title.rectTransform, y, 70, 60);
            y += 80;
            m_Source = UiFactory.CreateText("Source", m_Phone, "", 36, k_TextSub, TextAnchor.MiddleLeft);
            UiFactory.PlaceTop(m_Source.rectTransform, y, 50, 60);
            y += 50;
            m_LastKey = UiFactory.CreateText("LastKey", m_Phone, "最後の入力: -", 32, k_TextSub, TextAnchor.MiddleLeft);
            UiFactory.PlaceTop(m_LastKey.rectTransform, y, 45, 60);
            y += 60;

            // カメラの映像（認識に使う範囲そのもの）
            m_PreviewFrame = UiFactory.CreateImage("PreviewFrame", m_Phone, UiFactory.RoundedFill, k_Card, sliced: true);
            var pf = m_PreviewFrame.rectTransform;
            pf.anchorMin = pf.anchorMax = new Vector2(0.5f, 1f);
            pf.pivot = new Vector2(0.5f, 1f);
            pf.sizeDelta = new Vector2(640, 640);
            pf.anchoredPosition = new Vector2(0, -y);
            m_Preview = UiFactory.CreateRect("Preview", pf).gameObject.AddComponent<RawImage>();
            m_Preview.raycastTarget = false;
            m_Preview.color = Color.white;
            UiFactory.Stretch(m_Preview.rectTransform, 20);
            var noCam = UiFactory.CreateText("NoCamera", pf, "カメラの映像なし", 38, k_TextSub);
            UiFactory.Stretch(noCam.rectTransform);
            noCam.transform.SetAsFirstSibling();
            y += 655;

            m_Result = UiFactory.CreateText("Result", m_Phone, "", 36, k_TextMain, TextAnchor.UpperLeft);
            m_Result.verticalOverflow = VerticalWrapMode.Truncate; // 下のボタンに重ならないよう 2 行まで
            UiFactory.PlaceTop(m_Result.rectTransform, y, 110, 60);
            y += 120;

            // 通常時：登録ボタン
            m_NormalArea = UiFactory.CreateRect("NormalArea", m_Phone);
            UiFactory.PlaceTop(m_NormalArea, y, 140, 60);
            var add = CreateButton("NewObject", m_NormalArea, "＋ 新しい物を登録", k_Primary, 44, () => NewObjectPressed?.Invoke());
            SetAnchorsTop(add.transform as RectTransform, 0f, 0.6f, 0, 140, 0, 10);
            var bgButton = CreateButton("Background", m_NormalArea, "背景を撮る", k_Neutral, 40, () => BackgroundPressed?.Invoke());
            SetAnchorsTop(bgButton.transform as RectTransform, 0.6f, 1f, 0, 140, 10, 0);

            // 撮影中：撮影・完了
            m_CaptureArea = UiFactory.CreateRect("CaptureArea", m_Phone);
            UiFactory.PlaceTop(m_CaptureArea, y, 140, 60);
            var shot = CreateButton("Capture", m_CaptureArea, "撮影", k_Primary, 56, () => CapturePressed?.Invoke());
            SetAnchorsTop(shot.transform as RectTransform, 0f, 0.6f, 0, 140, 0, 10);
            m_CaptureLabel = shot.GetComponentInChildren<Text>();
            m_FinishButton = CreateButton("Finish", m_CaptureArea, "完了", k_Good, 48, () => FinishCapturePressed?.Invoke());
            SetAnchorsTop(m_FinishButton.transform as RectTransform, 0.6f, 1f, 0, 140, 10, 0);
            m_CaptureArea.gameObject.SetActive(false);
            y += 165;

            var listTitle = UiFactory.CreateText("ListTitle", m_Phone, "登録した物", 38, k_TextSub, TextAnchor.MiddleLeft);
            UiFactory.PlaceTop(listTitle.rectTransform, y, 55, 60);
            y += 60;
            BuildList(y, 600);
            y += 620;

            BuildSettings(y);
            BuildInputPanel();
            BuildDialog();
            BuildToast();
            EnsureEventSystem();
        }

        void BuildList(float top, float height)
        {
            var frame = UiFactory.CreateImage("List", m_Phone, UiFactory.RoundedFill, k_Card, sliced: true);
            frame.raycastTarget = true;
            UiFactory.PlaceTop(frame.rectTransform, top, height, 60);
            var scroll = frame.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            var viewport = UiFactory.CreateRect("Viewport", frame.rectTransform);
            UiFactory.Stretch(viewport, 12);
            viewport.gameObject.AddComponent<RectMask2D>();
            m_ListContent = UiFactory.CreateRect("Content", viewport);
            m_ListContent.anchorMin = new Vector2(0, 1);
            m_ListContent.anchorMax = new Vector2(1, 1);
            m_ListContent.pivot = new Vector2(0.5f, 1);
            m_ListContent.sizeDelta = Vector2.zero;
            scroll.viewport = viewport;
            scroll.content = m_ListContent;
        }

        void BuildSettings(float y)
        {
            // しきい値
            m_ThresholdLabel = SettingStepper("Threshold", y, 0f, 0.5f, i => ThresholdStep?.Invoke(i));
            m_CropLabel = SettingStepper("Crop", y, 0.5f, 1f, i => CropStep?.Invoke(i));
            y += 125;
            var src = CreateButton("SourceToggle", m_Phone, "", k_Card, 36, () => SourceTogglePressed?.Invoke());
            SetAnchorsTop(src.transform as RectTransform, 0f, 0.5f, y, 105, 60, 10);
            m_SourceLabel = src.GetComponentInChildren<Text>();
            var rot = CreateButton("Rotate", m_Phone, "", k_Card, 36, () => RotatePressed?.Invoke());
            SetAnchorsTop(rot.transform as RectTransform, 0.5f, 1f, y, 105, 10, 60);
            m_RotateLabel = rot.GetComponentInChildren<Text>();
            y += 120;
            var reset = CreateButton("ResetPresets", m_Phone, "同梱の写真だけに戻す", k_Card, 36, () => ResetPresetsPressed?.Invoke());
            SetAnchorsTop(reset.transform as RectTransform, 0f, 1f, y, 100, 60, 60);
        }

        /// <summary>「ラベル − ＋」の設定行。戻り値はラベル。</summary>
        Text SettingStepper(string name, float top, float x0, float x1, Action<int> step)
        {
            var row = UiFactory.CreateRect(name, m_Phone);
            SetAnchorsTop(row, x0, x1, top, 105, x0 == 0f ? 60 : 10, x1 == 1f ? 60 : 10);
            var label = UiFactory.CreateText("Label", row, "", 34, k_TextMain, TextAnchor.MiddleLeft);
            label.rectTransform.anchorMin = new Vector2(0, 0);
            label.rectTransform.anchorMax = new Vector2(0.52f, 1);
            label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
            var minus = CreateButton(name + "Minus", row, "−", k_Neutral, 48, () => step(-1));
            SetAnchorsTop(minus.transform as RectTransform, 0.52f, 0.76f, 0, 105, 0, 6);
            var plus = CreateButton(name + "Plus", row, "＋", k_Neutral, 48, () => step(1));
            SetAnchorsTop(plus.transform as RectTransform, 0.76f, 1f, 0, 105, 6, 0);
            return label;
        }

        void BuildInputPanel()
        {
            m_InputPanel = UiFactory.CreateRect("InputPanel", m_Phone);
            UiFactory.Stretch(m_InputPanel);
            var dim = m_InputPanel.gameObject.AddComponent<Image>();
            dim.color = new Color(0, 0, 0, 0.75f);
            var card = UiFactory.CreateImage("Card", m_InputPanel, UiFactory.RoundedFill, k_Card, sliced: true);
            card.raycastTarget = true;
            UiFactory.PlaceTop(card.rectTransform, 200, 1180, 50);

            m_InputTitle = UiFactory.CreateText("Title", card.rectTransform, "", 46, k_TextMain, TextAnchor.MiddleLeft);
            UiFactory.PlaceTop(m_InputTitle.rectTransform, 40, 70, 50);
            var nameLabel = UiFactory.CreateText("NameLabel", card.rectTransform, "名前（眼鏡に大きく出る）", 34, k_TextSub, TextAnchor.MiddleLeft);
            UiFactory.PlaceTop(nameLabel.rectTransform, 130, 50, 50);
            m_NameField = CreateField("Name", card.rectTransform, 185, 110, false);
            var capLabel = UiFactory.CreateText("CaptionLabel", card.rectTransform, "キャプション（説明文）", 34, k_TextSub, TextAnchor.MiddleLeft);
            UiFactory.PlaceTop(capLabel.rectTransform, 320, 50, 50);
            m_CaptionField = CreateField("Caption", card.rectTransform, 375, 560, true);

            var ok = CreateButton("OK", card.rectTransform, "OK", k_Primary, 50, Submit);
            SetAnchorsTop(ok.transform as RectTransform, 0f, 0.5f, 1000, 130, 50, 15);
            var cancel = CreateButton("Cancel", card.rectTransform, "キャンセル", k_Neutral, 46, () => InputCancelled?.Invoke());
            SetAnchorsTop(cancel.transform as RectTransform, 0.5f, 1f, 1000, 130, 15, 50);
            m_InputPanel.gameObject.SetActive(false);
        }

        InputField CreateField(string name, RectTransform parent, float top, float height, bool multiLine)
        {
            var bg = UiFactory.CreateImage(name, parent, UiFactory.RoundedFill, Color.white, sliced: true);
            bg.raycastTarget = true;
            UiFactory.PlaceTop(bg.rectTransform, top, height, 50);
            var text = UiFactory.CreateText("Text", bg.rectTransform, "", 40, Color.black, TextAnchor.UpperLeft);
            UiFactory.Stretch(text.rectTransform, 24);
            text.supportRichText = false;
            var field = bg.gameObject.AddComponent<InputField>();
            field.textComponent = text;
            field.lineType = multiLine ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;
            field.characterLimit = multiLine ? 300 : 40;
            bg.gameObject.AddComponent<KeyboardReopener>();
            return field;
        }

        void BuildDialog()
        {
            m_Dialog = UiFactory.CreateRect("Dialog", m_Phone);
            UiFactory.Stretch(m_Dialog);
            m_Dialog.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.75f);
            var card = UiFactory.CreateImage("Card", m_Dialog, UiFactory.RoundedFill, k_Card, sliced: true);
            card.raycastTarget = true;
            UiFactory.PlaceTop(card.rectTransform, 800, 520, 70);
            m_DialogText = UiFactory.CreateText("Text", card.rectTransform, "", 42, k_TextMain);
            UiFactory.PlaceTop(m_DialogText.rectTransform, 40, 290, 50);
            var cancel = CreateButton("Cancel", card.rectTransform, "キャンセル", k_Neutral, 44, CloseDialog);
            SetAnchorsTop(cancel.transform as RectTransform, 0f, 0.5f, 360, 120, 50, 15);
            var ok = CreateButton("OK", card.rectTransform, "OK", k_Danger, 44, () =>
            {
                var action = m_DialogOk;
                CloseDialog();
                action?.Invoke();
            });
            SetAnchorsTop(ok.transform as RectTransform, 0.5f, 1f, 360, 120, 15, 50);
            m_Dialog.gameObject.SetActive(false);
        }

        void BuildToast()
        {
            var bg = UiFactory.CreateImage("Toast", m_Phone, UiFactory.RoundedFill, new Color(0, 0, 0, 0.85f), sliced: true);
            m_ToastRoot = bg.rectTransform;
            m_ToastRoot.anchorMin = m_ToastRoot.anchorMax = new Vector2(0.5f, 0f);
            m_ToastRoot.pivot = new Vector2(0.5f, 0f);
            m_ToastRoot.sizeDelta = new Vector2(940, 130);
            m_ToastRoot.anchoredPosition = new Vector2(0, 80);
            m_Toast = UiFactory.CreateText("Text", m_ToastRoot, "", 38, Color.white);
            UiFactory.Stretch(m_Toast.rectTransform, 20);
            m_ToastRoot.gameObject.SetActive(false);
        }

        static void EnsureEventSystem()
        {
            var es = EventSystem.current;
            if (es == null) es = FindAnyObjectByType<EventSystem>();
            if (es == null)
            {
                es = new GameObject("EventSystem", typeof(EventSystem)).GetComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
                es.gameObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
                es.gameObject.AddComponent<StandaloneInputModule>();
#endif
            }
            // ドラッグとみなす距離を約 2.5 mm にする（既定の 10 px は Beam Pro では短すぎる）
            if (Screen.dpi > 0)
                es.pixelDragThreshold = Mathf.Max(10, Mathf.RoundToInt(Screen.dpi * 0.1f));
        }

        // ---- 外部から呼ぶ表示更新 ----

        public void SetSource(string text) => m_Source.text = text;
        public void SetLastInput(string text) => m_LastKey.text = $"最後の入力: {text}";
        public void SetPreview(Texture tex) => m_Preview.texture = tex;
        public void SetPreviewHighlight(bool on) => m_PreviewFrame.color = on ? k_Good : k_Card;
        public void SetResult(string text) => m_Result.text = text;
        public void SetThreshold(float v) => m_ThresholdLabel.text = $"しきい値 {v:0.00}";
        public void SetCrop(float v) => m_CropLabel.text = $"認識範囲 {v * 100f:0}%";
        public void SetSourceLabel(string name) => m_SourceLabel.text = $"カメラ：{name}";
        public void SetRotation(int deg) => m_RotateLabel.text = $"映像の回転 {deg}°";

        /// <summary>撮影モードの表示。name が null なら通常に戻す。</summary>
        public void SetCaptureMode(string name, int count)
        {
            bool on = name != null;
            m_CaptureArea.gameObject.SetActive(on);
            m_NormalArea.gameObject.SetActive(!on);
            if (on)
            {
                m_CaptureLabel.text = $"撮影（{count}枚）";
                SetEnabled(m_FinishButton, count > 0);
            }
        }

        /// <summary>登録した物の一覧を作り直す。</summary>
        public void SetObjects(IReadOnlyList<RegisteredObject> objects, string highlightId)
        {
            foreach (var row in m_Rows) Destroy(row);
            m_Rows.Clear();
            float y = 0;
            foreach (var obj in objects)
            {
                var o = obj;
                var row = UiFactory.CreateImage("Row_" + obj.id, m_ListContent, UiFactory.RoundedFill,
                    obj.id == highlightId ? new Color(0.25f, 0.45f, 0.32f, 1f) : new Color(0.22f, 0.23f, 0.27f, 1f), sliced: true);
                var rt = row.rectTransform;
                rt.anchorMin = new Vector2(0, 1);
                rt.anchorMax = new Vector2(1, 1);
                rt.pivot = new Vector2(0.5f, 1);
                rt.offsetMin = new Vector2(0, -y - k_RowHeight + 10);
                rt.offsetMax = new Vector2(0, -y);

                int cam = obj.CameraSampleCount;
                int preset = obj.samples.Count - cam;
                string info = preset > 0 ? $"写真 {preset}・撮影 {cam}" : $"撮影 {cam}";
                var label = UiFactory.CreateText("Name", rt, $"{obj.name}\n<size=28><color=#B3B8C7>{info}</color></size>", 38, k_TextMain, TextAnchor.MiddleLeft);
                label.rectTransform.anchorMin = new Vector2(0, 0);
                label.rectTransform.anchorMax = new Vector2(0.48f, 1);
                label.rectTransform.offsetMin = new Vector2(24, 0);
                label.rectTransform.offsetMax = new Vector2(0, 0);
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                label.verticalOverflow = VerticalWrapMode.Truncate;

                var shots = CreateButton("Shots", rt, "撮り足す", k_Primary, 32, () => AddShotsPressed?.Invoke(o));
                SetAnchorsTop(shots.transform as RectTransform, 0.48f, 0.68f, 20, k_RowHeight - 50, 6, 6);
                var edit = CreateButton("Edit", rt, "編集", k_Neutral, 32, () => EditPressed?.Invoke(o));
                SetAnchorsTop(edit.transform as RectTransform, 0.68f, 0.84f, 20, k_RowHeight - 50, 6, 6);
                SetEnabled(edit, !obj.negative);
                var del = CreateButton("Delete", rt, "削除", k_Danger, 32, () => DeletePressed?.Invoke(o));
                SetAnchorsTop(del.transform as RectTransform, 0.84f, 1f, 20, k_RowHeight - 50, 6, 16);
                m_Rows.Add(row.gameObject);
                y += k_RowHeight;
            }
            m_ListContent.sizeDelta = new Vector2(0, y);
        }

        public void OpenInput(string title, string name, string caption)
        {
            m_InputTitle.text = title;
            m_NameField.text = name ?? "";
            m_CaptionField.text = caption ?? "";
            m_InputPanel.gameObject.SetActive(true);
            GuardedButton.BlockClicksBriefly();
            if (string.IsNullOrEmpty(name))
            {
                m_NameField.Select();
                m_NameField.ActivateInputField();
            }
        }

        public void CloseInput()
        {
            m_InputPanel.gameObject.SetActive(false);
            GuardedButton.BlockClicksBriefly();
        }

        /// <summary>テスト用：入力欄に値を入れて OK を押したのと同じにする。</summary>
        public void SubmitForTest(string name, string caption)
        {
            m_NameField.text = name;
            m_CaptionField.text = caption;
            Submit();
        }

        void Submit()
        {
            var name = m_NameField.text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                ShowToast("名前を入力してください");
                return;
            }
            InputSubmitted?.Invoke(name, m_CaptionField.text.Trim());
        }

        public void ShowDialog(string message, Action onOk)
        {
            m_DialogText.text = message;
            m_DialogOk = onOk;
            m_Dialog.gameObject.SetActive(true);
            GuardedButton.BlockClicksBriefly();
        }

        /// <summary>テスト用：ダイアログの OK を押す。</summary>
        public void ConfirmDialogForTest()
        {
            var action = m_DialogOk;
            CloseDialog();
            action?.Invoke();
        }

        void CloseDialog()
        {
            m_Dialog.gameObject.SetActive(false);
            m_DialogOk = null;
            GuardedButton.BlockClicksBriefly();
        }

        public void ShowToast(string text, float seconds = 2.5f)
        {
            m_Toast.text = text;
            m_ToastRoot.gameObject.SetActive(true);
            m_ToastRoot.SetAsLastSibling();
            m_ToastUntil = Time.unscaledTime + seconds;
        }

        void Update()
        {
            if (m_ToastRoot.gameObject.activeSelf && Time.unscaledTime > m_ToastUntil)
                m_ToastRoot.gameObject.SetActive(false);
            var screen = new Vector2Int(Screen.width, Screen.height);
            if (screen != m_LastScreen)
            {
                m_LastScreen = screen;
                Layout(screen);
            }
        }

        /// <summary>縦長の 1080×2340 の画面を、実機では全面に、エディタでは右側の領域に収める。</summary>
        void Layout(Vector2Int screen)
        {
#if UNITY_EDITOR
            float areaW = screen.x * EditorPanelFraction;
            m_Phone.anchorMin = m_Phone.anchorMax = new Vector2(1f, 0.5f);
            m_Phone.pivot = new Vector2(1f, 0.5f);
#else
            float areaW = screen.x;
            m_Phone.anchorMin = m_Phone.anchorMax = new Vector2(0.5f, 0.5f);
            m_Phone.pivot = new Vector2(0.5f, 0.5f);
#endif
            float scale = Mathf.Min(areaW / k_W, screen.y / k_H);
            m_Phone.localScale = new Vector3(scale, scale, 1f);
            m_Phone.anchoredPosition = Vector2.zero;
        }

        /// <summary>画面キャプチャ用：size の領域の中央に収める。RestoreLayout で元に戻す。</summary>
        public void LayoutForCapture(Vector2 size)
        {
            m_Phone.anchorMin = m_Phone.anchorMax = m_Phone.pivot = new Vector2(0.5f, 0.5f);
            float scale = Mathf.Min(size.x / k_W, size.y / k_H);
            m_Phone.localScale = new Vector3(scale, scale, 1f);
            m_Phone.anchoredPosition = Vector2.zero;
        }

        public void RestoreLayout() => m_LastScreen = Vector2Int.zero;

        // ---- ボタン生成 ----

        static void SetEnabled(Button button, bool on)
        {
            button.interactable = on;
            foreach (var text in button.GetComponentsInChildren<Text>(true))
            {
                var c = text.color;
                c.a = on ? 1f : 0.35f;
                text.color = c;
            }
        }

        static Button CreateButton(string name, Transform parent, string label, Color color, int fontSize, Action onClick)
        {
            var img = UiFactory.CreateImage(name, parent, UiFactory.RoundedFill, Color.white, sliced: true);
            img.raycastTarget = true;
            Button button = img.gameObject.AddComponent<GuardedButton>();
            var colors = button.colors;
            colors.normalColor = color;
            colors.highlightedColor = color;
            colors.selectedColor = color;
            colors.pressedColor = color * 0.75f;
            colors.disabledColor = new Color(color.r, color.g, color.b, 0.25f);
            colors.fadeDuration = 0f; // 一覧を作り直した直後に白からフェードして薄く見えないように
            button.colors = colors;
            button.onClick.AddListener(() => onClick?.Invoke());
            var text = UiFactory.CreateText("Label", img.rectTransform, label, fontSize, Color.white);
            UiFactory.Stretch(text.rectTransform, 8);
            return button;
        }

        static void SetAnchorsTop(RectTransform rt, float x0, float x1, float top, float height, float left, float right)
        {
            rt.anchorMin = new Vector2(x0, 1);
            rt.anchorMax = new Vector2(x1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.offsetMin = new Vector2(left, -top - height);
            rt.offsetMax = new Vector2(-right, -top);
        }
    }
}
