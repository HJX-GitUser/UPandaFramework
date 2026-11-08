param(
    [string]$UnityPath = $env:UNITY_PATH,
    [Parameter(Mandatory = $true)][string]$ProjectPath,
    [ValidateSet('EditMode', 'PlayMode')][string]$TestPlatform = 'EditMode',
    [string]$ResultsPath = '',
    [string]$LogPath = '',
    [string]$PackagePath = '',
    [int]$TimeoutSeconds = 1800,
    [switch]$SkipWarmup
)

# 运行 Unity EditMode/PlayMode 测试并做结果判定：
# - 可先把本包同步进宿主工程（-PackagePath），并做一次 -quit 预热编译，避免“同步后首次编译漏文件”；
# - 轮询测试结果 XML，避免 Unity 收尾挂住时把 CI 卡死；
# - 解析 NUnit XML：0 个用例或存在 failed 都返回非 0（Unity 自身退出码不一定可靠）。

$ErrorActionPreference = 'Stop'
$project = (Resolve-Path $ProjectPath).Path
if ($PackagePath) {
    & (Join-Path $PSScriptRoot 'sync-package.ps1') -ProjectPath $project -PackagePath $PackagePath
    if (-not $SkipWarmup) {
        Write-Host '预热编译（同步后的第一次导入）...'
        $warmLog = if ([string]::IsNullOrEmpty($LogPath)) { Join-Path (Get-Location) 'test-results/warmup.log' } else { "$LogPath.warmup.log" }
        $null = & (Join-Path $PSScriptRoot 'invoke-unity.ps1') -UnityPath $UnityPath -ProjectPath $project -LogFile $warmLog -Arguments @('-quit') -TimeoutSeconds 900
    }
}

if ([string]::IsNullOrEmpty($ResultsPath)) { $ResultsPath = Join-Path (Get-Location) "test-results/$TestPlatform.xml" }
if ([string]::IsNullOrEmpty($LogPath)) { $LogPath = Join-Path (Get-Location) "test-results/$TestPlatform.log" }
$resultsFull = [System.IO.Path]::GetFullPath($ResultsPath)
$logFull = [System.IO.Path]::GetFullPath($LogPath)
New-Item -ItemType Directory -Path (Split-Path $resultsFull -Parent) -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path $logFull -Parent) -Force | Out-Null
Remove-Item $resultsFull -Force -ErrorAction SilentlyContinue

if ([string]::IsNullOrEmpty($UnityPath) -or -not (Test-Path $UnityPath)) {
    throw "找不到 Unity 可执行文件。请用 -UnityPath 或环境变量 UNITY_PATH 指定：$UnityPath"
}

$exitCode = & (Join-Path $PSScriptRoot 'invoke-unity.ps1') -UnityPath $UnityPath -ProjectPath $project -LogFile $logFull `
    -Arguments @('-runTests', '-testPlatform', $TestPlatform, '-testResults', $resultsFull) -TimeoutSeconds $TimeoutSeconds

if (Test-Path $logFull) {
    $compileErrors = Select-String -Path $logFull -Pattern 'error CS' -ErrorAction SilentlyContinue
    if ($compileErrors) {
        Write-Host '编译错误：'
        $compileErrors | Select-Object -First 20 | ForEach-Object { Write-Host $_.Line }
    }
}

& (Join-Path $PSScriptRoot 'check-test-results.ps1') -Path $resultsFull
exit $LASTEXITCODE
