# DeskGlow

### QR 연결 (0.2)

기존 Windows 프로그램을 종료하고 `windows/bin/publish-0.2/DeskGlow.exe`를 실행합니다. 갤럭시에 새 APK를 설치하고, PC 브라우저에서 [QR 표시](http://127.0.0.1:8766/pair)를 엽니다. 갤럭시 DeskGlow에서 **QR로 연결**을 누르고 PC의 Wi-Fi 주소에 해당하는 QR을 스캔합니다.

Windows 프로그램을 다시 실행해도 페어링 코드는 유지됩니다. PC의 내부 IP가 바뀌면 새 QR을 스캔하세요. QR에는 코드가 들어 있으므로 공유하지 마세요. Android 빌드는 Gradle 9.1 이상을 사용합니다.

## Android 앱과 Windows 프로그램

1. PC에서 `windows\bin\publish\DeskGlow.exe`를 실행합니다. 소스에서 실행하려면 `dotnet run --project windows`를 사용합니다. Wi-Fi 주소와 24자리 페어링 코드를 확인합니다. Windows 방화벽이 묻는 경우 신뢰하는 개인 네트워크에서만 허용합니다.
2. `android/app/build/outputs/apk/debug/app-debug.apk`를 USB로 S20 Ultra에 복사하고, 갤럭시의 **내 파일**에서 열어 설치합니다. USB 디버깅을 이미 승인했다면 `.\.android-sdk\platform-tools\adb.exe install -r android/app/build/outputs/apk/debug/app-debug.apk`로 설치할 수도 있습니다.
3. 갤럭시를 PC와 같은 Wi-Fi에 연결합니다. DeskGlow 앱 상단에 `http://`와 `:8766`을 포함한 PC 주소 및 코드를 입력하고 **연결 확인**을 누릅니다.
4. 볼륨 버튼을 눌러 PC 볼륨이 바뀌는지 확인합니다. 충전 중 앱이 화면에 떠 있으면 화면을 계속 켭니다.

Windows 프로그램은 현재 콘솔에서 실행합니다. 시험용 연결은 로컬 HTTP를 사용하며, Windows 프로그램을 재실행하면 새 코드가 발급됩니다. 사이트 버튼은 PC의 기본 브라우저를 사용합니다.

Android SDK 35는 이 저장소의 `.android-sdk`에 설치했습니다. Android Studio에서 소스를 열 경우 `android` 폴더를 프로젝트로 열고 이 SDK 경로를 지정하세요.

## 브라우저 프로토타입

갤럭시를 Windows용 스트림덱으로 쓰기 위한 첫 연결 테스트입니다.

1. Windows에 로그인한 상태에서 이 폴더의 PowerShell을 열고 `python deskglow.py`를 실행합니다.
2. S20 Ultra를 PC와 같은 공유기에 연결합니다. 휴대폰 Chrome에서 콘솔에 표시된 LAN 주소를 `#` 뒤의 문자열까지 포함해 엽니다. `127.0.0.1` 주소는 PC에서 확인할 때 사용합니다.
3. 볼륨 버튼을 눌러 Windows 작업 표시줄의 볼륨이 달라지는지 확인합니다. 휴대폰을 가로로 돌리면 2행 4열로 표시됩니다.
4. Windows 방화벽 알림이 나오면 신뢰하는 개인 네트워크에서만 허용합니다. 사용이 끝나면 PowerShell에서 Ctrl+C를 누릅니다.

테스트 서버는 로컬 네트워크에서 HTTP를 사용합니다. 신뢰하는 네트워크에서만 실행하고 공유기에 포트 포워딩을 설정하지 마세요. 실행할 때마다 새 접속 주소가 발급됩니다. 사이트 열기 버튼은 Windows 기본 브라우저에서 Google을 엽니다.

다음 단계는 Android 앱의 충전 중 화면 유지, 8~32개 버튼 편집, Windows의 Chrome 창 배치와 전원 제어입니다. 이후 설치 파일과 APK를 GitHub Releases에 배포하고, Android 앱을 Play 스토어 테스트 트랙에서 검증합니다.
