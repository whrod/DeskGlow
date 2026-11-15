# DeskGlow

## Windows와 Android 연결

1. Windows에서 .NET 10 SDK로 `dotnet publish windows -c Release -o windows/bin/publish-v0.3.0`를 실행합니다.
2. 이전 DeskGlow 프로그램이 켜져 있다면 닫고 `windows/bin/publish-v0.3.0/DeskGlow-Windows-v0.3.0.exe`를 엽니다. 방화벽 확인이 뜨면 신뢰하는 개인 네트워크에서 허용합니다.
3. 갤럭시와 PC를 같은 Wi-Fi에 연결하세요. 갤럭시 DeskGlow에서 **QR로 연결**을 누르고 Windows 창의 QR을 스캔합니다. 주소가 여러 개면 **PC 주소**에서 두 기기가 공유하는 네트워크의 주소를 선택합니다.
4. 갤럭시 앱이 화면에 떠 있으면 Windows 창에 **갤럭시 연결됨**이 표시되고 QR이 접힙니다. 창을 닫아도 트레이에서 실행됩니다. 트레이 아이콘을 우클릭하면 PC 제어를 켜고 끄거나 프로그램을 종료할 수 있습니다.

연결 상태를 자동으로 갱신하려면 Android 0.3 APK를 설치하세요. 새 APK를 빌드하려면 JDK 21, Android SDK 35, Gradle 9.1을 준비하고 `gradle -p android assembleDebug`를 실행합니다. 결과 파일명에는 버전이 붙습니다.

PC 주소가 유지되면 Windows 프로그램을 다시 실행해도 기존 연결을 사용할 수 있습니다. PC의 내부 IP가 바뀌면 새 QR을 스캔하세요. QR에는 페어링 코드가 들어 있습니다. 연결은 로컬 HTTP를 사용합니다.

전원 제어와 Chrome 작업 세트는 Windows 창의 **기능 추가** 탭에 모았습니다. PC 켜기는 Wake-on-LAN, 종료는 실행 중인 Windows 프로그램의 명령으로 구현할 예정입니다. Chrome 작업 세트에는 사이트 주소와 창별 모니터, 위치, 크기를 설정할 예정입니다.

## 브라우저 프로토타입

갤럭시를 Windows용 스트림덱으로 쓰기 위한 첫 연결 테스트입니다.

1. Windows에 로그인한 상태에서 이 폴더의 PowerShell을 열고 `python deskglow.py`를 실행합니다.
2. S20 Ultra를 PC와 같은 공유기에 연결합니다. 휴대폰 Chrome에서 콘솔에 표시된 LAN 주소를 `#` 뒤의 문자열까지 포함해 엽니다. `127.0.0.1` 주소는 PC에서 확인할 때 사용합니다.
3. 볼륨 버튼을 눌러 Windows 작업 표시줄의 볼륨이 달라지는지 확인합니다. 휴대폰을 가로로 돌리면 2행 4열로 표시됩니다.
4. Windows 방화벽 알림이 나오면 신뢰하는 개인 네트워크에서만 허용합니다. 사용이 끝나면 PowerShell에서 Ctrl+C를 누릅니다.

테스트 서버는 로컬 네트워크에서 HTTP를 사용합니다. 신뢰하는 네트워크에서만 실행하고 공유기에 포트 포워딩을 설정하지 마세요. 실행할 때마다 새 접속 주소가 발급됩니다. 사이트 열기 버튼은 Windows 기본 브라우저에서 Google을 엽니다.

다음 단계는 Android 앱의 충전 중 화면 유지, 8~32개 버튼 편집, Windows의 Chrome 창 배치와 전원 제어입니다. 이후 설치 파일과 APK를 GitHub Releases에 배포하고, Android 앱을 Play 스토어 테스트 트랙에서 검증합니다.
