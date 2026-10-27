param(
    [string]$ExecutablePath = (Join-Path $PSScriptRoot '..\src\bin\Debug\net48\TiaOpennessMcp.exe')
)

$ErrorActionPreference = 'Stop'

function Assert-Schema {
    param($Schema, [string]$Location)

    if ($Schema.type -eq 'array') {
        if ($null -eq $Schema.items -or $Schema.items -isnot [pscustomobject]) {
            throw "$Location must declare an items schema."
        }
        Assert-Schema $Schema.items "$Location.items"
    }
    if ($null -ne $Schema.properties) {
        foreach ($property in $Schema.properties.PSObject.Properties) {
            Assert-Schema $property.Value "$Location.$($property.Name)"
        }
    }
    if ($Schema.additionalProperties -is [pscustomobject]) {
        Assert-Schema $Schema.additionalProperties "$Location.additionalProperties"
    }
}

$outputDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("tia-schema-tests-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $outputDirectory | Out-Null
try {
    & $ExecutablePath --export-schemas $outputDirectory | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Schema export failed with exit code $LASTEXITCODE."
    }

    $files = @(Get-ChildItem -LiteralPath $outputDirectory -Filter '*.json')
    if ($files.Count -eq 0) {
        throw 'No tool schemas were exported.'
    }
    $arrayCount = 0
    foreach ($file in $files) {
        $exported = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
        Assert-Schema $exported.parameters $exported.name
        $checkedInPath = Join-Path $PSScriptRoot "..\schemas\$($file.Name)"
        $checkedIn = Get-Content -LiteralPath $checkedInPath -Raw | ConvertFrom-Json
        Assert-Schema $checkedIn.parameters $checkedIn.name

        foreach ($property in $exported.parameters.properties.PSObject.Properties) {
            if ($property.Value.type -ne 'array') { continue }
            $arrayCount++
            $expectedType = if ($property.Name -in @('ranges', 'entries')) { 'object' } else { 'string' }
            if ($property.Value.items.type -ne $expectedType) {
                throw "$($exported.name).$($property.Name) must have $expectedType items."
            }
            if ($expectedType -eq 'object' -and $null -eq $property.Value.items.properties) {
                throw "$($exported.name).$($property.Name) must declare its item properties."
            }
            $actual = $property.Value.items | ConvertTo-Json -Depth 30 -Compress
            $expected = $checkedIn.parameters.properties.($property.Name).items | ConvertTo-Json -Depth 30 -Compress
            if ($actual -ne $expected) {
                throw "$($exported.name).$($property.Name) differs from its checked-in item schema."
            }
        }
    }
    if ($arrayCount -lt 7) {
        throw "Expected at least seven array parameters; found $arrayCount."
    }
    Write-Output "PASS: $($files.Count) tool schemas; $arrayCount typed array parameters; checked-in schemas match."
}
finally {
    Remove-Item -LiteralPath $outputDirectory -Recurse -Force
}
