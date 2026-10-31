<#
.SYNOPSIS
    Registers the Task Scheduler task that runs deploy/update.ps1 every 5 minutes (Phase 11 §4.5).

.DESCRIPTION
    Run once on the self-hosted PC, from a normal (non-elevated) PowerShell, as the user who signs
    in and runs Docker Desktop. Re-running replaces the task, so it is safe after moving the clone.

    The task runs only while that user is logged on, which is when Docker Desktop is up (D14).
    It launches through `conhost.exe --headless` so no console window appears every 5 minutes. The
    cost is that Task Scheduler's "Last Run Result" always reads 0; failures are in deploy/logs/.

    Remove the task with: Unregister-ScheduledTask -TaskName RecipeAppUpdate
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$TaskName              = 'RecipeAppUpdate'
$UpdateIntervalMinutes = 5      # D12
$RunTimeLimitMinutes   = 30     # A first pull of the API image (Chromium included) can be slow.
$UpdateScript          = Join-Path $PSScriptRoot 'update.ps1'

$powershellArguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File `"$UpdateScript`""
$action = New-ScheduledTaskAction `
    -Execute 'conhost.exe' `
    -Argument "--headless powershell.exe $powershellArguments" `
    -WorkingDirectory (Split-Path -Parent $PSScriptRoot)

# A one-off start with indefinite repetition. The schedule carries on across reboots, and an
# Interactive task simply waits until the user is logged on.
$trigger = New-ScheduledTaskTrigger -Once -At (Get-Date) -RepetitionInterval (New-TimeSpan -Minutes $UpdateIntervalMinutes)

$principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Limited

$settings = New-ScheduledTaskSettingsSet `
    -MultipleInstances IgnoreNew `
    -ExecutionTimeLimit (New-TimeSpan -Minutes $RunTimeLimitMinutes) `
    -StartWhenAvailable `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings `
    -Description 'RecipeApp: pull and deploy the latest release (deploy/update.ps1, Phase 11).' -Force | Out-Null

Write-Host "Registered '$TaskName': runs $UpdateScript every $UpdateIntervalMinutes minutes while $env:USERNAME is logged on."
