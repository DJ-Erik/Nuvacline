# ============================================================================
# Nuvacline Release Manager - punto di ingresso
# Uso: .\tools\release.ps1 -Version 0.5.2
# Documentazione: docs\RELEASE-MANAGER.md
# Il file e' in solo ASCII: PowerShell 5.1 legge i .ps1 senza BOM come ANSI.
# ============================================================================

[CmdletBinding()]
param(
    [string]$Version,
    [switch]$Resume,
    [switch]$ApproveHumanTest,
    [switch]$DryRun,
    [switch]$NoPublish,
    [string]$RepoRoot,
    [string]$WorkflowRoot,
    [string]$StateFile,
    [string[]]$IncludeFiles = @(),
    [string]$NotesFile,
    [string]$GitHubRepo
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'release-lib.ps1')
. (Join-Path $PSScriptRoot 'release-prep.ps1')
. (Join-Path $PSScriptRoot 'release-phases.ps1')
. (Join-Path $PSScriptRoot 'release-package.ps1')
. (Join-Path $PSScriptRoot 'release-publish.ps1')
. (Join-Path $PSScriptRoot 'release-workflow.ps1')
. (Join-Path $PSScriptRoot 'release-resume.ps1')

exit (Invoke-ReleaseWorkflow -Version $Version -Resume:$Resume -ApproveHumanTest:$ApproveHumanTest -DryRun:$DryRun -NoPublish:$NoPublish -RepoRoot $RepoRoot -WorkflowRoot $WorkflowRoot -StateFile $StateFile -IncludeFiles $IncludeFiles -NotesFile $NotesFile -GitHubRepo $GitHubRepo)
