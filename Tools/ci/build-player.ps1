param(
    [string]$UnityPath = $env:UNITY_PATH,
    [Parameter(Mandatory = $true)][string]$ProjectPath,
    [ValidateSet('StandaloneWindows64', 'Android', 'WebGL')][string]$Target = 'StandaloneWindows64',
    [string]$OutputPath = 'Build',
    [string]$LogPath = '',
    [string]$PackagePath = '',
    [int]$TimeoutSeconds = 3600,
    [switch]$SkipWarmup
)

# CI 构建入口：调用 FrameworkBuildPipeline.BuildFromCommandLine（内部用 EditorApplication.Exit 返回 0/1/2）。
# Windows/Android/WebGL 三种目标共用一套脚本；同步包后先做一次 -quit 预热编译，避免首次导入漏文件。

$ErrorActionPreference = 'Stop'
$project = (Resolve-Path $ProjectPath).Path
if ([string]::IsNullOrEmpty($LogPath)) { $LogPath = Join-Path (Get-Location) "build-$Target.log" }
$logFull = [System.IO.Path]::GetFullPath($LogPath)
New-Item -ItemType Directory -Path (Split-Path $logFull -Parent) -Force | Out-Null

if ([string]::IsNullOrEmpty($UnityPath) -or -not (Test-Path $UnityPath)) {
    throw "找不到 Unity 可执行文件。请用 -UnityPath 或环境变量 UNITY_PATH 指定：$UnityPath"
}

if ($PackagePath) {
    & (Join-Path $PSScriptRoot 'sync-package.ps1') -ProjectPath $project -PackagePath $PackagePath
    if (-not $SkipWarmup) {
        Write-Host '预热编译（同步后的第一次导入）...'
        $null = & (Join-Path $PSScriptRoot 'invoke-unity.ps1') -UnityPath $UnityPath -ProjectPath $project `
            -LogFile "$logFull.warmup.log" -Arguments @('-quit') -TimeoutSeconds 900
    }
}

$env:UPANDA_BUILD_TARGET = $Target
$env:UPANDA_BUILD_OUTPUT = $OutputPath
$exitCode = & (Join-Path $PSScriptRoot 'invoke-unity.ps1') -UnityPath $UnityPath -ProjectPath $project -LogFile $logFull `
    -Arguments @('-executeMethod', 'UPandaGF.GFEditor.FrameworkBuildPipeline.BuildFromCommandLine') -TimeoutSeconds $TimeoutSeconds

if ($exitCode -ne 0) {
    if (Test-Path $logFull) {
        Write-Host '构建失败，日志末尾：'
        Get-Content $logFull -Tail 40 | ForEach-Object { Write-Host $_ }
    }
    exit $exitCode
}

# 产物存在性校验：避免 Unity 返回 0 但实际没有产物。
$outputFull = if ([System.IO.Path]::IsPathRooted($OutputPath)) { $OutputPath } else { Join-Path $project $OutputPath }
$produced = switch ($Target) {
    'StandaloneWindows64' { Join-Path $outputFull 'UPandaGF.exe' }
    'Android' { Join-Path $outputFull 'UPandaGF.apk' }
    'WebGL' { $outputFull }
    default { $outputFull }
}
if (-not (Test-Path $produced)) {
    Write-Error "构建返回成功但产物不存在：$produced"
    exit 1
}
Write-Host "构建成功：$produced（日志：$logFull）"
exit 0
