# ArcaDownloader

아카라이브 게시글을 이미지와 함께 ZIP 파일로 저장하는 Windows GUI 다운로더입니다.

현재 UI는 MewUI 기반이며 Native AOT publish를 목표로 합니다. Python 구현은 C# AOT로 포팅되었습니다.

## 기능

- 게시글 URL 여러 개 순차 다운로드
- 본문 HTML, 메타데이터, 이미지 ZIP 저장
- 원본 이미지 또는 미리보기 이미지 다운로드 선택
- 다운로드 진행률, 속도, ETA 표시
- 일시정지, 재개, 중지
- 중지 또는 비정상 종료 후 같은 URL 재실행 시 성공한 이미지부터 이어받기
- WebView2 로그인 및 쿠키 저장
- 실행 파일 옆 `settings.ini`에 저장 위치 기억

## 프로젝트 구조

```text
src/
├── ArcaDownloader.Core/          # 다운로드, 파싱, 쿠키 저장, ZIP 생성
└── ArcaDownloader.MewUI/         # MewUI Windows GUI
tests/
└── ArcaDownloader.Tests/
```

## 빌드

필요 환경:

- Windows 10/11
- .NET 10 SDK
- Microsoft Edge WebView2 Runtime

의존성 복원:

```powershell
dotnet restore .\ArcaDownloader.sln
```

Release 빌드:

```powershell
dotnet build .\ArcaDownloader.sln -c Release
```

테스트:

```powershell
dotnet test .\ArcaDownloader.sln -c Release
```

Native AOT publish:

```powershell
dotnet publish .\src\ArcaDownloader.MewUI\ArcaDownloader.MewUI.csproj -c Release -r win-x64 -o .\publish
```

GitHub Actions release workflow는 수동 실행 또는 `v*` 태그 push로 `win-x64`, `win-arm64` Native AOT ZIP을 생성합니다.

간편 빌드 스크립트:

```powershell
.\build-release.bat
```

또는:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-release.ps1
```

배포 파일은 아래 하나입니다.

```text
publish\ArcaDownloader.MewUI.exe
```

`arca_icon.png`는 실행 파일에 embedded resource로 포함됩니다. `settings.ini`는 프로그램 실행 시 exe와 같은 폴더에 생성됩니다.

빌드 산출물 정리:

```powershell
dotnet clean .\ArcaDownloader.sln
```

## 이어받기

다운로드 중 성공한 이미지는 출력 폴더의 `.arca_tmp\<url-hash>\images`에 즉시 저장됩니다. 프로세스가 중간에 꺼지거나 사용자가 중지해도 같은 URL을 다시 다운로드하면 이미 저장된 이미지는 재사용하고 나머지부터 진행합니다. ZIP 파일은 다운로드가 끝날 때 다시 생성됩니다.

## 로그인 동작

로그인 창은 WebView2를 사용합니다. 로그인 완료 확인 시 `https://arca.live/settings/profile`로 실제 이동해 접근 가능 여부를 확인하고, WebView2 CookieManager에서 arca.live 쿠키를 저장합니다. 이 방식은 `document.cookie`에 노출되지 않는 HttpOnly 쿠키도 처리합니다.

## 라이선스

MIT
