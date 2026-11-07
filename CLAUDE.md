# XREAL 物体キャプションアプリ

XREAL One＋XREAL Eye＋Beam Pro で、登録した物をカメラで見つけたら、その物のキャプション（名前と説明文）を眼鏡に出す Unity アプリ。
3DoF の頭部固定表示。土台（XR 設定・入力・Beam Pro 画面の作り）は `../XREAL_label_3DoF_3types` と同じ。

## フォルダ構成

```
XREAL_ObjectCaption/
  CLAUDE.md / README.md      ← README.md がアプリの説明の本体
  .gitignore
  com.xreal.xr.tar.gz        ← XREAL SDK 3.1.0（../XREAL_label_3DoF_3types のものへのハードリンク、git 管理外）
  model_img/<物のID>/*.HEIC  ← 認識したい物の元の写真（ユーザーが置いたもの）
  caption_img/*.HEIC         ← キャプションの元にした説明の紙（文だけ参考にする）
  Tools/download_model.sh    ← MobileNetV2 の ONNX を取得する
  XrealCaptionApp/           ← Unity プロジェクト（Unity 6000.0.58f2）
    Assets/Scripts/          ← アプリのコード（XrealCaption.asmdef）
    Assets/Editor/           ← ProjectSetup.cs（シーン・XR・Player 設定）、AndroidBuild.cs
    Assets/Tests/            ← PlayMode テスト（CaptionAppTests.cs）
    Assets/Models/           ← mobilenetv2-12.onnx（git 管理外）
    Assets/Resources/Presets/← 同梱の写真（512px の JPEG を .bytes にしたもの）と presets.json（名前・キャプション）
```

## 決まりごと

- ドキュメントとコメントは日本語
- 同梱する物を増やすときは、写真を `sips -s format jpeg -Z 512` で縮小して `Assets/Resources/Presets/<ID>_<元の名前>.bytes` に置き、`presets.json` に足す
- 特徴量の作り方（抽出器）を変えたら `FeatureExtractor.Name` も変える。保存データの抽出器名が違うと自動で作り直す
- SDK、モデル、ログ、APK はコミットしない。`.cs` などを追加・削除したら `.meta` もそろえる

## ビルドとテスト

バッチモードの Unity は `open -W -n -a` で起動する（実行ファイルを直接起動すると、初回のアセット取り込みで macOS のアイコン取得が落ちる。2026-11-07 確認）。

```bash
cd XrealCaptionApp
# 初期設定（シーン生成など）
open -W -n -a "/Applications/Unity/Hub/Editor/6000.0.58f2/Unity.app" --args -batchmode -quit -projectPath "$PWD" -executeMethod XrealCaption.EditorTools.ProjectSetup.Run -logFile "$PWD/Logs/setup.log"
# APK（Builds/XrealCaption.apk）
open -W -n -a "/Applications/Unity/Hub/Editor/6000.0.58f2/Unity.app" --args -batchmode -quit -buildTarget Android -projectPath "$PWD" -executeMethod XrealCaption.EditorTools.AndroidBuild.Run -logFile "$PWD/Logs/build.log"
```

PlayMode テストは README の「エディタでの確認」を参照。どのコマンドも、このMacでは HID クラッシュを避けるため、実行中だけ `ProjectSettings.asset` の `activeInputHandler` を 0 にして、終わったら 1 に戻す。
