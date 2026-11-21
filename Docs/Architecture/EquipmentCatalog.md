# 장비·소모품 카탈로그

장비 아이템은 기존 무기 종류를 사용한다. 이 작업은 추가 공격력·방어력 보너스를 만들지 않는다. 무기를 장착하면 기존 `SkillWeaponTable`의 기본 공격과 그 무기 종류의 직업/액티브 트리를 사용한다.

## 원천과 참조

모든 CSV는 `Assets/1_InternalTools/Util/DataSheetLoader/Table/`에 있다.

| 시트 | 관리 항목 |
| --- | --- |
| LootItemTable.csv | 거래/소유 아이템의 ID·이름·정가 |
| EquipmentDefinitionTable.csv | ItemId → 기존 SkillWeapon의 WeaponId |
| ConsumableDefinitionTable.csv | ItemId·Effect·Amount |
| CharacterEquipmentTable.csv | 인물별 시작 장비 ItemId |
| EquipmentTextTable.csv | 장비·사용·선택·제한·오류 UI 문구 |
| ShopStockTable.csv | 상인별 초기 재고 |

`EquipmentContent`는 장비/소모품/시작 장비/문구 네 시트를 묶는다. `Load(LootSheet, SkillSheet, CharacterSheet)`는 아이템과 무기, 인물 참조를 확인하고 `EquipmentSheet`를 만든다. 이름과 가격은 공용 LootItem에서 가져와 거래와 가방에서 서로 다른 값을 사용하지 않는다.

아이템 ID와 무기 종류 ID는 서로 다르다. 예를 들어 `weapon_sword`는 거래 가능한 아이템이고 `Equipment_Sword`는 직업·기본기 조건의 무기 종류다. 다른 검 아이템을 같은 `Equipment_Sword`에 연결할 수 있다. 한 아이템을 장비와 소모품 양쪽에 등록하거나 중복 등록하는 것은 거절한다.

## 시작 장비와 기획 기본값

아래 가격·재고·회복량은 이번 P2 기능에 정한 조정 가능한 초기값이다. DS 원본 아이템 가격이나 장비 스탯을 그대로 가져왔다고 간주하지 않는다. 기존 포션 가격 20G와 초기 재고 10개는 유지한다.

| 아이템 ID | 이름 | 무기 종류 | 정가 | 주점/시장 초기 재고 |
| --- | --- | --- | ---: | ---: |
| weapon_sword | 검 | Equipment_Sword | 50G | 각각 3개 |
| weapon_greatsword | 대검 | Equipment_Greatsword | 80G | 각각 3개 |
| weapon_blunt | 둔기 | Equipment_Blunt | 60G | 각각 3개 |
| weapon_bow | 활 | Equipment_Bow | 70G | 각각 3개 |
| weapon_fire_staff | 화염 지팡이 | Equipment_FireStaff | 90G | 각각 3개 |
| weapon_water_staff | 물 지팡이 | Equipment_WaterStaff | 90G | 각각 3개 |
| weapon_nature_staff | 자연 지팡이 | Equipment_NatureStaff | 90G | 각각 3개 |
| weapon_shield | 방패 | Equipment_Shield | 60G | 각각 3개 |
| weapon_spear | 창 | Equipment_Spear | 70G | 각각 3개 |
| health_potion | 생명력 포션 | 소모품 | 20G | 각각 10개 |

초기 검은 가장 낮은 50G, 나머지는 60~90G 범위로 둔다. 이는 장비 수치의 강약을 뜻하지 않으며 거래·장착·직업 변경을 확인하기 위한 초기 가격이다. 양쪽 상점에서 같은 종류를 구입할 수 있게 하되 실제 저장된 재고는 상인별로 독립적이다.

시작 장비는 hero 검, mira 둔기, orin 창, bran 대검, sera 방패, neri 화염 지팡이, cass 활이다. 시작 직업과 일치해야 하며 모든 인물의 시작 장비를 정확히 한 번 정의한다. 플레이어의 검 1개는 초기 소유 아이템과 장착 상태로 만들고 NPC 시작 장비는 각 인물의 개인 장비로 관리한다. NPC들의 시작 장비를 플레이어 가방에 복제하지 않는다.

생명력 포션의 효과는 `RestoreHealth`, 기본 Amount는 100이다. 최대 HP를 넘는 부분은 회복하지 않으며 실제 사용/소유 수량·대상·저장 성공 여부는 Store와 MainFlow가 검사한다. 현재 HP를 영속화하는 새 저장 원천은 추가하지 않는다. 마을/새 탐험의 기존 완전 회복 정책을 유지하며 가득 찬 HP에서는 소비하지 않는다.

## 저장 이행과 책임

신규/이전 저장의 시작 장비 부여와 v6 저장 검증은 Store/AdventureSave가 담당한다. 카탈로그는 진행 파일을 쓰거나 장착 상태를 바꾸지 않는다.

v5 이전 상점 저장에 새 무기를 추가할 때는 정의된 무기 중 없는 항목만 초기 재고로 한 번 채우는 정책을 사용한다. 기존 포션·플레이어 판매품·금화·거래 기록은 보존한다. v6 이후에는 품절로 없는 무기를 시작 재고로 다시 채우지 않는다. 읽기만으로 기존 파일을 덮어쓰지 않는다.

`Weapons`, `Consumables`, `StartingWeapons`는 조회 전용이다. `GetWeapon`/`TryGetWeapon`, `GetConsumable`/`TryGetConsumable`, `GetStartingWeapon`/`GetStartingWeaponId`가 입력·저장·UI의 공통 조회 경계다. 소모품 효과량은 유한한 양수이고 1,000,000을 넘지 않아야 한다. 중복, 없는 아이템/무기/인물 참조, 시작 장비 누락, 시작 직업 불일치는 시트 로드 시 거절한다.

`EquipmentSheetTests`는 실제 아홉 종류/양상점 재고·포션 정의, 같은 무기 종류를 공유하는 다른 거래 아이템, 잘못된 참조·시작 장비·효과량·유형 충돌을 검증한다. 거래/장착/소비의 원자 저장과 실제 씬 입력 검증은 별도 통합 테스트에서 다룬다.
