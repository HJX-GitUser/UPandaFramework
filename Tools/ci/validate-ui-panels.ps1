param(
    [string]$UnityPath = $env:UNITY_PATH,
    [Parameter(Mandatory = $true)][string]$ProjectPath,
    [string]$PackagePath = '',
    [string]$LogPath = '',
    [int]$TimeoutSeconds = 900
)

# 运行 UI-050 面板配置校验（Unity -executeMethod），失败返回非 0。
# 与 EditMode 用例 PackagePanelsPassValidation 互补：这个入口可在本地/CI 单独跑，不执行完整测试套件。

$ErrorActionPreference = 'Stop'
$project = (Resolve-Path $ProjectPath).Path
if ([string]::IsNullOrEmpty($UnityPath) -or -not (Test-Path $UnityPath)) {
    throw "找不到 Unity 可执行文件。请用 -UnityPath 或环境变量 UNITY_PATH 指定：$UnityPath"
}

if ($PackagePath) {
    & (Join-Path $PSScriptRoot 'sync-package.ps1') -ProjectPath $project -PackagePath $PackagePath
}

if ([string]::IsNullOrEmpty($LogPath)) { $LogPath = Join-Path (Get-Location) 'test-results/ui-panels.log' }
$logFull = [System.IO.Path]::GetFullPath($LogPath)
New-Item -ItemType Directory -Path (Split-Path $logFull -Parent) -Force | Out-Null

$exitCode = & (Join-Path $PSScriptRoot 'invoke-unity.ps1') -UnityPath $UnityPath -ProjectPath $project -LogFile $logFull `
    -Arguments @('-executeMethod', 'UPandaGF.GFEditor.UIPanelValidator.ValidateForCI') -TimeoutSeconds $TimeoutSeconds

$compileErrors = Select-String -Path $logFull -Pattern 'error CS' -ErrorAction SilentlyContinue
if ($compileErrors) {
    Write-Host '编译错误：'
    $compileErrors | Select-Object -First 20 | ForEach-Object { Write-Host $_.Line }
}

$summary = Select-String -Path $logFull -Pattern '\[UIPanelValidator\] 完成' -ErrorAction SilentlyContinue
if (-not $summary) {
    Write-Error "UI 面板校验器没有执行（检查 -executeMethod 与编译错误）。日志：$logFull"
    exit 2
}
$summary | ForEach-Object { Write-Host $_.Line }

if ($compileErrors -or $exitCode -ne 0) {
    Write-Error "UI 面板校验失败（Unity exit $exitCode）。日志：$logFull"
    exit 1
}
exit 0
