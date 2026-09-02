# PLATONICA SPACE 한국어 패치

PLATONICA SPACE 비공식 한국어 패치 작업 저장소입니다.

현재 정식 배포 버전은 `1.0.1`입니다. Steam 앱 ID `3846480`, 게임 빌드 `24960315`, Windows x64 환경에서 확인했습니다.

## 저장소 구성

- `src/KR.LanguageFontPoc`: BepInEx IL2CPP 런타임 패치 소스
- `package/BepInEx/plugins/KR.LanguageFontPoc`: 현재 테스트 중인 플러그인, 한글 폰트 번들, 번역 데이터

## 배포본

- `PLATONICA SPACE 한국어 패치 (Auto).zip`: ZIP 안의 GUI 설치기를 실행합니다.
- `PLATONICA SPACE 한국어 패치 (Manual).zip`: 실행 파일 없이 게임 폴더에 직접 복사합니다.
- `SHA-256 체크섬.txt`: 두 배포 ZIP의 무결성 확인용 체크섬입니다.

게임을 종료한 상태에서 설치하세요. 두 배포본 모두 게임 원본 파일과 세이브 파일을 수정하지 않습니다.

## 무설치판 설치

1. `*_Manual_*.zip`의 내용물을 `PLATONICA SPACE` 게임 설치 폴더에 풉니다.
2. 게임을 실행합니다. 첫 실행은 BepInEx 초기화 때문에 평소보다 오래 걸릴 수 있습니다.
3. BepInEx 콘솔에 `KR_PATCH_READY`가 표시되면 정상입니다.

## 설치판 설치

1. 자동 설치판 ZIP을 풀고 안의 EXE를 실행합니다.
2. 게임 폴더를 자동으로 찾지 못하면 `platonica-space.exe`가 있는 폴더를 선택합니다.
3. 완료 메시지를 확인하고 게임을 실행합니다.

## 확인된 범위

- 본편 대사 및 선택지 한국어 출력
- 옵션과 인벤토리 UI 한국어 출력
- 기억의 조각·인물 상세 설명 한국어 출력
- 아이템·키워드 목록의 잔여 일본어 보정
- 한국어 FontAsset 런타임 적용

## 삭제

게임을 종료한 뒤 `PLATONICA SPACE\BepInEx\plugins\KR.LanguageFontPoc` 폴더만 삭제합니다.

## 알려진 한계

- 일부 기억 로그에서 정답·오답 색상 라벨의 시각적 중앙 정렬이 약간 어긋날 수 있습니다.
- 게임 업데이트로 텍스트 자산이 변경되면 일부 문장이 원문으로 표시될 수 있습니다.

## 주의

- 비공식 팬 패치입니다.
- 원본 게임 파일은 이 저장소에 포함하지 않습니다.
- 게임 원본 자산과 실행 파일은 포함하지 않습니다.
- 한국어 폰트 자산은 Noto Sans KR을 기반으로 하며 SIL Open Font License 1.1을 따릅니다. `OFL-1.1.txt`를 확인해 주세요.
