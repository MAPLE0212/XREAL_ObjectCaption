using System;
using Unity.InferenceEngine;
using UnityEngine;

namespace XrealCaption
{
    /// <summary>
    /// 物体キャプションアプリの本体。
    /// カメラ（XREAL Eye / 端末のカメラ）の中央の範囲を一定間隔で特徴量にし、登録した物と照合する。
    /// 同じ物が続けて一致したら、眼鏡の枠の右にその物のキャプションを出す。見えなくなってしばらくしたら消す。
    /// 物の登録・撮り足し・編集・削除・設定は Beam Pro の画面で行う。撮影は眼鏡の Xボタン / 画面の「撮影」/ Space。
    /// </summary>
    public class CaptionApp : MonoBehaviour
    {
        [SerializeField] Camera m_Camera;
        [Tooltip("MobileNetV2 の ONNX（Assets/Models/mobilenetv2-12.onnx）。なければ色と輪郭のヒストグラムで代用する")]
        [SerializeField] ModelAsset m_Model;

        [Header("認識")]
        [Tooltip("これ以上似ていたら一致とみなす（コサイン類似度）")]
        [SerializeField] float m_Threshold = 0.6f;
        [Tooltip("1位と2位の差がこれ未満なら、どちらとも決めない")]
        [SerializeField] float m_Margin = 0.05f;
        [Tooltip("カメラの短い辺に対する認識範囲（中央の正方形）の割合")]
        [SerializeField] float m_Crop = 0.5f;
        [Tooltip("推論の間隔（秒）")]
        [SerializeField] float m_Interval = 0.1f;
        [Tooltip("同じ物がこの回数続けて一致したらキャプションを出す")]
        [SerializeField] int m_ShowStreak = 2;
        [Tooltip("一致しなくなってからキャプションを消すまでの時間（秒）")]
        [SerializeField] float m_HoldSeconds = 1.2f;

        [Header("眼鏡の表示")]
        [Tooltip("カメラの縦の画角（度）。眼鏡に出す認識範囲の枠の大きさ = 縦の画角 × 認識範囲。要実機確認")]
        [SerializeField] float m_CameraVerticalFovDeg = 24f;

        const string k_PrefThreshold = "threshold";
        const string k_PrefCrop = "crop";
        const string k_PrefSource = "source";
        const string k_PrefRotation = "rotation";

        GlassesView m_View;
        PhoneUI m_Phone;
        InputRouter m_Input;
        CaptionLogger m_Log;
        FeatureExtractor m_Extractor;
        ObjectDatabase m_Db;
        FrameSource m_Source;
        bool m_UseEye;
        int m_Rotation;

        readonly Color32[] m_Pixels = new Color32[FeatureExtractor.InputSize * FeatureExtractor.InputSize];
        Texture2D m_PreviewTex;
        bool m_HasFrame;
        float m_NextInference;
        float m_ScheduledAt;
        float m_LatencyMs;

        // 表示の安定化
        RegisteredObject m_Candidate;
        int m_Streak;
        RegisteredObject m_Shown;
        float m_LastSeen;
        MatchResult m_LastMatch;

        // 撮影・入力
        RegisteredObject m_CaptureTarget;
        int m_CaptureCount;
        RegisteredObject m_EditTarget;
        bool m_InputIsNew;

        bool m_Initialized;

        // ---- テスト用の差し替え（Start の前に設定する） ----
        /// <summary>登録データの保存先（null なら persistentDataPath）。</summary>
        public string DataDirectory { get; set; }
        /// <summary>カメラの代わりに使う画像の取り出し口。</summary>
        public FrameSource FrameSourceOverride { get; set; }
        public ModelAsset Model { get => m_Model; set => m_Model = value; }
        /// <summary>保存した設定（しきい値など）を読まない。</summary>
        public bool IgnoreSavedSettings { get; set; }

        public GlassesView View => m_View;
        public PhoneUI Phone => m_Phone;
        public ObjectDatabase Database => m_Db;
        public FeatureExtractor Extractor => m_Extractor;
        public RegisteredObject ShownObject => m_Shown;
        public MatchResult LastMatch => m_LastMatch;
        public bool IsCapturing => m_CaptureTarget != null;
        public bool HasFrame => m_HasFrame;
        public float Threshold => m_Threshold;

        void Start()
        {
            if (m_Camera == null) m_Camera = Camera.main;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            RequestCameraPermission();

            if (!IgnoreSavedSettings)
            {
                m_Threshold = PlayerPrefs.GetFloat(k_PrefThreshold, m_Threshold);
                m_Crop = PlayerPrefs.GetFloat(k_PrefCrop, m_Crop);
                m_Rotation = PlayerPrefs.GetInt(k_PrefRotation, 0);
            }
#if UNITY_ANDROID && !UNITY_EDITOR
            m_UseEye = IgnoreSavedSettings || PlayerPrefs.GetInt(k_PrefSource, 1) == 1;
#else
            m_UseEye = false;
#endif

            m_View = GlassesView.Create(m_Camera);
            m_Phone = PhoneUI.Create();
            m_Input = gameObject.AddComponent<InputRouter>();
            m_Log = new CaptionLogger();
            m_PreviewTex = new Texture2D(FeatureExtractor.InputSize, FeatureExtractor.InputSize, TextureFormat.RGBA32, false);
            m_Phone.SetPreview(m_PreviewTex);

            m_Extractor = CreateExtractor();
            m_Db = new ObjectDatabase(DataDirectory);
            var loadMessage = m_Db.Load(m_Extractor);
            if (loadMessage != null) m_Phone.ShowToast(loadMessage, 4f);

            StartSource();
            Wire();
            RefreshSettings();
            RefreshList();
            m_Log.Log("start", detail: $"extractor={m_Extractor.Name} source={m_Source.Name} objects={m_Db.Objects.Count}");
            m_Initialized = true;
        }

        FeatureExtractor CreateExtractor()
        {
            if (m_Model != null)
            {
                try
                {
                    var mobileNet = new MobileNetExtractor(m_Model, BackendType.CPU);
                    Debug.Log($"[CaptionApp] MobileNetV2 を使います（出力：{mobileNet.OutputInfo}）");
                    return mobileNet;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[CaptionApp] MobileNetV2 を読み込めませんでした: {e.Message}");
                }
            }
            Debug.Log("[CaptionApp] 色と輪郭のヒストグラムで認識します");
            return new SimpleFeatureExtractor();
        }

        void StartSource()
        {
            m_Source?.Stop();
            if (FrameSourceOverride != null) m_Source = FrameSourceOverride;
            else if (m_UseEye) m_Source = new XrealEyeFrameSource();
            else m_Source = new WebCamFrameSource();
            m_Source.Rotation = m_Rotation;
            m_Source.Start();
            m_HasFrame = false;
        }

        static void RequestCameraPermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Camera);
#endif
        }

        void Wire()
        {
            m_Input.Confirm += OnConfirm;
            m_Input.KeyPressed += (id, label) =>
            {
                m_Phone.SetLastInput(label);
                m_Log.Log("key_press", detail: id);
            };
            m_Phone.CapturePressed += m_Input.ConfirmFromPhoneButton;
            m_Phone.FinishCapturePressed += FinishCapture;
            m_Phone.NewObjectPressed += () =>
            {
                m_InputIsNew = true;
                m_EditTarget = null;
                m_Phone.OpenInput("新しい物を登録", "", "");
            };
            m_Phone.BackgroundPressed += () => StartCapture(m_Db.GetOrCreateBackground());
            m_Phone.AddShotsPressed += StartCapture;
            m_Phone.EditPressed += obj =>
            {
                m_InputIsNew = false;
                m_EditTarget = obj;
                m_Phone.OpenInput("名前とキャプションを編集", obj.name, obj.caption);
            };
            m_Phone.DeletePressed += obj => m_Phone.ShowDialog($"「{obj.name}」を削除しますか？\n撮影した見本も消えます", () => DeleteObject(obj));
            m_Phone.InputSubmitted += OnInputSubmitted;
            m_Phone.InputCancelled += () => m_Phone.CloseInput();
            m_Phone.ThresholdStep += step =>
            {
                m_Threshold = Mathf.Clamp(Mathf.Round((m_Threshold + step * 0.02f) * 100f) / 100f, 0.2f, 0.98f);
                PlayerPrefs.SetFloat(k_PrefThreshold, m_Threshold);
                m_Log.Log("setting", detail: $"threshold={m_Threshold:0.00}");
                RefreshSettings();
            };
            m_Phone.CropStep += step =>
            {
                m_Crop = Mathf.Clamp(Mathf.Round((m_Crop + step * 0.1f) * 10f) / 10f, 0.2f, 1f);
                PlayerPrefs.SetFloat(k_PrefCrop, m_Crop);
                m_Log.Log("setting", detail: $"crop={m_Crop:0.0}");
                RefreshSettings();
            };
            m_Phone.SourceTogglePressed += () =>
            {
                m_UseEye = !m_UseEye;
                PlayerPrefs.SetInt(k_PrefSource, m_UseEye ? 1 : 0);
                StartSource();
                m_Log.Log("setting", detail: $"source={m_Source.Name}");
                RefreshSettings();
            };
            m_Phone.RotatePressed += () =>
            {
                m_Rotation = (m_Rotation + 90) % 360;
                m_Source.Rotation = m_Rotation;
                PlayerPrefs.SetInt(k_PrefRotation, m_Rotation);
                m_Log.Log("setting", detail: $"rotation={m_Rotation}");
                RefreshSettings();
            };
            m_Phone.ResetPresetsPressed += () => m_Phone.ShowDialog("同梱の写真だけの状態に戻しますか？\n自分で登録した物と撮影した見本は消えます", () =>
            {
                EndCapture();
                SetShown(null);
                m_Db.ResetToPresets(m_Extractor);
                m_Log.Log("reset");
                RefreshList();
                m_Phone.ShowToast("同梱の写真だけに戻しました");
            });
        }

        void RefreshSettings()
        {
            m_Phone.SetThreshold(m_Threshold);
            m_Phone.SetCrop(m_Crop);
            m_Phone.SetSourceLabel(FrameSourceOverride != null ? m_Source.Name : (m_UseEye ? "XREAL Eye" : "端末のカメラ"));
            m_Phone.SetRotation(m_Rotation);
            m_View.SetFrameSize(Mathf.Clamp(m_CameraVerticalFovDeg * m_Crop, 5f, 14f)); // 大きすぎるとキャプションが視野の端に出る
        }

        void RefreshList() => m_Phone.SetObjects(m_Db.Objects, m_Shown?.id);

        void Update()
        {
            if (!m_Initialized) return;
#if UNITY_EDITOR
            FitEditorGlassesView();
#endif
            m_Phone.SetSource($"{m_Source.Name}：{m_Source.Status}　{m_Extractor.Name}" + (m_LatencyMs > 0 ? $"　{m_LatencyMs:0} ms" : ""));

            // 前のフレームで始めた推論の結果を受け取る
            if (m_Extractor.IsBusy)
            {
                if (m_Extractor.TryCollect(out var feature))
                {
                    m_LatencyMs = (Time.realtimeSinceStartup - m_ScheduledAt) * 1000f;
                    OnFeature(feature);
                }
                return;
            }

            if (Time.unscaledTime < m_NextInference) return;
            if (!m_Source.TryGetFrame(m_Pixels, FeatureExtractor.InputSize, m_Crop)) return;
            m_HasFrame = true;
            m_PreviewTex.SetPixels32(m_Pixels);
            m_PreviewTex.Apply(false);
            m_NextInference = Time.unscaledTime + m_Interval;
            m_ScheduledAt = Time.realtimeSinceStartup;
            m_Extractor.Schedule(m_Pixels);
        }

        void OnFeature(float[] feature)
        {
            var m = m_Db.Match(feature, m_Threshold, m_Margin);
            m_LastMatch = m;
            var decided = m.accepted ? m.best : null;

            if (decided == m_Candidate) m_Streak++;
            else
            {
                m_Candidate = decided;
                m_Streak = 1;
            }
            float now = Time.unscaledTime;
            if (decided != null && decided == m_Shown) m_LastSeen = now;
            if (m_Candidate != null && m_Streak >= m_ShowStreak && m_Candidate != m_Shown)
            {
                SetShown(m_Candidate, m);
                m_LastSeen = now;
            }
            else if (m_Shown != null && now - m_LastSeen > m_HoldSeconds)
            {
                SetShown(null, m);
            }

            if (m_Shown != null && !IsCapturing)
                m_View.ShowCaption(m_Shown.name, m_Shown.caption, $"類似度 {m.bestScore:0.00}");

            string best = m.best != null ? $"{m.best.name} {m.bestScore:0.00}" : "-";
            string second = m.secondScore > -1f ? $" / 差 {m.bestScore - Mathf.Max(m.secondScore, 0f):0.00}" : "";
            string state = IsCapturing ? $"撮影中：「{m_CaptureTarget.name}」{m_CaptureCount}枚"
                : m_Shown != null ? $"表示中：{m_Shown.name}" : "表示なし";
            m_Phone.SetResult($"1位：{best}{second}\n{state}");
            m_Phone.SetPreviewHighlight(m.accepted);
        }

        void SetShown(RegisteredObject obj, MatchResult m = default)
        {
            if (obj == m_Shown) return;
            if (m_Shown != null) m_Log.Log("hidden", m_Shown, m.bestScore, m.secondScore);
            m_Shown = obj;
            m_View.SetFrameActive(obj != null);
            if (obj == null)
            {
                m_View.ShowCaption(null, null, null);
            }
            else
            {
                m_Log.Log("shown", obj, m.bestScore, m.secondScore);
                if (!IsCapturing) m_View.ShowCaption(obj.name, obj.caption, $"類似度 {m.bestScore:0.00}");
            }
            RefreshList();
        }

        // ---- 登録・撮影 ----

        void OnInputSubmitted(string name, string caption)
        {
            m_Phone.CloseInput();
            if (m_InputIsNew)
            {
                var obj = m_Db.Add(name, caption);
                m_Log.Log("register", obj);
                RefreshList();
                StartCapture(obj);
            }
            else if (m_EditTarget != null)
            {
                m_EditTarget.name = name;
                m_EditTarget.caption = caption;
                m_Db.Save();
                m_Log.Log("edit", m_EditTarget);
                if (m_EditTarget == m_Shown) m_View.ShowCaption(name, caption, null);
                RefreshList();
                m_Phone.ShowToast("保存しました");
            }
        }

        void StartCapture(RegisteredObject obj)
        {
            m_CaptureTarget = obj;
            m_CaptureCount = 0;
            m_View.ShowCaption(null, null, null);
            m_View.SetGuide($"「{obj.name}」を枠に入れて Xボタンで撮影");
            m_Phone.SetCaptureMode(obj.name, 0);
            m_Phone.ShowToast(obj.negative
                ? "何も置いていない机や壁などを、場所を変えて何枚か撮ってください"
                : "向き・距離を変えて 3〜5 枚撮ると認識しやすくなります", 3.5f);
        }

        void OnConfirm(string source)
        {
            if (m_Phone.IsInputOpen || m_Phone.IsDialogOpen) return;
            if (!IsCapturing)
            {
                m_Phone.ShowToast("物の登録・撮り足しは Beam Pro の画面から始めます");
                return;
            }
            CaptureSample(source);
        }

        void CaptureSample(string source)
        {
            if (!m_HasFrame)
            {
                m_View.ShowToast("カメラの映像がありません");
                m_Phone.ShowToast("カメラの映像がありません");
                return;
            }
            var feature = m_Extractor.ExtractNow(m_Pixels);
            if (feature == null) return;
            m_Db.AddSample(m_CaptureTarget, feature);
            m_CaptureCount++;
            m_Log.Log("sample", m_CaptureTarget, detail: $"count={m_CaptureCount} input={source}");
            m_View.ShowToast($"撮影しました（{m_CaptureCount}枚）", 1.2f);
            m_Phone.SetCaptureMode(m_CaptureTarget.name, m_CaptureCount);
            RefreshList();
        }

        void FinishCapture()
        {
            var obj = m_CaptureTarget;
            if (obj == null) return;
            if (obj.samples.Count == 0)
            {
                m_Db.Remove(obj);
                m_Log.Log("delete", obj, detail: "no_samples");
                m_Phone.ShowToast("撮影しなかったので、登録をやめました");
            }
            else
            {
                m_Phone.ShowToast($"「{obj.name}」：{m_CaptureCount}枚撮りました");
            }
            EndCapture();
            RefreshList();
        }

        void EndCapture()
        {
            m_CaptureTarget = null;
            m_CaptureCount = 0;
            m_View.SetGuide(null);
            m_Phone.SetCaptureMode(null, 0);
        }

        void DeleteObject(RegisteredObject obj)
        {
            if (obj == m_CaptureTarget) EndCapture();
            if (obj == m_Shown) SetShown(null);
            if (obj == m_Candidate) m_Candidate = null;
            m_Db.Remove(obj);
            m_Log.Log("delete", obj);
            RefreshList();
            m_Phone.ShowToast($"「{obj.name}」を削除しました");
        }

        // ---- テスト用の操作 ----
        public void StartCaptureForTest(RegisteredObject obj) => StartCapture(obj);
        public void FinishCaptureForTest() => FinishCapture();
        /// <summary>撮影ボタン（Beam Pro の画面の「撮影」）を押したのと同じ。</summary>
        public void ConfirmForTest() => m_Input.ConfirmFromPhoneButton();

#if UNITY_EDITOR
        /// <summary>エディタでは Game ビューの左側を眼鏡ビュー（16:9）、右側を Beam Pro 画面として使う。</summary>
        void FitEditorGlassesView()
        {
            if (m_Camera.targetTexture != null) return;
            float areaW = Screen.width * (1f - PhoneUI.EditorPanelFraction);
            float areaH = Screen.height;
            if (areaW <= 0 || areaH <= 0) return;
            float w = Mathf.Min(areaW, areaH * 16f / 9f);
            float h = w * 9f / 16f;
            m_Camera.rect = new Rect((areaW - w) * 0.5f / Screen.width, (areaH - h) * 0.5f / Screen.height, w / Screen.width, h / Screen.height);
        }
#endif

        void OnDestroy()
        {
            m_Source?.Stop();
            m_Extractor?.Dispose();
            m_Log?.Dispose();
            if (m_PreviewTex != null) Destroy(m_PreviewTex);
        }
    }
}
