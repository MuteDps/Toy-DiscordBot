# publish.ps1
# 이 스크립트는 프로젝트를 빌드하고 다른 PC로 복사하기 좋게 'dist' 폴더에 패키징합니다.

$publishDir = "dist"
if (Test-Path $publishDir) {
    Remove-Item -Recurse -Force $publishDir
}
New-Item -ItemType Directory -Path $publishDir

echo "--- 1. .NET 프로젝트 빌드 및 게시 (Self-contained) ---"
# Self-contained 빌드를 통해 대상 PC에 .NET 런타임이 없어도 실행 가능하게 만듭니다.
dotnet publish src/YoutubeTogether.csproj -c Release -r win-x64 --self-contained true -o "$publishDir/app"

echo "--- 2. 필수 유틸리티(yt-dlp.exe) 복사 ---"
if (Test-Path "yt-dlp.exe") {
    Copy-Item "yt-dlp.exe" "$publishDir/app/"
} else {
    Write-Warning "yt-dlp.exe를 찾을 수 없습니다. 수동으로 dist/app 폴더에 복사해 주세요."
}

echo "--- 3. 간편 실행 스크립트(run.ps1) 생성 ---"
$runScriptContent = @"
# run.ps1
# VLC를 HTTP 모드로 실행하고 봇을 시작합니다.

`$vlcPath = "C:\Program Files\VideoLAN\VLC\vlc.exe"
if (-not (Test-Path `$vlcPath)) {
    Write-Error "VLC Player를 찾을 수 없습니다. 'C:\Program Files\VideoLAN\VLC\vlc.exe' 경로에 설치되어 있는지 확인하세요."
    pause
    exit
}

echo "VLC를 모드(HTTP/Fullscreen)로 실행 중..."
Start-Process `$vlcPath -ArgumentList "--extraintf http --http-password=ytqueue --fullscreen"

echo "YoutubeTogether 봇 실행 중..."
cd app
.\YoutubeTogether.exe
"@

$runScriptContent | Out-File -FilePath "$publishDir/run.ps1" -Encoding utf8

echo "--- 완료! ---"
echo "'$publishDir' 폴더만 압축하여 대상 PC로 전달하면 됩니다."
echo "대상 PC에서 실행 방법: run.ps1을 PowerShell에서 실행"
