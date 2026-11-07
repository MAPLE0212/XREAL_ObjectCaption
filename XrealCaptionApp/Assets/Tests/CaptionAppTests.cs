using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Unity.InferenceEngine;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace XrealCaption.Tests
{
    /// <summary>
    /// エディタでの通し確認。カメラの代わりに StillFrameSource で写真を流す。
    /// ・HeldOutPhotos：同梱の写真のうち 2 枚で登録し、残りの 1 枚を正しく見分けられるか（類似度を Logs/TestCaptures/similarity.txt に出す）
    /// ・AppFlow：同梱の写真での認識 → キャプション表示 → 別の物に切り替え → 背景の登録で消える → 新しい物の登録 → 削除
    /// 眼鏡ビューと Beam Pro 画面のスクリーンショットを Logs/TestCaptures/ に保存する。
    /// </summary>
    public class CaptionAppTests
    {
        const string k_CaptureDir = "Logs/TestCaptures";
        const string k_ModelPath = "Assets/Models/mobilenetv2-12.onnx";
        static readonly string[] k_ShirtImages = { "azamiT_IMG_1895", "azamiT_IMG_1896", "azamiT_IMG_1897" };
        static readonly string[] k_EarImages = { "azaminomimi_IMG_1892", "azaminomimi_IMG_1893", "azaminomimi_IMG_1894" };

        string m_DataDir;
        Camera m_Camera;
        CaptionApp m_App;
        StillFrameSource m_Frames;

        static ModelAsset LoadModel()
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<ModelAsset>(k_ModelPath);
#else
            return null;
#endif
        }

        [SetUp]
        public void SetUp()
        {
            Directory.CreateDirectory(k_CaptureDir);
            m_DataDir = Path.Combine(Application.temporaryCachePath, "CaptionTest_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_DataDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (m_App != null) Object.Destroy(m_App.gameObject);
            if (m_Camera != null) Object.Destroy(m_Camera.gameObject);
            foreach (var ui in Object.FindObjectsByType<PhoneUI>(FindObjectsSortMode.None)) Object.Destroy(ui.gameObject);
            try { Directory.Delete(m_DataDir, true); } catch { }
        }

        [UnityTest]
        public IEnumerator HeldOutPhotos()
        {
            var model = LoadModel();
            Assert.IsNotNull(model, $"{k_ModelPath} がありません");
            using var extractor = new MobileNetExtractor(model);
            var db = new ObjectDatabase(m_DataDir);
            var pixels = new Color32[FeatureExtractor.InputSize * FeatureExtractor.InputSize];
            var log = new StringBuilder();

            var sets = new[] { ("shirt", k_ShirtImages), ("ears", k_EarImages) };
            int correct = 0, total = 0;
            for (int holdOut = 0; holdOut < 3; holdOut++)
            {
                db = new ObjectDatabase(m_DataDir);
                foreach (var (name, images) in sets)
                {
                    var obj = db.Add(name, "");
                    for (int i = 0; i < images.Length; i++)
                    {
                        if (i == holdOut) continue;
                        var tex = ObjectDatabase.LoadPresetImage(images[i]);
                        foreach (var aug in ObjectDatabase.Augmentations)
                        {
                            PresetSampler.Sample(tex, pixels, aug.crop, aug.rotation);
                            db.AddSample(obj, extractor.ExtractNow(pixels), "preset");
                        }
                        Object.Destroy(tex);
                    }
                }
                foreach (var (name, images) in sets)
                {
                    var tex = ObjectDatabase.LoadPresetImage(images[holdOut]);
                    // 登録に使っていない切り抜き（50%）と、暗め・色かぶりで「カメラで見た」状態に近づける
                    PresetSampler.Sample(tex, pixels, 0.5f, 0);
                    Jitter(pixels, 0.8f, new Color(1.0f, 0.95f, 0.9f));
                    var m = db.Match(extractor.ExtractNow(pixels), 0.6f, 0.05f);
                    total++;
                    if (m.best != null && m.best.name == name) correct++;
                    log.AppendLine($"holdOut={images[holdOut]} expect={name} best={m.best?.name} score={m.bestScore:0.000} second={m.secondScore:0.000} accepted={m.accepted}");
                    Object.Destroy(tex);
                }
                yield return null;
            }

            // 背景だけ（机の色の無地）との類似度も記録する
            var plain = Enumerable.Repeat(new Color32(196, 170, 135, 255), pixels.Length).ToArray();
            var pm = db.Match(extractor.ExtractNow(plain), 0.6f, 0.05f);
            log.AppendLine($"plain_wood best={pm.best?.name} score={pm.bestScore:0.000} second={pm.secondScore:0.000} accepted={pm.accepted}");
            log.AppendLine($"correct {correct}/{total}");
            File.WriteAllText(Path.Combine(k_CaptureDir, "similarity.txt"), log.ToString());
            Debug.Log("[CaptionTest]\n" + log);
            Assert.AreEqual(total, correct, log.ToString());
        }

        [UnityTest]
        public IEnumerator AppFlow()
        {
            yield return CreateApp();
            Assert.AreEqual("mobilenetv2-gap", m_App.Extractor.Name);
            Assert.AreEqual(2, m_App.Database.Objects.Count, "同梱の写真から 2 個が登録されていない");
            Assert.AreEqual(ObjectDatabase.Augmentations.Length * 3, m_App.Database.Objects[0].samples.Count);
            yield return Capture("01_start");

            // Tシャツを見る → キャプションが出る
            ShowPreset(k_ShirtImages[2], 0.85f);
            yield return WaitUntil(() => m_App.ShownObject?.id == "azamiT", 5f, "Tシャツのキャプションが出ない");
            yield return WaitFrames(10);
            Assert.AreEqual("角間あざみTシャツ", m_App.View.ShownTitle);
            StringAssert.Contains("タオバオ", m_App.View.ShownBody);
            yield return Capture("02_shirt");

            // 耳に切り替え
            ShowPreset(k_EarImages[1], 0.9f);
            yield return WaitUntil(() => m_App.ShownObject?.id == "azaminomimi", 5f, "耳に切り替わらない");
            yield return WaitFrames(10);
            Assert.AreEqual("角間あざみの（多分）耳", m_App.View.ShownTitle);
            yield return Capture("03_ears");

            // 何も写っていない（グレーの無地）→ 背景として撮影すると、キャプションが消える
            var gray = Solid(new Color32(120, 120, 125, 255), 320, 240);
            m_Frames.SetImage(gray, 320, 240);
            yield return WaitSeconds(0.4f); // 推論の間隔（0.1 秒）より長く待って、新しい画像を取り込ませる
            m_App.StartCaptureForTest(m_App.Database.GetOrCreateBackground());
            Assert.IsTrue(m_App.IsCapturing);
            Assert.IsNotNull(m_App.View.GuideText);
            yield return Capture("04_capture_background");
            m_App.ConfirmForTest();
            m_App.ConfirmForTest();
            m_App.FinishCaptureForTest();
            Assert.IsFalse(m_App.IsCapturing);
            Assert.AreEqual(2, m_App.Database.Find(ObjectDatabase.BackgroundId).samples.Count);
            yield return WaitUntil(() => m_App.ShownObject == null, 5f, "背景でキャプションが消えない");
            Assert.AreEqual(ObjectDatabase.BackgroundId, m_App.LastMatch.best.id);
            Assert.IsFalse(m_App.LastMatch.accepted);

            // 新しい物を登録（画面の「新しい物を登録」→ 名前とキャプション → 撮影 3 枚）
            var pattern = Checker(new Color32(230, 40, 40, 255), new Color32(30, 60, 220, 255), 320, 240, 40);
            m_Frames.SetImage(pattern, 320, 240);
            yield return WaitSeconds(0.4f); // 推論の間隔（0.1 秒）より長く待って、新しい画像を取り込ませる
            ClickButton("NewObject");
            Assert.IsTrue(m_App.Phone.IsInputOpen);
            yield return Capture("05_input");
            m_App.Phone.SubmitForTest("テストの市松模様", "赤と青の市松模様。登録のテスト用。");
            Assert.IsFalse(m_App.Phone.IsInputOpen);
            Assert.IsTrue(m_App.IsCapturing);
            for (int i = 0; i < 3; i++) m_App.ConfirmForTest();
            m_App.FinishCaptureForTest();
            var added = m_App.Database.Objects.First(o => o.name == "テストの市松模様");
            Assert.AreEqual(3, added.samples.Count);
            yield return WaitUntil(() => m_App.ShownObject == added, 5f, "登録した物のキャプションが出ない");
            yield return WaitFrames(10);
            Assert.AreEqual("赤と青の市松模様。登録のテスト用。", m_App.View.ShownBody);
            yield return Capture("06_new_object");

            // 保存したデータを読み直せる
            var reloaded = new ObjectDatabase(m_DataDir);
            Assert.IsNull(reloaded.Load(m_App.Extractor));
            Assert.AreEqual(m_App.Database.Objects.Count, reloaded.Objects.Count);
            Assert.AreEqual(3, reloaded.Find(added.id).samples.Count);

            // 削除するとキャプションが消える
            ClickRowButton(added.id, "Delete");
            Assert.IsTrue(m_App.Phone.IsDialogOpen);
            m_App.Phone.ConfirmDialogForTest();
            Assert.IsNull(m_App.Database.Find(added.id));
            yield return WaitUntil(() => m_App.ShownObject != added, 3f, "削除した物のキャプションが残る");
            yield return Capture("07_after_delete");
        }

        // ---- 準備 ----

        IEnumerator CreateApp()
        {
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            m_Camera = camGo.AddComponent<Camera>();
            m_Camera.clearFlags = CameraClearFlags.SolidColor;
            m_Camera.backgroundColor = Color.black;
            m_Camera.fieldOfView = 25.6f;
            m_Camera.nearClipPlane = 0.05f;

            m_Frames = new StillFrameSource();
            var appGo = new GameObject("CaptionApp");
            appGo.SetActive(false);
            m_App = appGo.AddComponent<CaptionApp>();
            m_App.DataDirectory = m_DataDir;
            m_App.FrameSourceOverride = m_Frames;
            m_App.Model = LoadModel();
            m_App.IgnoreSavedSettings = true;
            appGo.SetActive(true);
            yield return null;
            yield return null;
        }

        void ShowPreset(string name, float brightness)
        {
            var tex = ObjectDatabase.LoadPresetImage(name);
            var px = tex.GetPixels32();
            Jitter(px, brightness, Color.white);
            m_Frames.SetImage(px, tex.width, tex.height);
            Object.Destroy(tex);
        }

        static void Jitter(Color32[] px, float brightness, Color tint)
        {
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                px[i] = new Color32(
                    (byte)Mathf.Clamp(c.r * brightness * tint.r, 0, 255),
                    (byte)Mathf.Clamp(c.g * brightness * tint.g, 0, 255),
                    (byte)Mathf.Clamp(c.b * brightness * tint.b, 0, 255), 255);
            }
        }

        static Color32[] Solid(Color32 c, int w, int h) => Enumerable.Repeat(c, w * h).ToArray();

        static Color32[] Checker(Color32 a, Color32 b, int w, int h, int cell)
        {
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = ((x / cell) + (y / cell)) % 2 == 0 ? a : b;
            return px;
        }

        void ClickButton(string name)
        {
            var button = m_App.Phone.GetComponentsInChildren<Button>(true).First(b => b.name == name);
            button.onClick.Invoke();
        }

        void ClickRowButton(string objectId, string name)
        {
            var row = m_App.Phone.GetComponentsInChildren<Image>(true).First(i => i.name == "Row_" + objectId);
            row.GetComponentsInChildren<Button>(true).First(b => b.name == name).onClick.Invoke();
        }

        static IEnumerator WaitSeconds(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        static IEnumerator WaitFrames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        static IEnumerator WaitUntil(System.Func<bool> condition, float timeout, string message)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail(message);
                yield return null;
            }
        }

        /// <summary>眼鏡ビュー（メインカメラ）と Beam Pro 画面をそれぞれ PNG に保存する。</summary>
        IEnumerator Capture(string name)
        {
            yield return WaitSeconds(0.4f); // キャプションのフェードインが終わるのを待つ（WaitForEndOfFrame はバッチモードで返らない）
            Canvas.ForceUpdateCanvases();

            // 眼鏡ビュー：OST では黒が透明。見やすいよう背景をグレーにして撮る
            var rt = new RenderTexture(1280, 720, 24);
            var rect = m_Camera.rect;
            var bg = m_Camera.backgroundColor;
            m_Camera.rect = new Rect(0, 0, 1, 1);
            m_Camera.backgroundColor = new Color(0.35f, 0.38f, 0.42f);
            m_Camera.targetTexture = rt;
            m_Camera.Render();
            m_Camera.targetTexture = null;
            m_Camera.rect = rect;
            m_Camera.backgroundColor = bg;
            Save(rt, $"{name}_glasses.png");

            // Beam Pro 画面
            var phone = m_App.Phone;
            var canvas = phone.GetComponent<Canvas>();
            var capGo = new GameObject("CaptureCam");
            capGo.transform.position = new Vector3(0, -1000, 0);
            var cap = capGo.AddComponent<Camera>();
            cap.clearFlags = CameraClearFlags.SolidColor;
            cap.backgroundColor = Color.black;
            var prt = new RenderTexture(540, 1170, 24);
            cap.targetTexture = prt;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cap;
            canvas.planeDistance = 1f;
            phone.LayoutForCapture(new Vector2(540, 1170));
            Canvas.ForceUpdateCanvases();
            cap.Render();
            Save(prt, $"{name}_phone.png");
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            phone.RestoreLayout();
            Object.Destroy(capGo);
            rt.Release();
            prt.Release();
        }

        static void Save(RenderTexture rt, string file)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(k_CaptureDir, file), tex.EncodeToPNG());
            Object.Destroy(tex);
        }
    }
}
