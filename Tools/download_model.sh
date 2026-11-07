#!/bin/sh
# MobileNetV2（ONNX model zoo の mobilenetv2-12、約 14MB）を XrealCaptionApp/Assets/Models/ に取得する
set -e
cd "$(dirname "$0")/.."
mkdir -p XrealCaptionApp/Assets/Models
curl -L -o XrealCaptionApp/Assets/Models/mobilenetv2-12.onnx \
  https://github.com/onnx/models/raw/main/validated/vision/classification/mobilenet/model/mobilenetv2-12.onnx
ls -l XrealCaptionApp/Assets/Models/mobilenetv2-12.onnx
