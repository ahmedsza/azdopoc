#requires -Version 7.2

$ErrorActionPreference = 'Stop'
$container = 'propertymanagement-sql'
$volume = 'propertymanagement-sql-data'
$backup = Join-Path $PSScriptRoot 'backup'

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw 'Install and start Docker Desktop first.'
}

New-Item -ItemType Directory -Force -Path $backup | Out-Null
$env:MSSQL_SA_PASSWORD = Read-Host 'Enter a strong SQL Server password' -AsSecureString | ConvertFrom-SecureString -AsPlainText

try {
    docker volume create $volume | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the SQL Server data volume.' }

    docker run --name $container --restart unless-stopped -e ACCEPT_EULA=Y -e MSSQL_PID=Developer -e MSSQL_SA_PASSWORD -p 1433:1433 -v "${volume}:/var/opt/mssql" -v "${backup}:/var/opt/mssql/backup" -d mcr.microsoft.com/mssql/server:2022-latest
    if ($LASTEXITCODE -ne 0) { throw "Could not start container '$container'. Check whether it already exists or port 1433 is in use." }
}
finally {
    Remove-Item Env:\MSSQL_SA_PASSWORD -ErrorAction SilentlyContinue
}

Write-Host 'SQL Server is starting at localhost,1433. Connect with login sa and the password you entered.'
