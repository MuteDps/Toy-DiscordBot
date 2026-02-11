# run.ps1
# VLC瑜?HTTP 紐⑤뱶濡??ㅽ뻾?섍퀬 遊뉗쓣 ?쒖옉?⑸땲??

$vlcPath = "C:\Program Files\VideoLAN\VLC\vlc.exe"
if (-not (Test-Path $vlcPath)) {
    Write-Error "VLC Player瑜?李얠쓣 ???놁뒿?덈떎. 'C:\Program Files\VideoLAN\VLC\vlc.exe' 寃쎈줈???ㅼ튂?섏뼱 ?덈뒗吏 ?뺤씤?섏꽭??"
    pause
    exit
}

echo "VLC瑜?紐⑤뱶(HTTP/Fullscreen)濡??ㅽ뻾 以?.."
Start-Process $vlcPath -ArgumentList "--extraintf http --http-password=ytqueue --fullscreen"

echo "YoutubeTogether 遊??ㅽ뻾 以?.."
cd app
.\YoutubeTogether.exe
