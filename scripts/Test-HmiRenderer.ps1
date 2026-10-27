param(
    [string]$ExecutablePath = (Join-Path $PSScriptRoot '..\src\bin\Debug\net48\TiaOpennessMcp.exe')
)

$ErrorActionPreference = 'Stop'
$ExecutablePath = (Resolve-Path -LiteralPath $ExecutablePath).Path
Add-Type -AssemblyName System.Drawing
$temporary = Join-Path ([System.IO.Path]::GetTempPath()) ('tia-render-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
$script:checks = 0

function Assert($Condition, [string]$Message) {
    if (-not $Condition) { throw "FAIL: $Message" }
    $script:checks++
}
function Write-Json([string]$Name, $Value) {
    [System.IO.File]::WriteAllText((Join-Path $temporary $Name), ($Value | ConvertTo-Json -Depth 50))
}
function Item([string]$Name, [string]$Type, $Props, $Text = @{}) {
    return @{ name=$Name; type=$Type; props=$Props; text=$Text; dynamizations=@(); events=@() }
}
function Run-Render([string]$Destination, [string]$Screen = 'Main', [double]$Scale = 2) {
    $result = & $ExecutablePath --render-hmi $temporary --output (Join-Path $temporary $Destination) --screen $Screen --scale $Scale
    Assert ($LASTEXITCODE -eq 0) "render $Destination exits successfully"
    return ($result | ConvertFrom-Json)
}
function Reject-Render([string]$Destination, [string]$Screen = 'Main', [double]$Scale = 2) {
    $old = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $ExecutablePath --render-hmi $temporary --output (Join-Path $temporary $Destination) --screen $Screen --scale $Scale 2>$null | Out-Null
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $old }
    Assert ($code -ne 0) "invalid render $Destination fails"
}
function Pixel($Bitmap, [int]$X, [int]$Y, [System.Drawing.Color]$Color, [string]$Message) {
    Assert ($Bitmap.GetPixel($X,$Y).ToArgb() -eq $Color.ToArgb()) $Message
}

try {
    $items = @(
        (Item 'Red' 'HmiRectangle' @{Left=10; Top=10; Width=50; Height=30; BackColor='#FF0000'; BorderWidth=0}),
        (Item 'Blue' 'HmiRectangle' @{Left=20; Top=15; Width=30; Height=20; BackColor='#0000FF'; BorderWidth=0}),
        (Item 'Hidden' 'HmiRectangle' @{Left=0; Top=0; Width=100; Height=100; BackColor='#000000'; Visible=$false}),
        (Item 'Window' 'HmiScreenWindow' @{Left=60; Top=10; Width=30; Height=25; Screen='Detail'; CurrentZoomFactor=2; Adaption='None'}),
        (Item 'Clip' 'HmiRectangle' @{Left=115; Top=80; Width=20; Height=10; BackColor='#00FFFF'; BorderWidth=0}),
        (Item 'Localized' 'HmiText' @{Left=0; Top=60; Width=10; Height=10; Font=@{Name='Arial'; Size=12}} @{Text=@{'en-US'=@{html='<body><p>Long &amp; localized</p></body>'}; 'fi-FI'='Suomi'}}),
        (Item 'Complex' 'HmiTrendControl' @{Left=95; Top=10; Width=15; Height=20}),
        (Item 'Rotated' 'HmiRectangle' @{Left=40; Top=75; Width=20; Height=10; RotationAngle=90; RotationCenterPlacement='AbsoluteFromCenter'; BackColor='#FFFF00'; BorderWidth=0}),
        (Item 'Horizontal' 'HmiLine' @{Left=10;Top=50;Width=30;Height=0;X1=0;Y1=0;X2=30;Y2=0;LineColor='#FF0000';LineWidth=2}),
        (Item 'Value' 'HmiIOField' @{Left=0;Top=90;Width=100;Height=10;ProcessValue='42';OutputFormat='Rate {F2} L/s'})
    )
    $items[-1].dynamizations = @(@{type='Tag';property='ProcessValue';tag='Flow'})
    $items[0].events = @(@{type='Tapped';script='throw new Error("Must never execute");'})
    $screen = @{format='tia-mcp-hmi/1';name='Main';group=$null;props=@{Width=120; Height=100; BackColor='#FFFFFF'};items=$items}
    $detail = @{format='tia-mcp-hmi/1';name='Detail';group='Components';props=@{Width=40; Height=20; BackColor='#FFFFFF'};items=@(
        (Item 'Green' 'HmiRectangle' @{Left=5;Top=3;Width=30;Height=15;BackColor='#00FF00';BorderWidth=0})
    )}
    $manifest = @{format='tia-mcp-hmi/1';compact=$false; languages=@('en-US','fi-FI');screenFiles=@(
        @{name='Main';group=$null;path='main.json'}, @{name='Detail';group='Components';path='detail.json'}
    )}
    Write-Json 'manifest.json' $manifest
    Write-Json 'main.json' $screen
    Write-Json 'detail.json' $detail
    $result = Run-Render 'full'
    $report = Get-Content -LiteralPath $result.screens[0].report -Raw | ConvertFrom-Json
    Assert ($result.language -eq 'en-US') 'default language'
    Assert ($report.items.Count -eq 11) 'nested item included in geometry'
    $green = $report.items | Where-Object path -eq 'Main/Window/Green'
    Assert (($green.bounds -join ',') -eq '70,16,60,30') 'nested translation and zoom geometry'
    Assert $green.clipped 'nested clipping reported'
    Assert (($green.clipBounds -join ',') -eq '60,10,30,25') 'nested viewport clip coordinates'
    $unknown = $report.items | Where-Object path -eq 'Main/Value'
    Assert ($unknown.valueUnknown -and $unknown.text -eq 'Rate ? L/s') 'dynamic value is visibly unknown, not invented'
    Assert ($unknown.dynamicProperties[0] -eq 'ProcessValue') 'unresolved dynamic property reported'
    Assert ($report.items | Where-Object path -eq 'Main/Clip').clipped 'screen bounds clipping reported'
    Assert (-not ($report.items | Where-Object path -eq 'Main/Hidden').visible) 'hidden item reported but not painted'
    Assert ($report.items | Where-Object path -eq 'Main/Complex').placeholder 'unsupported control explicitly marked'
    $localized = $report.items | Where-Object path -eq 'Main/Localized'
    Assert ($localized.text -eq 'Long & localized') 'localized HTML object decoded'
    Assert $localized.textOverflow 'text overflow detected'
    $rotated = ($report.items | Where-Object path -eq 'Main/Rotated').bounds
    Assert ([Math]::Abs($rotated[0]-45) -lt 0.001 -and [Math]::Abs($rotated[1]-70) -lt 0.001 -and
        [Math]::Abs($rotated[2]-10) -lt 0.001 -and [Math]::Abs($rotated[3]-20) -lt 0.001) 'rotated bounds'
    $bitmap = [System.Drawing.Bitmap]::new($result.screens[0].image)
    try {
        Assert ($bitmap.Width -eq 240 -and $bitmap.Height -eq 200) 'exact 2x PNG dimensions'
        Assert ($bitmap.RawFormat.Guid -eq [System.Drawing.Imaging.ImageFormat]::Png.Guid) 'lossless PNG output'
        Pixel $bitmap 24 24 ([System.Drawing.Color]::Red) 'rectangle geometry'
        Pixel $bitmap 50 40 ([System.Drawing.Color]::Blue) 'array z-order retained'
        Pixel $bitmap 150 40 ([System.Drawing.Color]::Lime) 'nested screen rendered'
        Pixel $bitmap 190 70 ([System.Drawing.Color]::White) 'nested contents clipped to window'
        Pixel $bitmap 220 140 ([System.Drawing.Color]::White) 'hidden rectangle not drawn'
        Pixel $bitmap 40 100 ([System.Drawing.Color]::Red) 'zero-height horizontal line is drawn'
    } finally { $bitmap.Dispose() }
    Assert (Test-Path -LiteralPath $result.screens[0].annotatedImage) 'annotated image generated'
    $imageHash = (Get-FileHash -LiteralPath $result.screens[0].image).Hash
    $second = Run-Render 'repeat'
    Assert ((Get-FileHash -LiteralPath $second.screens[0].image).Hash -eq $imageHash) 'deterministic PNG'
    Reject-Render 'full'
    Assert ((Get-FileHash -LiteralPath $result.screens[0].image).Hash -eq $imageHash) 'existing image not overwritten'
    Reject-Render 'invalid-scale' 'Main' 10
    Reject-Render 'missing-screen' 'Missing'

    $manifest.compact = $true
    $manifest.defaultsFile = 'defaults.json'
    $manifest.stylesFile = 'styles.json'
    $screen.items = @(
        @{name='Defaulted';type='HmiRectangle';props=@{Left=2;Top=3};style='blue';text=@{};dynamizations=@();events=@()}
    )
    Write-Json 'defaults.json' @{format='tia-mcp-hmi/1';items=@(
        @{type='HmiRectangle';props=@{Width=20;Height=10;BackColor='#FF0000';BorderWidth=0};text=@{}}
    )}
    Write-Json 'styles.json' @{format='tia-mcp-hmi/1';styles=@{blue=@{BackColor='#0000FF'}}}
    Write-Json 'manifest.json' $manifest
    Write-Json 'main.json' $screen
    $compact = Run-Render 'compact'
    $report = Get-Content -LiteralPath $compact.screens[0].report -Raw | ConvertFrom-Json
    Assert (($report.items[0].bounds -join ',') -eq '2,3,20,10') 'compact defaults resolve dimensions'
    $bitmap = [System.Drawing.Bitmap]::new($compact.screens[0].image)
    try { Pixel $bitmap 10 10 ([System.Drawing.Color]::Blue) 'style overrides default' } finally { $bitmap.Dispose() }
    Assert (-not ($report.warnings -like '*lacks defaultsFile*')) 'captured defaults avoid approximation warning'
    $manifest.Remove('defaultsFile')
    Write-Json 'manifest.json' $manifest
    $legacy = Run-Render 'legacy'
    $report = Get-Content -LiteralPath $legacy.screens[0].report -Raw | ConvertFrom-Json
    Assert ([bool]($report.warnings -like '*lacks defaultsFile*')) 'legacy compact limitations disclosed'

    $manifest.compact = $false
    $screen.items = @((Item 'Cycle' 'HmiScreenWindow' @{Left=0;Top=0;Width=50;Height=50;Screen='Main'}))
    Write-Json 'main.json' $screen
    Write-Json 'manifest.json' $manifest
    Reject-Render 'cyclic'
    $manifest.screenFiles[0].path = '..\outside.json'
    Write-Json 'manifest.json' $manifest
    Reject-Render 'path-escape'
    $manifest.screenFiles[0].path = 'main.json'
    Write-Json 'manifest.json' $manifest
    $screen.props.Width = 99999
    Write-Json 'main.json' $screen
    Reject-Render 'oversized'
    $screen.props.Width = 7999
    $screen.props.Height = 4000
    Write-Json 'main.json' $screen
    Reject-Render 'rounded-megapixel-limit' 'Main' 1.00001
    $screen.props.Width = 120
    $screen.props.Height = 100
    $screen.items = @((Item 'InvalidOpacity' 'HmiRectangle' @{Width=20;Height=10;Opacity=2}))
    Write-Json 'main.json' $screen
    Reject-Render 'invalid-opacity'
    $screen.items = @()
    Write-Json 'main.json' $screen

    Write-Json 'config.json' @{Server=@{Mode='Stdio'};Tia=@{AutoConnect=$false}}
    $request = @{jsonrpc='2.0';id=1;method='tools/call';params=@{name='hmi_render_json';arguments=@{
        input_directory=$temporary;output_directory=(Join-Path $temporary 'mcp');screen_name='Main';scale=1
    }}} | ConvertTo-Json -Depth 10 -Compress
    $rpc = $request | & $ExecutablePath --stdio --config (Join-Path $temporary 'config.json')
    Assert ($LASTEXITCODE -eq 0) 'offline MCP process exits'
    $response = $rpc | ConvertFrom-Json
    Assert (-not $response.result.isError) 'MCP rendering succeeds without TIA connection'
    $mcp = $response.result.content[0].text | ConvertFrom-Json
    Assert (Test-Path -LiteralPath $mcp.screens[0].image) 'MCP returns usable image path'
    Write-Output "PASS: $script:checks offline HMI renderer checks."
}
finally {
    Remove-Item -LiteralPath $temporary -Recurse -Force
}
