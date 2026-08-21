param(
    [switch]$Linux,
    [switch]$All
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$runtimes = if ($All) {
    @("win-x64", "linux-x64")
}
elseif ($Linux) {
    @("linux-x64")
}
else {
    @("win-x64")
}

foreach ($rid in $runtimes) {
    Write-Host "Publishing $rid..."
    dotnet publish -c Release -r $rid --self-contained true
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    $outDir = Join-Path $PSScriptRoot "bin\Release\net9.0\$rid\publish"
    Get-ChildItem $outDir | ForEach-Object {
        $mb = [math]::Round($_.Length / 1MB, 1)
        Write-Host "  $($_.FullName) ($mb MB)"
    }
}
