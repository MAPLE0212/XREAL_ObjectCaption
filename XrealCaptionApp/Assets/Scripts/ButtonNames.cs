using Unity.XR.XREAL;

namespace XrealCaption
{
    /// <summary>
    /// ボタンの呼び方をまとめる。眼鏡（XREAL One）のボタンは XREAL 公式チュートリアルの呼び方
    /// 「X Button」「+/- Button」「Quick Button」に合わせる（日本語の公式名は見つからなかったので英語名を使う）。
    /// https://tutorials.xreal.com/docs/glasses/one-series/first-use/buttons
    ///
    /// 位置はすべて右のつる：Xボタン＝下側の赤いボタン、+/-ボタン＝下側、Quickボタン＝上側の短いボタン（公式ユーザーガイド）。
    ///
    /// SDK のキー種別との対応（2026-10-26 実機で確認）：
    ///  MULTI_KEY    → Xボタン（長押しで眼鏡のリセンター、ダブルクリックで OSD メニュー＝公式の X Button の動きと一致）
    ///  INCREASE_KEY → +/-ボタンの +
    ///  DECREASE_KEY → +/-ボタンの −
    ///  MENU_KEY     → Quickボタン（残る1つから推定。要確認）
    /// Beam Pro の仮想コントローラーは公式ドキュメント（Controller）の Trigger / Home / App に合わせる。
    /// </summary>
    public static class ButtonNames
    {
        /// <summary>画面に出す呼び方（例：Xボタン）。</summary>
        public static string Glasses(XREALKeyType key) => key switch
        {
            XREALKeyType.MULTI_KEY => "Xボタン",
            XREALKeyType.INCREASE_KEY => "+/-ボタンの +",
            XREALKeyType.DECREASE_KEY => "+/-ボタンの −",
            XREALKeyType.MENU_KEY => "Quickボタン",
            _ => key.ToString(),
        };

        /// <summary>ログ（CSV の detail 列）に入れる英字の呼び方（例：X）。</summary>
        public static string GlassesId(XREALKeyType key) => key switch
        {
            XREALKeyType.MULTI_KEY => "X",
            XREALKeyType.INCREASE_KEY => "Plus",
            XREALKeyType.DECREASE_KEY => "Minus",
            XREALKeyType.MENU_KEY => "Quick",
            _ => key.ToString(),
        };

        public static string Click(XREALClickType click) => click switch
        {
            XREALClickType.CLICK => "クリック",
            XREALClickType.DOUBLE_CLICK => "ダブルクリック",
            XREALClickType.LONG_PRESS => "長押し",
            _ => click.ToString(),
        };

        public static string ClickId(XREALClickType click) => click switch
        {
            XREALClickType.CLICK => "Click",
            XREALClickType.DOUBLE_CLICK => "DoubleClick",
            XREALClickType.LONG_PRESS => "LongPress",
            _ => click.ToString(),
        };

        /// <summary>Beam Pro の仮想コントローラーのボタン（例：TriggerButton → Trigger）。</summary>
        public static string VirtualController(XREALButtonType button) => button switch
        {
            XREALButtonType.TriggerButton => "Trigger",
            _ => button.ToString().Replace("Button", ""),
        };

        /// <summary>確定に使う眼鏡のボタン（案内文に使う）。</summary>
        public const string ConfirmGlassesButton = "Xボタン";
    }
}
