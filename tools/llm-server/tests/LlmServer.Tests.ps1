# Pester 3.4 (the version inbox with Windows), so `Should Be` syntax.
# Run: Invoke-Pester tools\llm-server\tests

$modulePath = Join-Path $PSScriptRoot '..\LlmServer.psm1'
Import-Module $modulePath -Force

Describe 'Test-IsLoopbackAddress' {

    It 'treats 127.0.0.1 as loopback' {
        Test-IsLoopbackAddress '127.0.0.1' | Should Be $true
    }

    It 'treats the IPv6 loopback and localhost as loopback' {
        Test-IsLoopbackAddress '::1' | Should Be $true
        Test-IsLoopbackAddress 'localhost' | Should Be $true
    }

    It 'treats a Tailscale address as reachable from other machines' {
        Test-IsLoopbackAddress '100.101.102.103' | Should Be $false
    }

    It 'treats the any-address as reachable from other machines' {
        Test-IsLoopbackAddress '0.0.0.0' | Should Be $false
    }

    It 'treats a host name it cannot parse as reachable from other machines' {
        Test-IsLoopbackAddress 'gpu-box' | Should Be $false
    }
}

Describe 'Get-BindAddressRefusal' {

    It 'allows a loopback bind with no API key' {
        Get-BindAddressRefusal -BindAddress '127.0.0.1' -ApiKey '' | Should BeNullOrEmpty
    }

    It 'refuses a non-loopback bind with no API key' {
        Get-BindAddressRefusal -BindAddress '100.101.102.103' -ApiKey '' | Should Match 'ApiKey'
    }

    It 'refuses a non-loopback bind whose API key is only whitespace' {
        Get-BindAddressRefusal -BindAddress '0.0.0.0' -ApiKey '   ' | Should Match 'ApiKey'
    }

    It 'allows a non-loopback bind once an API key is set' {
        Get-BindAddressRefusal -BindAddress '100.101.102.103' -ApiKey 'secret' | Should BeNullOrEmpty
    }
}

Describe 'Test-PinnedHash' {

    $file = Join-Path $TestDrive 'download.zip'
    Set-Content -Path $file -Value 'pinned bytes' -NoNewline -Encoding Ascii
    $expected = (Get-FileHash -Path $file -Algorithm SHA256).Hash

    It 'accepts a file whose SHA-256 matches the pin' {
        Test-PinnedHash -Path $file -ExpectedSha256 $expected | Should Be $true
    }

    It 'accepts a pin written in lower case, as GitHub publishes digests' {
        Test-PinnedHash -Path $file -ExpectedSha256 $expected.ToLowerInvariant() | Should Be $true
    }

    It 'refuses a file whose SHA-256 does not match the pin' {
        $tampered = Join-Path $TestDrive 'tampered.zip'
        Set-Content -Path $tampered -Value 'other bytes' -NoNewline -Encoding Ascii

        Test-PinnedHash -Path $tampered -ExpectedSha256 $expected | Should Be $false
    }
}

Describe 'Get-LlamaServerArguments' {

    $settings = @{
        ModelPath   = 'C:\LLM\qwen3.5\Qwen3.5-9B-Q6_K.gguf'
        ContextSize = 8192
        GpuLayers   = 'all'
        Port        = 9931
        BindAddress = '127.0.0.1'
        ApiKey      = 'secret'
    }

    $arguments = Get-LlamaServerArguments -Settings $settings -LogFile 'C:\runtime\llama-server.log'
    $joined    = $arguments -join ' '

    It 'always runs a single slot, so dev and production queue rather than share the context' {
        $joined | Should Match '--parallel 1(\s|$)'
    }

    It 'names the model after its GGUF file, so the status can report it' {
        $joined | Should Match '--alias Qwen3\.5-9B-Q6_K\.gguf'
    }

    It 'turns thinking off, so the prompt carries no open think block' {
        $joined | Should Match '--reasoning off'
    }

    It 'passes the bind address, port, context size and GPU layers from the settings' {
        $joined | Should Match '--host 127\.0\.0\.1'
        $joined | Should Match '--port 9931'
        $joined | Should Match '--ctx-size 8192'
        $joined | Should Match '--n-gpu-layers all'
    }

    It 'never puts the API key on the command line' {
        $joined | Should Not Match 'secret'
        $joined | Should Not Match '--api-key'
    }

    It 'quotes a model path that contains a space' {
        $spaced = $settings.Clone()
        $spaced.ModelPath = 'C:\My Models\model.gguf'

        (Get-LlamaServerArguments -Settings $spaced -LogFile 'C:\log.txt') -join ' ' |
            Should Match '--model "C:\\My Models\\model\.gguf"'
    }
}

Describe 'Get-StartRefusal' {

    It 'refuses when the PID file names a live llama-server' {
        Get-StartRefusal -RecordedProcessName 'llama-server' -PortInUse $true -Port 9931 |
            Should Match 'already running'
    }

    It 'refuses when the port is taken by something else' {
        Get-StartRefusal -RecordedProcessName '' -PortInUse $true -Port 9931 | Should Match 'Port 9931'
    }

    It 'starts when a stale PID file names an unrelated process and the port is free' {
        Get-StartRefusal -RecordedProcessName 'notepad' -PortInUse $false -Port 9931 | Should BeNullOrEmpty
    }

    It 'starts when nothing is recorded and the port is free' {
        Get-StartRefusal -RecordedProcessName '' -PortInUse $false -Port 9931 | Should BeNullOrEmpty
    }
}

Describe 'Get-StopDecision' {

    It 'stops the process the PID file names when it is llama-server' {
        Get-StopDecision -RecordedProcessName 'llama-server' | Should Be 'Stop'
    }

    It 'leaves a reused PID alone when it now names another process' {
        Get-StopDecision -RecordedProcessName 'notepad' | Should Be 'NotLlamaServer'
    }

    It 'reports not running when no live process is recorded' {
        Get-StopDecision -RecordedProcessName '' | Should Be 'NotRunning'
    }
}
