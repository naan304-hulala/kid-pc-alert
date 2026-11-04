# 아이 컴퓨터 알림 제거 스크립트
#Requires -RunAsAdministrator
$name = 'KidPcAlert'

if (Get-Service $name -ErrorAction SilentlyContinue) {
    # 제거할 때도 '감지 프로그램이 중지됐어요' 카톡이 한 번 가요.
    Stop-Service $name -Force -ErrorAction SilentlyContinue
    sc.exe delete $name | Out-Null
}

Remove-Item (Join-Path $env:ProgramFiles $name) -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $env:ProgramData $name) -Recurse -Force -ErrorAction SilentlyContinue
Write-Host '제거 완료.'
