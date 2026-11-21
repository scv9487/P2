# P2 개발 기준

- P2는 DS 기반을 IZ처럼 필요한 실행 경로만 가져온 별도 Unity 프로젝트다. DS/IZ 원본은 별도 요청 없이 수정하지 않는다.
- 현재 구현은 게임 콘텐츠 없는 단일 기반 테스트 씬이다. [기획서](Docs/Planning/01_Foundation.md), [구조 및 코드 읽기](Docs/Architecture/Foundation.md)를 먼저 읽는다.
- DS에서 추가 이식할 때 `../DS/AGENTS.md`, `../DS/Docs/Architecture/Architecture.md`, 관련 상세 문서와 실제 호출자/소비자를 확인한다. 상대 경로·클래스·네임스페이스는 가능하면 유지하되 게임 전용 이름/의존성은 제거하고 차이를 문서화한다.
- MainFlow의 `Input → EventQueue/Handler → View` 실행 순서를 유지한다. DS의 전체 순서 `Input → EventQueue → EventProcessor → TickFlow → Observer → ObjectLoader → View` 중 현재 사용하지 않는 계층을 빈 프레임워크로 만들지 않는다.
- 중앙 EventQueue 처리 중 같은 큐에 요청을 추가하지 않는다. View는 원본 데이터를 변경하거나 저장하지 않고 입력을 기록한다. 상태는 DataContainer가 소유하고 Presenter는 Reader로 조회한다.
- 생성/연결/해제는 MainFlow가 조율한다. DI 등록, 버튼 구독 및 정리 경로를 함께 유지한다.
- 저장 DTO/버전/검증은 FoundationSave, 파일 I/O는 SaveLoadHelper 책임이다. 불러오기 실패 시 기존 상태를 보존한다.
- 기능 변경 시 사용자 요구와 구현자가 보완한 사양을 Docs/Planning에 구분하여 기록하고 구조 문서도 맞춘다. 문서는 Git으로 관리하고 빌드에 넣지 않는다.
- C#은 Allman 중괄호 스타일을 사용한다. 코드 컴파일, Unity 씬 실행, Player 실행을 구분해 검증 결과를 보고한다.
- 실제 기능 예제 씬과 유효한 회귀 테스트는 유지한다. 일회성 생성기/검증 부팅 코드/임시 빌드는 검증 후 제거한다.
