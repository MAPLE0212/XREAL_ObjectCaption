using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using Unity.XR.XREAL;

namespace XrealCaption
{
    /// <summary>
    /// 「確定」入力をまとめる。
    /// ・第一案：眼鏡の Xボタン（XREALCallbackHandler.OnXREALGlassesKeyClick）
    /// ・代替案：Beam Pro のタッチパッド（XREALVirtualController の Trigger）
    /// ・Beam Pro 画面の大きな青いボタン（PhoneUI 側）
    /// ・エディタ：Space キー
    /// ボタンの呼び方は ButtonNames を参照。届いたボタン入力はすべて key_press として記録する。
    /// </summary>
    public class InputRouter : MonoBehaviour
    {
        /// <summary>
        /// 確定として扱う眼鏡のボタン。実機（2026-10-26）では X / +/- / Quickボタンのすべてがアプリに届いたが、
        /// +/-ボタンは眼鏡の明るさなども変わるため誤操作になる。Xボタン（MULTI_KEY）のクリックだけを確定に使う。
        /// </summary>
        [SerializeField]
        XREALKeyType[] m_ConfirmKeys = { XREALKeyType.MULTI_KEY };

        /// <summary>確定入力。引数は入力元の ID（例：Glasses_X_Click。ログの detail 列に入る）。</summary>
        public event Action<string> Confirm;
        /// <summary>確定以外も含め、届いたボタン入力すべて。引数は（ログ用の ID, 画面に出す呼び方）。</summary>
        public event Action<string, string> KeyPressed;

        bool m_ControllerHooked;

        void OnEnable()
        {
            XREALCallbackHandler.OnXREALGlassesKeyClick += OnGlassesKeyClick;
        }

        void OnDisable()
        {
            XREALCallbackHandler.OnXREALGlassesKeyClick -= OnGlassesKeyClick;
            if (m_ControllerHooked && XREALVirtualController.Singleton != null)
                XREALVirtualController.Singleton.pointerDown -= OnVirtualControllerDown;
            m_ControllerHooked = false;
        }

        void Update()
        {
            // XREALVirtualController はシーン読み込み前にSDKが生成するので、見つかった時点で購読する
            if (!m_ControllerHooked && XREALVirtualController.Singleton != null)
            {
                XREALVirtualController.Singleton.pointerDown += OnVirtualControllerDown;
                m_ControllerHooked = true;
            }

#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null && kb.spaceKey.wasPressedThisFrame && !IsTypingInUI())
            {
                KeyPressed?.Invoke("Keyboard_Space", "Space キー");
                Confirm?.Invoke("Keyboard_Space");
            }
#endif
        }

        void OnGlassesKeyClick(XREALClickType clickType, XREALKeyType keyType)
        {
            var id = $"Glasses_{ButtonNames.GlassesId(keyType)}_{ButtonNames.ClickId(clickType)}";
            KeyPressed?.Invoke(id, $"眼鏡の{ButtonNames.Glasses(keyType)}（{ButtonNames.Click(clickType)}）");
            if (clickType == XREALClickType.CLICK && Array.IndexOf(m_ConfirmKeys, keyType) >= 0)
                Confirm?.Invoke(id);
        }

        void OnVirtualControllerDown(XREALButtonType buttonType, GameObject target, UnityEngine.EventSystems.PointerEventData data)
        {
            var name = ButtonNames.VirtualController(buttonType);
            var id = $"BeamPro_{name}";
            KeyPressed?.Invoke(id, $"Beam Pro の {name}");
            if (buttonType == XREALButtonType.TriggerButton)
                Confirm?.Invoke(id);
        }

        /// <summary>Beam Pro 画面の大きな青いボタンから呼ぶ。</summary>
        public void ConfirmFromPhoneButton()
        {
            KeyPressed?.Invoke("BeamPro_ConfirmButton", "Beam Pro 画面の青いボタン");
            Confirm?.Invoke("BeamPro_ConfirmButton");
        }

        static bool IsTypingInUI()
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es == null || es.currentSelectedGameObject == null) return false;
            var field = es.currentSelectedGameObject.GetComponent<UnityEngine.UI.InputField>();
            return field != null && field.isFocused;
        }
    }
}
