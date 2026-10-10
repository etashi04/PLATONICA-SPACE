# PLATONICA SPACE 한국어 패치

Steam판 **PLATONICA SPACE** 비공식 한국어 패치 저장소입니다.

현재 정식 배포 버전은 `v1.0.2`이며 Steam 앱 ID `3846480`, 게임 빌드 `25608431`, Windows x64 환경에서 확인했습니다.

<img width="850" height="478" alt="image" src="https://github.com/user-attachments/assets/bf2a6947-8cf4-4ed4-b56b-e7f03f53bce3" />
<img width="850" height="478" alt="image" src="https://github.com/user-attachments/assets/dc3ed09c-4b05-4a15-8c5f-951abd24bcc3" />


## 배포본

[최신 릴리스](https://github.com/etashi04/PLATONICA-SPACE/releases/latest)에서 다운로드할 수 있습니다.

- `PLATONICA_SPACE_Korean_Patch_v1.0.2.zip`: GUI 설치기를 통한 자동 설치·원본 복구
- `PLATONICA_SPACE_Korean_Patch_Manual_v1.0.2.zip`: 게임 실행 파일이 있는 폴더에 직접 복사
- `SHA256SUMS.txt`: 배포 ZIP 무결성 확인용 체크섬

게임과 Steam을 종료한 상태에서 설치하세요. 두 배포본 모두 게임 원본 파일과 저장 데이터를 수정하지 않습니다.


## 자동 설치

1. 자동 설치 ZIP을 모두 압축 해제합니다.
2. `PLATONICA SPACE 한국어 패치 v1.0.2.exe`를 실행합니다.
3. 자동으로 찾은 게임 폴더를 확인합니다. 찾지 못하면 `platonica-space.exe`가 있는 폴더를 선택합니다.
4. **한국어 패치 설치**를 누르고 완료 메시지를 확인한 뒤 게임을 실행합니다.

## 수동 설치

1. 수동 설치 ZIP을 모두 압축 해제합니다.
2. 안의 모든 파일과 폴더를 `platonica-space.exe`가 있는 경로에 복사하여 병합·덮어씁니다.
3. 게임을 실행합니다.

첫 실행은 BepInEx 초기화 때문에 평소보다 오래 걸릴 수 있습니다. BepInEx 콘솔에 `KR_PATCH_READY`가 표시되면 정상입니다.

## 복구

- 자동판: 같은 설치기에서 **원본 복구**를 선택합니다.
- 수동판: 게임을 종료한 뒤 게임 폴더의 `BepInEx/plugins/KR.LanguageFontPoc` 폴더를 삭제합니다.

## 오류 해결

### 스마트 앱 컨트롤이 설치기 또는 게임 실행을 차단할 때

이 패치의 설치 EXE와 일부 DLL은 디지털 서명이 없어 Windows 스마트 앱 컨트롤에서 차단될 수 있습니다. 오류 안내에서 스마트 앱 컨트롤에 의한 차단인지 먼저 확인하세요.

해당 기능이 차단 원인이라면 아래 설정을 끈 뒤 다시 실행할 수 있습니다. **이 설정은 PC 전체의 앱 실행 보호 수준을 낮추므로 변경 여부는 사용자가 판단해 주세요.** 변경 전 화면에 표시되는 재활성화 관련 안내도 확인하세요.

1. **Windows 보안**을 엽니다.
2. **앱 및 브라우저 컨트롤 → 스마트 앱 컨트롤 설정**으로 이동합니다.
3. 설정 변경을 선택한 경우 **끄기**로 바꾼 뒤, 차단됐던 설치기 또는 게임을 다시 실행합니다.

설치 EXE만 차단된다면 **수동 설치판**을 이용할 수 있습니다. 다만 게임이 불러오는 패치 DLL 자체가 차단되면 **수동 설치로도 해결되지 않습니다.**

차단 원인이 다르거나 문제가 계속되면 오류 문구와 차단된 파일명을 확인한 뒤, 저장소의 [이슈(Issues) 탭](https://github.com/etashi04/PLATONICA-SPACE/issues)에 제보해 주세요. 로그·스크린샷을 공개할 때는 사용자 이름과 개인 경로를 가려 주세요.

## 주의

- 원작자·개발사·배급사와 무관한 비공식 한국어 팬 패치입니다.
- 게임 업데이트 후 호환되지 않을 수 있습니다.
- 배포 파일은 `SHA256SUMS.txt`로 무결성을 확인할 수 있습니다.
- 게임 원본 자산과 실행 파일은 저장소에 포함하지 않습니다.
- 게임 업데이트 후 일부 문장이 원문으로 표시될 수 있습니다.
- 한국어 폰트 자산은 Noto Sans KR을 기반으로 하며 SIL Open Font License 1.1을 따릅니다. `OFL-1.1.txt`를 확인하세요.
