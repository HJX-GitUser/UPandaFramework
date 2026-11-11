#!/usr/bin/env bash
# 运行 UI-050 面板配置校验（Unity -executeMethod），失败返回非 0。
# 用法：validate-ui-panels.sh <project-path> [package-path] [log-file]
# 环境变量：UNITY_PATH（必填）、PACKAGE_PATH（可选，等价于参数 2）
set -euo pipefail

PROJECT_PATH="${1:?用法: validate-ui-panels.sh <project-path> [package-path] [log-file]}"
PACKAGE_PATH="${2:-${PACKAGE_PATH:-}}"
LOG_PATH="${3:-test-results/ui-panels.log}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

[ -n "${UNITY_PATH:-}" ] || { echo "缺少环境变量 UNITY_PATH" >&2; exit 2; }
[ -x "$UNITY_PATH" ] || { echo "Unity 不可执行：$UNITY_PATH" >&2; exit 2; }

if [ -n "$PACKAGE_PATH" ]; then
    "$SCRIPT_DIR/sync-package.sh" "$PROJECT_PATH" "$PACKAGE_PATH"
fi

mkdir -p "$(dirname "$LOG_PATH")"
rm -f "$LOG_PATH" "${PROJECT_PATH%/}/Temp/UnityLockfile"

set +e
"$UNITY_PATH" -batchmode -nographics -projectPath "$PROJECT_PATH" \
    -executeMethod UPandaGF.GFEditor.UIPanelValidator.ValidateForCI \
    -logFile "$LOG_PATH"
UNITY_EXIT=$?
set -e

if grep -q 'error CS' "$LOG_PATH"; then
    echo "编译错误："
    grep 'error CS' "$LOG_PATH" | head -n 20 || true
fi

if ! grep -q '\[UIPanelValidator\] 完成' "$LOG_PATH"; then
    echo "UI 面板校验器没有执行（检查 -executeMethod 与编译错误）。日志：$LOG_PATH" >&2
    exit 2
fi
grep '\[UIPanelValidator\] 完成' "$LOG_PATH" || true

if [ "$UNITY_EXIT" -ne 0 ] || grep -q 'error CS' "$LOG_PATH"; then
    echo "UI 面板校验失败（Unity exit $UNITY_EXIT）。日志：$LOG_PATH" >&2
    exit 1
fi
exit 0
