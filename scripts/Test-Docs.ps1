param(
    [string]$ExecutablePath = (Join-Path $PSScriptRoot '..\src\bin\Debug\net48\TiaOpennessMcp.exe'),
    # Also query the real docs.tia.siemens.cloud (needs internet access).
    [switch]$Live
)

$ErrorActionPreference = 'Stop'
$ExecutablePath = (Resolve-Path -LiteralPath $ExecutablePath).Path
$binaryDirectory = Split-Path $ExecutablePath
$temporary = Join-Path ([System.IO.Path]::GetTempPath()) ('tia-docs-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    # Compile with the Roslyn compiler the .NET SDK ships (the test uses C# 9 features).
    $sdkRoot = Join-Path (Split-Path (Get-Command dotnet).Source) 'sdk'
    $sdkCsc = Get-ChildItem -LiteralPath $sdkRoot -Directory |
        Where-Object { $_.Name -match '^\d+\.\d+\.\d+$' } |
        Sort-Object { [version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName 'Roslyn\bincore\csc.dll' } |
        Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $sdkCsc) { throw 'Roslyn csc.dll not found in the .NET SDK.' }
    $framework = "${env:ProgramFiles(x86)}\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8"
    if (-not (Test-Path -LiteralPath $framework)) { throw ".NET Framework 4.8 reference assemblies not found at $framework." }
    $testExe = Join-Path $temporary 'DocsTests.exe'
    $references = @(
        "/r:$ExecutablePath"
        "/r:$binaryDirectory\HtmlAgilityPack.dll"
        "/r:$binaryDirectory\System.Text.Json.dll"
        "/r:$binaryDirectory\System.Memory.dll"
        "/r:$framework\mscorlib.dll"
        "/r:$framework\System.dll"
        "/r:$framework\System.Core.dll"
        "/r:$framework\System.Net.Http.dll"
        "/r:$framework\Facades\netstandard.dll"
        "/r:$framework\Facades\System.Runtime.dll"
    )
    & dotnet $sdkCsc /nologo /noconfig /nostdlib /langversion:9.0 /nullable:enable /platform:x64 "/out:$testExe" @references (Join-Path $PSScriptRoot 'Test-Docs.cs')
    if ($LASTEXITCODE -ne 0) { throw "Test harness compilation failed: $LASTEXITCODE" }
    Get-ChildItem -LiteralPath $binaryDirectory -Filter '*.dll' | Copy-Item -Destination $temporary
    Copy-Item -LiteralPath $ExecutablePath -Destination $temporary
    Copy-Item -LiteralPath ($ExecutablePath + '.config') -Destination ($testExe + '.config')
    $testArgs = @()
    if ($Live) { $testArgs += 'live' }
    & $testExe @testArgs
    if ($LASTEXITCODE -ne 0) { throw "Docs tests failed: $LASTEXITCODE" }
}
finally {
    Remove-Item -LiteralPath $temporary -Recurse -Force
}
