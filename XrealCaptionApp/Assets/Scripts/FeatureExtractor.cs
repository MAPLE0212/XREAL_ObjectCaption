using System;
using Unity.InferenceEngine;
using UnityEngine;

namespace XrealCaption
{
    /// <summary>
    /// 画像（InputSize×InputSize、Unity の並び）から特徴ベクトル（L2 正規化済み）を作る。
    /// 推論は Schedule で始めて、次のフレーム以降に TryCollect で受け取る（眼鏡の描画を止めないため）。
    /// </summary>
    public abstract class FeatureExtractor : IDisposable
    {
        public const int InputSize = 224;

        /// <summary>登録データに記録する名前。違う抽出器で作った特徴量は比べられない。</summary>
        public abstract string Name { get; }
        public abstract bool IsBusy { get; }
        public abstract void Schedule(Color32[] pixels);
        /// <summary>結果ができていれば true を返して feature に入れる（1回の Schedule につき1回だけ）。</summary>
        public abstract bool TryCollect(out float[] feature);
        public abstract void Dispose();

        /// <summary>その場で結果まで待つ（登録の撮影で使う）。</summary>
        public float[] ExtractNow(Color32[] pixels)
        {
            if (IsBusy) TryCollectBlocking(out _);
            Schedule(pixels);
            TryCollectBlocking(out var f);
            return f;
        }

        protected abstract void TryCollectBlocking(out float[] feature);

        public static void Normalize(float[] v)
        {
            double sum = 0;
            for (int i = 0; i < v.Length; i++) sum += v[i] * v[i];
            float inv = sum > 1e-12 ? (float)(1.0 / Math.Sqrt(sum)) : 0f;
            for (int i = 0; i < v.Length; i++) v[i] *= inv;
        }
    }

    /// <summary>
    /// MobileNetV2（ImageNet で学習済み、ONNX model zoo の mobilenetv2-12）の最後の全体平均プーリングの出力（1280 次元）を特徴量にする。
    /// 分類の結果（1000 クラス）は使わず、「見た目の近さ」だけを使うので、ImageNet にない物でも登録できる。
    /// Beam Pro ではコンピュートシェーダーを避けて CPU（Burst）で動かす。
    /// </summary>
    public class MobileNetExtractor : FeatureExtractor
    {
        static readonly float[] k_Mean = { 0.485f, 0.456f, 0.406f };
        static readonly float[] k_Std = { 0.229f, 0.224f, 0.225f };

        readonly Worker m_Worker;
        readonly float[] m_Input = new float[3 * InputSize * InputSize];
        Tensor<float> m_InputTensor;
        bool m_Busy;
        readonly string m_OutputInfo;

        public override string Name => "mobilenetv2-gap";
        public override bool IsBusy => m_Busy;
        public string OutputInfo => m_OutputInfo;

        public MobileNetExtractor(ModelAsset asset, BackendType backend = BackendType.CPU)
        {
            var model = ModelLoader.Load(asset);
            // 全体平均プーリング（GlobalAveragePool）の出力を取り出す。見つからなければ元の出力（1000 クラスのスコア）を使う
            int poolOutput = -1;
            foreach (var layer in model.layers)
            {
                if (layer.GetType().Name == "GlobalAveragePool")
                    poolOutput = layer.outputs[0];
            }
            if (poolOutput >= 0)
            {
                model.outputs.Clear();
                model.AddOutput("features", poolOutput);
                m_OutputInfo = "GlobalAveragePool";
            }
            else
            {
                m_OutputInfo = "logits";
                Debug.LogWarning("[CaptionApp] GlobalAveragePool が見つからないので、分類スコアを特徴量に使います");
            }
            m_Worker = new Worker(model, backend);
            m_InputTensor = new Tensor<float>(new TensorShape(1, 3, InputSize, InputSize));
        }

        public override void Schedule(Color32[] pixels)
        {
            // NCHW、上の行から。ImageNet の平均・標準偏差で正規化する
            int n = InputSize * InputSize;
            for (int y = 0; y < InputSize; y++)
            {
                int srcRow = (InputSize - 1 - y) * InputSize; // Unity の並びは下の行から
                int dstRow = y * InputSize;
                for (int x = 0; x < InputSize; x++)
                {
                    var c = pixels[srcRow + x];
                    int i = dstRow + x;
                    m_Input[i] = (c.r / 255f - k_Mean[0]) / k_Std[0];
                    m_Input[n + i] = (c.g / 255f - k_Mean[1]) / k_Std[1];
                    m_Input[2 * n + i] = (c.b / 255f - k_Mean[2]) / k_Std[2];
                }
            }
            m_InputTensor.Upload(m_Input);
            m_Worker.Schedule(m_InputTensor);
            m_Busy = true;
        }

        public override bool TryCollect(out float[] feature)
        {
            feature = null;
            if (!m_Busy) return false;
            var output = m_Worker.PeekOutput() as Tensor<float>;
            if (output == null) { m_Busy = false; return false; }
            if (!output.IsReadbackRequestDone()) return false;
            feature = output.DownloadToArray();
            Normalize(feature);
            m_Busy = false;
            return true;
        }

        protected override void TryCollectBlocking(out float[] feature)
        {
            feature = null;
            if (!m_Busy) return;
            var output = m_Worker.PeekOutput() as Tensor<float>;
            m_Busy = false;
            if (output == null) return;
            feature = output.DownloadToArray();
            Normalize(feature);
        }

        public override void Dispose()
        {
            m_Worker?.Dispose();
            m_InputTensor?.Dispose();
            m_InputTensor = null;
        }
    }

    /// <summary>
    /// モデルがないときの代わり：色（HSV）のヒストグラムと、4×4 マスごとの輪郭の向きのヒストグラム。
    /// 色や模様がはっきり違う物なら見分けられる。その場で計算するので Schedule の時点で結果ができる。
    /// </summary>
    public class SimpleFeatureExtractor : FeatureExtractor
    {
        const int k_HueBins = 12, k_SatBins = 3, k_ValBins = 3;
        const int k_Grid = 4, k_OriBins = 8;
        const int k_Step = 2; // 112×112 に間引いて計算する

        float[] m_Result;
        public override string Name => "simple-hist";
        public override bool IsBusy => m_Result != null;

        public override void Schedule(Color32[] pixels) => m_Result = Compute(pixels);

        public override bool TryCollect(out float[] feature)
        {
            feature = m_Result;
            m_Result = null;
            return feature != null;
        }

        protected override void TryCollectBlocking(out float[] feature) => TryCollect(out feature);

        public override void Dispose() { }

        static float[] Compute(Color32[] px)
        {
            int colorDim = k_HueBins * k_SatBins * k_ValBins;
            var f = new float[colorDim + k_Grid * k_Grid * k_OriBins];
            int s = InputSize;
            for (int y = k_Step; y < s - k_Step; y += k_Step)
            {
                for (int x = k_Step; x < s - k_Step; x += k_Step)
                {
                    var c = px[y * s + x];
                    Color.RGBToHSV(c, out float h, out float sat, out float val);
                    int hb = Mathf.Min((int)(h * k_HueBins), k_HueBins - 1);
                    int sb = Mathf.Min((int)(sat * k_SatBins), k_SatBins - 1);
                    int vb = Mathf.Min((int)(val * k_ValBins), k_ValBins - 1);
                    f[(hb * k_SatBins + sb) * k_ValBins + vb] += 1f;

                    float gx = Luma(px[y * s + x + k_Step]) - Luma(px[y * s + x - k_Step]);
                    float gy = Luma(px[(y + k_Step) * s + x]) - Luma(px[(y - k_Step) * s + x]);
                    float mag = Mathf.Sqrt(gx * gx + gy * gy);
                    if (mag < 8f) continue;
                    float ang = Mathf.Atan2(gy, gx);
                    if (ang < 0) ang += Mathf.PI;
                    int ob = Mathf.Min((int)(ang / Mathf.PI * k_OriBins), k_OriBins - 1);
                    int cell = (y * k_Grid / s) * k_Grid + (x * k_Grid / s);
                    f[colorDim + cell * k_OriBins + ob] += mag / 64f;
                }
            }
            // 色と輪郭の重みをそろえてから全体を正規化する
            NormalizeRange(f, 0, colorDim);
            NormalizeRange(f, colorDim, f.Length);
            Normalize(f);
            return f;
        }

        static float Luma(Color32 c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;

        static void NormalizeRange(float[] v, int from, int to)
        {
            double sum = 0;
            for (int i = from; i < to; i++) sum += v[i] * v[i];
            if (sum < 1e-12) return;
            float inv = (float)(1.0 / Math.Sqrt(sum));
            for (int i = from; i < to; i++) v[i] *= inv;
        }
    }
}
