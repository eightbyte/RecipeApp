# Decisions behind deploy/update.ps1 (Phase 11 §4.5), kept free of docker calls so they can be
# tested. Windows PowerShell 5.1 compatible: Task Scheduler runs the updater with powershell.exe.

Set-StrictMode -Version Latest

# Characters of the commit SHA (or image ID) kept in a dump's file name, as in `git log --oneline`.
$ShortIdentityLength = 7

# Pre-update dumps are named recipeapp-<yyyyMMddTHHmmssZ>-<short SHA>.dump.
$PreUpdateDumpPrefix      = 'recipeapp-'
$PreUpdateDumpExtension   = '.dump'
$PreUpdateDumpNamePattern = "^$([regex]::Escape($PreUpdateDumpPrefix))\d{8}T\d{6}Z-[0-9a-f]{1,$ShortIdentityLength}$([regex]::Escape($PreUpdateDumpExtension))$"

function Get-PreUpdateDumpDecision {
    [CmdletBinding()]
    param(
        [string] $RunningApiImageId,
        [Parameter(Mandatory)] [string] $PulledApiImageId,
        [Parameter(Mandatory)] [bool] $PostgresContainerExists
    )

    if ([string]::IsNullOrEmpty($RunningApiImageId) -or -not $PostgresContainerExists) { return 'SkipFirstBootstrap' }
    if ($RunningApiImageId -eq $PulledApiImageId) { return 'SkipApiUnchanged' }
    return 'Dump'
}

function Get-PreUpdateDumpFileName {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [DateTime] $Timestamp,
        [string] $OldImageRevision,
        [Parameter(Mandatory)] [string] $OldImageId
    )

    # CI stamps the commit SHA as the OCI revision label. A local build has none, so fall back
    # to the image ID, which still tells two dumps' images apart.
    $identity = if ([string]::IsNullOrEmpty($OldImageRevision)) { $OldImageId -replace '^sha256:', '' } else { $OldImageRevision }
    $utcStamp = $Timestamp.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'")
    $shortIdentity = $identity.Substring(0, [Math]::Min($ShortIdentityLength, $identity.Length))
    return "$PreUpdateDumpPrefix$utcStamp-$shortIdentity$PreUpdateDumpExtension"
}

function Get-ExpiredPreUpdateDumps {
    [CmdletBinding()]
    param(
        [AllowEmptyCollection()] [string[]] $FileNames = @(),
        [Parameter(Mandatory)] [ValidateRange(1, [int]::MaxValue)] [int] $Keep
    )

    # Only the updater's own dumps are pruned. Their UTC timestamp leads the name, so name order
    # is age order.
    $oldestFirst = @($FileNames |
        Where-Object { $_ -cmatch $PreUpdateDumpNamePattern } |
        Sort-Object -Culture ([Globalization.CultureInfo]::InvariantCulture))
    $expiredCount = [Math]::Max(0, $oldestFirst.Count - $Keep)
    if ($expiredCount -eq 0) { return @() }
    return $oldestFirst[0..($expiredCount - 1)]
}

Export-ModuleMember -Function Get-PreUpdateDumpDecision, Get-PreUpdateDumpFileName, Get-ExpiredPreUpdateDumps
