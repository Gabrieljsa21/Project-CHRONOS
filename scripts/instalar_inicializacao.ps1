# Cria (ou remove, com -Remover) o atalho do Chronos na pasta Inicializar do Windows,
# para o agendador subir sozinho a cada login.
param([switch]$Remover)

$raiz = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $raiz "bin\Release\net48\Chronos.exe"
$atalho = Join-Path ([Environment]::GetFolderPath("Startup")) "Chronos.lnk"

if ($Remover) {
    if (Test-Path $atalho) { Remove-Item $atalho -Confirm:$false }
    Write-Output "Atalho removido: $atalho"
    return
}

if (-not (Test-Path $exe)) {
    Write-Error "Chronos.exe não encontrado. Compile antes: dotnet build -c Release"
    return
}

$shell = New-Object -ComObject WScript.Shell
$lnk = $shell.CreateShortcut($atalho)
$lnk.TargetPath = $exe
$lnk.WorkingDirectory = Split-Path -Parent $exe
$lnk.Save()
Write-Output "Atalho criado: $atalho"
