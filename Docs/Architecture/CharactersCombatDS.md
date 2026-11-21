# 인물 전투·성장 계산과 DS 근거

기준일: 2026-11-21. 현재 P2 코드와 DS 원본을 직접 대조한 문서다. 인물·파티 기능의 사용자 요구와 범위는 [인물·파티 기획](../Planning/06_CharactersAndParty.md)을 따른다.

## 데이터와 계산의 소유권

- `Assets/0_Project/99_Shares/1_Scripts/Character/CharacterGrowth.cs`: 불변 `MajorStats`, 불변 `CharacterGrowthState`, 성장 계산 및 난수 인터페이스.
- 같은 디렉터리의 `CombatStats.cs`: 불변 최종 전투 수치, 플레이어/적 수치 계산, 명중·방어·치명타·피해·흡혈과 공격 주기 계산.
- `Assets/0_Project/999_Refactoring/1_Scripts/SheetData/TableData/CharacterGrowthSheet.cs`, `CombatStatSheet.cs`: 아래 CSV의 형식·참조·필수 행 검증.
- 실제 진행 상태와 저장은 `AdventureProgressStore`가 소유한다. 계산기는 새 후보를 반환하며 저장·씬 이동·UI 변경을 수행하지 않는다. 난수로 결정한 여섯 능력치의 누적 성장 증가분을 기본값과 분리하여 저장하고, 로드 시 현재 시트 기본값에 다시 더한다. 이전 성장 결과를 다시 추첨하지 않는다.
- 전투 결과의 적용은 기존 EventQueue/CombatTickFlow 단계, 화면 표시는 View 단계다. 새 독립 Update나 중앙 큐 재등록 경로를 만들지 않는다.

## 사용 시트

모두 `Assets/1_InternalTools/Util/DataSheetLoader/Table/`에 있다.

| 시트 | 내용 |
| --- | --- |
| `CharacterGrowthTable.csv` | 여섯 능력치별 보통 재능의 최소·최대 성장량 |
| `CharacterLevelTable.csv` | 레벨별 현재 경험치 상한 |
| `CharacterGrowthSettingTable.csv` | 최대 레벨, 제물 없이 적용하는 성장 배율 |
| `CombatStatTable.csv` | 전투 능력치 이름, 플레이어/비플레이어 기본값, 여섯 주요 능력치 계수 |
| `CombatEnemyTable.csv` | DS 회색쥐의 원형 수치와 사망 경험치 |
| `CombatAttackTable.csv` | 플레이어 맨손·회색쥐 기본 공격의 피해 계수와 기본 주기 |

성별·나이·기초 주요 능력치는 별도 `CharacterDefinitionTable.csv`의 원천 데이터다. NPC 이름·대사·초상화는 `TownCharacterTable.csv`를 참조한다. 전투 계산은 성별·나이에서 숨은 보정을 만들지 않는다.

## DS 원본과 실제 호출 경로

아래 경로는 `D:/Dev/DS/` 기준이다. DS 파일은 변경하지 않았다.

| 역할 | 원본 파일과 확인한 경로 |
| --- | --- |
| 기본값·계수 | `Assets/1_InternalTools/Util/DataSheetLoader/Table/StatusValueTable.asset` → `Campaign/Component/Status/StatUtility.cs`의 `Init_DefaultStats`, `GetDefaultStat_PlayerUnit`, `Init_MajorStat`, `GetPlayerMajorStatusValue` |
| 최종 능력치 | `Assets/0_Project/2_Campaign/1_Scripts/Campaign/Component/Status/Statuses/Statuses_Combat.cs`, `Statuses_Health.cs` → `StatusReader.GetValue` |
| 전투 결과와 피해 | `Assets/0_Project/999_Refactoring/1_Scripts/Combat/Execution/Simulator/DamageSimulator.cs`: `CalculateResult` → `CalculateValue` → `CalculateApply` |
| 흡혈 | `Statuses_Combat.cs`의 `Status_LifeSteal` 패시브가 `OnDamage_AfterProcess`를 받고 `DamageExecutionLog.TotalDamage`를 읽어 회복 요청 생성 |
| 공격 주기 | `Combat/Execution/Data/DelayExecutionData.cs`와 `Combat/Skill/ActiveSkills/ActiveSkill_Base.cs`의 `SkillCooldownCalculator`; `SkillRoutine` 전후 Delay는 `AttackSpeed`로 나눔 |
| 플레이어 기본기 | `Combat/Skill/ActiveSkills/PlayerSkills/ActiveSkills_PlayerETC.cs`의 `SKILL_BasicPunchAttack`, `SkillTable.asset`의 동명 행 |
| 회색쥐 | `UnitTable.asset`의 `UNIT_GreyRat` 및 `SkillTable.asset`의 `SKILL_BasicMonsterBlunt` |
| 성장 난수·반영 | `Assets/0_Project/2_Campaign/1_Scripts/MainFlow/CampaignData/Helper/LevelUpHelper.cs`: `PrayLevelUp` → `GetOfferedMajorStat` → `ApplyLevelUp` |
| 경험치 상한 | `Statuses/Statuses_Unit.cs`의 `Status_Exp`, `Status_MaxExp`, `Status_Level`; `StateStatus.cs`의 Add/Set 후 clamp |
| 경험치 획득 | `MainFlow/Event/EventHandlers/EntityEventHandler.cs`: `OnEvent(DeadUnitRequested)` → `GainExp` → TickFlow `StatusApplyData` → `StatusApplier` |
| 초기 생성 원천 | `MainFlow/CampaignData/Helper/SpawnHelper/UnitSpawner.cs`의 `SetUnitStatus` → `Campaign/Component/Status/StatusComponent.cs`의 `SetGeneratedValue`: 초기 주요 능력치에서 시트 기본값을 뺀 차이를 기록 |
| 성장 원천 | `LevelUpHelper.ApplyLevelUp` → `StatusComponent.AddProgressionValue`: 주요 능력치 성장 증가분을 `ProgressionStats`에 누적 |
| 저장·복원 순서 | `StatusComponent.Serialize`, `Deserialize`, `ApplyStructuredUnitStatus`: 최신 기본값 → `GeneratedStats` → `ProgressionStats` → `PermanentStats` → 외부 원천 재적용 |
| 저장 정책 검증 | `Docs/UnitStatusSaveRebuildStrategy.md`, `Docs/UnitStatusMutationAudit.md`, `Docs/UnitStatusSaveAuditChecklist.md`: 기본값 변경 반영과 원천별 합산 검증 |

`Campaign/`와 `MainFlow/`로 시작하는 축약 경로는 `Assets/0_Project/2_Campaign/1_Scripts/` 아래이며, `Combat/`로 시작하는 경로는 `Assets/0_Project/999_Refactoring/1_Scripts/` 아래다.

## 주요 능력치와 최종 수치

DS의 여섯 기본값은 힘·맷집·의지·지능·민첩·지각 모두 10이다. 코드 이름과 순서는 `Strength`, `Constitution`, `WillPower`, `Intelligence`, `Agility`, `Perception`이다.

주요 능력치 기여가 있는 최종 수치는 기본값에 계수 합계를 더한다. DS `StatUtility`는 양수인 계수를 등록하고 각 최종 수치 클래스는 기여 합계가 양수일 때 더한다. 지원되는 비율 보정은 고정 보정 이후에 한 번 적용한다.

```text
최종값 = (시트 기본값 + 주요 능력치 기여 + 고정 보정) × (1 + 비율 보정 / 100)
```

| P2 속성 | DS 기본값 + 주요 능력치 기여 | 주요 능력치 모두 10 |
| --- | --- | --- |
| MaxHealth | 205 + 맷집 × 9 | 295 |
| PhysicalAttackPower | 13.5 + 힘 × 1 + 민첩 × 0.5 + 지각 × 0.5 | 33.5 |
| MagicAttackPower | 13.5 + 의지 × 0.4 + 지능 × 1.2 + 지각 × 0.4 | 33.5 |
| AttackSpeed | 0.4 + 민첩 × 0.01 | 0.5 |
| CriticalChance | 8 + 지각 × 0.3 | 11 |
| CriticalDamageBonus | 50 | 50 |
| CooldownReduction | 의지 × 0.3 | 3 |
| HitChance | 100 | 100 |
| DodgeChance | 10 | 10 |
| GuardChance | 0 | 0 |
| GuardDamageReduction | 50 | 50 |
| PhysicalResistance | 맷집 × 0.3 | 3 |
| MagicResistance | 지능 × 0.3 | 3 |
| MovementSpeed | 80 + 민첩 × 0.2 | 82 |
| LifeSteal | 0 | 0 |
| DamageDealtBonus / DamageTakenBonus | 각각 0 | 각각 0 |
| PhysicalDamageDealtBonus / MagicDamageDealtBonus | 각각 0 | 각각 0 |
| FinalDamageMultiplier | 1 | 1 |
| ExpGainedBonus / ExperienceGainBonus | 같은 값의 별칭, 기본 0 | 0 |

비율 보정은 MaxHealth, 물리·마법 공격력, AttackSpeed, CooldownReduction, 물리·마법 저항, MovementSpeed, DamageTakenBonus에 적용한다. 치명타 확률 등 나머지 수치에 임의의 비율 보정을 추가하지 않는다. 회피율·방어 확률·방어 피해 감소의 상한은 각각 80이다. 이동 속도의 비율 감소만 -90% 아래로 내려가지 않는다.

저항과 재사용 대기시간 감소는 직접 퍼센트가 아닌 지수다. DS의 표시 방식처럼 `원본값 (유효 감소율)`을 표시하며 유효 감소율은 `(1 - 1 / (1 + 값 / 100)) × 100`이다. 확률·피해 증가·흡혈은 10이 10%인 값이고 최종 피해 배율은 1이 1배인 값이다.

## 명중·방어·치명타와 피해

DS와 같은 `UnityEngine.Random.Range(0f, 100f)` 기반의 닫힌 실수 범위를 사용한다. 비교는 모두 `<`이며 경계값과 같으면 실패한다.

1. 명중: 난수 < 공격자 HitChance − 방어자 DodgeChance. 실패하면 Miss이며 이후 난수를 소비하지 않는다.
2. 물리 피해만 방어: 난수 < GuardChance. 성공하면 Guard이며 치명타 난수를 소비하지 않는다.
3. 치명타: 난수 < CriticalChance. 마법 피해도 명중·치명타를 판정하지만 방어를 건너뛴다.

확정 명중은 첫 난수만 생략한다. 확정 치명타도 물리 방어를 무시하지 않는다. DS에서 마법은 별도 원소 저항들을 나누지 않고 MagicResistance를 사용한다.

```text
결과 배율: Miss=0, Normal=1,
           Guard=1−GuardDamageReduction/100,
           Critical=1+CriticalDamageBonus/100
저항 배율 = 100 / (100 + 해당 피해 종류의 Resistance)
가산 피해 보정 = (DamageDealtBonus + 해당 물리/마법 DamageDealtBonus
                  + max(-99, 방어자 DamageTakenBonus)) / 100
피해 = max(0, 공격력 × 기본기 계수 × (1+가산 피해 보정)
              × 결과 배율 × 저항 배율 × FinalDamageMultiplier)
흡혈 = 피해 × max(0, LifeSteal) / 100
```

흡혈 기준은 적의 실제 잃은 HP로 제한한 값이 아니다. DS `DamageExecutionLog.TotalDamage`의 주석과 실제 소비자는 오버킬을 포함한 계산 피해를 사용한다. 현재 생명력 적용 단계에서 공격자 최대HP를 넘지 않도록 제한한다. P2 피해 수치는 float로 유지하고 UI 표시만 반올림한다.

## 기본 공격 주기와 회색쥐

| 정의 | 피해 계수 | 시작+종료 기본 딜레이 | 실제 주기 |
| --- | --- | --- | --- |
| player / DS BasicPunchAttack | 물리 공격력 × 0.3 | 0.4 + 0.2 = 0.6초 | 0.6 / AttackSpeed |
| grey_rat / DS BasicMonsterBlunt | 물리 공격력 × 1 | 0.8 + 0.2 = 1초 | 1 / AttackSpeed |

DS `DelayExecutionData`가 시작·종료 딜레이를 각각 공속으로 나눈다. P2 카드 전투는 별도의 시작/종료 애니메이션을 두지 않고 같은 총 주기를 ATB 게이지에 적용한다. 초당 게이지 증가량은 `AttackSpeed / BaseCycle`이다. 기본 능력치의 플레이어 주기는 1.2초, 원형 회색쥐는 약 3.333초다.

재사용 대기시간이 공속의 영향을 받는 스킬은 `base / AttackSpeed`, 그렇지 않은 스킬은 `base / (1 + CooldownReduction/100)`이다. DS와 같이 비양수 조정률은 기본 대기시간으로 돌아간다. P2에는 현재 기본 공격 한 개만 연결한다.

회색쥐의 원본 값은 HP 500, 물리 공격력 30, 마법 공격력 20, 공격속도 0.3, 치명타 확률 5, 물리/마법 저항 0, 이동속도 130, 사망 경험치 8이다. 명중 100, 회피 10, 치명타 추가 피해 50, 방어 확률 0, 방어 피해 감소 50은 DS 비플레이어 기본값을 사용한다. 던전 번호·층·보스의 기존 난이도 차이는 P2의 기존 기준 비율을 `WithMultipliers(health, attack, speed)`로 원형에 곱한다. 다른 전투 수치를 함께 증폭하지 않는다.

## 경험치와 성장

DS `Status_MaxExp`는 아래 배열을 `현재 Level - 1`로 읽는다. 시트 주석의 근사 다항식을 다시 계산하지 않는다.

| 현재 레벨 | 필요 경험치 | 현재 레벨 | 필요 경험치 |
| --- | --- | --- | --- |
| 1 | 30 | 7 | 3167 |
| 2 | 250 | 8 | 4153 |
| 3 | 567 | 9 | 5273 |
| 4 | 1017 | 10 | 6531 |
| 5 | 1600 | 11 | 7926 |
| 6 | 2315 | 12 | 9457 |

- 획득 경험치에 `1 + ExpGainedBonus/100`을 곱하고 현재 레벨 상한에서 제한한다. 초과 경험치를 이월하지 않는다.
- 사용자 요구에 따라 P2는 최대 경험치에 도달하는 즉시 한 번 레벨업한다. DS의 수동 기도·재물·각인 선택 절차는 가져오지 않는다.
- DS `TalentType.Moderate`의 여섯 성장 범위는 모두 정수 2~4다. 힘→맷집→의지→지능→민첩→지각의 고정 순서로 독립 `Random.Range(2, 5)`를 호출한다.
- 보통 **재능**과 보통 **제물**은 별개다. DS의 제물 배율은 Least 1, Ordinary 1.2, Plentiful 1.5다. 제물을 사용하지 않는 P2의 초기 배율은 1이며 시트에서 조정한다. 최종 상승치는 DS처럼 `Mathf.RoundToInt(정수 난수 × 배율)`이다. 절반의 값은 가장 가까운 짝수로 반올림한다.
- 레벨업은 해당 성장량을 여섯 누적 성장 증가분에 더하고 Level을 1 올린 다음 Experience를 0으로 만든다. 현재 주요 능력치는 시트 기본값과 누적 성장 증가분의 합이다. 최대 레벨 12에서는 성장을 추첨하지 않고 경험치만 그 레벨 상한까지 유지한다.
- 최대HP 증가 때 현재HP는 절대값을 유지한다. DS `ApplyLevelUp`에는 HP 회복 호출이 없고 `StateStatus`는 해당 HP 자체를 Add/Set할 때만 제한하기 때문이다. 자동 완전회복·비율 보존·잃은HP 보존을 추가하지 않는다.

DS `BuildMaxLevel=5`는 현재 원본에서 모집 후보 레벨 제한과 성장피로 효과의 분기에 사용된다. 전체 호출자 검색에서 레벨업 자체를 5로 제한하는 근거는 확인되지 않았다. P2는 `Status_Level.MaxValue`에 해당하는 시스템 최대 레벨 12를 유지하고 모집/성장피로 보조 설정은 이식하지 않는다.

DS 처치 경험치는 죽은 적의 `OnDeathExp`를 전투 영역의 적대 관계 Player 유닛 수로 나눈다. `GainExp` → `GetEntitiesOnArea` → 타일 점유목록 → `UnitDataContainer.GetEntities`에는 생존 필터가 없다. 사망 처리는 alive/dead 집합만 바꾸고 타일의 유닛 목록에서 제거하지 않으므로 영역에 남은 사망자도 인원과 지급 대상에 포함된다. P2는 처치 시점의 활성 파티 전원(HP 0 포함)을 기준으로 나누며, 공간 탐색 대신 현재 파티라는 기존 카드 전투 범위를 사용한다. 이미 추방한 참가자까지 포함하는 전리품 정산 명단과는 범위가 다르다. 각 처치의 경험치 경계가 합산으로 달라지지 않도록 처치별 보상을 따로 적용한다.

## 성장 원천 저장과 시트 변경 반영

DS의 초기 생성값과 성장값은 서로 다른 원천이다. `SetGeneratedValue`는 생성 당시 주요 능력치에서 그때의 `DefaultValue`를 뺀 차이를 `GeneratedStats`에 기록한다. 이후 `AddProgressionValue`는 레벨업의 주요 능력치 증가분만 `ProgressionStats`에 누적한다. Level과 Exp처럼 절대값으로 관리하는 progression 항목과 구분한다. `Serialize`는 두 원천을 따로 저장하고, `ApplyStructuredUnitStatus`는 먼저 최신 시트 기본값을 생성한 뒤 생성 증가분과 성장 증가분을 더한다. 주요 능력치 최종 합계를 저장하여 최신 기본값 위에 덮어쓰지 않는다.

P2의 고정 인물은 별도 초기 능력치 추첨 없이 `CharacterDefinitionTable.csv`의 기초 여섯 값을 그대로 사용한다. 따라서 DS의 생성 증가분에 해당하는 값은 0이며 별도 생성값 배열은 필요하지 않다. 현재 콘텐츠에 없는 영구 능력치 보상·장비·효과 원천도 추가하지 않는다. 복원 순서는 다음과 같다.

```text
현재 CharacterDefinition.BaseStats + 저장된 누적 성장 증가분
    → CharacterProgress.Stats / CharacterGrowthState
    → 현재 CombatStatSheet로 CombatStats 재계산
```

Adventure 저장 v3의 `CharacterStrengthGrowth`, `CharacterConstitutionGrowth`, `CharacterWillPowerGrowth`, `CharacterIntelligenceGrowth`, `CharacterAgilityGrowth`, `CharacterPerceptionGrowth` 배열에는 각 인물의 누적 증가분만 기록한다. 초기값은 여섯 항목 모두 0이다. 저장 성공 후에만 새 증가분과 레벨·경험치를 반영하고, 실패 재시도에는 이미 추첨한 후보를 사용한다.

예를 들어 힘 기본값이 10이고 누적 성장 증가분이 3인 저장을 현재 기본값 12인 시트로 불러오면 힘은 15가 된다. `CombatStatTable`의 계수·기본값 변경도 그 시트로 파생 수치를 다시 계산하므로 반영된다. 성장 범위나 배율 변경은 이후 레벨업에 적용하며 이미 저장한 증가분을 재추첨하거나 소급 보정하지 않는다. 반영 시점은 해당 시트를 로드하여 진행 상태를 구성할 때이며 실행 중 CSV 자동 재로딩을 의미하지 않는다.

최종 주요 능력치와 성장 계산 상태를 얻으려면 현재 `CharacterSheet`가 필요하다. 실제 MainFlow는 항상 이 시트를 전달한다. 상점·정산 등의 제한된 저장 경로에서 시트 없이 진행 데이터를 열 때는 누적 성장 원천을 보존할 수 있으나 `Stats`/`Growth` 조회는 명시적 예외로 거절한다. 기본값 0을 임의로 대입한 최종 능력치를 제공하지 않는다.

DS는 `ApplyProgressionStats`에서 레벨을 복원한 뒤 경험치를 `StateStatus.SetValue`로 설정해 현재 최대 경험치로 제한한다. P2도 필요 경험치표를 낮춘 뒤 기존 저장을 읽으면 현재 상한으로 제한한다. 읽기만으로 성장 난수나 저장 파일이 바뀌지 않도록 자동 성장은 다음 경험치 지급 때 처리한다.

## P2에 연결하는 경계

원본의 장비·부위별 HP·보호막·각인·스킬트리 전체를 새로 만들지는 않는다. P2의 기존 단일 생명력과 카드/ATB 전투, 플레이어의 수동 행동 선택·동료 자동 행동을 유지하면서 위 수치와 판정 계산을 연결한다. 데이터 계산에 사용하는 합산 최대HP는 DS `MaxHealthTotal`이다. 이동속도와 재사용 대기시간 수치는 계산·표시 가능하지만 현재 이동 애니메이션이나 별도 사용 스킬을 생성하지 않는다.

## 회귀 검증

`Assets/Tests/EditMode/CharacterCombatGrowthTests.cs`의 12개 테스트는 원본 기본값·계수, 고정/비율 보정 순서, 세 가지 80 상한, 원형 적과 배율 불변성, 명중 경계·난수 소비, 물리 방어 우선·마법 치명타, 저항·가산 피해·오버킬 흡혈, 실제 공격 주기, EXP 표와 초과분 폐기, 6회 독립 성장·반올림, 최고 레벨과 잘못된 시트를 검증한다. 이 문서는 테스트 소스와 근거를 기록하며 Unity 실행 통과 여부는 통합 검증 결과에서 별도로 기록한다.
