# P2 개발 기준

- **P2 작업을 시작할 때 [핵심 기획 방향](Docs/Planning/00_GameDirection.md)을 반드시 직접 읽고 기획·설계·구현·리뷰의 기준으로 삼는다.** 모험가의 인생, 다양한 모험가와의 상호작용·관계 및 그로부터 생기는 경험을 중심에 두고, 기획으로 조정할 수치·문자열은 데이터 시트로 분리한다.
- P2는 DS 기반을 IZ처럼 필요한 실행 경로만 가져온 별도 Unity 프로젝트다. DS/IZ 원본은 별도 요청 없이 수정하지 않는다.
- 현재 실행은 타이틀 → 마을 → 던전이다. [타이틀·마을 기획](Docs/Planning/02_TitleAndTown.md), [구조](Docs/Architecture/TitleAndTown.md), [던전 기획](Docs/Planning/03_Dungeon.md)과 [구조](Docs/Architecture/Dungeon.md)를 읽는다. 기존 [기반 테스트 기획](Docs/Planning/01_Foundation.md)과 [코드 읽기](Docs/Architecture/Foundation.md)는 별도 테스트 씬의 기준이다.
- DS에서 추가 이식할 때 `../DS/AGENTS.md`, `../DS/Docs/Architecture/Architecture.md`, 관련 상세 문서와 실제 호출자/소비자를 확인한다. 상대 경로·클래스·네임스페이스는 가능하면 유지하되 게임 전용 이름/의존성은 제거하고 차이를 문서화한다.
- MainFlow의 `Input → EventQueue/Handler → CombatTickFlow → View` 실행 순서를 유지한다. 던전의 게이지와 동료·적의 자동 공격은 TickFlow에서 처리한다. 직접 선택하는 행동은 플레이어 한 명에게만 허용한다. DS의 전체 순서 `Input → EventQueue → EventProcessor → TickFlow → Observer → ObjectLoader → View` 중 현재 사용하지 않는 계층을 빈 프레임워크로 만들지 않는다.
- 중앙 EventQueue 처리 중 같은 큐에 요청을 추가하지 않는다. View는 원본 데이터를 변경하거나 저장하지 않고 입력을 기록한다. 상태는 DataContainer가 소유하고 Presenter는 Reader로 조회한다.
- 생성/연결/해제는 MainFlow가 조율한다. DI 등록, 버튼 구독 및 정리 경로를 함께 유지한다.
- 저장 DTO/버전/검증은 FoundationSave, 파일 I/O는 SaveLoadHelper 책임이다. 불러오기 실패 시 기존 상태를 보존한다.
- 전리품·금화·소지품 작업은 [전리품 기획](Docs/Planning/04_LootAndSettlement.md)과 [구조](Docs/Architecture/LootAndSettlement.md)를 읽는다. 모험 진행 저장은 AdventureProgressStore/AdventureSave가 책임지고 Foundation 저장과 분리한다. 정산 저장에 성공한 뒤에만 지급·차감과 마을 이동을 확정한다.
- 거래 작업은 [상점 기획](Docs/Planning/05_Shop.md)과 [구조](Docs/Architecture/Shop.md)를 따른다. 플레이어 금화·아이템과 상인 재고를 함께 저장한 뒤에만 거래를 반영하고, 기존 모험 저장의 이행과 던전 정산 후 재고 보존을 확인한다.
- 인물·관계·파티·성장은 [기획](Docs/Planning/06_CharactersAndParty.md), [구조](Docs/Architecture/CharactersAndParty.md), [DS 수식 근거](Docs/Architecture/CharactersCombatDS.md)를 읽는다. 실제 파티를 전투와 정산에 사용하고, 영입한 인물의 방문자 중복 표시·추방으로 인한 원천 성장/금화 소실·불러올 때 성장 재추첨을 막는다.
- 기능 변경 시 사용자 요구와 구현자가 보완한 사양을 Docs/Planning에 구분하여 기록하고 구조 문서도 맞춘다. 문서는 Git으로 관리하고 빌드에 넣지 않는다.
- 의뢰는 [기획](Docs/Planning/07_Quests.md)과 [구조](Docs/Architecture/Quests.md)를 따른다. 수락 이후의 사건만 진척에 반영하고, 납부·보상·완료를 함께 저장한다. 구조 노드는 원본 지도 대신 이번 탐험에만 추가한다.
- C#은 Allman 중괄호 스타일을 사용한다. 코드 컴파일, Unity 씬 실행, Player 실행을 구분해 검증 결과를 보고한다.
- 직업·스킬·개인 메뉴는 [기획](Docs/Planning/08_SkillsAndClasses.md), [구조](Docs/Architecture/SkillsAndClasses.md), [DS 원본 근거](Docs/Architecture/SkillsDS.md)를 따른다. 액티브는 직업 트리 하나, 패시브는 모든 트리에서 선택한다. NPC 레벨업의 무작위 습득은 EXP 저장 후보에 포함하며 불러오기/재시도로 다시 추첨하지 않는다.
- 실제 기능 예제 씬과 유효한 회귀 테스트는 유지한다. 일회성 생성기/검증 부팅 코드/임시 빌드는 검증 후 제거한다.
- 장비·소모품은 [기획](Docs/Planning/09_EquipmentAndItems.md)과 [구조](Docs/Architecture/EquipmentAndItems.md)를 따른다. 현재 직업은 장착 아이템에서 파생하고 학습 ID/SP는 보존한다. 장착품 1개는 판매에서 제외하며 포션 저장 성공 뒤에만 탐험 HP와 행동 게이지에 적용한다.
