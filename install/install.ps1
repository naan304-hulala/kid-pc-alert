# 아이 컴퓨터 알림 설치 스크립트 (관리자 권한으로 실행돼요)
#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'

$name   = 'KidPcAlert'
$target = Join-Path $env:ProgramFiles $name
$data   = Join-Path $env:ProgramData $name
$source = Join-Path $PSScriptRoot 'KidPcAlert.exe'

if (-not (Test-Path $source)) { throw "KidPcAlert.exe 를 이 폴더에서 찾지 못했어요: $PSScriptRoot" }

# 이미 설치돼 있으면 멈추고 지운 뒤 새로 설치해요.
if (Get-Service $name -ErrorAction SilentlyContinue) {
    Write-Host '기존 설치를 정리하는 중...'
    Stop-Service $name -Force -ErrorAction SilentlyContinue
    sc.exe delete $name | Out-Null
    Start-Sleep -Seconds 2
}

New-Item -ItemType Directory -Force -Path $target, $data | Out-Null
Copy-Item $source $target -Force

# 설정 폴더는 관리자와 시스템만 열 수 있게 잠가요. (아이 계정은 못 열어요)
icacls $data /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' | Out-Null

& (Join-Path $target 'KidPcAlert.exe') setup
if ($LASTEXITCODE -ne 0) { throw '설정이 끝나지 않았어요. install.cmd 를 다시 실행해 주세요.' }

$exe = Join-Path $target 'KidPcAlert.exe'
sc.exe create $name binPath= "`"$exe`"" start= auto DisplayName= $name | Out-Null
sc.exe description $name 'PC usage notifier' | Out-Null
# 혹시 멈추면 5초 뒤 자동으로 다시 시작해요.
sc.exe failure $name reset= 86400 actions= restart/5000/restart/5000/restart/5000 | Out-Null

# '빠른 시작'을 끄면 시작/종료가 정확히 감지돼요.
Set-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Power' -Name HiberbootEnabled -Value 0 -Type DWord

Start-Service $name
Write-Host ''
Write-Host '설치 완료! 이제 이 컴퓨터가 켜지고 꺼질 때 카카오톡으로 알려드려요.'
