#!/usr/bin/env python3
"""校验关键程序集的 asmdef 设置，防止默认引用策略被误改。

- Runtime/Diagnostics/UPandaGF.Diagnostics.asmdef 必须 autoReferenced=true
  （日志/诊断要在未显式引用的业务程序集中可用）。
- Samples 与各测试程序集必须 autoReferenced=false
  （示例与测试不得进入 Player 的默认引用）。

用法：python3 Tools/ci/check_asmdef_settings.py [包根目录]
"""
import json
import pathlib
import sys

ROOT = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else pathlib.Path(__file__).resolve().parents[2])

RULES = [
    ("Runtime/Diagnostics/UPandaGF.Diagnostics.asmdef", True),
    ("Samples/UPandaGF.Samples.asmdef", False),
    ("Tests/PlayMode/UPandaGF.Tests.asmdef", False),
    ("Tests/EditMode/UPandaGF.Tests.EditMode.asmdef", False),
]

failures = []
for rel, expected in RULES:
    path = ROOT / rel
    if not path.exists():
        failures.append(f"{rel}: 文件不存在")
        continue
    try:
        data = json.loads(path.read_text(encoding="utf-8-sig"))
    except Exception as exc:  # noqa: BLE001
        failures.append(f"{rel}: JSON 解析失败（{exc}）")
        continue
    actual = bool(data.get("autoReferenced", True))
    if actual != expected:
        failures.append(f"{rel}: autoReferenced={str(actual).lower()}，期望 {str(expected).lower()}")
    else:
        print(f"ok: {rel} autoReferenced={str(actual).lower()}")

if failures:
    print("asmdef 设置校验失败：", file=sys.stderr)
    for item in failures:
        print(f"  - {item}", file=sys.stderr)
    sys.exit(1)
print(f"asmdef 设置校验通过（{len(RULES)} 项）")
