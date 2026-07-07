param(
    [string] $Configuration = "Release",
    [string] $Runtime = "win-x64",
    [string] $Output = "publish"
)

$ErrorActionPreference = "Stop"

$Root = Resolve-Path (Join-Path $PSScriptRoot "..")
$Solution = Join-Path $Root "ArcaDownloader.sln"
$Project = Join-Path $Root "src\ArcaDownloader\ArcaDownloader.MewUI\ArcaDownloader.MewUI.csproj"
$OutputPath = Join-Path $Root $Output

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)]
        [string] $FilePath,
        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]] $Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath failed with exit code $LASTEXITCODE"
    }
}

Write-Host "Restoring..."
Invoke-Checked "dotnet" @("restore", $Solution)

Write-Host "Testing..."
Invoke-Checked "dotnet" @("test", $Solution, "-c", $Configuration, "--no-restore")

Write-Host "Publishing Native AOT..."
Invoke-Checked "dotnet" @("publish", $Project, "-c", $Configuration, "-r", $Runtime, "-o", $OutputPath, "--no-restore")

$Exe = Join-Path $OutputPath "ArcaDownloader.MewUI.exe"
if (-not (Test-Path $Exe)) {
    throw "Publish failed: $Exe was not created."
}

Write-Host ""
Write-Host "Done: $Exe"
Get-Item $Exe | Select-Object FullName, Length
