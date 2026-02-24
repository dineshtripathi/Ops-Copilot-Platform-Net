#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Architecture Dependency Conformance Guard for OpsCopilot.
.DESCRIPTION
    Validates module/host .csproj ProjectReference entries against the rules
    defined in docs/pdd/DEPENDENCY_RULES.md. Returns exit code 0 (PASS) or
    1 (FAIL).
.PARAMETER RepoRoot
    Repository root directory. Defaults to parent of scripts/ folder.
.PARAMETER Format
    Output format: text (default) or json.
.EXAMPLE
    ./scripts/Test-DependencyConformance.ps1
    ./scripts/Test-DependencyConformance.ps1 -Format json
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path $PSScriptRoot),
    [ValidateSet('text','json')]
    [string]$Format = 'text'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

#region ── Configuration ──────────────────────────────────────────────────

# Normalise repo root to forward slashes for consistent matching
$RepoRoot = (Resolve-Path $RepoRoot).Path.Replace('\', '/').TrimEnd('/')

# Rule ID → human-readable description
$RuleDescriptions = @{
    # Host rules
    'H1' = 'ApiHost must not reference module Application'
    'H2' = 'ApiHost must not reference module Infrastructure'
    'H3' = 'ApiHost must not reference module Domain'
    'H4' = 'McpHost must not reference module Infrastructure'
    # Layer rules (intra- and inter-module)
    'L1' = 'Domain must not reference Application'
    'L2' = 'Domain must not reference Infrastructure'
    'L3' = 'Domain must not reference Presentation'
    'L4' = 'Domain must not reference Host projects'
    'L5' = 'Application must not reference Infrastructure'
    'L6' = 'Application must not reference Presentation'
    'L7' = 'Application must not reference Host projects'
    'L8' = 'Presentation must not reference Infrastructure'
    'L9' = 'Infrastructure must not reference Presentation'
    # Cross-module rules
    'X1' = 'Cross-module: Application -> another Application'
    'X2' = 'Cross-module: Application -> another Domain'
    'X3' = 'Cross-module: Infrastructure -> another Application'
    'X4' = 'Cross-module: Infrastructure -> another Domain'
    'X5' = 'Cross-module: Infrastructure -> another Infrastructure'
    'X6' = 'Cross-module: Presentation -> another Application'
    'X7' = 'Cross-module: Presentation -> another Domain'
    'X8' = 'Cross-module: Presentation -> another Presentation'
    # MCP boundary (warning only)
    'M1' = 'McpHost contains Web SDK / ASP.NET Core references (potential stdio bypass)'
}

# Allowlist — explicit exceptions with documented reasons.
# Keys use the format "SrcKey -> TgtKey" where keys come from Classify-Project.
# To add an exception: add an entry here AND document it in
# docs/architecture/DEPENDENCY_CONFORMANCE.md.
$Allowlist = @{
    'Mod:AgentRuns:Presentation -> Mod:AgentRuns:Infrastructure' =
        'Composition root: wires EF Core DbContext + migration runner via DI'
    'Mod:Rag:Presentation -> Mod:Rag:Infrastructure' =
        'Composition root: wires RAG infrastructure stack via DI'
    'Mod:SafeActions:Presentation -> Mod:SafeActions:Infrastructure' =
        'Composition root: wires EF Core DbContext + migration runner via DI'
}

#endregion

#region ── Project Classification ─────────────────────────────────────────

function Classify-Project {
    <#
    .SYNOPSIS Classifies a .csproj by its path relative to src/.
    .OUTPUTS PSCustomObject with Type, Host, Module, Layer, Key properties.
    #>
    param([string]$AbsPath)

    $rel = $AbsPath.Replace('\', '/').Replace("$RepoRoot/", '').TrimStart('/')

    # Hosts
    if ($rel -match '^src/Hosts/OpsCopilot\.(ApiHost|McpHost|WorkerHost)/') {
        return [PSCustomObject]@{
            Type   = 'Host'
            Host   = $Matches[1]
            Module = $null
            Layer  = $null
            Key    = "Host:$($Matches[1])"
        }
    }

    # BuildingBlocks
    if ($rel -match '^src/BuildingBlocks/(Contracts|Domain|Application|Infrastructure)/') {
        return [PSCustomObject]@{
            Type   = 'BuildingBlock'
            Host   = $null
            Module = $null
            Layer  = $Matches[1]
            Key    = "BB:$($Matches[1])"
        }
    }

    # Modules
    if ($rel -match '^src/Modules/([^/]+)/(Domain|Application|Infrastructure|Presentation)/') {
        return [PSCustomObject]@{
            Type   = 'Module'
            Host   = $null
            Module = $Matches[1]
            Layer  = $Matches[2]
            Key    = "Mod:$($Matches[1]):$($Matches[2])"
        }
    }

    return [PSCustomObject]@{
        Type   = 'Unknown'
        Host   = $null
        Module = $null
        Layer  = $null
        Key    = "?:$rel"
    }
}

#endregion

#region ── Reference Extraction ───────────────────────────────────────────

function Get-ProjectReferences {
    <#
    .SYNOPSIS Extracts ProjectReference paths from a .csproj file.
    .OUTPUTS Array of resolved absolute paths.
    #>
    param([string]$CsprojPath)

    [xml]$proj = Get-Content $CsprojPath -Raw
    $refs = @()
    $nsMgr = $null  # handle default namespace if present

    foreach ($pr in $proj.SelectNodes('//ProjectReference')) {
        $include = $pr.GetAttribute('Include')
        if (-not $include) { continue }

        $refDir  = Split-Path $CsprojPath
        $resolved = [System.IO.Path]::GetFullPath(
            [System.IO.Path]::Combine($refDir, $include)
        )
        $refs += $resolved
    }
    return $refs
}

#endregion

#region ── Rule Engine ────────────────────────────────────────────────────

function Test-Reference {
    <#
    .SYNOPSIS Tests a single source→target reference against all rules.
    .OUTPUTS Array of violated rule IDs (empty if clean).
    #>
    param(
        [PSCustomObject]$Src,
        [PSCustomObject]$Tgt
    )

    $hits = @()

    # BuildingBlock targets are always allowed
    if ($Tgt.Type -eq 'BuildingBlock') { return $hits }

    # ── Host rules (H) ──────────────────────────────────────────────
    if ($Src.Type -eq 'Host' -and $Tgt.Type -eq 'Module') {
        if ($Src.Host -eq 'ApiHost') {
            if ($Tgt.Layer -eq 'Application')    { $hits += 'H1' }
            if ($Tgt.Layer -eq 'Infrastructure') { $hits += 'H2' }
            if ($Tgt.Layer -eq 'Domain')         { $hits += 'H3' }
        }
        if ($Src.Host -eq 'McpHost') {
            if ($Tgt.Layer -eq 'Infrastructure') { $hits += 'H4' }
        }
    }

    # ── Layer rules (L) ─────────────────────────────────────────────
    if ($Src.Type -eq 'Module' -and $Tgt.Type -eq 'Module') {
        $sl = $Src.Layer
        $tl = $Tgt.Layer
        $crossMod = ($Src.Module -ne $Tgt.Module)

        switch ($sl) {
            'Domain' {
                if ($tl -eq 'Application')    { $hits += 'L1' }
                if ($tl -eq 'Infrastructure') { $hits += 'L2' }
                if ($tl -eq 'Presentation')   { $hits += 'L3' }
            }
            'Application' {
                if ($tl -eq 'Infrastructure') { $hits += 'L5' }
                if ($tl -eq 'Presentation')   { $hits += 'L6' }
                if ($crossMod -and $tl -eq 'Application') { $hits += 'X1' }
                if ($crossMod -and $tl -eq 'Domain')      { $hits += 'X2' }
            }
            'Infrastructure' {
                if ($tl -eq 'Presentation') { $hits += 'L9' }
                if ($crossMod -and $tl -eq 'Application')    { $hits += 'X3' }
                if ($crossMod -and $tl -eq 'Domain')         { $hits += 'X4' }
                if ($crossMod -and $tl -eq 'Infrastructure') { $hits += 'X5' }
            }
            'Presentation' {
                if ($tl -eq 'Infrastructure') { $hits += 'L8' }
                if ($crossMod -and $tl -eq 'Application')  { $hits += 'X6' }
                if ($crossMod -and $tl -eq 'Domain')       { $hits += 'X7' }
                if ($crossMod -and $tl -eq 'Presentation') { $hits += 'X8' }
            }
        }
    }

    # Module → Host (any layer)
    if ($Src.Type -eq 'Module' -and $Tgt.Type -eq 'Host') {
        if ($Src.Layer -eq 'Domain')      { $hits += 'L4' }
        if ($Src.Layer -eq 'Application') { $hits += 'L7' }
    }

    return $hits
}

#endregion

#region ── MCP Boundary Check (M1 — warning only) ────────────────────────

function Test-McpBoundary {
    param([string]$SrcDir)

    $mcpCsproj = Get-ChildItem -Path "$SrcDir/Hosts" -Filter 'OpsCopilot.McpHost.csproj' -Recurse -ErrorAction SilentlyContinue
    if (-not $mcpCsproj) { return @() }

    $content = Get-Content $mcpCsproj.FullName -Raw
    $warnings = @()

    if ($content -match 'Microsoft\.NET\.Sdk\.Web') {
        $warnings += [PSCustomObject]@{
            Rule   = 'M1'
            Detail = 'McpHost uses Microsoft.NET.Sdk.Web — stdio transport may be bypassed'
        }
    }
    if ($content -match 'Microsoft\.AspNetCore') {
        $warnings += [PSCustomObject]@{
            Rule   = 'M1'
            Detail = 'McpHost references ASP.NET Core packages — potential HTTP bypass of MCP stdio'
        }
    }
    return $warnings
}

#endregion

#region ── Main Scan ──────────────────────────────────────────────────────

$srcDir   = "$RepoRoot/src"
$csprojFiles = Get-ChildItem -Path $srcDir -Filter '*.csproj' -Recurse

$violations       = @()
$allowedExceptions = @()
$scannedCount     = 0
$refCount         = 0

foreach ($csproj in $csprojFiles) {
    $srcClass = Classify-Project -AbsPath $csproj.FullName
    if ($srcClass.Type -eq 'Unknown') { continue }

    $refs = Get-ProjectReferences -CsprojPath $csproj.FullName
    $scannedCount++

    foreach ($ref in $refs) {
        $tgtClass = Classify-Project -AbsPath $ref
        $refCount++

        $ruleHits = Test-Reference -Src $srcClass -Tgt $tgtClass
        foreach ($ruleId in $ruleHits) {
            $allowKey = "$($srcClass.Key) -> $($tgtClass.Key)"
            if ($Allowlist.ContainsKey($allowKey)) {
                $allowedExceptions += [PSCustomObject]@{
                    Rule   = $ruleId
                    Source = $srcClass.Key
                    Target = $tgtClass.Key
                    Reason = $Allowlist[$allowKey]
                }
            }
            else {
                $violations += [PSCustomObject]@{
                    Rule   = $ruleId
                    Desc   = $RuleDescriptions[$ruleId]
                    Source = $srcClass.Key
                    Target = $tgtClass.Key
                }
            }
        }
    }
}

# MCP boundary warnings
$mcpWarnings = @(Test-McpBoundary -SrcDir $srcDir)
$totalRules  = $RuleDescriptions.Count

#endregion

#region ── Output ─────────────────────────────────────────────────────────

$pass = ($violations.Count -eq 0)

if ($Format -eq 'json') {
    $result = [ordered]@{
        result             = if ($pass) { 'PASS' } else { 'FAIL' }
        scannedProjects    = $scannedCount
        referencesChecked  = $refCount
        rulesEnforced      = $totalRules
        violationCount     = $violations.Count
        violations         = $violations
        allowedExceptions  = $allowedExceptions
        warnings           = $mcpWarnings
    }
    $result | ConvertTo-Json -Depth 5
}
else {
    Write-Host ''
    Write-Host '================================================' -ForegroundColor Cyan
    Write-Host '  OpsCopilot Dependency Conformance Check'        -ForegroundColor Cyan
    Write-Host '================================================' -ForegroundColor Cyan
    Write-Host "  Projects scanned : $scannedCount"
    Write-Host "  References checked: $refCount"
    Write-Host "  Rules enforced   : $totalRules"
    Write-Host ''

    # Allowed exceptions
    if ($allowedExceptions.Count -gt 0) {
        Write-Host "  Allowed exceptions ($($allowedExceptions.Count)):" -ForegroundColor Yellow
        foreach ($ae in $allowedExceptions) {
            Write-Host "    [$($ae.Rule)] $($ae.Source) -> $($ae.Target)" -ForegroundColor Yellow
            Write-Host "           $($ae.Reason)" -ForegroundColor DarkYellow
        }
        Write-Host ''
    }

    # MCP boundary warnings
    if ($mcpWarnings.Count -gt 0) {
        Write-Host "  Warnings ($($mcpWarnings.Count)):" -ForegroundColor DarkYellow
        foreach ($w in $mcpWarnings) {
            Write-Host "    [$($w.Rule)] $($w.Detail)" -ForegroundColor DarkYellow
        }
        Write-Host ''
    }

    # Result
    if ($pass) {
        Write-Host '  RESULT: PASS' -ForegroundColor Green
        Write-Host '  No dependency rule violations detected.' -ForegroundColor Green
    }
    else {
        Write-Host "  RESULT: FAIL  ($($violations.Count) violation(s))" -ForegroundColor Red
        Write-Host ''

        # Group by source project for readability
        $grouped = $violations | Group-Object -Property Source
        foreach ($g in $grouped) {
            Write-Host "  $($g.Name)" -ForegroundColor Red
            foreach ($v in $g.Group) {
                Write-Host "    [$($v.Rule)] -> $($v.Target)  ($($v.Desc))" -ForegroundColor Red
            }
        }
    }

    Write-Host ''
    Write-Host '================================================' -ForegroundColor Cyan
    Write-Host ''
}

if ($pass) { exit 0 } else { exit 1 }

#endregion
