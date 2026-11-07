using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace XrealCaption
{
    /// <summary>
    /// パネル（文字入力・確認ダイアログ）の開閉直後のタップを無視するボタン。
    /// 実機で、候補ボタンを押して入力パネルが閉じた同じタッチが、下にある青いボタン（確定）にも届いて
    /// 2回目の配置が始まってしまったため（2026-10-25 実機確認）。
    /// </summary>
    public class GuardedButton : Button
    {
        const float k_GuardSeconds = 0.4f;
        static float s_IgnoreUntil;

        /// <summary>パネルの表示が切り替わったときに呼ぶ。しばらくのあいだタップを無視する。</summary>
        public static void BlockClicksBriefly() => s_IgnoreUntil = Time.unscaledTime + k_GuardSeconds;

        public override void OnPointerClick(PointerEventData eventData)
        {
            if (Time.unscaledTime < s_IgnoreUntil) return;
            base.OnPointerClick(eventData);
        }

        public override void OnSubmit(BaseEventData eventData)
        {
            if (Time.unscaledTime < s_IgnoreUntil) return;
            base.OnSubmit(eventData);
        }
    }
}
