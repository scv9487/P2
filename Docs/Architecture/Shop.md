# 상점 구조와 저장

기획 기준은 [상점 거래](../Planning/05_Shop.md)다.

[장비와 아이템](EquipmentAndItems.md) 추가 후에는 무기 거래와 장착 수량 보호, v6 이행 시 신규 무기 재고 추가를 적용한다. 확정 판매 가능량은 총 소유량에서 장착한 1개를 제외한 값이다.

인물·파티 추가 후 공용 진행 저장은 v3이며 [인물·파티 구조](CharactersAndParty.md)의 이행·복사 규칙을 함께 적용한다. 아래 v2와 검증 기록은 상점 최초 구현 시점을 설명한다.

## 실행 경로

```text
시장/주점 → 거래 → TownDataContainer.ShopOpen/ShopId
→ MainFlow가 ShopDataContainer 조립 → TownView/ShopView 표시

양쪽 인벤토리 선택·수량 조절·확정
→ UIInputStream → InputBinding → EventQueue → TownEventHandler
→ MainFlow의 상점 조율 → AdventureProgressStore.TryTrade
→ 저장 성공 → 금화/소지품/상인 재고 교체 → View 갱신
```

- TownDataContainer는 열린 상점과 장소 화면만 소유한다. ShopDataContainer는 거래 선택·수량·미리보기와 입력 문맥을 소유하며 저장은 수행하지 않는다.
- 선택·수량 변경과 거래 후 선택 초기화 때 문맥이 바뀐다. Handler와 MainFlow는 열린 상점·문맥·잠금 상태를 검사하고 Store는 거래 시점의 금화·수량·원본 상태를 다시 검사한다.
- View는 조회 결과를 양쪽 목록과 선택 내역에 표시하며 원본 데이터나 파일을 직접 변경하지 않는다. 별도 게임 Update나 중앙 큐 재등록을 추가하지 않는다.
- 성공한 거래만 저장 후 메모리에 반영한다. 거래 ID로 재적용을 막으며 성공 뒤에는 선택을 초기화하여 다른 물품을 고를 수 있다.
- 기존 Foundation 저장은 유지하고 정산과 거래는 같은 AdventureProgressStore 및 진행 파일을 사용한다. 마을과 던전 양쪽에서 상인 재고를 보존하므로 던전 정산이 상점 상태를 초기화하지 않는다.

## 데이터와 저장

- 아이템 ID·이름·정가는 기존 `LootItemTable.csv`에서 관리한다. 생명력 포션 ID는 `health_potion`이다.
- ShopContent/ShopSheet는 상인 ID·장소 연결·초기 재고·UI 문구를 정의하고 기존 DungeonContent에 연결한다. 플레이어가 판매한 아이템도 공통 아이템 정의를 사용한다.
- `ShopMerchantTable.csv`는 상인 ID·장소·이름, `ShopStockTable.csv`는 초기 아이템과 수량, `ShopTextTable.csv`는 상점 문구를 관리한다. 모두 기존 `Assets/1_InternalTools/Util/DataSheetLoader/Table/`에 있다.
- 저장 v2는 기존 파티원 금화·보유 아이템·정산 ID에 상인별 재고와 거래 ID를 추가한다. 수량이 0인 물품은 재고 목록에서 제거하지만 상인 ID는 남겨, 빈 상점도 초기 재고로 다시 채우지 않는다.
- v1 파일은 기존 데이터를 그대로 읽고 초기 상인 재고를 더한 메모리 상태로 이행한다. 읽기만으로 원본을 덮어쓰지 않으며 다음 거래 또는 정산을 성공적으로 저장할 때 v2를 기록한다.
- 손상·지원하지 않는 버전·읽기 실패는 원본을 보존하고 변경 저장을 차단한다. 쓰기 실패는 금화·아이템·재고를 부분 반영하지 않는다.
- UI 및 Player 검증에는 `MainFlow.AdventureSavePathOverride`로 고유 임시 파일을 주입하여 실제 사용자 저장과 분리한다.

## 검증

2026-11-21 확인:

- Unity 6000.0.58f2 EditMode **141/141 통과**. 상점 26개, 마을 13개, 기존 기반 37개·던전 24개·전리품 41개를 포함한다. 매매 계산, 판매품 재구매, 품절 후 재입고 방지, 파티원 금화 보존, 기존 v1 이행, v2 필수 배열과 참조 검증, 오래된 거래·정산 거절, 저장 실패 후 원자성·재시도를 확인했다.
- PlayMode **15/15 통과**. 상점 3개는 실제 씬 UI에서 매매·씬 재진입·독립 재고·금화 부족·배경 입력 차단·이전 입력 문맥·동일 프레임 중복 확인·파일 쓰기 실패와 재시도를 확인한다. 기존 기반·마을·던전 12개도 통과했다.
- Windows x64 Development Player 빌드·실행 통과. 격리된 v1 저장(플레이어 60G, 미라 7G, 쥐꼬리 3개)을 읽고, 쥐꼬리 2개 판매 → 포션 2개 구매 → 쥐꼬리 1개 되사기를 수행했다. 결과는 플레이어 30G·쥐꼬리 2개·포션 2개, 시장 재고 쥐꼬리 1개·포션 8개였다. 미라 7G, 주점 포션 10개와 기존 정산 기록은 유지됐다.
- 같은 검증 저장으로 Player를 새 프로세스에서 재실행하여 위 결과를 복원했다. 1280×800과 1024×768의 좌우 인벤토리·수량·가격·부족 금화 안내, 마을 소지품과 재실행 후 시장 화면을 확인했다.
- Player 입력은 EventSystem 최상단 raycast 대상에 합성 포인터 이벤트를 전달했다. 검증 코드에서만 포커스를 부여했으며 OS의 물리 마우스 입력 검증과 구분한다. 실제 사용자 저장 파일은 사용하지 않았다.

결과는 `Logs/Shop-EditMode.xml`, `Logs/Shop-PlayMode.xml`, `Logs/Shop-Build.log`, `Logs/ShopPlayerQA-Player.log`, `Logs/ShopPlayerQA-Reload.log`, `Logs/ShopPlayerQA/`에 남긴다. 로그·캡처·격리 저장은 Git에 포함하지 않으며 임시 검증 코드와 빌드는 제거한다.
