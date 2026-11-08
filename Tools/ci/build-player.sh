#!/usr/bin/env bash
# CI 构建入口：调用 FrameworkBuildPipeline.BuildFromCommandLine（内部用 EditorApplication.Exit 返回 0/1/2）。
# 用法：build-player.sh <StandaloneWindows64|Android|WebGL> [project-path] [output-path] [log-file]
# 环境变量：UNITY_PATH（必填）、PACKAGE_PATH（可选：构建前把包同步进工程）
set -euo pipefail

TARGET="${1:?用法: build-player.sh <StandaloneWindows64|Android|WebGL> [project-path] [output-path] [log-file]}"
PROJECT_PATH="${2:-${UNITY_PROJECT_PATH:-}}"
OUTPUT_PATH="${3:-Build}"
LOG_PATH="${4:-build-${TARGET}.log}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

[ -n "${UNITY_PATH:-}" ] || { echo "缺少环境变量 UNITY_PATH" >&2; exit 2; }
[ -n "$PROJECT_PATH" ] || { echo "缺少工程路径（参数 2 或 UNITY_PROJECT_PATH）" >&2; exit 2; }
[ -x "$UNITY_PATH" ] || { echo "Unity 不可执行：$UNITY_PATH" >&2; exit 2; }

if [ -n "${PACKAGE_PATH:-}" ]; then
    "$SCRIPT_DIR/sync-package.sh" "$PROJECT_PATH" "$PACKAGE_PATH"
fi

mkdir -p "$(dirname "$LOG_PATH")"
rm -f "$LOG_PATH" "${PROJECT_PATH%/}/Temp/UnityLockfile"

export UPANDA_BUILD_TARGET="$TARGET"
export UPANDA_BUILD_OUTPUT="$OUTPUT_PATH"

set +e
"$UNITY_PATH" -batchmode -nographics -projectPath "$PROJECT_PATH" \
    -executeMethod UPandaGF.GFEditor.FrameworkBuildPipeline.BuildFromCommandLine \
    -logFile "$LOG_PATH"
EXIT_CODE=$?
set -e

if [ "$EXIT_CODE" -ne 0 ]; then
    echo "构建失败（退出码 $EXIT_CODE），日志末尾：" >&2
    tail -n 40 "$LOG_PATH" >&2 || true
    exit "$EXIT_CODE"
fi

case "$TARGET" in
    StandaloneWindows64) PRODUCED="$OUTPUT_PATH/UPandaGF.exe" ;;
    Android) PRODUCED="$OUTPUT_PATH/UPandaGF.apk" ;;
    *) PRODUCED="$OUTPUT_PATH" ;;
esac
case "$PRODUCED" in
    /*) FULL="$PRODUCED" ;;
    *) FULL="${PROJECT_PATH%/}/$PRODUCED" ;;
esac
if [ ! -e "$FULL" ]; then
    echo "构建返回成功但产物不存在：$FULL" >&2
    exit 1
fi
echo "构建成功：$FULL（日志：$LOG_PATH）"
