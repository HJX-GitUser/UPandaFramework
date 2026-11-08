#!/usr/bin/env bash
# 校验 NUnit XML（文件或目录）：必须至少 1 个用例且 failed=0，否则返回非 0。
set -euo pipefail

TARGET="${1:?用法: check-test-results.sh <xml-file-or-directory>}"

[ -e "$TARGET" ] || { echo "测试结果不存在：$TARGET" >&2; exit 2; }
if [ -d "$TARGET" ]; then
    mapfile -t FILES < <(find "$TARGET" -type f -name '*.xml' | sort)
else
    FILES=("$TARGET")
fi
[ "${#FILES[@]}" -gt 0 ] || { echo "目录中没有测试结果 XML：$TARGET" >&2; exit 2; }

TOTAL=0; PASSED=0; FAILED=0; SKIPPED=0
for FILE in "${FILES[@]}"; do
    LINE=$(grep -o '<test-run[^>]*>' "$FILE" | head -n 1 || true)
    [ -n "$LINE" ] || { echo "不是 NUnit 测试结果：$FILE" >&2; exit 2; }
    get_attr() { printf '%s' "$LINE" | grep -o "$1=\"[0-9]*\"" | head -n 1 | cut -d'"' -f2 || echo 0; }
    T=$(get_attr total); P=$(get_attr passed); F=$(get_attr failed); S=$(get_attr skipped)
    T=${T:-0}; P=${P:-0}; F=${F:-0}; S=${S:-0}
    echo "$(basename "$FILE"): total=$T passed=$P failed=$F skipped=$S"
    TOTAL=$((TOTAL + T)); PASSED=$((PASSED + P)); FAILED=$((FAILED + F)); SKIPPED=$((SKIPPED + S))
done

echo "汇总：total=$TOTAL passed=$PASSED failed=$FAILED skipped=$SKIPPED"
if [ "$FAILED" -gt 0 ]; then echo "测试失败 $FAILED 个。" >&2; exit 1; fi
if [ "$TOTAL" -le 0 ]; then echo "没有执行任何测试用例。" >&2; exit 1; fi
exit 0
