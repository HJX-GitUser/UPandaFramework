#!/usr/bin/env bash
# 运行 Unity EditMode/PlayMode 测试并做结果判定。
# 用法：run-tests.sh <EditMode|PlayMode> [project-path] [results-xml] [log-file]
# 环境变量：UNITY_PATH（必填）、PACKAGE_PATH（可选：测试前把包同步进工程）
set -euo pipefail

TEST_PLATFORM="${1:?用法: run-tests.sh <EditMode|PlayMode> [project-path] [results-xml] [log-file]}"
PROJECT_PATH="${2:-${UNITY_PROJECT_PATH:-}}"
RESULTS_PATH="${3:-test-results/${TEST_PLATFORM}.xml}"
LOG_PATH="${4:-test-results/${TEST_PLATFORM}.log}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

[ -n "${UNITY_PATH:-}" ] || { echo "缺少环境变量 UNITY_PATH" >&2; exit 2; }
[ -n "$PROJECT_PATH" ] || { echo "缺少工程路径（参数 2 或 UNITY_PROJECT_PATH）" >&2; exit 2; }
[ -x "$UNITY_PATH" ] || { echo "Unity 不可执行：$UNITY_PATH" >&2; exit 2; }

if [ -n "${PACKAGE_PATH:-}" ]; then
    "$SCRIPT_DIR/sync-package.sh" "$PROJECT_PATH" "$PACKAGE_PATH"
fi

mkdir -p "$(dirname "$RESULTS_PATH")" "$(dirname "$LOG_PATH")"
rm -f "$RESULTS_PATH" "$LOG_PATH" "${PROJECT_PATH%/}/Temp/UnityLockfile"

set +e
"$UNITY_PATH" -batchmode -nographics -projectPath "$PROJECT_PATH" \
    -runTests -testPlatform "$TEST_PLATFORM" \
    -testResults "$RESULTS_PATH" -logFile "$LOG_PATH"
UNITY_EXIT=$?
set -e

if [ -f "$LOG_PATH" ] && grep -q 'error CS' "$LOG_PATH"; then
    echo "编译错误："
    grep 'error CS' "$LOG_PATH" | head -n 20 || true
fi

# Unity 的退出码不足以判断测试失败（尤其在收尾异常时），以 XML 为准。
"$SCRIPT_DIR/check-test-results.sh" "$RESULTS_PATH" || {
    echo "Unity 退出码：$UNITY_EXIT" >&2
    exit 1
}
