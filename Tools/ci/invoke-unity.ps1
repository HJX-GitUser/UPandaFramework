param(
    [Parameter(Mandatory = $true)][string]$UnityPath,
    [Parameter(Mandatory = $true)][string]$ProjectPath,
    [Parameter(Mandatory = $true)][string]$LogFile,
    [string[]]$Arguments = @(),
    [int]$TimeoutSeconds = 1800
)

# 统一的 Unity batchmode 调用器：
# - 清理项目锁与旧日志；
# - 轮询日志中的退出标记，避免 Unity 收尾挂住时把调用方卡死；
# - 结束时按进程树清理本次启动的 Unity 进程；
# - 只向管道输出退出码（其余信息走 Write-Host）。

$ErrorActionPreference = 'Stop'
Remove-Item $LogFile -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $ProjectPath 'Temp/UnityLockfile') -Force -ErrorAction SilentlyContinue

$arguments = @('-batchmode', '-nographics', '-projectPath', $ProjectPath, '-logFile', $LogFile) + $Arguments
$started = Get-Date
$quoted = $arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
$process = Start-Process -FilePath $UnityPath -ArgumentList ($quoted -join ' ') -PassThru -WindowStyle Hidden
$markers = 'Exiting batchmode successfully|Batchmode quit successfully invoked|Aborting batchmode due to fatal error|Exiting without the bug reporter|构建成功|构建失败'
$timer = [System.Diagnostics.Stopwatch]::StartNew()
$completed = $false

while (-not $process.HasExited -and $timer.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
    Start-Sleep -Seconds 1
    if (Test-Path $LogFile) {
        $tail = Get-Content $LogFile -Tail 40 -ErrorAction SilentlyContinue
        if ($tail -match $markers) { $completed = $true; Start-Sleep -Seconds 2; break }
    }
}
if (-not $process.HasExited) { Write-Warning "Unity 在 $TimeoutSeconds 秒内未结束，强制终止：$LogFile" }

$code = if ($process.HasExited) { [int]$process.ExitCode } else { 124 }
$stray = Get-Process -Name Unity -ErrorAction SilentlyContinue | Where-Object {
    [string]::IsNullOrEmpty($_.MainWindowTitle) -and $_.StartTime -ge $started.AddSeconds(-5)
}
foreach ($item in $stray) { & cmd /c "taskkill /PID $($item.Id) /T /F >nul 2>&1" }
Write-Output $code
