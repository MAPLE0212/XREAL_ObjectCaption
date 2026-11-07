using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace XrealCaption
{
    /// <summary>登録した物の見本 1 枚ぶんの特徴量。</summary>
    [Serializable]
    public class FeatureSample
    {
        public float[] v;
        /// <summary>preset（同梱の写真）/ camera（その場で撮影）</summary>
        public string source;
    }

    /// <summary>登録した物。negative が true のものは「背景」で、一致してもキャプションを出さない。</summary>
    [Serializable]
    public class RegisteredObject
    {
        public string id;
        public string name;
        public string caption;
        public bool negative;
        public List<FeatureSample> samples = new List<FeatureSample>();

        public int CameraSampleCount
        {
            get
            {
                int n = 0;
                foreach (var s in samples) if (s.source == "camera") n++;
                return n;
            }
        }
    }

    [Serializable]
    class ObjectStore
    {
        public string extractor;
        public List<RegisteredObject> objects = new List<RegisteredObject>();
    }

    [Serializable]
    class PresetFile
    {
        public List<PresetEntry> objects = new List<PresetEntry>();
    }

    [Serializable]
    class PresetEntry
    {
        public string id;
        public string name;
        public string caption;
        public string[] images;
    }

    /// <summary>照合の結果。</summary>
    public struct MatchResult
    {
        /// <summary>いちばん近い物（何も登録がなければ null）。</summary>
        public RegisteredObject best;
        public float bestScore;
        /// <summary>2 番目に近い物（別の物）との類似度。</summary>
        public float secondScore;
        /// <summary>しきい値と差の条件を満たし、背景でもない物が見つかったか。</summary>
        public bool accepted;
    }

    /// <summary>
    /// 登録した物と見本の特徴量を持ち、カメラの画像の特徴量と照合する（コサイン類似度の最近傍）。
    /// persistentDataPath/objects.json に保存する。初回は Resources/Presets の写真から登録済みの状態を作る。
    /// </summary>
    public class ObjectDatabase
    {
        public const string BackgroundId = "background";
        const string k_FileName = "objects.json";

        ObjectStore m_Store = new ObjectStore();
        readonly string m_Path;

        public IReadOnlyList<RegisteredObject> Objects => m_Store.objects;
        public string FilePath => m_Path;

        public ObjectDatabase(string directory = null)
        {
            m_Path = Path.Combine(directory ?? Application.persistentDataPath, k_FileName);
        }

        /// <summary>
        /// 保存したデータを読む。ファイルがない、または抽出器が変わっていたら、同梱の写真から作り直す。
        /// 戻り値は画面に出す説明（問題がなければ null）。
        /// </summary>
        public string Load(FeatureExtractor extractor)
        {
            if (File.Exists(m_Path))
            {
                try
                {
                    var store = JsonUtility.FromJson<ObjectStore>(File.ReadAllText(m_Path));
                    if (store != null && store.extractor == extractor.Name)
                    {
                        m_Store = store;
                        return null;
                    }
                    Debug.LogWarning($"[CaptionApp] 特徴量の種類が違う（保存 {store?.extractor} / 今 {extractor.Name}）ので作り直します");
                    ResetToPresets(extractor);
                    return "認識の方式が変わったので、撮影した見本を消して作り直しました";
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[CaptionApp] {m_Path} を読めませんでした: {e.Message}");
                }
            }
            ResetToPresets(extractor);
            return null;
        }

        /// <summary>同梱の写真だけの状態に戻す（自分で登録した物と撮影した見本は消える）。</summary>
        public void ResetToPresets(FeatureExtractor extractor)
        {
            m_Store = new ObjectStore { extractor = extractor.Name };
            var file = Resources.Load<TextAsset>("Presets/presets");
            if (file == null)
            {
                Debug.LogWarning("[CaptionApp] Resources/Presets/presets.json がありません");
                Save();
                return;
            }
            var presets = JsonUtility.FromJson<PresetFile>(file.text);
            var pixels = new Color32[FeatureExtractor.InputSize * FeatureExtractor.InputSize];
            foreach (var p in presets.objects)
            {
                var obj = new RegisteredObject { id = p.id, name = p.name, caption = p.caption };
                foreach (var imageName in p.images)
                {
                    var tex = LoadPresetImage(imageName);
                    if (tex == null) continue;
                    foreach (var aug in Augmentations)
                    {
                        PresetSampler.Sample(tex, pixels, aug.crop, aug.rotation);
                        obj.samples.Add(new FeatureSample { v = extractor.ExtractNow(pixels), source = "preset" });
                    }
                    UnityEngine.Object.Destroy(tex);
                }
                m_Store.objects.Add(obj);
            }
            Save();
            Debug.Log($"[CaptionApp] 同梱の写真から {m_Store.objects.Count} 個を登録しました");
        }

        /// <summary>同梱の写真 1 枚から作る見本の種類（切り抜きの大きさ × 回転）。眼鏡から見る向き・距離の違いを補う。</summary>
        public static readonly (float crop, int rotation)[] Augmentations =
        {
            (1.0f, 0), (0.7f, 0), (1.0f, 90), (0.7f, 90), (1.0f, 180), (0.7f, 180), (1.0f, 270), (0.7f, 270),
        };

        public static Texture2D LoadPresetImage(string name)
        {
            var bytes = Resources.Load<TextAsset>("Presets/" + name);
            if (bytes == null)
            {
                Debug.LogWarning($"[CaptionApp] 同梱の写真 {name} がありません");
                return null;
            }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(bytes.bytes);
            return tex;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(m_Path));
                File.WriteAllText(m_Path, JsonUtility.ToJson(m_Store));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CaptionApp] 保存に失敗: {e.Message}");
            }
        }

        public RegisteredObject Find(string id) => m_Store.objects.Find(o => o.id == id);

        public RegisteredObject Add(string name, string caption, bool negative = false)
        {
            var obj = new RegisteredObject
            {
                id = negative ? BackgroundId : "obj_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_") + Guid.NewGuid().ToString("N").Substring(0, 6),
                name = name,
                caption = caption,
                negative = negative,
            };
            m_Store.objects.Add(obj);
            Save();
            return obj;
        }

        /// <summary>背景（キャプションを出さない物）。なければ作る。</summary>
        public RegisteredObject GetOrCreateBackground()
        {
            return Find(BackgroundId) ?? Add("背景（何もない）", "", negative: true);
        }

        public void Remove(RegisteredObject obj)
        {
            m_Store.objects.Remove(obj);
            Save();
        }

        public void AddSample(RegisteredObject obj, float[] feature, string source = "camera")
        {
            obj.samples.Add(new FeatureSample { v = feature, source = source });
            Save();
        }

        /// <summary>
        /// 特徴量に一番近い物を探す。物ごとの類似度は見本の中の最大値。
        /// threshold 以上で、2 番目の物より margin 以上近く、背景でなければ accepted。
        /// </summary>
        public MatchResult Match(float[] feature, float threshold, float margin)
        {
            var result = new MatchResult { bestScore = -1f, secondScore = -1f };
            if (feature == null) return result;
            foreach (var obj in m_Store.objects)
            {
                if (obj.samples.Count == 0) continue;
                float s = -1f;
                foreach (var sample in obj.samples)
                {
                    if (sample.v == null || sample.v.Length != feature.Length) continue;
                    s = Mathf.Max(s, Dot(feature, sample.v));
                }
                if (s > result.bestScore)
                {
                    result.secondScore = result.bestScore;
                    result.bestScore = s;
                    result.best = obj;
                }
                else if (s > result.secondScore)
                {
                    result.secondScore = s;
                }
            }
            result.accepted = result.best != null && !result.best.negative &&
                              result.bestScore >= threshold &&
                              result.bestScore - Mathf.Max(result.secondScore, 0f) >= margin;
            return result;
        }

        static float Dot(float[] a, float[] b)
        {
            float s = 0f;
            for (int i = 0; i < a.Length; i++) s += a[i] * b[i];
            return s;
        }
    }

    /// <summary>同梱の写真（Texture2D）から、中央の正方形を切り抜いて回した画像を作る。</summary>
    public static class PresetSampler
    {
        public static void Sample(Texture2D tex, Color32[] dst, float crop, int rotation)
        {
            int size = FeatureExtractor.InputSize;
            int w = tex.width, h = tex.height;
            var src = tex.GetPixels32();
            float side = Mathf.Min(w, h) * crop;
            float x0 = (w - side) * 0.5f, y0 = (h - side) * 0.5f;
            for (int oy = 0; oy < size; oy++)
            for (int ox = 0; ox < size; ox++)
            {
                // 出力の (s, t)（左下原点）を回転してから元画像の座標に
                float s = (ox + 0.5f) / size, t = (oy + 0.5f) / size;
                float u = s, v = t;
                switch (rotation)
                {
                    case 90: u = t; v = 1f - s; break;
                    case 180: u = 1f - s; v = 1f - t; break;
                    case 270: u = 1f - t; v = s; break;
                }
                int px = Mathf.Clamp((int)(x0 + u * side), 0, w - 1);
                int py = Mathf.Clamp((int)(y0 + v * side), 0, h - 1);
                dst[oy * size + ox] = src[py * w + px];
            }
        }
    }
}
