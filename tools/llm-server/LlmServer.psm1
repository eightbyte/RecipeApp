# Decisions behind tools/llm-server/start.ps1 and stop.ps1, kept free of process and network calls so Pester can test them (Windows PowerShell 5.1).

Set-StrictMode -Version Latest

# The process name Windows reports for llama-server.exe, the only process stop.ps1 will kill.
$LlamaServerProcessName = 'llama-server'

function Test-IsLoopbackAddress {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $Address)

    if ($Address -eq 'localhost') { return $true }

    $parsed = $null
    if (-not [System.Net.IPAddress]::TryParse($Address, [ref] $parsed)) { return $false }
    return [System.Net.IPAddress]::IsLoopback($parsed)
}

function Get-BindAddressRefusal {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $BindAddress,
        [AllowEmptyString()] [string] $ApiKey = ''
    )

    # Off loopback the server is reachable from other machines, and plain HTTP with no key would let any of them use the GPU.
    if ((Test-IsLoopbackAddress $BindAddress) -or -not [string]::IsNullOrWhiteSpace($ApiKey)) { return $null }

    return "BindAddress '$BindAddress' is not a loopback address, so the Model Server would be reachable " +
           "from other machines. Set ApiKey in settings.psd1, or bind to 127.0.0.1."
}

function Test-PinnedHash {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $ExpectedSha256
    )

    $actual = (Get-FileHash -Path $Path -Algorithm SHA256).Hash
    return $actual -eq $ExpectedSha256.ToUpperInvariant()
}

function Get-LlamaServerArguments {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [hashtable] $Settings,
        [Parameter(Mandatory)] [string] $LogFile
    )

    # The alias is the GGUF's file name, so the API's status can report which model is loaded.
    $alias = [System.IO.Path]::GetFileName($Settings.ModelPath)

    # The API key is passed through LLAMA_API_KEY rather than here, so it never appears in a process listing.
    return @(
        '--model',        (ConvertTo-QuotedArgument $Settings.ModelPath),
        '--ctx-size',     [string] $Settings.ContextSize,
        '--n-gpu-layers', [string] $Settings.GpuLayers,
        '--parallel',     '1',
        '--host',         $Settings.BindAddress,
        '--port',         [string] $Settings.Port,
        '--alias',        (ConvertTo-QuotedArgument $alias),
        '--reasoning',    'off',
        '--no-webui',
        '--log-file',     (ConvertTo-QuotedArgument $LogFile)
    )
}

function ConvertTo-QuotedArgument {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $Value)

    # Start-Process joins its argument list with spaces, so a path with a space must carry its own quotes.
    if ($Value -match '\s') { return '"' + $Value + '"' }
    return $Value
}

function Get-StartRefusal {
    [CmdletBinding()]
    param(
        [AllowEmptyString()] [string] $RecordedProcessName = '',
        [Parameter(Mandatory)] [bool] $PortInUse,
        [Parameter(Mandatory)] [int] $Port
    )

    if ($RecordedProcessName -eq $LlamaServerProcessName) {
        return 'The Model Server is already running (see the PID file). Run stop.ps1 first.'
    }

    if ($PortInUse) {
        return "Port $Port is already in use. Stop whatever is listening on it, or change Port in settings.psd1."
    }

    return $null
}

function Get-StopDecision {
    [CmdletBinding()]
    param([AllowEmptyString()] [string] $RecordedProcessName = '')

    # A PID is reused once its process exits, so a stale PID file can name an unrelated process that must not be killed.
    if ([string]::IsNullOrEmpty($RecordedProcessName)) { return 'NotRunning' }
    if ($RecordedProcessName -ne $LlamaServerProcessName) { return 'NotLlamaServer' }
    return 'Stop'
}

Export-ModuleMember -Function Test-IsLoopbackAddress, Get-BindAddressRefusal, Test-PinnedHash,
    Get-LlamaServerArguments, Get-StartRefusal, Get-StopDecision
