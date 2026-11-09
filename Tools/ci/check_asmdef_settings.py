#!/usr/bin/env python3
"""校验关键程序集的 asmdef 设置，防止默认引用策略被误改。

规则：
- Runtime/Diagnostics/UPandaGF.Diagnostics.asmdef 必须 autoReferenced=true；
- 名称以 UPandaGF.Samples 开头的程序集（教程示例与模块内演示）必须 autoReferenced=false；
- Tests 下两个测试程序集必须 autoReferenced=false。

用法：python3 Tools/ci/check_asmdef_settings.py [包根目录]
"""
import json
import pathlib
import sys

ROOT = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else pathlib.Path(__file__).resolve().parents[2])

failures = []
checked = 0


def read_asmdef(path):
    try:
        return json.loads(path.read_text(encoding="utf-8-sig"))
    except Exception as exc:  # noqa: BLE001
        failures.append(f"{path.relative_to(ROOT)}: JSON 解析失败（{exc}）")
        return None


def expect(path, expected):
    global checked
    if not path.exists():
        failures.append(f"{path.relative_to(ROOT)}: 文件不存在")
        return
    data = read_asmdef(path)
    if data is None:
        return
    checked += 1
    actual = bool(data.get("autoReferenced", True))
    if actual != expected:
        failures.append(
            f"{path.relative_to(ROOT)}: autoReferenced={str(actual).lower()}，期望 {str(expected).lower()}"
        )
    else:
        print(f"ok: {path.relative_to(ROOT)} autoReferenced={str(actual).lower()}")


expect(ROOT / "Runtime/Diagnostics/UPandaGF.Diagnostics.asmdef", True)
expect(ROOT / "Tests/PlayMode/UPandaGF.Tests.asmdef", False)
expect(ROOT / "Tests/EditMode/UPandaGF.Tests.EditMode.asmdef", False)

sample_asmdefs = sorted(
    list((ROOT / "Samples~").rglob("*.asmdef")) + list((ROOT / "Runtime").rglob("UPandaGF.Samples*.asmdef"))
)
if not sample_asmdefs:
    failures.append("Samples~/ 与 Runtime/ 下未找到任何 UPandaGF.Samples*.asmdef")
for path in sample_asmdefs:
    expect(path, False)

if failures:
    print("asmdef 设置校验失败：", file=sys.stderr)
    for item in failures:
        print(f"  - {item}", file=sys.stderr)
    sys.exit(1)
print(f"asmdef 设置校验通过（{checked} 项）")
