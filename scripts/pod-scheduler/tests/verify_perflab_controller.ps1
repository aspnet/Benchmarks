param(
    [Parameter(Mandatory = $true)][string] $ControllerPath,
    [Parameter(Mandatory = $true)][string] $OutputPath,
    [string] $VariablesPath
)

$ErrorActionPreference = 'Stop'
$controllerDirectory = Split-Path (Resolve-Path $ControllerPath)
$dotnetDirectory = Split-Path (Get-Command dotnet).Source
$sharedDirectory = Get-ChildItem "$dotnetDirectory\shared\Microsoft.AspNetCore.App" -Directory |
    Where-Object Name -Like '8.*' | Sort-Object Name -Descending | Select-Object -First 1
foreach ($directory in @($sharedDirectory.FullName, $controllerDirectory)) {
    Get-ChildItem $directory -Filter *.dll | ForEach-Object {
        try { [System.Reflection.Assembly]::LoadFrom($_.FullName) | Out-Null }
        catch [System.BadImageFormatException] { }
    }
}

$repository = Resolve-Path "$PSScriptRoot\..\..\.."
$profile = Join-Path $repository 'build\perflab.profile.yml'
$fixture = Join-Path $PSScriptRoot 'fixtures\perflab.benchmarks.yml'
$configurations = @{}
foreach ($enabled in @('false', 'true')) {
    $variables = [Newtonsoft.Json.Linq.JObject]::Parse('{}')
    if ($VariablesPath) {
        $variables = [Newtonsoft.Json.Linq.JObject]::Parse((Get-Content $VariablesPath -Raw))
    }
    $variables['perfLabPublication'] = [Newtonsoft.Json.Linq.JValue]::new($enabled)
    $configuration = [Microsoft.Crank.Controller.Program]::BuildConfigurationAsync(
        [string[]]@($fixture, $profile), 'perflab-fixture', [string[]]@(),
        [System.Collections.Generic.KeyValuePair[string,string][]]@(),
        $variables, [string[]]@('fixture', 'perflab'), [string[]]@(), 0
    ).GetAwaiter().GetResult()
    if (($configuration.Jobs['application'].AfterJob -join ',') -ne 'perflab-export') {
        throw 'Expected one application export hook.'
    }
    foreach ($name in @('load', 'db')) {
        if ($configuration.Jobs[$name].AfterJob.Count -ne 0) {
            throw "Unexpected export hook on $name."
        }
    }
    $job = $configuration.Jobs['application']
    $commands = $job.Commands['perflab-export']
    foreach ($platform in @('windows', 'linux', 'osx')) {
        foreach ($returnCode in @(0, 1)) {
            foreach ($hasResults in @($false, $true)) {
                $result = [Microsoft.Crank.Controller.ExecutionResult]::new()
                $result.ReturnCode = $returnCode
                $result.JobResults = [Microsoft.Crank.Models.JobResults]::new()
                if ($hasResults) {
                    $resultType = $result.JobResults.Jobs.GetType().GenericTypeArguments[1]
                    $result.JobResults.Jobs.Add('application', [Activator]::CreateInstance($resultType))
                }
                $engine = [Jint.Engine]::new()
                $engine.SetValue('job', [object]$job) | Out-Null
                if ($platform -ne $job.Environment.Platform) {
                    $engine.Execute("job = { environment: { platform: '$platform' } };") | Out-Null
                }
                $engine.SetValue('result', [object]$result) | Out-Null
                $selected = @($commands | Where-Object {
                    $engine.Evaluate($_.Condition).ToObject()
                })
                if ($selected.Count -ne 1) {
                    throw "Expected one command for $enabled/$platform/$returnCode/$hasResults."
                }
                $shouldExport = $enabled -eq 'true' -and $returnCode -eq 0 -and $hasResults
                $isExport = $selected[0].Script.Contains('CRANK_AZDO_POST_PROCESS_EXECUTABLE')
                if ($shouldExport -ne $isExport) {
                    throw "Incorrect command for $enabled/$platform/$returnCode/$hasResults."
                }
            }
        }
    }
    $configurations[$enabled] = $commands
}
$configurations | ConvertTo-Json -Depth 20 | Set-Content -Encoding utf8 $OutputPath
