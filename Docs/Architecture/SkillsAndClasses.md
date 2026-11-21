# 스킬과 개인 메뉴 구조

기획 기준은 [무기·직업·스킬](../Planning/08_SkillsAndClasses.md), DS 데이터/실행 근거는 [SkillsDS](SkillsDS.md)다.

이후 [장비와 아이템](EquipmentAndItems.md)에서 현재 직업·기본기·액티브 트리를 장착 무기로 결정하도록 연결했다. 아래 v5와 검증 기록은 스킬 최초 구현 시점이며 현재 진행 저장은 v6이다.

## 데이터와 영속 상태

`SkillContent`는 여섯 CSV를 `SkillSheet`로 읽는다. `DungeonContent.LoadSkills()`가 인물별 직업 배정도 검증한다. `SkillSheet.Skills`는 구매 가능한 목록이고 `BasicSkills`는 기본 공격/스킬이 바꾼 기본기다. 기본기는 구매 목록에 섞이지 않는다. 직업·기본기·선행 참조, 순환, 중복 ID, 숫자 범위를 로드 시 검증한다.

`CharacterProgress`는 잔여 Main/Sub SP, `LearnedSkillIds`, `BasicAttackKills`를 제공한다. 효과와 가격은 저장하지 않고 현재 시트에서 조회한다. `SkillLearningRules`는 구매 가능 조건을 검사하고 `AdventureProgressStore.TryLearnSkill`은 요청 ID·원본 Revision 검증 → 후보 복사 → 포인트 차감/ID 추가 → 파일 저장 → 교체 순서로 처리한다.

레벨업 SP와 NPC 자동 구매는 기존 `TryGrantExperience`의 후보 안에서 계산한다. 저장 실패 때 후보를 재사용하므로 성장과 스킬 무작위 선택을 다시 뽑지 않는다. 기본 공격 처치 누적도 같은 보상 후보에서 저장해 샤프 슈터의 영구 치명타 증가를 재구성한다. 경험치 보상이 발생한 시점에 이미 해당 패시브를 배운 처치자만 누적한다.

`AdventureSave` v5의 스킬 원천 배열은 인물 ID와 대응한다. 버전 1~4의 누적 SP는 메모리에서 이행하고 자동 저장·학습하지 않는다. SkillSheet 없는 기존 API 경로에서는 이미 저장된 스킬 원천을 보존한다. 저장 검증 실패는 기존처럼 쓰기를 막는다.

## 입력과 메뉴

`UIInputType` 500번대 → `HeroMenuActionRequested` → `HeroMenuEventHandler` → `MainFlow.Skills`로 실행한다. `HeroMenuData`는 메뉴 모드·선택 ID·GUID 문맥·소스 Revision만 소유한다. 팝업의 선택/습득 요청은 문맥을 검사하고, 성공 후 문맥을 교체해 같은 프레임의 중복·오래된 요청을 폐기한다.

`MainFlow`는 개인 메뉴·인물·상점·정산·의뢰의 동시 열기를 막는다. 메뉴가 열린 경우 Town/Dungeon Handler에서 배경 요청을 폐기하고 Esc만 닫기로 처리한다. 같은 상태를 View 잠금과 CombatTickFlow 실행 조건에 사용한다. View는 조회와 UIInput 기록만 수행한다.

## 전투

`DungeonSkills`는 배운 액티브/기본기의 선택·대상 검증, 사용, 기력/대기시간, 다중 타격과 보상 사실을 처리한다. 플레이어는 준비된 차례에만 직접 선택한다. 동료 자동 행동도 같은 실행 경로를 사용한다.

`CombatantSkills`는 탐험 중 기력·대기시간과 전투 중 상태이상·보호막을 소유한다. `CombatSkillPassives.CalculateStats`는 저장된 습득 원천으로 영구 능력치를 계산하고 `EffectiveStats`는 현재 조건과 임시 효과를 적용한다. 전투 시작, 스킬 사용 전후, 피해 전후, 처치, 효과 부여 사건에서 패시브를 평가한다.

`CombatTickFlow`는 기존 플레이어 대기 정책을 유지하며 기절 해제와 스킬 효과 갱신 경계를 넘기지 않게 시간을 나눈다. 메뉴·포커스 잠금·저장 실패 동안 시간은 흐르지 않는다. 상태 피해나 반격으로 적이 죽어도 `RecordSkillKill`에서 전투 내 적 ID별로 한 번만 전리품·EXP·의뢰 사실을 만든다. MainFlow는 후속 단계에서 이를 순서대로 커밋한다.

능력치 갱신은 현재 생명력·기력의 절대량을 보존한다. 레벨업, 학습, 파티 변경만으로 부상이나 소모 자원을 초기화하지 않는다. 개인 저장에는 탐험의 임시 HP·게이지·대기시간·상태이상을 저장하지 않는다.

## 검증

- EditMode 전체 303/303 통과: 기존 성장·저장·상점·정산·의뢰 회귀와 스킬 구매·SP 분리·NPC 후보 재사용·이전 버전 이행·누적 처치 원천, DS 피해/쿨다운·기력·기절·보호막·패시브 조합을 확인했다.
- PlayMode 전체 25/25 통과: 실제 마을·던전 씬에서 가방/스킬 창, 배경 차단·전투 시간 정지, 액티브/패시브 습득·저장 실패 재시도·손상 저장 보호, 단일/자기 대상 선택을 확인했다.
- Windows Development Player 빌드와 별도 프로세스 재실행을 검증했다. 테스트용 v4 저장을 v5로 읽고, 플레이어 스킬 선택·NPC의 실제 레벨업 자동 학습·동료 자동 사용·던전 전투·귀환 정산 후 인벤토리/금화/SP/습득 목록 복원을 확인했다.
- Player 입력은 EventSystem의 최상단 Raycast와 합성 포인터 클릭으로 실행했다. 물리 OS 마우스 테스트를 의미하지 않는다. 1280×800과 1024×640에서 화면을 캡처해 확인했으며 실제 사용자 저장은 사용하지 않았다.
- 결과는 `Logs/Skills-Final-EditMode.xml`, `Logs/Skills-Final-PlayMode.xml`, `Logs/Skills-Player*.log`, `Logs/SkillPlayerQA/`에 남는다. 임시 QA 코드와 빌드는 검증 후 제거한다.
