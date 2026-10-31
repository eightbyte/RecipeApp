<#
.SYNOPSIS
    Pulls the latest RecipeApp release onto this PC and restarts what changed (Phase 11 §4.5).

.DESCRIPTION
    Run every 5 minutes by the task deploy/install-update-task.ps1 registers. Each run:
      1. git pull --ff-only, so compose and updater changes reach the PC.
      2. docker compose pull.
      3. If the API image changed, dumps the database to deploy/backups/ first. A failed dump
         stops the run before anything is recreated (D17).
      4. docker compose up -d --remove-orphans.
      5. If a container was recreated, waits for /health/ready through nginx.
      6. Logs one line to deploy/logs/ for any run that changed something or failed.
    Exits quietly when Docker Desktop is not running (before login, D14).

    Windows PowerShell 5.1 compatible.

.PARAMETER SkipGitPull
    Leave the working tree as it is. For trying the updater on a branch other than master.

.PARAMETER AdditionalComposeFile
    Extra compose files layered after docker-compose.prod.yml. For trying the updater against
    a scratch stack, together with COMPOSE_PROJECT_NAME and COMPOSE_ENV_FILES.
#>
[CmdletBinding()]
param(
    [switch] $SkipGitPull,
    [string[]] $AdditionalComposeFile = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot   = Split-Path -Parent $PSScriptRoot
$BackupDir  = Join-Path $PSScriptRoot 'backups'
$LogDir     = Join-Path $PSScriptRoot 'logs'

$DefaultHttpPort        = 80
$DefaultBackupKeep      = 10
$HealthTimeoutSeconds   = 300
$HealthPollSeconds      = 5
$HealthRequestSeconds   = 5
$LogMonthsKept          = 12
$LogImageIdLength       = 12    # As `docker images` shows them.
$ImageRevisionLabel     = 'org.opencontainers.image.revision'
$ContainerDumpDirectory = '/tmp'
$ServiceNames           = @('postgres', 'api', 'frontend')

$ComposeFileArguments = @('-f', 'docker-compose.yml', '-f', 'docker-compose.prod.yml')
foreach ($file in $AdditionalComposeFile) { $ComposeFileArguments += @('-f', $file) }

# ── Native command helpers ────────────────────────────────────────────────────
# Windows PowerShell 5.1 turns native stderr into terminating errors under
# $ErrorActionPreference = 'Stop', and docker writes progress to stderr. So native calls run
# under 'Continue' and are judged by exit code alone.

function Invoke-Native {
    param(
        [Parameter(Mandatory)] [string] $FilePath,
        [Parameter(Mandatory)] [string[]] $Arguments,
        [switch] $AllowFailure
    )

    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $lines = @(& $FilePath @Arguments 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }

    $stdout = @($lines | Where-Object { $_ -isnot [System.Management.Automation.ErrorRecord] } | ForEach-Object { "$_" })
    $stderr = @($lines | Where-Object { $_ -is [System.Management.Automation.ErrorRecord] } | ForEach-Object { "$_" })

    if ($exitCode -ne 0 -and -not $AllowFailure) {
        throw "'$FilePath $($Arguments -join ' ')' exited $exitCode. $($stderr -join ' ')"
    }

    return [pscustomobject]@{ ExitCode = $exitCode; Output = $stdout; Error = $stderr }
}

function Invoke-Compose {
    param([Parameter(Mandatory)] [string[]] $Arguments, [switch] $AllowFailure)
    Invoke-Native -FilePath 'docker' -Arguments (@('compose') + $ComposeFileArguments + $Arguments) -AllowFailure:$AllowFailure
}

# ── State queries ─────────────────────────────────────────────────────────────

function Get-ServiceContainerId {
    param([Parameter(Mandatory)] [string] $Service)
    # '' when the service has no container, so before/after comparisons never mix $null and ''.
    $result = Invoke-Compose -Arguments @('ps', '--all', '--quiet', $Service)
    return [string]($result.Output | Select-Object -First 1)
}

function Get-ContainerField {
    param([string] $ContainerId, [Parameter(Mandatory)] [string] $Format)
    if ([string]::IsNullOrEmpty($ContainerId)) { return '' }
    $value = (Invoke-Native -FilePath 'docker' -Arguments @('inspect', $ContainerId, '--format', $Format)).Output -join ''
    if ($value -eq '<no value>') { return '' }
    return $value
}

function Get-ContainerLabel {
    param([Parameter(Mandatory)] [string] $ContainerId, [Parameter(Mandatory)] [string] $Label)
    # Read all labels as JSON. A template naming the label needs embedded double quotes, which
    # Windows PowerShell 5.1 strips when it passes arguments to a native command.
    $labels = (Get-ContainerField -ContainerId $ContainerId -Format '{{json .Config.Labels}}') | ConvertFrom-Json
    if ($null -eq $labels) { return '' }
    # Index Properties by name: `.Properties.Name` on an empty label set throws under StrictMode.
    $property = $labels.PSObject.Properties[$Label]
    if ($null -eq $property) { return '' }
    return [string]$property.Value
}

function Get-ImageId {
    param([Parameter(Mandatory)] [string] $ImageReference)
    return ((Invoke-Native -FilePath 'docker' -Arguments @('image', 'inspect', $ImageReference, '--format', '{{.Id}}')).Output -join '')
}

function Get-PositiveIntegerSetting {
    param([Parameter(Mandatory)] [string[]] $EnvironmentLines, [Parameter(Mandatory)] [string] $Name, [Parameter(Mandatory)] [int] $Default)
    $line = $EnvironmentLines | Where-Object { $_.StartsWith("$Name=") } | Select-Object -First 1
    if ($null -eq $line) { return $Default }
    $value = $line.Substring($Name.Length + 1)
    if ([string]::IsNullOrWhiteSpace($value)) { return $Default }

    # Checked before anything changes, so a bad .env value fails the run up front instead of
    # half-way through a dump.
    $parsed = 0
    if (-not [int]::TryParse($value, [ref]$parsed) -or $parsed -lt 1) {
        throw "$Name in .env must be a whole number of 1 or more, not '$value'."
    }
    return $parsed
}

function Get-LogImageId {
    param([string] $ImageId)
    if ([string]::IsNullOrEmpty($ImageId)) { return 'none' }
    $hex = $ImageId -replace '^sha256:', ''
    return $hex.Substring(0, [Math]::Min($LogImageIdLength, $hex.Length))
}

# ── Logging ───────────────────────────────────────────────────────────────────

function Write-UpdateLog {
    param([Parameter(Mandatory)] [string] $Message)
    New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
    $now = [DateTime]::UtcNow
    $logFile = Join-Path $LogDir "update-$($now.ToString('yyyy-MM')).log"
    Add-Content -Path $logFile -Encoding UTF8 -Value "$($now.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"))  $Message"

    # One file per month; drop the oldest beyond $LogMonthsKept.
    Get-ChildItem -Path $LogDir -Filter 'update-*.log' |
        Sort-Object Name -Descending |
        Select-Object -Skip $LogMonthsKept |
        Remove-Item -Force
}

# ── Steps ─────────────────────────────────────────────────────────────────────

function Invoke-PreUpdateDump {
    param(
        [Parameter(Mandatory)] [string] $OldApiContainerId,
        [Parameter(Mandatory)] [int] $Keep,
        [Parameter(Mandatory)] [string] $DatabaseName,
        [Parameter(Mandatory)] [string] $DatabaseUser
    )

    $oldImageId       = Get-ContainerField -ContainerId $OldApiContainerId -Format '{{.Image}}'
    $oldImageRevision = Get-ContainerLabel -ContainerId $OldApiContainerId -Label $ImageRevisionLabel
    $fileName  = Get-PreUpdateDumpFileName -Timestamp ([DateTime]::UtcNow) -OldImageRevision $oldImageRevision -OldImageId $oldImageId
    $inContainer = "$ContainerDumpDirectory/$fileName"
    $onHost      = Join-Path $BackupDir $fileName

    New-Item -ItemType Directory -Force -Path $BackupDir | Out-Null
    try {
        # Dump to a file inside the container and copy it out. Redirecting pg_dump's stdout
        # through PowerShell would re-encode the binary dump as text and corrupt it.
        Invoke-Compose -Arguments @('exec', '-T', 'postgres', 'pg_dump', '--username', $DatabaseUser,
            '--dbname', $DatabaseName, '--format=custom', "--file=$inContainer") | Out-Null
        Invoke-Compose -Arguments @('cp', "postgres:$inContainer", $onHost) | Out-Null

        if (-not (Test-Path $onHost) -or (Get-Item $onHost).Length -eq 0) {
            throw "The dump copied to $onHost is missing or empty."
        }
    }
    catch {
        if (Test-Path $onHost) { Remove-Item -Force $onHost }
        throw
    }
    finally {
        Invoke-Compose -Arguments @('exec', '-T', 'postgres', 'rm', '-f', $inContainer) -AllowFailure | Out-Null
    }

    $existing = @(Get-ChildItem -Path $BackupDir -File | ForEach-Object { $_.Name })
    foreach ($expired in (Get-ExpiredPreUpdateDumps -FileNames $existing -Keep $Keep)) {
        Remove-Item -Force (Join-Path $BackupDir $expired)
    }

    return $fileName
}

function Wait-ForReadiness {
    param([Parameter(Mandatory)] [int] $HttpPort)

    $url = "http://127.0.0.1:$HttpPort/health/ready"
    $deadline = [DateTime]::UtcNow.AddSeconds($HealthTimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec $HealthRequestSeconds
            if ($response.StatusCode -eq 200) { return $true }
        }
        catch {
            # Not up yet (connection refused, 502/503/504 from nginx). Keep polling.
        }
        Start-Sleep -Seconds $HealthPollSeconds
    }
    return $false
}

# ── Run ───────────────────────────────────────────────────────────────────────

Import-Module (Join-Path $PSScriptRoot 'RecipeAppUpdate.psm1') -Force
Push-Location $RepoRoot
try {
    # Docker Desktop starts only at login (D14). Before then there is nothing to do.
    if ((Invoke-Native -FilePath 'docker' -Arguments @('info', '--format', '{{.ServerVersion}}') -AllowFailure).ExitCode -ne 0) {
        exit 0
    }

    try {
        if (-not $SkipGitPull) {
            Invoke-Native -FilePath 'git' -Arguments @('pull', '--ff-only', '--quiet') | Out-Null
        }

        $environment = (Invoke-Compose -Arguments @('config', '--environment')).Output
        $httpPort    = Get-PositiveIntegerSetting -EnvironmentLines $environment -Name 'RECIPEAPP_HTTP_PORT' -Default $DefaultHttpPort
        $backupKeep  = Get-PositiveIntegerSetting -EnvironmentLines $environment -Name 'RECIPEAPP_BACKUP_KEEP' -Default $DefaultBackupKeep
        $services    = (((Invoke-Compose -Arguments @('config', '--format', 'json')).Output -join "`n") | ConvertFrom-Json).services
        $apiImageReference = $services.api.image
        # The database and its owner are whatever docker-compose.yml gives the Postgres container.
        $databaseName = $services.postgres.environment.POSTGRES_DB
        $databaseUser = $services.postgres.environment.POSTGRES_USER

        $before = @{}
        foreach ($service in $ServiceNames) {
            $containerId = Get-ServiceContainerId -Service $service
            $before[$service] = [pscustomobject]@{
                ContainerId = $containerId
                ImageId     = Get-ContainerField -ContainerId $containerId -Format '{{.Image}}'
            }
        }

        Invoke-Compose -Arguments @('--progress', 'quiet', 'pull') | Out-Null

        $decision = Get-PreUpdateDumpDecision `
            -RunningApiImageId $before['api'].ImageId `
            -PulledApiImageId (Get-ImageId -ImageReference $apiImageReference) `
            -PostgresContainerExists (-not [string]::IsNullOrEmpty($before['postgres'].ContainerId))

        $dumpFile = $null
        if ($decision -eq 'Dump') {
            try {
                $dumpFile = Invoke-PreUpdateDump -OldApiContainerId $before['api'].ContainerId -Keep $backupKeep `
                    -DatabaseName $databaseName -DatabaseUser $databaseUser
            }
            catch {
                # The old containers keep running. The next scheduled run tries again.
                Write-UpdateLog "FAILED pre-update dump, update cancelled: $($_.Exception.Message)"
                exit 1
            }
        }

        Invoke-Compose -Arguments @('--progress', 'quiet', 'up', '--detach', '--remove-orphans') | Out-Null

        $changes = @()
        foreach ($service in $ServiceNames) {
            $containerId = Get-ServiceContainerId -Service $service
            if ($containerId -ne $before[$service].ContainerId) {
                $imageId = Get-ContainerField -ContainerId $containerId -Format '{{.Image}}'
                $changes += "$service $(Get-LogImageId $before[$service].ImageId)->$(Get-LogImageId $imageId)"
            }
        }

        if ($changes.Count -eq 0) { exit 0 }

        $healthy = Wait-ForReadiness -HttpPort $httpPort
        $dumpNote = if ($dumpFile) { "dump $dumpFile" } else { "no dump ($decision)" }
        $healthNote = if ($healthy) { 'health ok' } else { "health FAILED (no 200 from /health/ready within ${HealthTimeoutSeconds}s)" }
        Write-UpdateLog "$($changes -join ', ')  $dumpNote  $healthNote"

        if (-not $healthy) { exit 1 }

        Invoke-Native -FilePath 'docker' -Arguments @('image', 'prune', '--force') | Out-Null
        exit 0
    }
    catch {
        Write-UpdateLog "FAILED: $($_.Exception.Message)"
        exit 1
    }
}
finally {
    Pop-Location
}
