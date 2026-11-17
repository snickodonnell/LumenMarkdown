param([string]$ExePath = (Join-Path $PSScriptRoot "dist\LumenMarkdown.exe"))

$resolved = (Resolve-Path -LiteralPath $ExePath).Path
$classes = "HKCU:\Software\Classes"
$progId = "LumenMarkdown.File"
$appName = "Lumen Markdown"

New-Item -Path "$classes\$progId" -Force | Out-Null
Set-Item -Path "$classes\$progId" -Value "Markdown Document"
New-Item -Path "$classes\.md" -Force | Out-Null
Set-Item -Path "$classes\.md" -Value $progId
New-Item -Path "$classes\.markdown" -Force | Out-Null
Set-Item -Path "$classes\.markdown" -Value $progId
New-Item -Path "$classes\$progId\DefaultIcon" -Force | Out-Null
Set-Item -Path "$classes\$progId\DefaultIcon" -Value "`"$resolved`",0"
New-Item -Path "$classes\$progId\shell\open\command" -Force | Out-Null
Set-Item -Path "$classes\$progId\shell\open\command" -Value "`"$resolved`" `"%1`""

$application = "$classes\Applications\LumenMarkdown.exe"
New-Item -Path "$application\shell\open\command" -Force | Out-Null
Set-Item -Path "$application\shell\open\command" -Value "`"$resolved`" `"%1`""
New-Item -Path "$application\SupportedTypes" -Force | Out-Null
New-ItemProperty -Path "$application\SupportedTypes" -Name ".md" -Value "" -PropertyType String -Force | Out-Null
New-ItemProperty -Path "$application\SupportedTypes" -Name ".markdown" -Value "" -PropertyType String -Force | Out-Null

$capabilities = "HKCU:\Software\LumenMarkdown\Capabilities"
New-Item -Path "$capabilities\FileAssociations" -Force | Out-Null
New-ItemProperty -Path $capabilities -Name "ApplicationName" -Value $appName -PropertyType String -Force | Out-Null
New-ItemProperty -Path $capabilities -Name "ApplicationDescription" -Value "A lightweight, native Markdown reader" -PropertyType String -Force | Out-Null
New-ItemProperty -Path "$capabilities\FileAssociations" -Name ".md" -Value $progId -PropertyType String -Force | Out-Null
New-ItemProperty -Path "$capabilities\FileAssociations" -Name ".markdown" -Value $progId -PropertyType String -Force | Out-Null
New-Item -Path "HKCU:\Software\RegisteredApplications" -Force | Out-Null
New-ItemProperty -Path "HKCU:\Software\RegisteredApplications" -Name $appName -Value "Software\LumenMarkdown\Capabilities" -PropertyType String -Force | Out-Null

Write-Host "Registered $appName for Markdown files."
