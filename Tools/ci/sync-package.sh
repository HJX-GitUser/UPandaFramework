#!/usr/bin/env bash
# 把本包镜像到宿主的 Unity 工程里（用于 CI / 本地验证“以包形式导入”的编译与测试）。
# 用法：sync-package.sh <project-path> [package-path]
set -euo pipefail

PROJECT_PATH="${1:?用法: sync-package.sh <project-path> [package-path]}"
PACKAGE_PATH="${2:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)}"
TARGET="${PROJECT_PATH%/}/Assets/upanda-framework"

[ -f "$PROJECT_PATH/ProjectSettings/ProjectVersion.txt" ] || {
    echo "不是 Unity 工程（缺少 ProjectSettings/ProjectVersion.txt）：$PROJECT_PATH" >&2
    exit 2
}
[ -f "$PACKAGE_PATH/package.json" ] || {
    echo "包目录缺少 package.json：$PACKAGE_PATH" >&2
    exit 2
}

mkdir -p "$TARGET"
if command -v rsync >/dev/null 2>&1; then
    rsync -a --delete \
        --exclude '.git' --exclude 'Library' --exclude 'Temp' --exclude 'Logs' --exclude 'obj' \
        "$PACKAGE_PATH/" "$TARGET/"
else
    # 无 rsync 时用 tar 复制；不做删除，CI 的干净工作区足够。
    (cd "$PACKAGE_PATH" && tar --exclude=.git --exclude=Library --exclude=Temp --exclude=Logs -cf - .) \
        | (cd "$TARGET" && tar -xf -)
fi
echo "已同步 $PACKAGE_PATH -> $TARGET"
