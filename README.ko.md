# NetThrottle

프로세스별로 TCP/UDP 대역폭(다운로드/업로드 한도를 각각 분리)을 제한하는 Windows 데스크톱 앱입니다.

[English](README.md) | **한국어**

## 주요 기능

- 프로세스(이미지 이름) 단위로 다운로드와 업로드 대역폭을 **각각 따로** 제한합니다.
- 프로토콜별 규칙 지정: TCP, UDP, 또는 둘 다(Both).
- 한도는 KB/s 단위로 입력하며, `0`은 무제한을 의미합니다.
- 패킷을 버리지 않고 **지연(셰이핑)** 시켜 설정한 평균 속도를 유지합니다.
- 프로세스별 실시간 업로드/다운로드 속도를 1초마다 표시합니다.
- 실행 중인 모든 프로세스를 이름순으로 보여주며, 각 행에서 한도를 입력하면 즉시 적용됩니다.
- 한도를 건 프로세스는 종료되어도 목록에 남고(이름이 빨간색), 다시 실행되면 한도가 자동으로 재적용됩니다.
- GitHub Releases 기반 자동 업데이트 확인(설치형은 자체 업데이트 지원).
- 설치형과 포터블, 두 가지 배포 형태를 제공합니다.

## 동작 방식

NetThrottle의 트래픽 셰이핑 코어는 [WinDivert](https://github.com/basil00/WinDivert)를 사용하여 네트워크 계층에서 TCP/UDP 패킷을 가로챕니다. 잡아낸 각 패킷의 로컬 포트는 Windows IP Helper의 확장 TCP/UDP 테이블을 통해 PID로 매핑되고, 그 PID는 다시 프로세스 이미지 이름으로 변환됩니다. 규칙은 이 **이미지 이름 + 프로토콜** 조합으로 매칭됩니다.

매칭된 패킷은 (프로세스, 방향)별 **토큰 버킷**을 거쳐 통과합니다. 토큰 버킷은 패킷을 드롭하지 않고 설정된 평균 속도에 맞춰 **지연**시키는 방식으로 한도를 적용합니다. 실시간 업/다운 속도는 1초에 한 번씩 샘플링하여 UI에 표시합니다.

전체 파이프라인:

```
WinDivert(네트워크 계층 캡처) → IP Helper(포트 → PID → 이미지 이름) → 규칙 매칭 → 토큰 버킷(지연/셰이핑) → 패킷 재전송
```

## 요구 사항

- Windows 10/11 (x64)
- **관리자 권한 실행** 필수 — WinDivert가 서명된 커널 드라이버를 로드합니다.
- `WinDivert.dll`과 `WinDivert64.sys`(v2.2.x, x64)가 `NetThrottle.exe`와 같은 폴더에 있어야 합니다.

## 설치

### 설치 프로그램 (.exe)

1. GitHub Releases에서 `NetThrottle_vX.Y.Z_Setup.exe`를 내려받습니다.
2. 설치 프로그램을 실행합니다(관리자 권한이 필요하며, 자동으로 권한 상승을 요청합니다).
3. 설치 후 시작 메뉴 또는 바탕화면 바로 가기로 NetThrottle을 실행합니다.

설치형은 설정 파일이 `%AppData%\NetThrottle\settings.json`에 저장됩니다.

### 포터블 (.zip)

1. GitHub Releases에서 `NetThrottle_vX.Y.Z_Portable.zip`을 내려받습니다.
2. 압축을 풀어 나온 `NetThrottle/` 폴더를 원하는 위치에 둡니다.
3. 폴더 안의 `NetThrottle.exe`를 관리자 권한으로 직접 실행합니다.

포터블 빌드에는 실행 파일 옆에 `portable.marker` 파일이 함께 들어 있습니다. 이 마커가 있으면 `settings.json`이 실행 파일과 같은 폴더에 저장되므로, 폴더 전체가 자체 완결형이며 그대로 이동할 수 있습니다.

## 사용법

1. 툴바의 **Start**(▶)를 눌러 모니터링/제한을 시작합니다. WinDivert 드라이버 로드를 위해 관리자 권한이 자동으로 요청됩니다.
2. 그리드에 실행 중인 모든 프로세스가 이미지 이름 기준 이름순으로 표시됩니다. **Filter**로 검색하거나 **Refresh apps**로 목록을 새로 고칠 수 있습니다.
3. 원하는 프로세스 행에서 **Down KB/s** / **Up KB/s**에 한도를 입력합니다. `0`은 무제한이며, 엔진이 켜져 있으면 즉시 적용됩니다.
4. 필요하면 해당 프로세스의 **Protocol**(Tcp/Udp/Both)을 바꿉니다.
5. **↓ live** / **↑ live** 열에서 프로세스별 실시간 속도를 확인합니다.
6. 한도를 건 프로세스는 종료되어도 목록에 남으며 이름이 **빨간색**으로 표시되고, 다시 실행되면 한도가 자동 재적용됩니다. **Clear**로 한도를 제거하고, **Stop**(■)으로 모든 제한을 해제합니다.

## 업데이트

앱은 실행 시 GitHub Releases API(latest)를 조회해 최신 릴리스 태그를 자신의 어셈블리 버전과 비교합니다. 더 새로운 버전이 있으면 상단에 알림 배너가 나타나며, 툴바의 **Check updates**로 직접 확인할 수도 있습니다.

- **설치형 빌드**: `*_Setup.exe` 자산을 내려받아 권한 상승된 상태로 실행한 뒤 앱을 종료하여 **자체 업데이트**를 수행합니다.
- **포터블 빌드**: 자체 설치는 하지 않고 릴리스 페이지로 연결되며, 수동으로 새 버전을 내려받아 교체합니다.

## 소스에서 빌드하기

1. [.NET 9 SDK](https://dotnet.microsoft.com/download)를 설치합니다.
2. 솔루션을 빌드합니다.

   ```bash
   dotnet build NetThrottle.sln -c Release
   ```

로컬에서 실행하려면 `WinDivert.dll`과 `WinDivert64.sys`(v2.2.x, x64)가 필요합니다. 두 파일을 `src/NetThrottle.Engine/native/x64/`에 넣으면(.csproj가 출력 폴더로 복사) 또는 빌드된 실행 파일과 같은 폴더에 두면 됩니다. 실행은 반드시 **관리자 권한**으로 해야 합니다.

### 엔진 동작 검증

`tools/NetThrottle.SmokeTest`는 헤드리스 종단(end-to-end) 검증 도구입니다. 루프백 TCP 소켓으로 대량 데이터를 (1) 엔진 없이 기준 측정하고 (2) 프로세스 캡을 적용해 다시 측정한 뒤 처리량을 비교합니다. 빌드된 `NetThrottle.SmokeTest.exe` 옆에 WinDivert 파일을 두고 **관리자 권한**으로 실행하면, 통과 시 처리량이 캡 근처로 떨어지는 것을 확인할 수 있습니다(예: 4 MB/s 캡에서 ~5400 MB/s → ~7 MB/s).

## 릴리스

버전 태그 `vX.Y.Z`를 푸시하면 `.github/workflows/release.yml`(GitHub Actions)이 전체 릴리스를 처리합니다.

1. 태그에서 버전을 추출합니다.
2. 자체 완결형(single-file) win-x64 실행 파일 `NetThrottle.exe`를 게시합니다.
3. WinDivert를 내려받아 `WinDivert.dll`과 `WinDivert64.sys`를 함께 묶습니다.
4. GitHub Release에 정확히 **두 개의 자산**을 생성하여 업로드합니다.

   - `NetThrottle_vX.Y.Z_Setup.exe` (NSIS 설치 프로그램)
   - `NetThrottle_vX.Y.Z_Portable.zip` (`NetThrottle/` 폴더를 풀어 `NetThrottle.exe`를 바로 실행)

## 프로젝트 구조

- **src/NetThrottle.Core** (`net9.0`) — 모델(`ThrottleRule`, `Direction`, `ProtocolKind`), 토큰 버킷 셰이퍼, 설정(`AppSettings`, `SettingsStore`).
- **src/NetThrottle.Engine** (`net9.0-windows`) — WinDivert P/Invoke, IP Helper 포트→PID 매핑, 패킷 펌프(`PacketEngine`), 패킷 파서.
- **src/NetThrottle.App** (`net9.0-windows`, WPF) — MVVM UI, 서비스(`EngineController`, `SettingsService`, `ProcessListProvider`, `GitHubUpdateService`), `MainWindow.xaml`, 관리자 권한 매니페스트(`requireAdministrator`).
- **tools/NetThrottle.SmokeTest** (`net9.0-windows`) — 실제 throttle 동작을 검증하는 헤드리스 루프백 처리량 테스트(관리자 권한 실행).

## 라이선스

MIT 라이선스로 배포됩니다. 자세한 내용은 [LICENSE](LICENSE)를 참고하세요.

제작: Kim, Hwan (akon47@naver.com) · https://github.com/akon47/NetThrottle
