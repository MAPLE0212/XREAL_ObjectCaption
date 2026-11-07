using System.Collections.Generic;
using System.IO;
using Unity.XR.CoreUtils;
using Unity.XR.XREAL;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;
using TrackedPoseDriver = UnityEngine.InputSystem.XR.TrackedPoseDriver;

namespace XrealCaption.EditorTools
{
    /// <summary>
    /// プロジェクトの初期設定を一括で行う（メニュー：XREAL Caption → Setup Project）。
    /// ・シーン Assets/Scenes/Main.unity（XR Origin＋CaptionApp）
    /// ・XR Plug-in Management（Android で XREAL を有効、3DoF）
    /// ・Android の Player 設定（XREAL SDK の要件：API 29+ / IL2CPP / ARM64 / OpenGLES3）
    /// バッチモード：Unity -batchmode -quit -projectPath . -executeMethod XrealCaption.EditorTools.ProjectSetup.Run
    /// </summary>
    public static class ProjectSetup
    {
        const string k_ScenePath = "Assets/Scenes/Main.unity";
        public const string ModelPath = "Assets/Models/mobilenetv2-12.onnx";

        [MenuItem("XREAL Caption/Setup Project")]
        public static void Run()
        {
            ConfigurePlayer();
            ConfigureXR();
            BuildScene();
            AssetDatabase.SaveAssets();
            Debug.Log("[ProjectSetup] 完了");
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "mp";
            PlayerSettings.productName = "XREAL Object Caption";
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.mp.xrealobjectcaption");
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
            // Beam Pro の画面UIは縦長で作っている
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;

            // 入力は Input System を使う（XREAL SDK の仮想コントローラーも Input System 前提）
            var playerSettings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (playerSettings.Length > 0)
            {
                var so = new SerializedObject(playerSettings[0]);
                var prop = so.FindProperty("activeInputHandler");
                if (prop != null)
                {
                    prop.intValue = 1; // 1 = Input System Package (New)
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }

        static void ConfigureXR()
        {
            Directory.CreateDirectory("Assets/XR");

            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget) || perTarget == null)
            {
                perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
            }

            foreach (var group in new[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone })
            {
                if (!perTarget.HasSettingsForBuildTarget(group))
                    perTarget.CreateDefaultSettingsForBuildTarget(group);
                if (!perTarget.HasManagerSettingsForBuildTarget(group))
                    perTarget.CreateDefaultManagerSettingsForBuildTarget(group);
            }

            // Android：XREAL を有効にして起動時に初期化する
            var android = perTarget.SettingsForBuildTarget(BuildTargetGroup.Android);
            android.InitManagerOnStart = true;
            XRPackageMetadataStore.AssignLoader(android.Manager, typeof(XREALXRLoader).FullName, BuildTargetGroup.Android);
            EditorUtility.SetDirty(android);

            // XREAL の設定：3DoF、XREAL One（VISION）を対象に含める
            if (!EditorBuildSettings.TryGetConfigObject(XREALSettings.k_SettingsKey, out XREALSettings xreal) || xreal == null)
            {
                // ローダー登録時に XR Management が作った設定があればそれを使う
                var guids = AssetDatabase.FindAssets("t:" + nameof(XREALSettings));
                if (guids.Length > 0)
                {
                    xreal = AssetDatabase.LoadAssetAtPath<XREALSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));
                }
                else
                {
                    Directory.CreateDirectory("Assets/XR/Settings");
                    xreal = ScriptableObject.CreateInstance<XREALSettings>();
                    AssetDatabase.CreateAsset(xreal, "Assets/XR/Settings/XREALSettings.asset");
                }
                EditorBuildSettings.AddConfigObject(XREALSettings.k_SettingsKey, xreal, true);
            }
            xreal.InitialTrackingType = TrackingType.MODE_3DOF;
            xreal.SupportDevices = new List<XREALDeviceCategory>
            {
                XREALDeviceCategory.XREAL_DEVICE_CATEGORY_VISION,
                XREALDeviceCategory.XREAL_DEVICE_CATEGORY_REALITY,
            };
#if UNITY_ANDROID
            xreal.InitialInputSource = InputSource.Controller;
#endif
            EditorUtility.SetDirty(xreal);
        }

        static void BuildScene()
        {
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // XR Origin（3DoF なので Device 原点・高さオフセットなし）
            var originGo = new GameObject("XR Origin");
            var offsetGo = new GameObject("Camera Offset");
            offsetGo.transform.SetParent(originGo.transform, false);
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(offsetGo.transform, false);

            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black; // OST では黒が透明
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 100f;
            cam.fieldOfView = 25.6f; // エディタ用：XREAL One の縦画角の目安（実機ではXRが上書き）
            camGo.AddComponent<AudioListener>();

            var tpd = camGo.AddComponent<TrackedPoseDriver>();
            tpd.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            tpd.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            tpd.positionInput = new InputActionProperty(new InputAction("Position", InputActionType.Value, "<XRHMD>/centerEyePosition", expectedControlType: "Vector3"));
            tpd.rotationInput = new InputActionProperty(new InputAction("Rotation", InputActionType.Value, "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion"));
            tpd.trackingStateInput = new InputActionProperty(new InputAction("TrackingState", InputActionType.Value, "<XRHMD>/trackingState", expectedControlType: "Integer"));

            var origin = originGo.AddComponent<XROrigin>();
            origin.Camera = cam;
            origin.CameraFloorOffsetObject = offsetGo;
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;
            origin.CameraYOffset = 0f;

            var appGo = new GameObject("CaptionApp");
            var app = appGo.AddComponent<CaptionApp>();
            var so = new SerializedObject(app);
            so.FindProperty("m_Camera").objectReferenceValue = cam;
            // 認識モデル（なければ色と輪郭のヒストグラムで動く）
            var model = AssetDatabase.LoadAssetAtPath<Unity.InferenceEngine.ModelAsset>(ModelPath);
            if (model == null)
                Debug.LogWarning($"[ProjectSetup] {ModelPath} がありません。Tools/download_model.sh で取得してください");
            so.FindProperty("m_Model").objectReferenceValue = model;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, k_ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(k_ScenePath, true) };
        }
    }
}
