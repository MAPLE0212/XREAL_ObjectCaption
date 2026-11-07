# XREAL 物体キャプションアプリ

XREAL One＋XREAL Eye＋Beam Pro で、**登録した物をカメラで見つけたら、その物のキャプション（名前と説明文）を眼鏡に出す** Unity アプリ。

- 眼鏡の中央に「認識範囲の枠」があり、物をそこに入れると、枠が緑になって右側にキャプションのカードが出る
- 登録は Beam Pro の画面で行う。名前とキャプションを書いて、物をいくつかの向きから撮るだけ（3〜5 枚）
- 最初から「角間あざみTシャツ」と「角間あざみの（多分）耳」が登録されている（`model_img/` の写真から作成）

## 認識のしくみ

1. カメラの画像の中央の正方形（短い辺 × 認識範囲。既定 50%）を 224×224 に縮める
2. MobileNetV2（ImageNet で学習済み）の最後の全体平均プーリングの出力（1280 次元）を特徴量にする。分類結果は使わないので、ImageNet にない物でも登録できる
3. 登録した物の見本の特徴量とコサイン類似度を比べ、いちばん近い物を選ぶ（物ごとの類似度は見本の中の最大値）
4. 類似度がしきい値（既定 0.60）以上で、2 位の物との差が 0.05 以上なら「一致」とする。同じ物が 2 回続けて一致したらキャプションを出す。一致しなくなってから 1.2 秒で消す
5. 「背景」として撮った画像に一番近いときは、キャプションを出さない（何もない所で誤って出るときに使う）

推論は Unity の Inference Engine（`com.unity.ai.inference` 2.2.2）で、Beam Pro では CPU（Burst）で動かす。推論は前のフレームで始めて次のフレームで受け取るので、眼鏡の描画は止まらない。
モデルがない場合は、色（HSV）と輪郭の向きのヒストグラムで代わりに認識する（色や模様がはっきり違う物向け）。

## 環境

| 項目 | 内容 |
|---|---|
| Unity | 6000.0.58f2（Android Build Support が必要） |
| XREAL SDK | `com.xreal.xr` 3.1.0（`../XREAL_label_3DoF_3types` と同じもの） |
| 主なパッケージ | Inference Engine 2.2.2 / Input System 1.14 / uGUI 2.0 / XR Plug-in Management 4.5 |
| 機材 | XREAL One＋XREAL Eye（RGB カメラ）＋Beam Pro。XREAL Eye がなければ Beam Pro の背面カメラでも動く |
| ビルド先 | Android（Beam Pro）。API 29+ / IL2CPP / ARM64 / OpenGLES3 / 縦画面。パッケージ名 `com.mp.xrealobjectcaption` |

## 初回セットアップ

1. XREAL SDK 3.1.0 の `com.xreal.xr.tar.gz` をこのフォルダの一番上に置く（このマシンでは `../XREAL_label_3DoF_3types` のものをハードリンクしてある）
2. モデルを取得する（このマシンでは取得済み）
   ```bash
   Tools/download_model.sh
   ```
3. Unity Hub で `XrealCaptionApp` を開く。必要ならメニュー **XREAL Caption → Setup Project**（シーン `Assets/Scenes/Main.unity` と XR・Player 設定を作り直す）
4. File → Build Profiles で Android に切り替え、**XREAL Caption → Build Android APK**。`Builds/XrealCaption.apk` ができる（2026-11-07 にこの手順でビルドが通ることを確認済み。約 140MB）
5. Beam Pro に入れる
   ```bash
   adb install -r XrealCaptionApp/Builds/XrealCaption.apk
   ```

**眼鏡をつないでから起動する。** 眼鏡なしで起動したアプリが裏に残っていると、眼鏡をつないでから起動しても古いアプリに戻るだけで眼鏡につながらない（2026-11-07 実機確認）。そのときは Beam Pro の履歴から閉じるか、`adb shell am force-stop com.mp.xrealobjectcaption` で終了させてから起動し直す。

初回起動時はカメラの許可を求められる。また、同梱の写真から特徴量を作るため、最初の起動だけ数秒かかる。

## 使い方

| やること | 操作 |
|---|---|
| 物を見る | 物を眼鏡の中央の枠に入れる。認識すると枠が緑になり、右にキャプションが出る |
| 新しい物を登録 | Beam Pro の「＋ 新しい物を登録」→ 名前とキャプションを入力して OK → 物を枠に入れて **眼鏡の Xボタン**（または画面の「撮影」）で撮影。向き・距離を変えて 3〜5 枚 →「完了」 |
| 撮り足す | 一覧の「撮り足す」。同梱の写真だけだと実際のカメラの見え方と違うので、**実機では最初に 2 つとも数枚ずつ撮り足すのがおすすめ** |
| 誤って出るのを防ぐ | 「背景を撮る」で、何も置いていない机や壁を何枚か撮る。背景に近いときはキャプションを出さない |
| 編集・削除 | 一覧の「編集」「削除」 |
| しきい値 | 「しきい値 − ＋」（0.02 刻み）。誤って出るなら上げる、出にくいなら下げる |
| 認識範囲 | 「認識範囲 − ＋」（10% 刻み）。小さいほど中央を拡大して見る。眼鏡の枠の大きさも変わる |
| カメラ | 「カメラ：XREAL Eye ⇄ 端末のカメラ」 |
| 映像の回転 | Beam Pro 画面の映像が横倒し・逆さのときに 90° ずつ回す（認識もこの向きで行う） |
| 最初の状態に戻す | 「同梱の写真だけに戻す」（自分で登録した物・撮った見本は消える） |

Beam Pro の画面の上には、認識に使っている範囲そのものの映像と、「いちばん近い物・類似度・2 位との差」が出る。しきい値を決めるときの目安にする。

エディタでは Game ビューの左が眼鏡、右が Beam Pro の画面になり、カメラは Mac のカメラを使う。撮影は Space キーでもできる。

## 同梱の物（プリセット）

`XrealCaptionApp/Assets/Resources/Presets/presets.json` に名前とキャプション、同じフォルダに写真（512px の JPEG。拡張子 `.bytes`）がある。
写真 1 枚につき、切り抜き 2 種類（100% / 70%）× 回転 4 種類 = 8 個の見本を作る。キャプションは `caption_img/` の説明の紙の文をもとに、眼鏡で読みやすい長さに直した。

| ID | 名前 | 写真 |
|---|---|---|
| azamiT | 角間あざみTシャツ | `model_img/azamiT/` の 3 枚 |
| azaminomimi | 角間あざみの（多分）耳 | `model_img/azaminomimi/` の 3 枚 |

物を増やすときは、写真を縮小して置き、`presets.json` に足す（すでに起動したことのある端末では「同梱の写真だけに戻す」で読み直す）。

```bash
sips -s format jpeg -Z 512 model_img/<ID>/IMG_xxxx.HEIC --out XrealCaptionApp/Assets/Resources/Presets/<ID>_IMG_xxxx.jpg
mv XrealCaptionApp/Assets/Resources/Presets/<ID>_IMG_xxxx.jpg XrealCaptionApp/Assets/Resources/Presets/<ID>_IMG_xxxx.bytes
```

## データとログ

`Application.persistentDataPath`（Beam Pro では `/sdcard/Android/data/com.mp.xrealobjectcaption/files/`）に保存する。

- `objects.json`：登録した物（名前・キャプション・見本の特徴量）。特徴量の作り方が変わったら自動で作り直す
- `Logs/caption_YYYYMMDD_HHMMSS.csv`：`timestamp,event,objectId,name,score,second,detail`
  - event：`start / shown（キャプションを出した）/ hidden（消した）/ register / sample / edit / delete / reset / setting / key_press`

```bash
adb pull /sdcard/Android/data/com.mp.xrealobjectcaption/files/Logs ./Logs
adb logcat -s Unity | grep CaptionApp
```

## 要実機確認

| 確認すること | 調整する所 |
|---|---|
| XREAL Eye の映像が取れるか、向き | Beam Pro 画面の映像。向きは「映像の回転」。logcat の `XREAL Eye frame ... Y= U= V=` に各面の大きさが出る |
| 色が入れ替わっていないか（赤い物が青く見えるなど） | `XrealEyeFrameSource` の `k_UPlane` / `k_VPlane` を入れ替える |
| 眼鏡の枠と、実際に認識している範囲が合っているか | `CaptionApp.m_CameraVerticalFovDeg`（XREAL Eye の縦の画角。仮に 24°）。枠の大きさ = 縦の画角 × 認識範囲（5〜14° に収める） |
| 推論の速さ | Beam Pro 画面の上の「○○ ms」。遅ければ `m_Interval` を延ばす |
| しきい値 | 画面の「しきい値」。実際に撮り足した後の類似度を見て決める |
| キャプションの読みやすさ（文字の大きさ・カードの位置） | `GlassesView`（カード幅 `k_CardWidth`、文字の大きさ 64 / 46） |

## エディタでの確認（PlayMode テスト）

`XrealCaptionApp/Assets/Tests/CaptionAppTests.cs`。カメラの代わりに写真を流す。

- `HeldOutPhotos`：同梱の写真のうち 2 枚で登録し、残りの 1 枚（切り抜き・明るさ・色味を変えたもの）を正しく見分けられるか。3 通り × 2 物。類似度は `Logs/TestCaptures/similarity.txt`
- `AppFlow`：起動（同梱の写真から登録）→ Tシャツでキャプション → 耳に切り替え → 背景を撮るとキャプションが消える → 新しい物の登録（入力 → 3 枚撮影）→ 認識 → 保存データの読み直し → 削除

眼鏡ビューと Beam Pro 画面のスクリーンショットは `XrealCaptionApp/Logs/TestCaptures/` に保存される。

2026-11-07 の結果：2 件とも合格。写真で見分けるテストは 6/6 正解で、正しい物との類似度は 0.77〜0.89、違う物は 0.52〜0.63、机の色の無地は 0.35 だった（しきい値 0.60・差 0.05 の根拠）。実際のカメラでは類似度が下がるはずなので、実機で撮り足した後にもう一度見直す。

```bash
cd XrealCaptionApp
open -W -n -a "/Applications/Unity/Hub/Editor/6000.0.58f2/Unity.app" --args -batchmode -projectPath "$PWD" -runTests -testPlatform PlayMode -testResults "$PWD/Logs/test-results.xml" -logFile "$PWD/Logs/test.log"
```

このMacでは HID クラッシュを避けるため、テストの実行中だけ `ProjectSettings/ProjectSettings.asset` の `activeInputHandler` を 0 にして、終わったら 1 に戻す。

## スクリプト構成

```
Assets/Scripts/
  CaptionApp.cs        本体：認識ループ、表示の安定化（2 回連続で出す・1.2 秒で消す）、登録・撮影・設定
  FrameSource.cs       カメラ：XREAL Eye（YUV420 を直接読む）/ 端末のカメラ（WebCamTexture）/ テスト用の写真。中央の切り抜き・回転
  FeatureExtractor.cs  特徴量：MobileNetV2 の全体平均プーリング出力（Inference Engine）/ 代わりの色・輪郭ヒストグラム
  ObjectDatabase.cs    登録した物と見本、照合、保存、同梱の写真からの登録
  GlassesView.cs       眼鏡の表示：認識範囲の枠、キャプションのカード、案内・通知
  PhoneUI.cs           Beam Pro 画面：映像・認識結果・登録・一覧・設定・入力・確認ダイアログ
  CaptionLogger.cs     ログ CSV
  InputRouter.cs / ButtonNames.cs / UiFactory.cs / GuardedButton.cs / KeyboardReopener.cs
                       ../XREAL_label_3DoF_3types から流用（Xボタン等の入力、UI 部品）
Assets/Editor/ProjectSetup.cs / AndroidBuild.cs
```
