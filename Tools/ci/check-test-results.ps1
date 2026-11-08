param(
    [Parameter(Mandatory = $true)][string]$Path
)

# 校验 NUnit XML（单个文件或目录）：必须至少 1 个用例且 failed=0，否则退出码 1。
# 单独抽出来是为了让 CI 不依赖 Unity 的退出码（Unity 在部分失败场景仍返回 0）。

$ErrorActionPreference = 'Stop'
if (-not (Test-Path $Path)) { Write-Error "测试结果不存在：$Path"; exit 2 }
$item = Get-Item $Path
if ($item.PSIsContainer) {
    $files = @(Get-ChildItem $item.FullName -Filter '*.xml' -Recurse -File)
} else {
    $files = @($item)
}
if ($files.Count -eq 0) { Write-Error "目录中没有测试结果 XML：$Path"; exit 2 }

$total = 0; $passed = 0; $failed = 0; $skipped = 0
foreach ($file in $files) {
    try { [xml]$xml = Get-Content $file.FullName -Raw }
    catch { Write-Error "无法解析 XML：$($file.FullName)"; exit 2 }
    $run = $xml.'test-run'
    if ($null -eq $run) { Write-Error "不是 NUnit 测试结果：$($file.FullName)"; exit 2 }
    $total += [int]$run.total
    $passed += [int]$run.passed
    $failed += [int]$run.failed
    $skipped += [int]$run.skipped
    Write-Host ("{0}: total={1} passed={2} failed={3} skipped={4} result={5}" -f $file.Name, $run.total, $run.passed, $run.failed, $run.skipped, $run.result)
}

Write-Host "汇总：total=$total passed=$passed failed=$failed skipped=$skipped"
if ($failed -gt 0) { Write-Error "测试失败 $failed 个。"; exit 1 }
if ($total -le 0) { Write-Error "没有执行任何测试用例。"; exit 1 }
exit 0
