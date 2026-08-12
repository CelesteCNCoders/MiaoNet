#requires -Version 7.0
$mod_name = "MiaoNet"
dotnet build -c Release
$v_str = Get-Content -Path ModFolder/everest.yaml -Raw
$v = [regex]::Match($v_str, "(?<=Version:\s)(.*?)\n").Value.Trim()
Compress-Archive ModFolder/* "$mod_name v$v.zip" -Force -CompressionLevel NoCompression
dotnet clean