using System;
using System.Runtime.InteropServices;
using UnityEngine;
using Unity.XR.XREAL;

namespace XrealCaption
{
    /// <summary>
    /// 認識に使うカメラ画像の取り出し口。
    /// どのカメラでも、画像の中央の正方形（短い辺 × CropFraction）を size×size に縮めて渡す。
    /// 出力の並びは Unity の Texture2D と同じ（左下が原点、行は下から上）。
    /// </summary>
    public abstract class FrameSource : IDisposable
    {
        /// <summary>画面に出す名前（例：XREAL Eye）。</summary>
        public abstract string Name { get; }
        public abstract bool IsRunning { get; }
        /// <summary>解像度などの補足（Beam Pro 画面に出す）。</summary>
        public virtual string Status => IsRunning ? $"{Width}×{Height}" : "停止中";

        /// <summary>元の画像の幅・高さ（ピクセル）。</summary>
        protected abstract int Width { get; }
        protected abstract int Height { get; }

        /// <summary>元の画像のピクセル（x は左から、y は上から）。</summary>
        protected abstract Color32 ReadPixel(int x, int y);

        /// <summary>新しいフレームを取り込めたら true（以降の ReadPixel はそのフレームを読む）。</summary>
        protected abstract bool AcquireFrame();

        /// <summary>映像の回転（0 / 90 / 180 / 270 度、時計回り）。カメラの付き方に合わせて設定画面で変える。</summary>
        public int Rotation { get; set; }

        public abstract void Start();
        public abstract void Stop();
        public void Dispose() => Stop();

        /// <summary>
        /// 新しいフレームがあれば、中央の正方形を size×size で dst に書き込んで true を返す。
        /// cropFraction は短い辺に対する正方形の一辺の割合（小さいほど拡大）。
        /// </summary>
        public bool TryGetFrame(Color32[] dst, int size, float cropFraction)
        {
            if (!IsRunning || !AcquireFrame()) return false;
            int w = Width, h = Height;
            if (w <= 0 || h <= 0) return false;

            float side = Mathf.Min(w, h) * Mathf.Clamp(cropFraction, 0.1f, 1f);
            float x0 = (w - side) * 0.5f;
            float y0 = (h - side) * 0.5f;
            int rot = ((Rotation % 360) + 360) % 360;

            // 1 ピクセルあたり 2×2 点を平均する（縮小時のちらつきを抑える）
            for (int oy = 0; oy < size; oy++)
            {
                for (int ox = 0; ox < size; ox++)
                {
                    int r = 0, g = 0, b = 0;
                    for (int sy = 0; sy < 2; sy++)
                    for (int sx = 0; sx < 2; sx++)
                    {
                        // 出力の座標（s：左→右、t：上→下、0〜1）
                        float s = (ox + 0.25f + sx * 0.5f) / size;
                        float t = 1f - (oy + 0.25f + sy * 0.5f) / size;
                        Rotate(rot, ref s, ref t);
                        int px = Mathf.Clamp((int)(x0 + s * side), 0, w - 1);
                        int py = Mathf.Clamp((int)(y0 + t * side), 0, h - 1);
                        var c = ReadPixel(px, py);
                        r += c.r; g += c.g; b += c.b;
                    }
                    dst[oy * size + ox] = new Color32((byte)(r >> 2), (byte)(g >> 2), (byte)(b >> 2), 255);
                }
            }
            return true;
        }

        /// <summary>出力の (s, t) を、元の画像を時計回りに rot 度回して見たときの座標に変える。</summary>
        static void Rotate(int rot, ref float s, ref float t)
        {
            float u = s, v = t;
            switch (rot)
            {
                case 90: s = v; t = 1f - u; break;
                case 180: s = 1f - u; t = 1f - v; break;
                case 270: s = 1f - v; t = u; break;
            }
        }
    }

    /// <summary>
    /// XREAL Eye（眼鏡に付ける RGB カメラ）。SDK の XREALPlugin から YUV420 の各面を直接読む。
    /// XREALRGBCameraTexture は GPU テクスチャを作るだけなので使わず、CPU 側に必要な分だけコピーする。
    /// 面の並びは SDK の XREALRGBCameraTexture と同じく 0=Y、1=V、2=U として扱う（要実機確認：色が入れ替わっていたら k_UPlane / k_VPlane を入れ替える）。
    /// </summary>
    public class XrealEyeFrameSource : FrameSource
    {
        const int k_UPlane = 2;
        const int k_VPlane = 1;

        bool m_Running;
        ulong m_LastTimestamp;
        Vector2Int m_Resolution;
        Vector2Int m_SizeY, m_SizeU, m_SizeV;
        byte[] m_Y = Array.Empty<byte>();
        byte[] m_U = Array.Empty<byte>();
        byte[] m_V = Array.Empty<byte>();
        bool m_LoggedLayout;
        string m_Error = "";

        public override string Name => "XREAL Eye";
        public override bool IsRunning => m_Running;
        public override string Status => !string.IsNullOrEmpty(m_Error) ? m_Error
            : m_Resolution.x > 0 ? $"{m_Resolution.x}×{m_Resolution.y}" : (m_Running ? "フレーム待ち" : "停止中");
        protected override int Width => m_Resolution.x;
        protected override int Height => m_Resolution.y;

        public override void Start()
        {
            if (m_Running) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                ulong result = XREALPlugin.StartRGBCameraDataCapture();
                m_Running = result != ulong.MaxValue;
                m_Error = m_Running ? "" : "開始できません（XREAL Eye が付いているか確認）";
            }
            catch (Exception e)
            {
                m_Running = false;
                m_Error = "開始できません";
                Debug.LogWarning($"[CaptionApp] XREAL Eye の開始に失敗: {e.Message}");
            }
#else
            m_Error = "XREAL Eye は実機（Beam Pro）でだけ使えます";
#endif
            Debug.Log($"[CaptionApp] XREAL Eye start: running={m_Running} {m_Error}");
        }

        public override void Stop()
        {
            if (!m_Running) return;
            m_Running = false;
#if UNITY_ANDROID && !UNITY_EDITOR
            try { XREALPlugin.StopRGBCameraDataCapture(); }
            catch (Exception e) { Debug.LogWarning($"[CaptionApp] XREAL Eye の停止に失敗: {e.Message}"); }
#endif
        }

        protected override bool AcquireFrame()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            ulong ts = 0;
            if (!XREALPlugin.TryGetRGBCameraFrame(ref ts)) return false;
            int handle = 0;
            var res = Vector2Int.zero;
            if (!XREALPlugin.TryAcquireLatestImage(ref handle, ref res, ref ts)) return false;
            try
            {
                if (ts == m_LastTimestamp) return false;
                m_LastTimestamp = ts;
                m_Resolution = res;
                if (!CopyPlane(handle, 0, ref m_Y, out m_SizeY)) return false;
                if (!CopyPlane(handle, k_UPlane, ref m_U, out m_SizeU)) return false;
                if (!CopyPlane(handle, k_VPlane, ref m_V, out m_SizeV)) return false;
                if (!m_LoggedLayout)
                {
                    m_LoggedLayout = true;
                    Debug.Log($"[CaptionApp] XREAL Eye frame {res.x}x{res.y} Y={m_SizeY} U={m_SizeU} V={m_SizeV}");
                }
                return true;
            }
            finally
            {
                XREALPlugin.DisposeRGBCameraDataHandle(handle);
            }
#else
            return false;
#endif
        }

        static bool CopyPlane(int handle, int plane, ref byte[] buffer, out Vector2Int size)
        {
            if (!XREALPlugin.TryGetRGBCameraDataPlane(handle, plane, out IntPtr ptr, out size) || ptr == IntPtr.Zero)
                return false;
            int length = size.x * size.y;
            if (length <= 0) return false;
            if (buffer.Length != length) buffer = new byte[length];
            Marshal.Copy(ptr, buffer, 0, length);
            return true;
        }

        protected override Color32 ReadPixel(int x, int y)
        {
            // 面の大きさから位置を割り出す（Y は全解像度、U・V は縦横半分の想定）
            int iy = y * m_SizeY.x + x * m_SizeY.x / m_Resolution.x;
            int cx = x * m_SizeU.x / m_Resolution.x;
            int cy = y * m_SizeU.y / m_Resolution.y;
            int iu = cy * m_SizeU.x + cx;
            int iv = (y * m_SizeV.y / m_Resolution.y) * m_SizeV.x + x * m_SizeV.x / m_Resolution.x;
            if (iy >= m_Y.Length || iu >= m_U.Length || iv >= m_V.Length) return new Color32(0, 0, 0, 255);
            return YuvToRgb(m_Y[iy], m_U[iu], m_V[iv]);
        }

        /// <summary>BT.601 の YUV → RGB。</summary>
        public static Color32 YuvToRgb(byte y, byte u, byte v)
        {
            float fy = y, fu = u - 128f, fv = v - 128f;
            int r = (int)(fy + 1.402f * fv);
            int g = (int)(fy - 0.344f * fu - 0.714f * fv);
            int b = (int)(fy + 1.772f * fu);
            return new Color32((byte)Mathf.Clamp(r, 0, 255), (byte)Mathf.Clamp(g, 0, 255), (byte)Mathf.Clamp(b, 0, 255), 255);
        }
    }

    /// <summary>
    /// 端末のカメラ（WebCamTexture）。エディタでは Mac のカメラ、Beam Pro では背面カメラ。
    /// XREAL Eye がないときの代わりと、エディタでの動作確認に使う。
    /// </summary>
    public class WebCamFrameSource : FrameSource
    {
        WebCamTexture m_Texture;
        Color32[] m_Pixels = Array.Empty<Color32>();
        int m_W, m_H;
        string m_Error = "";

        public override string Name => "端末のカメラ";
        public override bool IsRunning => m_Texture != null && m_Texture.isPlaying;
        public override string Status => !string.IsNullOrEmpty(m_Error) ? m_Error : base.Status;
        protected override int Width => m_W;
        protected override int Height => m_H;

        public override void Start()
        {
            if (m_Texture != null) return;
            var devices = WebCamTexture.devices;
            if (devices.Length == 0)
            {
                m_Error = "カメラが見つかりません";
                return;
            }
            // スマホでは背面カメラを優先する
            string name = devices[0].name;
            foreach (var d in devices)
            {
                if (!d.isFrontFacing) { name = d.name; break; }
            }
            m_Texture = new WebCamTexture(name, 640, 480, 30);
            m_Texture.Play();
            m_Error = "";
            Debug.Log($"[CaptionApp] WebCam start: {name}");
        }

        public override void Stop()
        {
            if (m_Texture == null) return;
            m_Texture.Stop();
            UnityEngine.Object.Destroy(m_Texture);
            m_Texture = null;
        }

        protected override bool AcquireFrame()
        {
            if (m_Texture == null || !m_Texture.didUpdateThisFrame || m_Texture.width < 32) return false;
            m_W = m_Texture.width;
            m_H = m_Texture.height;
            if (m_Pixels.Length != m_W * m_H) m_Pixels = new Color32[m_W * m_H];
            m_Texture.GetPixels32(m_Pixels);
            return true;
        }

        protected override Color32 ReadPixel(int x, int y)
        {
            // WebCamTexture は下の行から並んでいる
            return m_Pixels[(m_H - 1 - y) * m_W + x];
        }
    }

    /// <summary>テスト用：決めた画像を、毎回新しいフレームとして流し続ける。</summary>
    public class StillFrameSource : FrameSource
    {
        Color32[] m_Pixels = Array.Empty<Color32>();
        int m_W, m_H;
        bool m_Running;

        public override string Name => "テスト画像";
        public override bool IsRunning => m_Running;
        protected override int Width => m_W;
        protected override int Height => m_H;

        public override void Start() => m_Running = true;
        public override void Stop() => m_Running = false;

        /// <summary>画像を差し替える（pixels は Unity の並び：左下原点）。</summary>
        public void SetImage(Color32[] pixels, int width, int height)
        {
            m_Pixels = pixels;
            m_W = width;
            m_H = height;
        }

        protected override bool AcquireFrame() => m_Pixels.Length > 0;

        protected override Color32 ReadPixel(int x, int y) => m_Pixels[(m_H - 1 - y) * m_W + x];
    }
}
