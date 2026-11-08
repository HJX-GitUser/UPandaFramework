param(
    [Parameter(Mandatory = $true)][string]$ProjectPath,
    [string]$PackagePath = '',
    [string]$TargetFolder = 'Assets/upanda-framework'
)

# 把本包镜像到宿主的 Unity 工程里（用于 CI / 本地验证“以包形式导入”的编译与测试）。
# 只删除目标包目录内的内容，不碰宿主工程其他资源。

$ErrorActionPreference = 'Stop'
$project = (Resolve-Path $ProjectPath).Path
$versionFile = Join-Path $project 'ProjectSettings/ProjectVersion.txt'
if (-not (Test-Path $versionFile)) { throw "不是 Unity 工程（缺少 ProjectSettings/ProjectVersion.txt）：$project" }

if ([string]::IsNullOrEmpty($PackagePath)) { $PackagePath = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path }
$package = (Resolve-Path $PackagePath).Path
if (-not (Test-Path (Join-Path $package 'package.json'))) { throw "包目录缺少 package.json：$package" }

$target = Join-Path $project $TargetFolder
New-Item -ItemType Directory -Path $target -Force | Out-Null
robocopy $package $target /MIR /XD '.git' 'Library' 'Temp' 'Logs' 'obj' /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "同步包失败（robocopy exit $LASTEXITCODE）" }
Write-Host "已同步 $package -> $target"
