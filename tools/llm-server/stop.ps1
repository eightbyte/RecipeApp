<#
.SYNOPSIS
    Stops the Model Server that start.ps1 launched.

.DESCRIPTION
    Reads runtime\llama-server.pid and kills that process only after checking it is llama-server,
    then removes the PID file. A PID file left behind by a server that already exited is removed
    without killing anything.

    Windows PowerShell 5.1 compatible. See README.md.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'LlmServer.psm1') -Force

$PidFile = Join-Path $PSScriptRoot 'runtime\llama-server.pid'

# Stop-Process returns before the process has released the GPU and the port.
$ExitWaitSeconds = 10

if (-not (Test-Path $PidFile)) {
    Write-Host 'The Model Server is not running (no PID file).'
    return
}

$recordedId = (Get-Content -Path $PidFile -Raw).Trim()
$process    = Get-Process -Id $recordedId -ErrorAction SilentlyContinue
$name       = if ($null -eq $process) { '' } else { $process.ProcessName }

switch (Get-StopDecision -RecordedProcessName $name) {
    'NotRunning' {
        Write-Host "The Model Server had already exited (PID $recordedId). Removed the stale PID file."
    }
    'NotLlamaServer' {
        Write-Warning "PID $recordedId now belongs to '$name', not llama-server, so it was left running. Removed the stale PID file."
    }
    'Stop' {
        Stop-Process -Id $recordedId -Force
        $process.WaitForExit($ExitWaitSeconds * 1000) | Out-Null
        Write-Host "Stopped the Model Server (PID $recordedId)."
    }
}

Remove-Item -Path $PidFile -Force
