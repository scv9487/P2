# P2

모험가의 인생을 살아가는 던전 RPG입니다. 다양한 모험가와 상호작용하며 관계를 쌓고, 그 관계에서 생기는 경험에 집중합니다. 작업 전 [핵심 기획 방향](Docs/Planning/00_GameDirection.md)을 읽어 주세요.

DS/IZ의 입력·요청 처리·데이터·UI·저장 기반을 사용하는 Unity 프로젝트입니다. 타이틀, 마을의 다섯 장소, 던전 선택, 그래프 탐험과 행동 게이지 전투를 제공합니다.

Unity **6000.0.58f2**로 열고 [1_Title.unity](Assets/0_Project/1_Title/0_Scenes/1_Title.unity)를 열어 Play하세요. [2_Town.unity](Assets/0_Project/2_Town/0_Scenes/2_Town.unity)도 직접 Play할 수 있습니다.

- 타이틀에서 `마을로` → 장소 선택 → 좌측 커맨드 또는 상단 인물 초상화 선택
- Esc: 세부 팝업 닫기 → 장소 나가기
- 장소의 공통 `마을로 돌아가기`, 마을 전경의 `타이틀로` 버튼으로 이동

[타이틀·마을 기획](Docs/Planning/02_TitleAndTown.md) · [구조 및 시트 수정 안내](Docs/Architecture/TitleAndTown.md)

던전은 마을에서 `던전 → 입장 → 카드 선택 → 입장 확정`으로 들어갑니다. [3_Dungeon.unity](Assets/0_Project/3_Dungeon/0_Scenes/3_Dungeon.unity)를 직접 Play하면 첫 번째 던전을 시작합니다.

- 현재 위치와 선으로 연결된 노드만 이동할 수 있습니다. 방문한 노드도 다시 갈 수 있습니다.
- 지도 우측 상단의 `귀환`으로 전리품 정산창을 열고, 정산을 확정하면 마을로 돌아갑니다.
- 계단 노드의 `다음 층`으로 진행하고 마지막 층에서 보스 회색쥐와 싸웁니다.
- 내 게이지가 차면 기본 공격 또는 배운 액티브를 고르고 대상 카드를 선택하세요. 자신/전체 대상 스킬은 사용 버튼으로 확정합니다. 동료는 배운 스킬과 기본 공격을 자동으로 사용합니다.
- 전투 밖에서 휴식·인물 대화·귀환을 이용합니다. 전투·휴식·물음표 이벤트는 한 탐험에서 노드별 한 번만 발생합니다.

[던전 기획](Docs/Planning/03_Dungeon.md) · [구조 및 검증](Docs/Architecture/Dungeon.md)

회색쥐를 처치할 때마다 쥐 꼬리 1개를 얻습니다. 탐험 종료 시 기본적으로 모두 판매하고 참가자의 합의 비율대로 금화를 나눕니다. 정산창에서 원하는 수량을 정가로 구매하면 소지품으로 보관합니다. 현재 쥐 꼬리는 10금화이며 각 참가자의 기본 분배 가중치는 1입니다(2명은 50:50). 가격은 Loot CSV, 인물별 가중치는 CharacterDefinitionTable에서 조정합니다.

[전리품 기획](Docs/Planning/04_LootAndSettlement.md) · [정산 및 저장 구조](Docs/Architecture/LootAndSettlement.md)

시장·주점의 `거래`에서 좌측 내 아이템을 선택해 판매하거나 우측 상인 아이템을 선택해 구매할 수 있습니다. 생명력 포션의 기본 정가는 20금화이며 각 상인이 10개씩 보유합니다. 수량을 정해 거래를 확정하면 금화·아이템·상인 재고가 함께 저장됩니다. 가격과 초기 재고는 시트에서 조정할 수 있습니다.

[상점 기획](Docs/Planning/05_Shop.md) · [거래 및 저장 구조](Docs/Architecture/Shop.md)

주점에는 미라·오린·브란·세라·네리·카스가 있습니다. 인물 카드를 눌러 대화하면 친밀도가 기본 10씩 오르며, 친밀도와 같은 확률로 영입합니다. 플레이어 포함 최대 4명이고, 영입된 인물은 장소 방문자 목록에서 사라집니다. 마을·던전 하단 파티 카드에서는 대화와 추방을 할 수 있습니다.

인물 상세에는 이름·성별·나이·레벨·경험치·친밀도와 DS 기반 전투 수치를 표시합니다. 처치 경험치가 차면 DS의 보통 재능 범위로 6종 주요 능력치가 무작위 상승합니다. 확정한 관계·파티·성장도 자동 저장합니다. [인물·파티 기획](Docs/Planning/06_CharactersAndParty.md) · [구조](Docs/Architecture/CharactersAndParty.md) · [DS 수식과 근거](Docs/Architecture/CharactersCombatDS.md)

길드의 `의뢰 수주`에서 네 가지 의뢰 중 하나를 맡을 수 있습니다. 던전 1 클리어, 쥐 꼬리 3개 납부, 모험가 구조, 회색쥐 3마리 처치를 제공하며 달성 후 `의뢰 완료`에서 보고하고 금화를 받습니다. 구조 의뢰는 대상 던전 입장 시 전용 전투 노드가 생깁니다. 납부할 쥐 꼬리는 정산에서 구매해 소지품으로 보관하세요. 진행과 완료 기록은 저장됩니다. [의뢰 기획](Docs/Planning/07_Quests.md) · [구조](Docs/Architecture/Quests.md)

소문·여관 휴식은 안내 팝업까지 연결되어 있습니다. 던전의 전투·휴식·이벤트 회복은 실제 체력에 반영됩니다. 마을은 Town CSV, 던전 강도·탐험은 Dungeon CSV, 인물·성장·전투 원형은 Character/Combat CSV, 의뢰 조건·보상·구조 노드는 Quest CSV에서 수정할 수 있습니다.

마을·던전 좌측 하단 자신의 초상화 옆 `가방`으로 개인 소지품을 확인하고, `스킬`에서 전용 액티브와 공용 패시브를 습득합니다. DS와 같은 초기 Main/Sub SP 10/6, 레벨업마다 각각 6을 얻습니다. 플레이어는 직접 선택하며 NPC는 레벨업 때 배울 수 있는 높은 요구 레벨 후보부터 무작위 습득합니다. 직업·배정·스킬 조건·효과·문구는 Skill CSV에서 조정합니다. [기획](Docs/Planning/08_SkillsAndClasses.md) · [구조](Docs/Architecture/SkillsAndClasses.md) · [DS 대응과 제외 근거](Docs/Architecture/SkillsDS.md)

기반 기능 예제는 별도 [2_Test.unity](Assets/0_Project/2_Test/0_Scenes/2_Test.unity)에 있습니다.

가방에서 무기를 선택해 `장착 · 교체`하면 직업·기본 공격·사용 가능한 액티브가 바뀝니다. 배운 스킬과 SP는 유지됩니다. 시장·주점에서 9종 무기를 거래할 수 있으며 장착 중인 1개는 판매되지 않습니다. 생명력 포션은 가방에서 자신에게 사용해 기본 100 HP를 회복합니다. 전투 중 장착·포션 사용은 자신의 차례 한 행동을 소비하고, HP가 가득 차면 포션은 소모하지 않습니다. [장비 기획](Docs/Planning/09_EquipmentAndItems.md) · [구조·저장·검증](Docs/Architecture/EquipmentAndItems.md)

- F1 / F2: 숫자 증가 / 감소
- 이름 입력 → 이름 적용: 문자열 데이터 변경
- F5 / F9: 저장 / 불러오기
- Esc: 팝업 열기 / 닫기
- 현재 데이터 초기화: 메모리만 초기화하고 저장 파일 유지

저장 파일 위치는 화면 하단에 표시됩니다. 문자 편집 중에는 단축키가 차단됩니다.

[기획 및 기본값](Docs/Planning/01_Foundation.md) · [구조와 코드 읽기](Docs/Architecture/Foundation.md)

회귀 테스트는 Unity Test Runner의 EditMode / PlayMode에서 실행할 수 있습니다. 저장 테스트는 별도 임시 폴더를 사용합니다.
