# P2

DS/IZ의 입력·요청 처리·데이터·UI·저장 기반을 가져온 Unity 프로젝트입니다. 현재 게임 콘텐츠 없이 기반 기능 테스트 씬만 연결되어 있습니다.

Unity **6000.0.58f2**로 열고 [2_Test.unity](Assets/0_Project/2_Test/0_Scenes/2_Test.unity)를 열어 Play하세요.

- F1 / F2: 숫자 증가 / 감소
- 이름 입력 → 이름 적용: 문자열 데이터 변경
- F5 / F9: 저장 / 불러오기
- Esc: 팝업 열기 / 닫기
- 현재 데이터 초기화: 메모리만 초기화하고 저장 파일 유지

저장 파일 위치는 화면 하단에 표시됩니다. 문자 편집 중에는 단축키가 차단됩니다.

[기획 및 기본값](Docs/Planning/01_Foundation.md) · [구조와 코드 읽기](Docs/Architecture/Foundation.md)

회귀 테스트는 Unity Test Runner의 EditMode / PlayMode에서 실행할 수 있습니다. 저장 테스트는 별도 임시 폴더를 사용합니다.
