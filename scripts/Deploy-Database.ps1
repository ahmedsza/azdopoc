#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Preview', 'Publish')][string]$Action,
    [Parameter(Mandatory)][string]$Dacpac,
    [string]$ConnectionString = $env:SQL_DEPLOY_CONNECTION_STRING,
    [string]$Profile = (Join-Path $PSScriptRoot '../database/PropertyManagement.Database/PublishProfiles/Safe.publish.xml'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/database-preview'),
    [string]$ApprovedPlan
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    throw 'Supply ConnectionString or set SQL_DEPLOY_CONNECTION_STRING. Never commit credentials.'
}
$Dacpac = (Resolve-Path $Dacpac).Path
$Profile = (Resolve-Path $Profile).Path
$null = New-Item -ItemType Directory -Force -Path $OutputDirectory
$OutputDirectory = (Resolve-Path $OutputDirectory).Path
$approved = if ($ApprovedPlan) { Get-Content -LiteralPath $ApprovedPlan -Raw | ConvertFrom-Json } else { $null }
$builder = [System.Data.Common.DbConnectionStringBuilder]::new()
$builder.set_ConnectionString($ConnectionString)
$server = @('Server', 'Data Source', 'Address', 'Addr', 'Network Address') | Where-Object { $builder.ContainsKey($_) } | Select-Object -First 1
$catalog = @('Database', 'Initial Catalog') | Where-Object { $builder.ContainsKey($_) } | Select-Object -First 1
if (!$server -or !$catalog -or [string]::IsNullOrWhiteSpace($builder[$catalog])) {
    throw 'An explicit server and database are required.'
}
if ($builder[$catalog] -in @('master', 'model', 'msdb', 'tempdb')) { throw 'System databases cannot be deployment targets.' }

function Invoke-SqlPackage([string[]]$Arguments) {
    & dotnet tool run sqlpackage -- @Arguments
    if ($LASTEXITCODE -ne 0) { throw "SqlPackage failed with exit code $LASTEXITCODE." }
}

function Get-TextHash([string]$Text) {
    [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($Text)))
}

$common = @(
    "/SourceFile:$Dacpac", "/Profile:$Profile", "/TargetConnectionString:$ConnectionString",
    '/p:BlockOnPossibleDataLoss=True', '/p:DropObjectsNotInSource=False',
    '/p:DropConstraintsNotInSource=False', '/p:DropIndexesNotInSource=False',
    '/p:DropDmlTriggersNotInSource=False', '/p:CreateNewDatabase=False',
    '/p:AllowIncompatiblePlatform=False', '/p:VerifyDeployment=True', '/p:ScriptDatabaseOptions=False'
)
$reportPath = Join-Path $OutputDirectory 'deployment-report.xml'
$scriptPath = Join-Path $OutputDirectory 'deployment.sql'
Invoke-SqlPackage (@('/Action:DeployReport', "/OutputPath:$reportPath") + $common)
Invoke-SqlPackage (@('/Action:Script', "/OutputPath:$scriptPath") + $common)
[xml]$report = Get-Content -LiteralPath $reportPath -Raw
$report.PreserveWhitespace = $false
$script = Get-Content -LiteralPath $scriptPath -Raw
$normalizedScript = ($script -replace '(?m)^\s*--\s*Generated on:.*\r?\n', '') -replace '\r\n', "`n"
$plan = [ordered]@{
    Version = 1
    Server = [string]$builder[$server]
    Database = [string]$builder[$catalog]
    DacpacSha256 = (Get-FileHash -LiteralPath $Dacpac -Algorithm SHA256).Hash
    ProfileSha256 = (Get-FileHash -LiteralPath $Profile -Algorithm SHA256).Hash
    ReportSha256 = Get-TextHash $report.OuterXml
    ScriptSha256 = Get-TextHash $normalizedScript
    CreatedUtc = [DateTime]::UtcNow.ToString('O')
}

if ($Action -eq 'Preview') {
    $plan | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'plan.json') -Encoding utf8
    Write-Host "Preview saved to $OutputDirectory. No schema changes were applied."
    return
}

if ($approved) {
    foreach ($key in @('Version', 'Server', 'Database', 'DacpacSha256', 'ProfileSha256', 'ReportSha256', 'ScriptSha256')) {
        if ($plan[$key] -cne $approved.$key) {
            throw "Approved deployment changed ($key). Generate a fresh preview and obtain approval again."
        }
    }
}
Invoke-SqlPackage (@('/Action:Publish') + $common)
$plan | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'published-plan.json') -Encoding utf8
Write-Host 'Database published from the supplied DACPAC. Application data was not seeded.'