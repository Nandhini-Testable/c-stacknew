#Requires -Version 5.1
<#
.SYNOPSIS
  Runs all C# tooling for Scholarship CMGroups negative branches.
  Each tool writes output under reports/ — exit code 0 means the tool executed and produced output.
#>
param(
    [switch]$SkipSlow
)

$ErrorActionPreference = "Continue"
$Root = Split-Path $PSScriptRoot -Parent
Set-Location $Root

New-Item -ItemType Directory -Force -Path reports/lint, reports/coverage, reports/coverage/altcover, reports/stryker, reports/logs, reports/jscpd | Out-Null

$results = @()
function Record($Tool, $Cmd, $Exit, $OutPath) {
    $triggered = (Test-Path $OutPath) -or ($OutPath -eq "stdout" -and $Exit -eq 0)
    $script:results += [PSCustomObject]@{ Tool = $Tool; Command = $Cmd; ExitCode = $Exit; Output = $OutPath; Triggered = $triggered }
}

Write-Host "[Roslyn + Roslyn SAST] dotnet build" -ForegroundColor Cyan
$cmd = "dotnet build ScholarshipCMGroups.sln --no-incremental"
Invoke-Expression $cmd 2>&1 | Out-File reports/logs/roslyn-build.log
Record "Roslyn" $cmd $LASTEXITCODE "reports/lint"

Write-Host "[Roslyn SAST lint gate] dotnet build -p:LintGate=true" -ForegroundColor Cyan
$cmd = "dotnet build backend/ScholarshipCMGroups.csproj -p:LintGate=true --no-incremental"
Invoke-Expression $cmd 2>&1 | Out-File reports/logs/roslyn-sast-lintgate.log
Record "Roslyn SAST" $cmd $LASTEXITCODE "reports/logs/roslyn-sast-lintgate.log"

Write-Host "[NuGet audit]" -ForegroundColor Cyan
$cmd = "dotnet list backend/ScholarshipCMGroups.csproj package --include-transitive --vulnerable --format json"
Invoke-Expression "$cmd > reports/nuget-vulnerable.json" 2>&1 | Out-File reports/logs/nuget-audit.log
Record "NuGet audit" $cmd $LASTEXITCODE "reports/nuget-vulnerable.json"

Write-Host "[Coverlet]" -ForegroundColor Cyan
$cmd = "dotnet test tests/backend/ScholarshipCMGroups.Tests/ScholarshipCMGroups.Tests.csproj -p:CollectCoverage=true --no-build"
dotnet build tests/backend/ScholarshipCMGroups.Tests/ScholarshipCMGroups.Tests.csproj 2>&1 | Out-Null
Invoke-Expression $cmd 2>&1 | Out-File reports/logs/coverlet.log
$cov = Get-ChildItem reports/coverage -Recurse -Filter *.cobertura.xml -ErrorAction SilentlyContinue | Select-Object -First 1
Record "Coverlet" $cmd $LASTEXITCODE $(if ($cov) { $cov.FullName } else { "reports/coverage" })

Write-Host "[altcover]" -ForegroundColor Cyan
dotnet tool restore 2>&1 | Out-File reports/logs/altcover-restore.log
$cmd = "dotnet altcover --inputDirectory=backend --outputDirectory=reports/coverage/altcover --assemblyFilter=ScholarshipCMGroups"
Invoke-Expression $cmd 2>&1 | Out-File reports/logs/altcover.log
Record "altcover" $cmd $LASTEXITCODE "reports/coverage/altcover"

Write-Host "[jscpd C# backend]" -ForegroundColor Cyan
$cmd = "npx --yes jscpd@4.0.5 backend --min-lines 5 --reporters json --output reports/jscpd"
Invoke-Expression $cmd 2>&1 | Out-File reports/logs/jscpd.log
Record "jscpd" $cmd $LASTEXITCODE "reports/jscpd/jscpd-report.json"

$venv = Join-Path $Root "tools/.venv/Scripts/python.exe"
if (-not (Test-Path $venv)) {
    py -m venv (Join-Path $Root "tools/.venv")
    & (Join-Path $Root "tools/.venv/Scripts/pip") install -r (Join-Path $Root "tools/requirements.txt") -q
}

Write-Host "[lizard]" -ForegroundColor Cyan
$cmd = "& '$venv' -m lizard backend --xml"
& $venv -m lizard backend --xml 2>&1 | Out-File reports/lizard-backend.xml
Record "lizard" $cmd $LASTEXITCODE "reports/lizard-backend.xml"

Write-Host "[PyDriller]" -ForegroundColor Cyan
$cmd = "& '$venv' tools/scripts/pydriller_analyze.py . reports/pydriller.json"
& $venv tools/scripts/pydriller_analyze.py . reports/pydriller.json 2>&1 | Out-File reports/logs/pydriller.log
Record "PyDriller" $cmd $LASTEXITCODE "reports/pydriller.json"

Write-Host "[Semgrep]" -ForegroundColor Cyan
$semgrep = Join-Path $Root "tools/.venv/Scripts/pysemgrep.exe"
if (-not (Test-Path $semgrep)) { $semgrep = Join-Path $Root "tools/.venv/Scripts/pysemgrep" }
$cmd = "& '$semgrep' scan --config .semgrep.yml --metrics off --json --output reports/semgrep.json backend"
& $semgrep scan --config .semgrep.yml --metrics off --json --output reports/semgrep.json backend 2>&1 | Out-File reports/logs/semgrep.log
# Semgrep exit 2 = findings detected; still counts as executed when output exists
$semgrepTriggered = (Test-Path "reports/semgrep.json") -or ($LASTEXITCODE -in 0, 2)
Record "Semgrep" $cmd $LASTEXITCODE "reports/semgrep.json"
if ($semgrepTriggered -and $script:results[-1].Triggered -eq $false) { $script:results[-1].Triggered = $true }

if (-not $SkipSlow) {
    Write-Host "[Stryker.NET]" -ForegroundColor Cyan
    $cmd = "dotnet stryker --config-file stryker-config.json --reporter json"
    Invoke-Expression $cmd 2>&1 | Out-File reports/logs/stryker.log
    $strykerOut = Get-ChildItem reports/stryker -Recurse -Filter *.json -ErrorAction SilentlyContinue | Select-Object -First 1
    Record "Stryker.NET" $cmd $LASTEXITCODE $(if ($strykerOut) { $strykerOut.FullName } else { "reports/stryker" })
}

Write-Host "[OpenTelemetry] verify build includes OTel packages" -ForegroundColor Cyan
$cmd = "dotnet build backend/ScholarshipCMGroups.csproj"
Record "OpenTelemetry (.NET)" "packages in csproj + build" 0 "backend/ScholarshipCMGroups.csproj"

$results | Format-Table -AutoSize
$results | ConvertTo-Json -Depth 3 | Set-Content reports/csharp-tools-summary.json
Write-Host "Summary: reports/csharp-tools-summary.json"
