# 던전 구조

기획 기준은 [던전 탐험과 전투](../Planning/03_Dungeon.md)다.

인물·파티 추가 후 실제 실행은 저장된 최대 4인 파티와 [DS 전투·성장 수식](CharactersCombatDS.md)을 사용한다. 추가된 입력·경험치 저장·참가자 정산은 [인물·파티 구조](CharactersAndParty.md)를 따른다. 아래 임시 파티 및 고정 공격 수치는 이전 기반 회귀 경로의 기록이다.

## 실행 경로

```text
마을의 던전 입구 → 입장 → 6개 카드 중 선택 → 확정
→ MainFlow의 선택 ID 전달 및 SceneLoader
→ 3_Dungeon의 MainFlow 초기화

View 입력 기록 → InputDispatcher → 중앙 EventQueue → DungeonEventHandler
→ CombatTickFlow.Update(deltaTime) → DungeonViewPresenter → DungeonView
```

- MainFlow가 기존 순서에 실제 전투 시간 시뮬레이션을 추가한다. EventQueue 다음, View 이전에 CombatTickFlow를 호출한다. 별도 Update를 가진 게임 로직이나 중앙 큐로 재등록하는 경로는 만들지 않는다.
- 명시적인 노드 이동·공격 선택·휴식·층 이동·대화·귀환은 EventQueue 단계에서 처리한다. 게이지 진행과 동료·적의 자동 공격은 TickFlow 단계에서 처리한다.
- DungeonDataContainer가 체력·게이지·위치·방문/해결 여부·전투와 결과를 소유한다. View/Presenter는 읽기 인터페이스로 조회하며 버튼은 UIInputStream에 의도만 기록한다.
- 노드 요청은 발생한 층과 노드를, 공격 요청은 행동 대기 중인 플레이어를 문맥으로 전달한다. `ReadyAllyId`는 직접 조작하는 플레이어만 가리킨다. 뒤늦게 도착한 이전 화면의 요청·동료의 수동 공격 요청·중복 공격을 실행 시점에 거절한다.
- 플레이어 행동 선택 대기 중에는 설정에 따라 시뮬레이션을 멈춘다. 포커스를 잃거나 씬을 전환할 때도 TickFlow를 멈추며 멈춘 시간을 나중에 몰아서 처리하지 않는다.
- 타이틀/마을/던전은 씬별 MainFlow를 유지한다. 선택한 던전 번호만 일시적으로 다음 씬에 전달하고 입장 시 소비한다. 전역 캠페인 프레임워크나 영속 오브젝트는 추가하지 않는다.
- 지도 우측 상단의 `DungeonReturnTownButton`은 ‘귀환’으로 표시하고 강조색을 사용한다. `DungeonReturnTown` 요청 → `CanReturnTown` 검사 → 전리품 정산창 열기 → 정산 저장 성공 → MainFlow의 마을 씬 전환 순서다. 노드를 선택할 수 있는 지도에서는 항상 사용할 수 있으며, 문구와 지도 안내는 DungeonTextTable에서 조정한다.
- 기존 Foundation 테스트 씬·저장 DTO·파일 형식은 유지한다.
- 처치 보상·정산·금화·소지품과 별도 진행 저장은 [전리품 정산 구조](LootAndSettlement.md)를 따른다. 기존 Foundation 저장과 탐험 중 상태는 이 저장에 포함하지 않는다.

## 콘텐츠와 상태

던전 정의, 층별 그래프, 임시 파티, 전투 설정, UI/결과 문구는 `Assets/1_InternalTools/Util/DataSheetLoader/Table/`의 Dungeon CSV에서 읽는다. DungeonContent가 CSV와 기존 TownContent를 연결하고 DungeonSheet가 불변 정의를 만든다. 마을에 등장하던 인물의 ID와 초상화를 그대로 참조한다.

노드 ID와 층 번호가 방문/해결 상태의 기준이다. 시트의 표시명이나 화면 Text를 식별자로 사용하지 않는다. 콘텐츠 수정은 다음 씬 초기화 때 반영된다.

| 시트 | 조정 항목 |
| --- | --- |
| DungeonDefinitionTable.csv | 던전 번호·이름·설명·층 수, 회색쥐 기본 체력·공격력·게이지 속도 |
| DungeonNodeTable.csv | 층별 노드 ID·종류·이름·설명, 정규화 좌표 X/Y, 양방향 이웃 ID, 방문 인물 ID |
| DungeonPartyTable.csv | 임시 아군 두 명의 ID·이름·체력·공격력·게이지 속도·초상화 번호, IsPlayer(플레이어 1 / 자동 동료 0) |
| DungeonSettingTable.csv | 일반/보스 적 수(1~3), 보스 배율·층별 증가량, 휴식·이벤트 회복량, WaitOnReady |
| DungeonTextTable.csv | 선택 카드·지도·노드 아이콘·커맨드·전투·결과·안내 문구 |

게이지는 0~1이며 Speed는 초당 충전량이다. 파티의 `IsPlayer=1`은 정확히 한 명이어야 하며, 나머지는 자동 동료다. `WaitOnReady=1`이면 플레이어가 준비된 경계에서 동료의 동시 자동 행동을 먼저 처리한 뒤 모든 게이지·자동 공격을 멈추고 플레이어의 선택을 기다린다. `0`이면 기다리는 동안에도 동료와 적이 행동한다. 동료는 살아 있는 첫 번째 적을 공격하고, 적은 살아 있는 아군을 순서대로 공격한다. 플레이어가 쓰러진 경우에는 동료가 계속 자동으로 전투한다.

현재 모든 던전은 같은 3층 그래프를 사용하며, 적의 능력치가 번호와 층에 따라 증가한다. 서로 다른 층 수를 혼용하려면 던전별 그래프 구분도 함께 확장해야 한다.

각 층의 첫 행은 휴식 시작점이다. 이웃 목록은 `;`로 구분하며 자기 연결·중복·끊어진/단방향 연결·도달할 수 없는 노드를 거절한다. 마지막 층에만 보스가 있어야 하고 이전 층에는 계단이 필요하다. CSV 형식, 중복 키, 숫자 범위, 인물 참조와 필수 문구도 초기화 때 검사한다.

DS의 `MainFlow`, `TimeHandler`, `TickFlowHandler`에서 시간 처리의 호출자와 소비자를 확인하고 단계별 책임을 유지했다. P2는 자동 공격만 필요하므로 다중 맵·BT·Observer·내부 Tick 요청 큐를 이식하지 않고 CombatTickFlow가 DataContainer의 시간 처리를 직접 조율한다. 회색쥐 그림은 UI 전용 RatGraphic이 메시로 그리며 게임 상태를 변경하지 않는다.

## 검증

Unity 6000.0.58f2에서 2026-11-21에 확인했다.

전리품 정산 추가 후 통합 검증은 EditMode **113/113**, PlayMode **12/12**이며 [최신 정산 검증 기록](LootAndSettlement.md)을 따른다. 아래는 던전 초기 구현과 이전 변경의 검증 기록이다.

- **EditMode 68/68 통과**: 기존 기반·마을 48개와 던전 20개. 여섯 던전의 강도 증가, 그래프 연결과 보스 층 제한, 인접/오래된 요청 판정, 재방문 시 일회성 처리, 체력 유지, 게이지 준비 경계·중복 공격 차단·대기/실시간 설정·승리/패배를 검증했다. 플레이어 전용 수동 입력, 동료 자동 행동, 동시 준비 순서, 플레이어 사망 후 동료의 전투 지속, 시트 역할 교체와 잘못된 역할 설정도 포함한다.
- **PlayMode 10/10 통과**: 기존 7개와 던전 3개. 실제 씬에서 여섯 카드 선택·취소·확정·재입장, 노드 이동, 휴식·인물·이벤트 팝업, 모달 뒤 클릭 차단, 3층 전투와 최종 보스 결과의 마을 귀환을 검증했다. 미라가 플레이어보다 먼저 자동 공격하는 순간, 플레이어만 선택 대기하는 상태, 카드의 나/자동 표시와 동료의 자동 승리도 확인했다. 새 UI가 실제 raycast 대상이 될 때까지 제한 시간 안에서 기다리며 최상단 hit를 검사한다. 현재 문구의 글자 높이와 주요 제목 너비도 검사한다.
- Windows x64 Development 빌드와 별도 Player 실행에서 던전 1의 모든 노드 종류와 3층 보스 처치·마을 귀환, 던전 6 선택과 중도 귀환을 확인했다. 검증 입력은 Unity EventSystem raycast로 선택한 버튼에 포인터 이벤트를 전달했다. 테스트에서만 포커스를 부여하고 시간을 4배속으로 진행했으며 실제 OS 키보드·마우스 입력 검증과 구분한다.
- Player 화면 캡처 24장을 남겼다. 1280×800에서 입구·선택 카드·지도·각 노드·일반/보스 전투·결과창, 1024×768과 1920×1080에서 지도 배치를 확인했다. 전투 중 지도 문구가 겹치지 않도록 수정한 뒤 Player 전체 진행을 다시 통과했다. 폰트에 없는 인물 아이콘은 지원되는 한글로 교체했다.
- 동료 자동 행동 반영 후 Windows Player를 새로 빌드해 첫 전투를 확인했다. 미라가 입력 없이 12 피해를 주고, 플레이어만 선택 대기하며 그동안 모든 게이지와 자동 공격이 정지했다. 실제 EventSystem 최상단 hit를 통한 공격·대상 선택 두 번으로 승리했다. 1280×800의 선택 대기/승리 화면 2장과 카드의 나/자동 표시를 확인했다. 검증 전용 포커스와 4배속을 사용했으며 결과는 `Logs/Companion-Build.log`, `Logs/CompanionPlayerQA-Player.log`, `Logs/CompanionPlayerQA/`에 있다.
- 지도 버튼을 ‘귀환’으로 명확히 표시한 뒤 던전 PlayMode **3/3 통과**. 시작 지도와 노드 이동 후 지도에서 실제 EventSystem raycast 클릭으로 마을 복귀·재입장을 확인했고, 모달 뒤 버튼 차단과 3층 진행도 통과했다. 문구 높이 검사도 포함하며 결과는 `Logs/ReturnTown-PlayMode.xml`에 있다. 이번 표시 변경은 별도 Player 빌드를 반복하지 않았다.

초기 던전 검증 결과는 `Logs/Dungeon-EditMode.xml`, `Logs/Dungeon-PlayMode.xml`, `Logs/Dungeon-Build.log`, `Logs/DungeonPlayerQA-Player.log`, `Logs/DungeonPlayerQA/`에 있다. 동료 자동 행동 반영 후 최신 테스트 결과는 `Logs/Companion-EditMode.xml`, `Logs/Companion-PlayMode.xml`이다. 로그·검증 캡처는 Git에 포함하지 않는다. 일회성 에디터 생성 도구와 Player 검증 코드·빌드는 정리하고, 실제 씬/콘텐츠/그림 및 유효한 회귀 테스트는 유지한다.
