# DeskGlow

갤럭시를 Windows용 스트림덱으로 쓰는 앱입니다. [제품 사이트 및 Windows 다운로드](https://whrod.github.io/DeskGlow/) · [버전별 변경 기록](CHANGELOG.md)

## 사용하기

1. Windows용 DeskGlow를 실행하고 갤럭시와 PC를 같은 Wi-Fi에 연결합니다.
2. 갤럭시 DeskGlow에서 **QR로 연결**을 누르고 Windows 창의 QR을 스캔합니다.
3. **PC 절전**을 누르면 PC가 절전 모드로 들어갑니다. PC에 연결된 키보드로 깨울 수 있습니다.

Windows 창을 닫아도 트레이에서 계속 실행됩니다. 트레이 메뉴에서 PC 제어를 켜고 끄거나 앱을 종료할 수 있습니다.

Windows의 **작업 배치 → 새 배치**에서 이름을 입력하고 **현재 창 가져오기 → 저장**을 누릅니다. Android에 배치별 버튼이 자동으로 생깁니다. 자세한 사용법은 Windows의 **도움말** 탭에서 확인할 수 있습니다.

## 배포

[업데이트 및 배포 절차](updatelogic.md)

홈페이지는 GitHub Pages가 `main`의 `docs/` 변경을 자동 배포합니다. 새 공개 릴리스를 최신 릴리스로 지정하고 `DeskGlow-Windows-x64.exe`와 `DeskGlow-Android.apk`를 함께 첨부하면 홈페이지 다운로드가 새 파일로 연결됩니다.

## 빌드

- Windows (.NET 10 SDK) — `dotnet publish windows -c Release -o windows/bin/publish`
- Android (JDK 21, Android SDK 36, Gradle 9.1) — `gradle -p android assembleDebug`. 버전명이 붙은 APK는 `android/app/build/outputs/dist/`에 생성됩니다.

무선 디버깅으로 페어링한 갤럭시는 Windows의 **개발용 → 기기 검색 → 설치 / 업데이트**에서 새 APK를 설치할 수 있습니다. 검색되지 않으면 갤럭시 무선 디버깅 메인 화면의 **IP 주소 및 포트**를 입력해 연결하세요.
