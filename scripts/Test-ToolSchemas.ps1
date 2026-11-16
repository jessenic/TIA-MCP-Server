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

    $requiredHmiContracts = @{
        hmi_get_screen_item = @('screen_name', 'item_name')
        hmi_update_screen_item = @('screen_name', 'item_name', 'item_properties')
        hmi_read_event_scripts = @('screen_name')
        hmi_set_event_script = @('screen_name', 'event_type', 'script')
        hmi_set_expression_dynamization = @('screen_name', 'item_name', 'property_name', 'expression', 'result_type')
        hmi_set_expression_dynamization_entries = @('screen_name', 'item_name', 'property_name', 'entries')
        hmi_get_js_type_definitions = @()
    }
    foreach ($toolName in $requiredHmiContracts.Keys) {
        $schemaPath = Join-Path $outputDirectory "$toolName.json"
        if (-not (Test-Path -LiteralPath $schemaPath)) {
            throw "Required HMI tool schema '$toolName' was not exported."
        }
        $schema = Get-Content -LiteralPath $schemaPath -Raw | ConvertFrom-Json
        foreach ($parameter in $requiredHmiContracts[$toolName]) {
            if ($parameter -notin $schema.parameters.required) {
                throw "$toolName must require '$parameter'."
            }
        }
    }
    $requiredDiscoveryContracts = @{
        tia_get_block_info = @('block_name')
        tia_get_block_attributes = @('block_name')
        tia_get_tag_table_info = @('table_name')
        tia_get_type_info = @('type_name')
        tia_get_type_attributes = @('type_name')
        tia_list_global_libraries = @()
        tia_search_project_library_types = @('project_path')
        tia_list_project_faceplates = @('project_path')
        tia_list_project_text_categories = @('project_path')
        tia_download_project_texts = @('project_path', 'destination_file_path', 'primary_culture', 'reference_culture')
        hmi_list_screen_groups = @()
        hmi_list_graphic_lists = @()
    }
    foreach ($toolName in $requiredDiscoveryContracts.Keys) {
        $schemaPath = Join-Path $outputDirectory "$toolName.json"
        if (-not (Test-Path -LiteralPath $schemaPath)) {
            throw "Required discovery tool schema '$toolName' was not exported."
        }
        $schema = Get-Content -LiteralPath $schemaPath -Raw | ConvertFrom-Json
        foreach ($parameter in $requiredDiscoveryContracts[$toolName]) {
            if ($parameter -notin $schema.parameters.required) {
                throw "$toolName must require '$parameter'."
            }
        }
        if ($schema.parameters.additionalProperties -ne $false) {
            throw "$toolName must reject undeclared parameters."
        }

        $checked = Get-Content -LiteralPath (Join-Path $PSScriptRoot "..\schemas\$toolName.json") -Raw | ConvertFrom-Json
        if ($checked.name -ne $schema.name -or $checked.description -ne $schema.description) {
            throw "Checked-in $toolName identity or description differs from its registered schema."
        }
        $exportedRequired = @($schema.parameters.required) -join ','
        $checkedRequired = @($checked.parameters.required) -join ','
        if ($exportedRequired -ne $checkedRequired) {
            throw "Checked-in $toolName required fields differ from its registered schema."
        }
        $exportedProperties = @($schema.parameters.properties.PSObject.Properties)
        $checkedProperties = @($checked.parameters.properties.PSObject.Properties)
        if ($exportedProperties.Count -ne $checkedProperties.Count) {
            throw "Checked-in $toolName parameter count differs from its registered schema."
        }
        foreach ($property in $exportedProperties) {
            $checkedProperty = $checked.parameters.properties.($property.Name)
            if ($null -eq $checkedProperty -or $checkedProperty.type -ne $property.Value.type) {
                throw "Checked-in $toolName.$($property.Name) differs from its registered schema."
            }
        }
    }
    $requiredAdditionalContracts = @{
        tia_get_plc_tag_info = @('tag_name', 'table_name')
        tia_read_user_constant = @('constant_name')
        tia_get_tag_table_attributes = @('table_name')
        tia_create_block_group = @('group_name')
        tia_get_db_member_attributes = @('db_name', 'member_name')
        tia_get_library_type_instances = @('project_path', 'plc_name', 'library_type_id')
        hmi_create_navigation_button = @('target_name', 'screen_name', 'item_name', 'text', 'destination_screen', 'left', 'top', 'width', 'height', 'on_conflict')
        tia_create_plc_tag_table = @('table_name')
        tia_create_tagtable_group = @('group_name')
        tia_create_user_constant = @('table_name', 'constant_name', 'data_type', 'value')
        tia_update_user_constant = @('table_name', 'constant_name', 'value')
        tia_create_project = @('destination_location', 'project_name')
        hmi_create_screen_group = @('group_name', 'on_conflict')
        hmi_create_screen_under_group = @('parent_group_path', 'screen_name', 'on_conflict')
        hmi_create_tag_table = @('table_name', 'on_conflict')
        hmi_rename_screen = @('screen_name', 'group_path', 'new_name', 'on_conflict')
        hmi_rename_screen_group = @('group_path', 'new_name', 'on_conflict')
        hmi_rename_tag_table = @('table_name', 'new_name', 'on_conflict')
    }
    foreach ($toolName in $requiredAdditionalContracts.Keys) {
        $schemaPath = Join-Path $outputDirectory "$toolName.json"
        if (-not (Test-Path -LiteralPath $schemaPath)) {
            throw "Required additional tool schema '$toolName' was not exported."
        }
        $schema = Get-Content -LiteralPath $schemaPath -Raw | ConvertFrom-Json
        foreach ($parameter in $requiredAdditionalContracts[$toolName]) {
            if ($parameter -notin $schema.parameters.required) {
                throw "$toolName must require '$parameter'."
            }
        }
        if ($schema.parameters.additionalProperties -ne $false) {
            throw "$toolName must reject undeclared parameters."
        }

        $checked = Get-Content -LiteralPath (Join-Path $PSScriptRoot "..\schemas\$toolName.json") -Raw | ConvertFrom-Json
        if ($checked.name -ne $schema.name -or $checked.description -ne $schema.description) {
            throw "Checked-in $toolName identity or description differs from its registered schema."
        }
        if ((@($checked.parameters.required) -join ',') -ne (@($schema.parameters.required) -join ',')) {
            throw "Checked-in $toolName required fields differ from its registered schema."
        }
        $exportedProperties = @($schema.parameters.properties.PSObject.Properties)
        $checkedProperties = @($checked.parameters.properties.PSObject.Properties)
        if ($exportedProperties.Count -ne $checkedProperties.Count) {
            throw "Checked-in $toolName parameter count differs from its registered schema."
        }
        foreach ($property in $exportedProperties) {
            $checkedProperty = $checked.parameters.properties.($property.Name)
            if ($null -eq $checkedProperty -or
                $checkedProperty.type -ne $property.Value.type -or
                $checkedProperty.description -ne $property.Value.description -or
                (@($checkedProperty.enum) -join ',') -ne (@($property.Value.enum) -join ',')) {
                throw "Checked-in $toolName.$($property.Name) differs from its registered schema."
            }
        }
    }
    $screenSchema = Get-Content -LiteralPath (Join-Path $outputDirectory 'hmi_create_screen_under_group.json') -Raw | ConvertFrom-Json
    $checkedScreenSchema = Get-Content -LiteralPath (Join-Path $PSScriptRoot "..\schemas\hmi_create_screen_under_group.json") -Raw | ConvertFrom-Json
    foreach ($dimension in @('width', 'height')) {
        if ($screenSchema.parameters.properties.$dimension.minimum -ne 1 -or
            $checkedScreenSchema.parameters.properties.$dimension.minimum -ne 1) {
            throw "hmi_create_screen_under_group.$dimension must require a minimum of 1."
        }
    }
    foreach ($toolName in @(
        'hmi_create_screen_group',
        'hmi_create_screen_under_group',
        'hmi_create_tag_table',
        'hmi_rename_screen',
        'hmi_rename_screen_group',
        'hmi_rename_tag_table'
    )) {
        $schema = Get-Content -LiteralPath (Join-Path $outputDirectory "$toolName.json") -Raw | ConvertFrom-Json
        if ((@($schema.parameters.properties.on_conflict.enum) -join ',') -ne 'fail') {
            throw "$toolName.on_conflict must expose only the safe fail behavior."
        }
    }
    foreach ($toolName in @(
        'tia_list_global_libraries',
        'tia_search_project_library_types',
        'tia_list_project_faceplates',
        'tia_list_project_text_categories'
    )) {
        $schema = Get-Content -LiteralPath (Join-Path $outputDirectory "$toolName.json") -Raw | ConvertFrom-Json
        if ($schema.parameters.properties.offset.type -ne 'integer' -or
            $schema.parameters.properties.page_size.type -ne 'integer') {
            throw "$toolName must expose integer offset and page_size parameters."
        }
    }

    $updateSchema = Get-Content -LiteralPath (Join-Path $outputDirectory 'hmi_update_screen_item.json') -Raw | ConvertFrom-Json
    foreach ($propertyName in @('Visible', 'Top', 'Left', 'Width', 'Height')) {
        if ($null -eq $updateSchema.parameters.properties.item_properties.properties.$propertyName) {
            throw "hmi_update_screen_item must expose '$propertyName' in its property allowlist."
        }
    }
    if ($updateSchema.parameters.properties.item_properties.additionalProperties -ne $false) {
        throw 'hmi_update_screen_item.item_properties must reject properties outside its allowlist.'
    }
    $expressionSchema = Get-Content -LiteralPath (Join-Path $outputDirectory 'hmi_set_expression_dynamization.json') -Raw | ConvertFrom-Json
    if ($expressionSchema.parameters.properties.on_conflict.enum -notcontains 'replace') {
        throw "hmi_set_expression_dynamization must expose explicit conflict handling."
    }
    $expressionEntriesSchema = Get-Content -LiteralPath (Join-Path $outputDirectory 'hmi_set_expression_dynamization_entries.json') -Raw | ConvertFrom-Json
    if ($expressionEntriesSchema.parameters.properties.entries.items.properties.condition.type -ne 'string' -or
        $expressionEntriesSchema.parameters.properties.entries.items.properties.value.properties.type.type -ne 'string' -or
        $expressionEntriesSchema.parameters.properties.entries.items.properties.value.properties.value.type -ne 'string' -or
        $expressionEntriesSchema.parameters.properties.entries.items.additionalProperties -ne $false -or
        $expressionEntriesSchema.parameters.properties.entries.items.properties.value.additionalProperties -ne $false) {
        throw 'hmi_set_expression_dynamization_entries must define strict condition and typed-value row objects.'
    }
    $sclImportSchema = Get-Content -LiteralPath (Join-Path $outputDirectory 'tia_import_scl_block.json') -Raw | ConvertFrom-Json
    if ('scl_code' -notin $sclImportSchema.parameters.required) {
        throw 'tia_import_scl_block must require the complete SCL source string.'
    }
    if ($sclImportSchema.parameters.properties.compile_after_import.default -ne $true) {
        throw 'tia_import_scl_block.compile_after_import must default to true.'
    }
    if ($sclImportSchema.parameters.properties.open_after_import.default -ne $false) {
        throw 'tia_import_scl_block.open_after_import must default to false.'
    }
    $checkedSclImportSchema = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\schemas\tia_import_scl_block.json') -Raw | ConvertFrom-Json
    if ($checkedSclImportSchema.description -ne $sclImportSchema.description) {
        throw 'Checked-in tia_import_scl_block description differs from the exported tool schema.'
    }
    foreach ($propertyName in @('scl_code', 'plc_name', 'folder', 'compile_after_import', 'open_after_import')) {
        $exportedProperty = $sclImportSchema.parameters.properties.$propertyName
        $checkedProperty = $checkedSclImportSchema.parameters.properties.$propertyName
        if ($null -eq $checkedProperty -or
            $checkedProperty.type -ne $exportedProperty.type -or
            $checkedProperty.description -ne $exportedProperty.description -or
            $checkedProperty.default -ne $exportedProperty.default) {
            throw "Checked-in tia_import_scl_block.$propertyName differs from the exported tool schema."
        }
    }

    Write-Output "PASS: $($files.Count) tool schemas; $arrayCount typed array parameters; checked-in schemas match."
}
finally {
    Remove-Item -LiteralPath $outputDirectory -Recurse -Force
}
