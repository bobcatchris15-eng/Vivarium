param(
 [Parameter(Mandatory)][ValidateSet('Inventory','Bootstrap','BlenderGenerate','BlenderReference','MaterialExport','ValidateGltf','OptimizeGltf','EncodeMotion','InspectCapture')][string]$Action,
 [string]$InputPath, [string]$OutputPath,
 [string]$Blender='C:/Program Files/Blender Foundation/Blender 5.2/blender.exe'
)
$ErrorActionPreference='Stop'
$repo=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$evidence=Join-Path $repo 'build/visual-lab/tool-tests'
if(Test-Path (Join-Path $repo '.clanker/HALT')){throw 'HALT active'}
New-Item -ItemType Directory -Force $evidence|Out-Null
function Checked([string]$Exe,[string[]]$Arguments){
 & $Exe @Arguments
 if($LASTEXITCODE -ne 0){throw "$Exe exited $LASTEXITCODE"}
}
if($Action -eq 'Inventory'){
 $tools=@('dotnet','python','pwsh','ffmpeg','magick','renderdoccmd','gltf-transform','gltfpack','gltf_validator','nvidia-smi')
 $tools|ForEach-Object{Get-Command $_ -ErrorAction SilentlyContinue|Select-Object Name,Source}
 Get-Item -LiteralPath $Blender,'C:/tools/MaterialMaker/material_maker.exe' -ErrorAction SilentlyContinue|Select-Object FullName,@{n='Version';e={$_.VersionInfo.ProductVersion}}
 return
}
if($Action -eq 'Bootstrap'){
 # Keep portable source isolated; never reinstall or overwrite working machine-wide tools.
 $mm=Join-Path $evidence 'material-maker/source'
 if(!(Test-Path -LiteralPath $mm)){
  Checked 'git' @('clone','https://github.com/RodZill4/material-maker.git',$mm)
  Checked 'git' @('-C',$mm,'checkout','8719010210b84cc45a4a1d4f0f5c1bc62d7b68e1')
 }
 $revision=(& git -C $mm rev-parse HEAD).Trim()
 if($revision -ne '8719010210b84cc45a4a1d4f0f5c1bc62d7b68e1'){throw 'Material Maker source revision differs; reconcile without resetting it'}
 Checked 'python' @((Join-Path $PSScriptRoot 'prepare_material_maker.py'),$mm)
 if(!(Test-Path -LiteralPath (Join-Path $mm '.godot/global_script_class_cache.cfg'))){
  . (Join-Path $repo 'scripts/common.ps1')
  Checked (Get-GodotExe) @('--headless','--path',$mm,'--editor','--import','--quit')
 }
 Write-Output "Pinned Material Maker source: $mm ($revision). Batch startup repairs and their hashes are recorded in material-maker/source-repair.json."
 & $PSCommandPath -Action Inventory
 return
}
$lease=$null
try{
 if($Action -in 'BlenderReference','MaterialExport'){
  $lease=[IO.File]::Open((Join-Path $repo '.clanker/visual-lab-gpu.lease'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
  if(Test-Path (Join-Path $repo '.clanker/HALT')){throw 'HALT active'}
 }
 switch($Action){
  {$_ -in 'BlenderGenerate','BlenderReference'} {
   if(!$OutputPath){$OutputPath=Join-Path $evidence 'blender'}
   $argsList=@('--background','--factory-startup','--python',(Join-Path $PSScriptRoot 'generate_leaf.py'),'--','--output',$OutputPath)
   if($Action -eq 'BlenderReference'){$argsList+='--render'}
   Checked $Blender $argsList
  }
  'MaterialExport' {
   if(!$InputPath -or !$OutputPath){throw 'MaterialExport requires InputPath and OutputPath'}
   New-Item -ItemType Directory -Force $OutputPath|Out-Null
   . (Join-Path $repo 'scripts/common.ps1')
   Checked (Get-GodotExe) @('--path',(Join-Path $evidence 'material-maker/source'),'--export-material','--target','Godot/Godot 4 Standard','-o',$OutputPath,$InputPath)
   if(!(Get-ChildItem -LiteralPath $OutputPath -Filter '*.tres')){throw 'No material export produced'}
  }
  'ValidateGltf' {if(!$InputPath){throw 'InputPath required'}; Checked 'gltf_validator' @('-o',$InputPath); Checked 'gltf-transform' @('inspect',$InputPath)}
  'OptimizeGltf' {
   if(!$InputPath -or !$OutputPath){throw 'InputPath and distinct OutputPath required'}
   if([IO.Path]::GetFullPath($InputPath) -eq [IO.Path]::GetFullPath($OutputPath)){throw 'Never overwrite input asset'}
   Checked 'gltfpack' @('-i',$InputPath,'-o',$OutputPath,'-kn','-km','-kv','-noq','-af','0'); Checked 'gltf_validator' @('-o',$OutputPath)
  }
  'EncodeMotion' {if(!$InputPath -or !$OutputPath){throw 'PNG sequence pattern and OutputPath required'}; Checked 'ffmpeg' @('-y','-framerate','24','-i',$InputPath,'-c:v','libx264','-crf','18','-pix_fmt','yuv420p',$OutputPath)}
  'InspectCapture' {
   if(!$InputPath -or !$OutputPath){throw 'RDC InputPath and XML OutputPath required'}
   Checked 'renderdoccmd' @('convert','-f',$InputPath,'-o',$OutputPath,'-c','zip.xml')
   Checked 'python' @((Join-Path $PSScriptRoot 'inspect_rdc_xml.py'),$OutputPath)
  }
 }
}finally{if($lease){$lease.Dispose()}}
