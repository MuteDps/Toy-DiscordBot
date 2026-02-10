# YoutubeTogether

이 문서는 이 저장소를 다른 컴퓨터에서 동일하게 재현(reproducible snapshot)할 수 있도록 필요한 실행 파일(유틸), 설정, 빌드 및 실행 절차를 모두 정리한 안내서입니다.

주의: README에는 소스코드가 포함되지 않습니다. 소스 코드가 필요하면 저장소의 `src/` 폴더를 사용하세요.

**목표**: Windows 환경에서 VLC(HTTP 인터페이스) + yt-dlp + .NET 실행 환경을 준비하고, 프로젝트를 빌드하여 CLI/Discord 동시 실행 환경을 재현합니다.

## 파일/구성 요약 (스냅샷 정보)
- 프로젝트 루트: 이 README 파일
- 소스 코드: [src](src) 폴더에 포함 (예: [src/Program.cs](src/Program.cs), [src/VlcController.cs](src/VlcController.cs), [src/VLCCommandExecutor.cs](src/VLCCommandExecutor.cs), [src/DiscordBot.cs](src/DiscordBot.cs), [src/YtDlpHelper.cs](src/YtDlpHelper.cs))
- 실행에 필요한 외부 유틸/바이너리(설치 또는 포함):
  - VLC Player (권장 경로: `C:\Program Files\VideoLAN\VLC\vlc.exe`) — HTTP 인터페이스 사용
  - yt-dlp (명령행 도구, 시스템 `PATH`에 추가 또는 프로젝트 도구 폴더에 복사)
  - .NET SDK 8 (net8.0-windows)

## 권장 버전
- Windows 10/11 (최신 보안 업데이트 적용)
- .NET SDK 8.x (프로젝트는 `net8.0-windows` 타겟)
- VLC 최신 안정 버전
- yt-dlp 최신 버전

## 설치 및 준비 단계
1. .NET SDK 설치
   - https://dotnet.microsoft.com/ 에서 .NET 8 SDK(windows) 설치

2. VLC 설치
   - https://www.videolan.org/vlc/ 에서 VLC 설치
   - VLC를 HTTP 인터페이스로 실행하려면 다음 중 하나를 수행합니다:

```powershell
"C:\Program Files\VideoLAN\VLC\vlc.exe" --extraintf http --http-password=ytqueue --fullscreen
```

   - 위 예제에서 HTTP 비밀번호는 `ytqueue`로 사용됩니다(프로그램 기본값과 일치해야 합니다).

3. yt-dlp 설치
   - Windows용 실행파일을 다운로드 후 `yt-dlp.exe`를 `C:\Windows\System32` 또는 프로젝트와 같은 폴더에 두거나, `PATH`에 추가합니다.
   - 또는 Python이 있다면 `pip install -U yt-dlp` 로 설치 가능.

4. (선택) ffmpeg
   - 일부 환경에서 yt-dlp나 VLC와 연동해 필요할 수 있습니다. ffmpeg가 필요하면 설치하고 `PATH`에 추가하세요.

## 구성 (필수값)
- VLC HTTP 비밀번호: `ytqueue` (프로그램 기본값)
- VLC HTTP 주소: `http://localhost:8080` (기본값)
- Discord 봇 토큰: [src/Program.cs](src/Program.cs)에서 `string token = "YOUR_DISCORD_BOT_TOKEN";` 부분을 실제 토큰으로 교체하거나, 코드를 수정하여 환경변수로 읽도록 변경하세요.

## 빌드 및 실행
1. 저장소에서 프로젝트 루트로 이동합니다.

```powershell
cd <repository-root>
dotnet restore
dotnet build src/YoutubeTogether.csproj
```

2. 프로그램 실행(동시: CLI + Discord 백그라운드)

```powershell
dotnet run --project src/YoutubeTogether.csproj
```

3. CLI 사용 예시 (실행 후 콘솔에서 입력)
- 대기열 추가: `!play https://www.youtube.com/watch?v=...`
  - 참고: `!play`에 플레이리스트(또는 재생목록 URL)를 넣는 경우, 프로그램은 첫 항목의 스트림이 준비되는 즉시 재생을 시작하고 나머지 항목들을 백그라운드에서 순차적으로 처리(메타/스트림 추출 및 대기열 추가)합니다.
  - 플레이리스트 처리에는 시간이 소요될 수 있으니 `!jobs`로 진행 상태를 확인하세요. 100개 이상의 항목은 지원되지 않습니다.
- 건너뛰기: `!skip`
- 대기열 보기: `!queue`
- 항목 삭제: `!remove 2` (번호는 `!queue`에서 확인)
# YoutubeTogether

이 문서는 다른 Windows PC에서 이 프로젝트를 문제없이 재현할 수 있도록, 설치·구성·빌드·실행 과정을 사람 읽기 좋게 정리한 한글 가이드입니다.

중요: 소스 코드는 이 README에 포함되어 있지 않습니다. 소스는 `src/` 폴더에 있습니다(예: [src/Program.cs](src/Program.cs), [src/VlcController.cs](src/VlcController.cs), [src/VLCCommandExecutor.cs](src/VLCCommandExecutor.cs), [src/DiscordBot.cs](src/DiscordBot.cs), [src/YtDlpHelper.cs](src/YtDlpHelper.cs)).

개요
- 목적: VLC(HTTP 인터페이스)를 단일 재생 엔진으로 사용하고, `yt-dlp`로 스트림을 추출하여 CLI와 Discord로 재생/대기열을 제어하는 애플리케이션입니다.
- 핵심 원칙: VLC의 HTTP 플레이리스트를 권위(source-of-truth)로 사용하며, 애플리케이션은 VLC에 항목을 `enqueue`하고 자신이 넣은 항목에 한해 메타데이터를 보관합니다.

사전 준비물
- Windows 10/11
- .NET SDK 8.x
- VLC(권장 설치 경로: `C:\Program Files\VideoLAN\VLC\vlc.exe`)
- `yt-dlp.exe`(Windows) — 아래 위치 중 하나에 둡니다:
  - `<repo-root>/tools/yt-dlp.exe` (권장)
  - 컴파일된 바이너리와 같은 폴더(`src/bin/Debug/net8.0-windows/` 등)
  - 시스템 `PATH`에 포함된 폴더
- 선택사항: `ffmpeg`(필요 시)

권장 디렉터리 구조

RepositoryRoot/
- README.md
- src/
- tools/
  - yt-dlp.exe

설정값(필요한 경우 변경)
- VLC HTTP 비밀번호: 기본값은 `ytqueue`. VLC 시작 시 설정한 비밀번호와 일치해야 합니다.
- VLC HTTP 주소: `http://localhost:8080` (기본값)
- Discord 봇 토큰: [src/Program.cs](src/Program.cs)에 예시 토큰이 있습니다. 실제 토큰으로 교체하거나 환경변수로 읽도록 수정하세요.

설치 및 빌드 (PowerShell 예)

1) .NET SDK 설치 확인

```powershell
dotnet --version
# 8.x 버전이 나와야 합니다.
```

2) `yt-dlp.exe` 준비

```powershell
mkdir -Force tools
# 다운로드한 yt-dlp.exe를 tools 폴더에 복사하세요.
```

3) VLC를 HTTP 모드로 실행 (테스트용)

```powershell
& "C:\Program Files\VideoLAN\VLC\vlc.exe" --extraintf http --http-password=ytqueue --fullscreen
```

HTTP 인터페이스 확인

```powershell
curl -u :ytqueue http://localhost:8080/requests/status.xml
```

4) 프로젝트 빌드

```powershell
cd <repository-root>
dotnet restore
dotnet build src/YoutubeTogether.csproj
```

5) 앱 실행(콘솔 CLI + Discord 백그라운드)

```powershell
dotnet run --project src/YoutubeTogether.csproj
```

기본 CLI 명령
- `!play <YouTube URL>` — 단일 비디오 또는 플레이리스트를 추가합니다.
  - 플레이리스트인 경우: 첫 항목은 가능한 한 빨리 재생되도록 바로 처리하고, 나머지 항목은 백그라운드 작업으로 순차적으로 `enqueue`합니다.
  - 플레이리스트의 항목 수가 많으면 처리에 시간이 걸릴 수 있습니다. `!jobs`로 진행 상태를 확인하세요.
- `!skip` — 현재 재생 건 건너뛰기
- `!queue` — VLC 플레이리스트 보기
- `!remove <번호>` — `!queue`에서 표시된 번호로 항목 삭제
- `!clear` — 전체 대기열 삭제
- `!stop` — 재생 정지
- `!jobs` — 현재 실행 중인 백그라운드 작업 목록(요청자 포함)
- `!stats` — 완료된 작업 통계(설명+요청자별 평균/최대 시간)
- `!help` — 도움말(플레이리스트 동기화 주의 포함)

Discord로 사용
- [src/Program.cs](src/Program.cs)의 토큰을 실제 토큰으로 바꾸거나 환경변수(`DISCORD_BOT_TOKEN`)로 읽도록 수정하세요.
- 동일한 명령을 Discord 채널에서도 사용 가능합니다.

스모크 테스트 체크리스트

1. VLC가 `--extraintf http`로 실행되어 있는지 확인.
2. `yt-dlp -g <youtube-url>`로 스트림 URL이 나오는지 확인.
3. 앱 실행: `dotnet run --project src/YoutubeTogether.csproj`.
4. 콘솔에서 `!play https://www.youtube.com/watch?v=<id>` 실행 → 즉시 재생/대기열에 들어가는지 확인. `!jobs` 확인.
5. 플레이리스트 테스트: `!play https://www.youtube.com/playlist?list=<id>` → 첫 항목 즉시 재생, 나머지는 `!jobs`에서 진행됨.

문제 해결 팁

- 빌드 오류(파일 잠금): 이전 `YoutubeTogether` 프로세스를 종료하세요.
- VLC HTTP 오류: VLC를 HTTP 모드로 시작하고 비밀번호가 `ytqueue`인지 확인하세요.
- `yt-dlp` 오류: `yt-dlp -j <url>` 또는 `yt-dlp -g <url>`로 수동 검사하세요.

보안 주의

- Discord 토큰, VLC 비밀번호 등 민감정보는 저장소에 평문으로 올리지 마세요. 환경변수 또는 비밀관리 서비스를 사용하세요.

구성 관련 주요 파일
- `src/VlcController.cs` — VLC 프로세스 및 HTTP 처리
- `src/YtDlpHelper.cs` — `yt-dlp` 호출 로직(프로그램 폴더나 `tools/` 우선 검색)
- `src/VLCCommandExecutor.cs` — 명령 처리, 작업 추적, 메타데이터 매핑

페르소나 및 검토(요청에 따른 추가)
- 페르소나: GitHub Copilot — "친절하고 실용적인 개발 파트너" 역할로 작성합니다.
- 검토 의견: 이 문서와 레포지토리에 적힌 내용만으로, 동일한 Windows 환경에서 저는 맨땅에서 이 프로젝트를 문제없이 재현하고 실행할 수 있습니다. (필요한 도구: VLC, yt-dlp, .NET 8)

추가 작업 제안
- `run.ps1` 스크립트를 추가해 VLC 자동 시작(권장 플래그 포함)과 앱 실행을 편하게 만들 수 있습니다. 추가를 원하시면 `yes`라고 알려주세요.

작성일: 2026-02-10

---
Written: 2026-02-10
