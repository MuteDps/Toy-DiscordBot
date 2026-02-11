---
description: 프로젝트를 빌드하고 다른 PC로 배포하기 위해 패키징하는 방법입니다.
---
// turbo
1. 프로젝트 루트에서 `.\publish.ps1` 스크립트를 실행합니다.
   ```powershell
   .\publish.ps1
   ```
2. 실행이 완료되면 루트 디렉토리에 `dist` 폴더가 생성됩니다.
3. `dist` 폴더를 통째로 압축하여 대상 PC로 복사합니다.
4. 대상 PC에 [VLC Player](https://www.videolan.org/vlc/)가 설치되어 있는지 확인합니다.
5. 대상 PC에서 `run.ps1`을 마우스 우클릭 후 'PowerShell에서 실행'을 눌러 프로그램을 구동합니다.
