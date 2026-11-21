# 전리품 정산과 보관

기획 기준은 [전리품과 탐험 정산](../Planning/04_LootAndSettlement.md)이다.

인물·파티 추가 후 실제 정산은 [탐험 참가자 가중치](CharactersAndParty.md)를 받고 공용 저장 v3에 기록한다. 기존 고정 두 명 분배 경로는 이전 기반 회귀에 유지한다.

## 실행과 책임

- DungeonDataContainer의 공통 아군 공격 경로에서 적이 쓰러지면 LootSheet의 `gray_rat` 드롭을 탐험 전리품에 추가한다. 전리품은 조회 전용 사전으로 노출한다.
- 귀환 요청은 DungeonEventHandler에서 정산 상태를 열고, MainFlow가 전리품·합의 비율·현재 소지 금화를 복사하여 LootSettlement를 만든다. 정산 중에는 노드 이동·휴식·대화와 뒤쪽 팝업 조작을 막는다.
- 구매 수량 변경·확정·취소는 기존 UIInputStream → InputBinding → EventQueue → DungeonEventHandler 경로를 사용한다. 정산마다 새 문맥 ID를 주어 이전 정산창의 지연된 입력을 거절한다.
- 금액 계산과 구매 선택은 LootSettlement가 소유한다. UI는 조회 결과를 표시하고 요청만 기록한다. 별도 게임 Update나 중앙 큐 재등록은 추가하지 않는다.
- 확정 시 AdventureProgressStore가 새로운 금화·소지품 상태를 검증하여 저장한다. 저장 성공 뒤에만 메모리 상태와 정산 확정 상태를 바꾸고 마을 씬을 불러온다. 씬 전환 실패 후 재확인은 전환만 재시도한다.
- MainFlow는 마을/던전 초기화 때 진행 저장을 읽는다. 전역 오브젝트를 유지하지 않으며 타이틀과 Foundation 테스트 씬은 이 저장을 수정하지 않는다.
- DungeonView의 정산창과 TownView의 보유 내역은 기존 Canvas·폰트를 사용한다. MainFlow의 View 단계에서만 갱신한다.

## 기획 시트

경로: `Assets/1_InternalTools/Util/DataSheetLoader/Table/`.

| 파일 | 항목 |
| --- | --- |
| LootItemTable.csv | 아이템 ID·이름·정가 |
| LootDropTable.csv | 적 종류 ID·드롭 아이템 ID·수량 |
| LootShareTable.csv | 파티원 ID·합의된 분배 비율(합계 100%) |
| LootSettingTable.csv | 처음 생성할 진행 데이터의 시작 금화 |
| LootTextTable.csv | 정산·보유 내역·저장 오류 안내 문구 |

LootContent는 시트를 묶으며 기존 DungeonContent에 연결한다. 이름과 플레이어 역할은 DungeonSheet.Party를 참조한다. 분배 시트는 모든 파티원을 정확히 한 번 포함해야 하며, 변경한 역할·이름을 별도 중복 데이터로 유지하지 않는다. 시트 변경은 다음 씬 초기화 때 반영되며 이미 열린 정산의 가격이나 비율을 소급 변경하지 않는다.

## 저장과 실패

- 기본 경로는 `Application.persistentDataPath/P2Adventure/progress.json`이다. Foundation의 기존 저장과 별도 파일·형식·버전을 사용한다.
- 파티원별 금화, 플레이어 보유 아이템 수량, 이미 확정한 정산 ID를 저장한다. 미정산 탐험 상태와 UI 선택은 저장하지 않는다.
- 상점 추가 후 저장 형식은 v2이며 상인별 재고·거래 ID도 함께 보존한다. 기존 v1 이행과 거래 저장은 [상점 구조](Shop.md)를 따른다. 아래 검증 기록은 전리품 기능 최초 구현 시점의 결과다.
- SaveLoadHelper의 임시 쓰기 → JSON 검사 → 교체/복원 경로를 재사용한다. 저장 성공 전에 현재 상태를 변경하지 않는다.
- 손상·지원하지 않는 버전·읽기 실패는 원본을 보존하고 정산 저장을 차단한다. 일반 저장 실패는 정산 선택을 유지하여 다시 시도할 수 있게 한다.
- `MainFlow.AdventureSavePathOverride`는 테스트와 검증 Player의 임시 경로 주입 지점이다. 자동 검증은 실제 사용자 진행 파일에 쓰지 않는다.

## 검증

2026-11-21, Unity 6000.0.58f2에서 확인했다.

- EditMode **113/113 통과**: 기존 기반·마을 48개, 던전 24개, 전리품·저장 41개. 플레이어/동료/보스 처치 보상, 패배 전 획득 보존, 재방문·중복 처치 방지, 정산 입력 문맥과 취소 복원을 확인했다. 분배 총합·잔여 금화, 구매 예산·수량, 저장 성공 전 상태 보존, 중복 확정, 오래된 정산 거절, 금액·수량 범위와 저장 필드 누락/null/형식/버전 오류도 검증했다.
- PlayMode **12/12 통과**: 기존 기반·마을 7개와 던전 5개. 실제 UI raycast로 정산창의 수량 변경·취소·자금 부족·모두 판매·기존 금화를 사용한 구매·같은 프레임 이중 확정·마을 표시·새 씬에서 불러오기를 확인했다. 실제 파일 쓰기 실패 후 씬과 금화를 보존하고 재시도하는 경로, 모달 뒤 입력 차단, 문구 높이 검사와 기존 3층 진행도 통과했다.
- 저장 필드 검증에서 Unity의 null 배열 변환을 확인하고 원시 JSON의 필수 배열 존재/타입/중복 검사로 보완했다. 씬 파괴 순서에 따라 이미 파괴된 정산 View를 재해제하던 문제를 수정한 뒤 전체 테스트를 다시 통과했다.
- Windows x64 Development Player 빌드와 실행도 통과했다. 회색쥐 2마리 처치 → 쥐꼬리 2개 → 기본 판매 평가 20금화·각 10금화 → 2개 구매 시 10금화 부족으로 확정 차단 → 1개 구매 → 모험가 0금화/쥐꼬리 1개·미라 10금화로 저장 및 마을 표시를 확인했다.
- Player를 종료하고 새 프로세스로 재실행하여 같은 검증 저장에서 위 금화·소지품을 복원했다. 1280×800에서 기본 정산·자금 부족·1개 구매·마을·재실행 후 마을 화면 5장을 확인했다. 입력은 EventSystem의 실제 최상단 raycast 대상에 합성 포인터 이벤트를 전달했으며, 검증 전용 포커스와 4배속을 사용했다. OS의 실제 마우스 입력 검증과 구분한다.

최종 테스트 결과는 `Logs/Loot-Final-EditMode.xml`, `Logs/Loot-Final-PlayMode.xml`이다. Player 결과는 `Logs/Loot-Build.log`, `Logs/LootPlayerQA-Player.log`, `Logs/LootPlayerQA-Reload.log`, `Logs/LootPlayerQA/`의 캡처·결과·격리된 저장 파일에 있다. 검증 파일은 Git에 포함하지 않으며 임시 실행 코드와 검증 빌드는 제거한다.
