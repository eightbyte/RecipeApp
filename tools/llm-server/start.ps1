<#
.SYNOPSIS
    Starts the Model Server (llama.cpp's llama-server) in the background.

.DESCRIPTION
    On first run, downloads the pinned llama.cpp release (Windows, CUDA 12) into runtime\, checks each
    zip against its pinned SHA-256 and refuses to extract one that does not match.
    Then launches llama-server hidden, writes runtime\llama-server.pid and runtime\llama-server.log,
    and returns while the model is still loading.

    Refuses to start when the PID file names a live llama-server, when the port is in use, or when
    BindAddress is not loopback and no ApiKey is set.

    Windows PowerShell 5.1 compatible. See README.md.

.PARAMETER SettingsPath
    The settings file to read. Defaults to settings.psd1 beside this script.
#>
[CmdletBinding()]
param(
    [string] $SettingsPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Windows PowerShell 5.1 leaves $PSScriptRoot empty while it binds parameter defaults.
if (-not $SettingsPath) { $SettingsPath = Join-Path $PSScriptRoot 'settings.psd1' }

Import-Module (Join-Path $PSScriptRoot 'LlmServer.psm1') -Force

# --- Pinned release ---
# Changing these is a deliberate llama.cpp update, followed by the benchmark in README.md.
$PinnedTag = 'b12110'
$PinnedAssets = @(
    @{ Name = 'llama-b12110-bin-win-cuda-12.4-x64.zip';  Sha256 = '1ce755bc3a061889234048f88e33328e17971eaad73c4666cfeb26026b317fee' },
    @{ Name = 'cudart-llama-bin-win-cuda-12.4-x64.zip';  Sha256 = '8c79a9b226de4b3cacfd1f83d24f962d0773be79f1e7b75c6af4ded7e32ae1d6' }
)
$ReleaseDownloadUrl = "https://github.com/ggml-org/llama.cpp/releases/download/$PinnedTag"

$RuntimeDir   = Join-Path $PSScriptRoot 'runtime'
$DownloadDir  = Join-Path $RuntimeDir 'downloads'
$BinDir       = Join-Path $RuntimeDir 'bin'
$ReleaseStamp = Join-Path $BinDir 'pinned-release.txt'
$ServerExe    = Join-Path $BinDir 'llama-server.exe'
$PidFile      = Join-Path $RuntimeDir 'llama-server.pid'
$LogFile      = Join-Path $RuntimeDir 'llama-server.log'

# A process that dies this soon after launch was refused its arguments or its model, not still loading.
$EarlyExitSeconds = 3

function Read-Settings {
    if (-not (Test-Path $SettingsPath)) {
        throw "No settings file at $SettingsPath. Copy settings.example.psd1 to settings.psd1 and edit it."
    }
    return Import-PowerShellDataFile -Path $SettingsPath
}

function Get-RecordedProcessName {
    if (-not (Test-Path $PidFile)) { return '' }

    $recordedId = (Get-Content -Path $PidFile -Raw).Trim()
    $process = Get-Process -Id $recordedId -ErrorAction SilentlyContinue
    if ($null -eq $process) { return '' }
    return $process.ProcessName
}

function Test-PortInUse([int] $Port) {
    $listener = Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue
    return $null -ne $listener
}

function Install-PinnedRelease {
    if ((Test-Path $ServerExe) -and (Test-Path $ReleaseStamp) -and
        ((Get-Content -Path $ReleaseStamp -Raw).Trim() -eq $PinnedTag)) {
        return
    }

    New-Item -ItemType Directory -Force -Path $DownloadDir | Out-Null

    # Windows PowerShell 5.1 defaults to TLS 1.0, which GitHub refuses, and its progress bar slows a large download many times over.
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $ProgressPreference = 'SilentlyContinue'

    foreach ($asset in $PinnedAssets) {
        $zip = Join-Path $DownloadDir $asset.Name
        if (-not (Test-Path $zip)) {
            Write-Host "Downloading $($asset.Name) (llama.cpp $PinnedTag)..."
            $partial = "$zip.partial"
            Invoke-WebRequest -Uri "$ReleaseDownloadUrl/$($asset.Name)" -OutFile $partial -UseBasicParsing
            Move-Item -Path $partial -Destination $zip -Force
        }

        if (-not (Test-PinnedHash -Path $zip -ExpectedSha256 $asset.Sha256)) {
            Remove-Item -Path $zip -Force
            throw "$($asset.Name) does not match its pinned SHA-256 and was deleted unextracted. " +
                  "Run start.ps1 again to re-download it. If it fails again, the release asset has changed: do not use it."
        }
    }

    # Extract into a staging folder and swap it in, so an interrupted extraction never leaves a half-populated bin\.
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $staging = Join-Path $RuntimeDir 'bin.staging'
    if (Test-Path $staging) { Remove-Item -Path $staging -Recurse -Force }
    New-Item -ItemType Directory -Path $staging | Out-Null

    foreach ($asset in $PinnedAssets) {
        Write-Host "Extracting $($asset.Name)..."
        [System.IO.Compression.ZipFile]::ExtractToDirectory((Join-Path $DownloadDir $asset.Name), $staging)
    }
    Set-Content -Path (Join-Path $staging 'pinned-release.txt') -Value $PinnedTag -Encoding Ascii

    if (Test-Path $BinDir) { Remove-Item -Path $BinDir -Recurse -Force }
    Move-Item -Path $staging -Destination $BinDir

    # The zips are only needed again after a pin change, which downloads new ones.
    Remove-Item -Path (Join-Path $DownloadDir '*.zip') -Force
}

# --- Main ---
$settings = Read-Settings

$bindRefusal = Get-BindAddressRefusal -BindAddress $settings.BindAddress -ApiKey $settings.ApiKey
if ($bindRefusal) { throw $bindRefusal }

$startRefusal = Get-StartRefusal -RecordedProcessName (Get-RecordedProcessName) `
    -PortInUse (Test-PortInUse $settings.Port) -Port $settings.Port
if ($startRefusal) { throw $startRefusal }

if (-not (Test-Path $settings.ModelPath)) { throw "ModelPath '$($settings.ModelPath)' does not exist." }

Install-PinnedRelease

if (Test-Path $LogFile) { Remove-Item -Path $LogFile -Force }

$arguments = Get-LlamaServerArguments -Settings $settings -LogFile $LogFile

# The child inherits the environment, and restoring it afterwards keeps the key out of the caller's session.
$previousKey = $env:LLAMA_API_KEY
try {
    if ([string]::IsNullOrWhiteSpace($settings.ApiKey)) {
        Remove-Item Env:LLAMA_API_KEY -ErrorAction SilentlyContinue
    } else {
        $env:LLAMA_API_KEY = $settings.ApiKey
    }
    $server = Start-Process -FilePath $ServerExe -ArgumentList $arguments -WorkingDirectory $BinDir `
        -WindowStyle Hidden -PassThru
} finally {
    if ($null -eq $previousKey) { Remove-Item Env:LLAMA_API_KEY -ErrorAction SilentlyContinue }
    else { $env:LLAMA_API_KEY = $previousKey }
}

Set-Content -Path $PidFile -Value $server.Id -Encoding Ascii

Start-Sleep -Seconds $EarlyExitSeconds
if ($server.HasExited) {
    Remove-Item -Path $PidFile -Force
    $tail = if (Test-Path $LogFile) { (Get-Content -Path $LogFile -Tail 20) -join "`n" } else { '(no log written)' }
    throw "llama-server exited straight away (exit code $($server.ExitCode)). Last log lines:`n$tail"
}

$endpoint = "http://$($settings.BindAddress):$($settings.Port)"
Write-Host "Model Server started (PID $($server.Id)) at $endpoint and is loading the model."
Write-Host "It is ready when $endpoint/health returns 200. Log: $LogFile"
