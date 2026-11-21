# 장비와 소모품 구조

기획은 [장비와 아이템 사용](../Planning/09_EquipmentAndItems.md), 데이터 정의는 [장비 카탈로그](EquipmentCatalog.md)를 따른다.

## 콘텐츠와 직업

`DungeonContent.Equipment`의 `EquipmentContent`가 무기·소모품·시작 장비·문구 CSV를 읽는다. 아이템/무기/인물 참조와 시작 직업 일치를 검증한다. `MainFlow`가 장비 시트를 Store와 마을·던전 View에 연결한다.

`CharacterProgress.EquippedItemId`가 영속 원천이고 `WeaponId`는 현재 장비 시트에서 파생한다. `SkillSheet.GetClassForCharacter(CharacterProgress)`는 이 무기에 해당하는 직업을 반환한다. 기존 문자열 ID 오버로드는 초기 배정/장비 시트가 없는 이전 API용이다. 학습 자격, 가방·인물·스킬 UI, 전투 기본기·액티브 필터·무기 조건부 패시브가 현재 직업을 조회한다. 습득 ID와 SP는 교체 시 유지한다.

`DungeonDataContainer.SynchronizeParty`는 새 능력치/기본기를 적용하며 탐험 중 HP·기력의 절대량과 스킬 쿨다운을 보존한다. 창의 트윈 스러스트 기본기 변경과 자연 지팡이의 힐링 터치도 현재 무기를 검사한다.

## 입력·사용·거래

`InventorySelectItem/InventoryEquipItem/InventoryUseItem` → `HeroMenuActionRequested` → `HeroMenuEventHandler` → `MainFlow.Equipment`로 전달한다. 메뉴 문맥·선택 아이템·소스 Revision을 확인한다. 선택/성공 후 문맥을 갱신하여 중복 입력을 버린다. `HeroMenuData`는 선택/표시 상태와 현재 HP·최대 HP·아이템 행동 가능 여부를 읽기 전용으로 제공한다.

`AdventureProgressStore.TryEquipItem/TryConsumeItem`은 후보 복사 → 원천 변경 → 저장 → 상태 교체를 수행한다. MainFlow는 현재 HP·전투 차례·모달/보상 상태를 먼저 검증하고 `Saved` 결과일 때만 `DungeonInventory.CompleteInventoryAction`으로 회복/행동 게이지 차감을 실행한다. `AlreadyApplied`는 회복이나 차례를 다시 소비하지 않는다. 저장 실패 시 임시 전투 상태에도 손대지 않는다.

장착품은 플레이어 총 수량에 포함하며 `GetAvailableItemCount`가 장착된 1개를 제외한다. 상점 수량 미리보기와 최종 `ShopTrade` 검사 모두 이 값을 사용한다. 가방은 소유 총량과 장착 표시를, 상점은 총량과 판매 가능 수량을 표시한다.

## 저장 이행

v6 DTO는 `EquipmentCharacterIds`, `EquippedItemIds`, `ItemActionIds`를 추가한다. 장착 원천이 없는 v1~5 저장에 시작 장비와 신규 무기 재고를 추가한다. 기존 포션 수량과 판매품은 보존하며 이미 초기화된 저장의 품절 재고는 보충하지 않는다. 이행 자체는 디스크를 덮어쓰지 않는다.

장비 시트가 없는 API 경로도 이미 저장된 아이템 ID·행동 기록·상점 재고를 보존한다. 이 상태에서 장착 무기의 의미를 알 수 없으면 잘못된 직업으로 학습/성장하지 않게 해당 작업을 거절한다. 탐험 HP는 이전과 같이 임시 상태이며 장비 변경으로 재설정하지 않는다.

장비 기능을 초기화하는 Store에는 인물·스킬·상점 시트를 모두 전달해야 한다. 상점 없는 부분 초기화가 신규 무기 재고 이행을 누락시키지 않도록 생성 시 검사한다.

## 검증

- Unity 6000.0.58f2 EditMode 전체 **346/346**, PlayMode 전체 **28/28** 통과. 장비 카탈로그 10사례, 진행 저장 25사례, 전투/입력 통합 8사례와 실제 씬 UI 3사례를 새로 추가했다.
- 무기 거래·여분 판매·장착품 판매 차단, 클래스/액티브 목록 변경과 이전 습득/SP 보존, 무기 조건부 패시브·기본기 재계산, HP/기력/쿨다운 보존, 포션 최대 HP 제한·자기 차례 한 행동·중복/오래된 요청·저장 실패 재시도, v1~5 이행과 손상 저장 보호를 검증했다.
- Windows Development Player 빌드와 별도 프로세스 재실행 성공. v4 격리 저장을 v6로 이행하고 시장에서 창을 구매한 뒤 검 여분 판매·창/활 교체·창 스킬 학습·실제 회색쥐 공격 후 포션 사용을 실행했다. 재실행 후 장착 창, 포션 2개, 검/활/창 각 1개, 금화 980, Main SP 12, 검/창 습득 ID, 상인 재고를 확인했다. 이 수치는 검증 전용 시드의 결과이며 새 게임 기본 지급값이 아니다.
- 1280×800과 1024×768 화면 16장을 캡처하고 장비/상점/스킬/인물/전투 화면의 겹침·잘림을 확인했다. Player 입력은 EventSystem 최상단 Raycast와 합성 포인터 클릭이며 물리 OS 마우스 검증을 뜻하지 않는다. 실제 사용자 저장은 사용하지 않았다.
- 결과는 `Logs/Equipment-Final-EditMode.xml`, `Logs/Equipment-Final-PlayMode.xml`, `Logs/Equipment-PlayerBuild.log`, `Logs/Equipment-Player.log`, `Logs/Equipment-PlayerReload.log`, `Logs/EquipmentPlayerQA/`에 남는다. 일회성 QA 소스와 테스트 Player 빌드는 검증 뒤 제거한다.
