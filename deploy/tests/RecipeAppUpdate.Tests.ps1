# Pester 3.4 (the version inbox with Windows), so `Should Be` syntax.
# Run: Invoke-Pester deploy\tests

$modulePath = Join-Path $PSScriptRoot '..\RecipeAppUpdate.psm1'
Import-Module $modulePath -Force

Describe 'Get-PreUpdateDumpDecision' {

    It 'requires a dump when the pulled API image differs from the running one' {
        $decision = Get-PreUpdateDumpDecision `
            -RunningApiImageId 'sha256:aaa' -PulledApiImageId 'sha256:bbb' -PostgresContainerExists $true

        $decision | Should Be 'Dump'
    }

    It 'skips the dump when the API image is unchanged, as in a frontend-only release' {
        $decision = Get-PreUpdateDumpDecision `
            -RunningApiImageId 'sha256:aaa' -PulledApiImageId 'sha256:aaa' -PostgresContainerExists $true

        $decision | Should Be 'SkipApiUnchanged'
    }

    It 'skips the dump on first bootstrap, when no API container exists yet' {
        $decision = Get-PreUpdateDumpDecision `
            -RunningApiImageId '' -PulledApiImageId 'sha256:bbb' -PostgresContainerExists $true

        $decision | Should Be 'SkipFirstBootstrap'
    }

    It 'skips the dump on first bootstrap, when no Postgres container exists yet' {
        $decision = Get-PreUpdateDumpDecision `
            -RunningApiImageId 'sha256:aaa' -PulledApiImageId 'sha256:bbb' -PostgresContainerExists $false

        $decision | Should Be 'SkipFirstBootstrap'
    }
}

Describe 'Get-PreUpdateDumpFileName' {

    It 'names the dump after the UTC time and the short commit SHA of the image it replaces' {
        $timestamp = [DateTime]::new(2026, 10, 28, 1, 30, 5, [DateTimeKind]::Utc)

        $name = Get-PreUpdateDumpFileName -Timestamp $timestamp `
            -OldImageRevision 'c96a758b84aa60d5a51638db296bd03a1f870be5' -OldImageId 'sha256:0fa1dcb485ec34bf'

        $name | Should Be 'recipeapp-20261028T013005Z-c96a758.dump'
    }

    It 'falls back to the short image ID when the image carries no revision label' {
        $timestamp = [DateTime]::new(2026, 10, 28, 1, 30, 5, [DateTimeKind]::Utc)

        $name = Get-PreUpdateDumpFileName -Timestamp $timestamp `
            -OldImageRevision '' -OldImageId 'sha256:0fa1dcb485ec34bf'

        $name | Should Be 'recipeapp-20261028T013005Z-0fa1dcb.dump'
    }

    It 'keeps a revision label shorter than a short SHA whole instead of failing' {
        $timestamp = [DateTime]::new(2026, 10, 28, 1, 30, 5, [DateTimeKind]::Utc)

        $name = Get-PreUpdateDumpFileName -Timestamp $timestamp `
            -OldImageRevision 'abc12' -OldImageId 'sha256:0fa1dcb485ec34bf'

        $name | Should Be 'recipeapp-20261028T013005Z-abc12.dump'
    }
}

Describe 'Get-ExpiredPreUpdateDumps' {

    It 'returns every dump older than the newest Keep, whatever order they are listed in' {
        $listed = @(
            'recipeapp-20261003T120000Z-3333333.dump'
            'recipeapp-20261001T120000Z-1111111.dump'
            'recipeapp-20261005T120000Z-5555555.dump'
            'recipeapp-20261002T120000Z-2222222.dump'
            'recipeapp-20261004T120000Z-4444444.dump'
        )

        $expired = Get-ExpiredPreUpdateDumps -FileNames $listed -Keep 2

        ($expired -join ',') | Should Be (@(
            'recipeapp-20261001T120000Z-1111111.dump'
            'recipeapp-20261002T120000Z-2222222.dump'
            'recipeapp-20261003T120000Z-3333333.dump'
        ) -join ',')
    }

    It 'returns nothing while there are no more dumps than Keep' {
        $listed = @('recipeapp-20261001T120000Z-1111111.dump', 'recipeapp-20261002T120000Z-2222222.dump')

        $expired = @(Get-ExpiredPreUpdateDumps -FileNames $listed -Keep 2)

        $expired.Count | Should Be 0
    }

    It 'never returns a file the updater did not name, such as a hand-made dump' {
        $listed = @(
            'before-phase-12-by-hand.dump'
            'recipeapp-20261001T120000Z-1111111.dump'
            'recipeapp-20261002T120000Z-2222222.dump'
            'notes.txt'
        )

        $expired = Get-ExpiredPreUpdateDumps -FileNames $listed -Keep 1

        ($expired -join ',') | Should Be 'recipeapp-20261001T120000Z-1111111.dump'
    }
}
