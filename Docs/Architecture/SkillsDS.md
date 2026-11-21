# DS 스킬·직업 이식 근거

이 문서는 DS를 읽기 전용으로 대조한 원본 근거와 P2의 명시적 변환을 기록한다. 사용자 요구는 무기 종류별 직업, 직업당 하나의 액티브 트리, 모든 적합한 패시브를 직접 고르는 권한, DS와 같은 수치·효과·습득 조건이다. 부적합한 스킬을 제외할 수 있다는 요구를 적용하며, 기술 구현 비용만으로 효과 있는 패시브를 제외하지 않는다. 원본 자체가 없는 직업 선택/장비 구매 기능은 추가하지 않는다.

## 원본 자료와 실제 소비 경로

아래 경로는 `D:/Dev/DS/` 기준이다.

- `Assets/1_InternalTools/Util/DataSheetLoader/Table/SkillTable.asset`: 전체 253행 = Active 126 + Passive 47 + Item 74 + Building 6. 스킬 ID, 계수 배열, 대상, 재사용 대기, 기력, 요구 레벨/SP/선행 스킬의 원천이다.
- `.../SkillTreeTable.asset`: 비어 있지 않은 직업 액티브 9트리/46개, 패시브 10트리/44개. Axe/Dagger/Crossbow와 생활 계열의 빈 슬롯은 실행 가능한 스킬로 세지 않는다. 패시브 3개가 트리 밖에 별도로 정의되어 총 47개다.
- `.../AffecterTable.asset`, `.../StatusValueTable.asset`, `.../GameSystemTable.asset`: 상태효과 보정값/중첩, 실제 스탯 기본값, SP·글로벌 쿨다운·논리 갱신 간격.
- `Assets/0_Project/2_Campaign/1_Scripts/MainFlow/CampaignData/PlayerDataContainer/Skill/SkillTreeData.cs`: `IsLearnAvailable`, 선행 스킬 전체 보유, 레벨, Main/Sub SP 검사. `SkillPointCostCalculator`가 적성을 비용에서 차감한다. P2의 적성은 0이다.
- `.../MainFlow/Event/EventHandlers/EntityEventHandler.cs`: LearnSkillRequested 검사 뒤 스킬 등록과 SP 차감을 진행한다. `.../Helper/SpawnHelper/UnitSpawner.cs`는 초기 SP에 `(레벨-1) × 레벨당 SP`를 더한다. `.../Helper/LevelUpHelper.cs`는 레벨 상승 시 두 종류 SP를 지급한다.
- `Assets/0_Project/999_Refactoring/1_Scripts/Combat/Skill/Skill_Base.cs`: 원본 ID에 대응하는 `Refactor.Combat.SkillRelated` 클래스를 생성한다. 타입이 없으면 임의 효과를 만들지 않고 null/오류를 반환한다.
- `.../Combat/Skill/ActiveSkills/ActiveSkill_Base.cs`: 조건 검사 → StartDelay → SkillRoutine → EndDelay. 장비, 기력, 쿨다운, 탄약/소환물 조건을 실제 검사한다. `SkillCooldownCalculator`는 공격 속도 대상이면 양수 AttackSpeed, 아니면 `1+CooldownReduction×.01`로 나눈다.
- `.../Combat/Skill/ActiveSkills/PlayerSkills/ActiveSkills_{Sword,GreatSword,Club,Bow,FireMagic,WaterMagic,NatureMagic,Shield,Spear}.cs`: 아래 액티브·기본기의 실제 실행. `.../Combat/Skill/PassiveSkills/`의 해당 무기별 파일들은 TickEvent 구독으로 피해 전후, 스킬 시작/종료, 사망, 효과 적용, 장비 변경을 관찰한다.
- `.../SheetData/TableData/AffecterSheet.cs:LinkValues`: BindingValue가 가리키는 Skill.FloatValues를 효과에 연결한 뒤 AdjustStats/AdjustStatsPerStack을 해석한다. 설명의 숫자보다 이 바인딩과 실행 코드가 우선한다.
- `.../Combat/Affecter/Affecters_Normal.cs`: ExposedFlaw/Burning/Dizziness/HealthBoost의 실제 동작. `.../Combat/Affecter/PlayerSkill/Affecters_GreatSword.cs`: BladeGrinding 소모·보정.
- `.../Util/EnumExtensions/ItemSubTypeExtension.cs:IsTwoHanded`: Greatsword/Bow/세 지팡이/Spear가 양손 무기다. Sword/Blunt에만 한손 빈 보조장비 조건의 TwoHandGrip을 적용한다.

## P2 데이터/API

`SkillDefinitionTable.csv`, `SkillClassTable.csv`, `CharacterClassTable.csv`, `SkillWeaponTable.csv`, `SkillSettingTable.csv`, `SkillTextTable.csv`를 `SkillContent`가 `SkillSheet`로 조립한다. 값 배열/선행 목록은 세미콜론으로 구분한다. DescriptionFormat의 `{n}`을 Values에서 매번 치환하므로 수치 조정과 문구가 함께 바뀐다. 이름/직업 배정/무기명/효과 수치/최대 중첩/SP는 시트로 관리한다.

- `Skills`: 학습 가능한 액티브 18 + 패시브 33 = 51개. `BasicSkills`: 무기/대체 기본기 10개. `GetSkill(id)`는 양쪽을 조회하지만 기본기는 구매하지 않는다.
- `GetClassForCharacter(id)` → `ActiveTreeId`, `WeaponId`, `BasicAttackId`. `GetWeapon(id)` → 이름과 기본기 ID. 9개 직업을 유지하며 활/물/자연 트리는 현재 적합한 학습 액티브가 0개다. 기본기와 적합 패시브는 사용할 수 있다.
- 인물 기본 배정: hero Sword, mira Blunt, orin Spear, bran Greatsword, sera Shield, neri FireMagic, cass Bow. 배정은 P2용 콘텐츠이며 DS에 이 인물별 배정이 원래 존재한다는 뜻이 아니다.
- 초기 Main/Sub SP = 10/6, 레벨당 6/6, 최대 각각 9999. 요구 레벨/비용은 원본을 유지한다. 제외 스킬이 선행 조건에 있을 때 그 링크만 제거하고, 남은 모든 선행 조건은 유지한다. 아래 포함 표에 최종 조건을 적었다.
- 원본 0(None) DamageType인 버프/패시브는 CSV에 Physical 메타데이터를 두지만 피해를 실행하지 않는 버프가 그 값 때문에 물리 피해를 만들지는 않는다. 실제 피해를 발행하는 BattleCadence 등 Passive publisher는 전투 엔진이 명시적으로 CombatDamageType.None을 사용한다. 기본기/액티브의 Physical/Magic 분류는 원본 그대로다.

## 포함한 액티브 18개

원본 계수 배열의 순서를 그대로 저장했다. 목록의 `SKILL_` 접두사는 생략했다. 쿨다운과 기력은 보정 전 값이다.

| 직업 | ID / 이름 | Values | 레벨 / SP | CD / 기력 | P2 선행 |
|---|---|---|---|---|---|
| Sword | VerticalSlash / 버티컬 슬래시 | 300 | 0 / 8 | 12 / 3 | 없음 |
| Sword | SwordCounter / 소드 카운터 | 12;200 | 0 / 8 | 18 / 4 | 없음 |
| Sword | ChainSlash / 체인 슬래시 | 100;4 | 4 / 10 | 12 / 3 | VerticalSlash |
| Sword | PommelStrike / 폼멜 스트라이크 | 120;1 | 4 / 10 | 6 / 3 | SwordCounter |
| Greatsword | Slay / 슬레이 | 450;3;1 | 0 / 8 | 10 / 4 | 없음 |
| Greatsword | BladeGrinding / 블레이드 그라인딩 | 50;30 | 4 / 10 | 12 / 2 | Slay |
| Blunt | BattleStomp / 배틀 스텀프 | 30;15;1 | 0 / 8 | 30 / 6 | 없음 |
| Blunt | IronClad / 아이언 클래드 | 12;120 | 0 / 8 | 18 / 3 | 없음 |
| Blunt | Hammering / 해머링 | 260;10;1;1 | 0 / 8 | 10 / 3 | 없음 |
| Blunt | VileInsult / 바일 인설트 | 100;10 | 4 / 10 | 8 / 3 | IronClad, BattleStomp |
| Blunt | WildFlurry / 와일드 플러리 | 30;80;4 | 4 / 10 | 12 / 3 | IronClad, Hammering |
| FireMagic | Incinerate / 인시너레이트 | 300;30 | 0 / 8 | 12 / 4 | 없음 |
| FireMagic | BecomeFire / 비컴 파이어 | 30;2;30;12 | 4 / 10 | 15 / 5 | 없음 |
| FireMagic | Explosion / 익스플로전 | 400;10;5 | 4 / 10 | 30 / 7 | Incinerate |
| Shield | ShieldUp / 실드 업 | 25;35 | 0 / 8 | 30 / 5 | 없음 |
| Shield | GuardiansTaunt / 가디언즈 타운트 | 6 | 4 / 10 | 30 / 5 | ShieldUp |
| Spear | TwinThrust / 트윈 스러스트 | 10;70;2 | 0 / 8 | 20 / 4 | 없음 |
| Spear | OpeningStrike / 오프닝 스트라이크 | 250;8 | 0 / 8 | 12 / 4 | 없음 |

## 제외한 직업 액티브 28개

| ID | 제외 근거 |
|---|---|
| ChargeAttack | 이동 경로의 타일 수가 피해 계수에 포함된다. 이동 거리와 돌진을 제거하면 원본 공격식 자체가 달라진다. |
| SwordFlow | ActiveSkills_Sword.cs에 퀵슬롯 테스트용 임시 더미라고 명시되어 있다. 설명의 버프를 구현하지 않는다. |
| SaberGale | 동일 파일의 미완성 더미. 설명의 세 직선 범위 효과를 실제 구현하지 않는다. |
| DualRequest | 동일 파일의 미완성 더미. 결투·타인 피해 감소 설명과 실제 실행이 다르다. |
| SentinelCry | 동일 파일의 미완성 더미. 원본 설명도 TBD다. |
| DestructiveSplit | 선택 타일과 그 뒤 타일의 별도 대상 집합을 공격한다. 카드의 배치·앞뒤 타일이 없다. |
| DeadlyFear | 위협 범위와 이동 둔화를 조합한 원본 기술이다. 현재 고정 카드 전투에서 이동 둔화가 무의미하므로 이번 학습 목록에서 제외한다. |
| ChargingWhirlwind | 이동 경로의 각 타일마다 인접한 대상을 반복 타격한다. 경로 자체가 타격 횟수와 대상 수를 결정한다. |
| SnipingShot | 대상까지의 타일 거리에 비례해 피해가 증가한다. |
| ArrowRain | 지정 지면에 일정 시간 반복 투사체를 배치하고 그곳의 적을 타격한다. 고정 카드의 즉시 연타로 바꾸면 지면/시간 기능이 달라진다. |
| AnkleBreak | 넉백 타일 수와 이동 둔화·이동 방해가 핵심 부수 효과다. 피해만 남기지 않는다. |
| HeadAim | 부위별 생명력의 머리만 공격하도록 바꾼다. P2에는 단일 HP만 있다. |
| QuickDash | 지정 지점으로 이동하고 이동 가속을 부여한다. |
| FireCloak | 주변 범위 내 대상에게 지속적으로 접촉 피해를 주는 위치 기반 오라와 보호막을 결합한다. 위치 오라를 삭제하지 않는다. |
| BlazingEcho | 인접 3타일 내 대상 사이로 튕기는 투사체다. 인접성·최소 피격 수 대상 선택이 필요하다. |
| TidalMark | 지정 타일 타격 후 소환 생물이 표시된 대상을 공격하도록 지시한다. 소환 생물과 이동 지시가 없다. |
| EmbracingWave | 버블링 1개를 소모하고 3타일 이내 아군에게 보호막이 연쇄된다. 소환 자원/인접성이 없다. |
| GlacierDrop | 버블링을 소모해 얼음 기둥을 건설하고 넉백·주변 오라를 만든다. |
| SummonBubbleToad | 버블링 소모/버블 토드 소환 기술이다. 소환 전투원이 없다. |
| WaterDive | 버블링을 만들고 모든 버블링에게 돌진·폭발을 지시한다. |
| MucusSprayer | 기묘한 씨앗을 점액 분사 구조물로 바꾼다. |
| ClingingVines | 6타일 직선 범위와 속박을 사용하는 위치 기반 기술이다. |
| SolarBloom | 기묘한 씨앗을 솔라 블룸 구조물로 바꾼다. |
| SnatchingPrey | 점액 분사 구조물이 사방으로 덩굴을 발사하게 한다. |
| SunHeartBloom | 기존 솔라 블룸 구조물을 업그레이드한다. |
| Anchor | 이동 불가를 대가로 공격·돌격 버프를 얻는 기술이다. 위치 선택이 없는 현재 전투에서 대가를 없애지 않는다. |
| QuickRetreat | 속박·둔화 제거와 이동 가속이 중심인 후퇴 기술이다. |
| SpearBash | 3타일 넉백과 이동 둔화를 포함하는 창 공격이다. 위치 효과만 제거하지 않는다. |

## 포함한 패시브 33개

패시브는 원래의 트리 구분/선행/레벨/SP를 보존하지만 직업에 관계없이 학습 후보를 보여 준다. 무기 조건이 있는 효과는 실제 무기가 맞을 때만 발동한다. 스킬이 배워졌다는 이유로 해당 조건을 생략하지 않는다.

| ID / 이름 | 원본 Values | 요구 레벨 / SP | 적용 효과 |
|---|---|---|---|
| TwoHandGrip / 투 핸드 그립 | 35 | 0 / 5 | 양손 무기가 아닌 주무기를 장착하고 보조 장비가 없는 동안 물리 공격력이 35% 증가합니다. |
| SwordmansResolve / 소드맨스 리졸브 | 12 | 0 / 5 | 스킬 재사용 대기 시간 감소가 12 증가합니다. |
| SharpEdge / 샤프 엣지 | 8;6 | 4 / 6 | 스킬을 사용할 때 8초 동안 물리 공격력이 중첩당 8% 증가합니다. 최대 6회 중첩됩니다. |
| BladeSpirit / 블레이드 스프릿 | 1 | 4 / 6 | 적에게 스킬로 근접 물리 치명타 피해를 입힐 시 1의 기력을 회복합니다. |
| FlowSeeker / 플로 시커 | 5 | 4 / 6 | 단일 적 대상 근접 스킬 사용시 대상에게 5초 동안 1의 [빈틈]을 부여합니다. |
| Executioner / 익세큐셔너 | 10;10;30 | 4 / 6 | 적 처치시 10초 동안 치명타 확률이 10% 증가합니다. 최대 30% 까지 증가할 수 있습니다. |
| ColdFury / 콜드 퓨리 | 5;20 | 0 / 5 | 적 처치시 5초 동안 주는 피해량이 20% 증가합니다. |
| InevitableBlow / 인에비터블 블로우 | -1 | 0 / 5 | [기절] 상태의 유닛을 대상으로 근접 피해를 가할 때 확정적으로 공격이 명중합니다. |
| CrimsonRecoil / 크림슨 리코일 | 6;30 | 0 / 5 | 적 처치시 6초 동안 잃은 생명력의 30%에 해당하는 [보호막]을 얻습니다. |
| FatalVerdict / 페이탈 버딕트 | 30 | 4 / 6 | 적에게 근접 물리 피해를 입힐 시, 상대의 잃은 생명력에 비례하여 최대 30%까지 피해량이 증가합니다. |
| SavageBarrier / 세비지 베리어 | 25;5;30 | 0 / 5 | 현재 생명력이 25% 이하로 감소하면 5초 동안 최대 생명력의 30% 만큼의 보호막을 얻습니다. 이 효과는 한 전투에서 최대 1회 발동합니다. |
| GuardBarrier / 가드 베리어 | 5;5 | 0 / 5 | 적 유닛에게 부정적 효과 부여시 5초 동안 최대 생명력의 5%에 해당하는 보호막을 얻습니다. |
| Fortified / 포티파이드 | 20 | 0 / 5 | 보호막 습득 시 추가로 20%의 중첩을 얻습니다. |
| FinesseEdge / 피니스 엣지 | 12 | 4 / 6 | 부정적 효과를 가진 대상으로부터 받는 피해량이 12% 감소합니다. |
| BarrierResurge / 배리어 리서지 | 20 | 0 / 5 | 보호막이 피격으로 소모되거나 만료되면 감소한 정수 스택의 20%만큼 생명력을 회복합니다. |
| MockingGrin / 모킹 그린 | 20;10 | 4 / 6 | 자신에게 도발 당한 대상으로부터 피해를 받을 때 마다 10초 동안 받은 피해의 20%에 해당하는 보호막을 얻습니다. |
| FastHand / 패스트 핸드 | 12 | 0 / 5 | 공격 속도가 12% 증가합니다. |
| SharpShooter / 샤프 슈터 | 10;25;100;250;1000;2500;7 | 0 / 5 | 기본 공격으로 처치한 적의 수가 10/25/100/250/1000/2500 달성 될 때마다 치명타 확률이 7% 증가합니다. |
| FirstAssault / 퍼스트 어설트 | 15;35 | 4 / 6 | 전투 시작시 15초 동안 물리 공격력이 35% 증가합니다. |
| CombatMomentum / 컴뱃 모멘텀 | 5;8 | 4 / 6 | 적 처치시 5초 동안 공격 속도가 8% 증가합니다. 최대 3회까지 증가할 수 있습니다. |
| FireProof / 파이어 프루프 | 100;40 | 4 / 6 | 얻는 [연소] 중첩량을 추가로 100% 얻습니다. [연소]으로 인한 피해를 40% 무시합니다. |
| LastCinder / 라스트 신더 | 50;1;5 | 0 / 5 | 생명력이 50% 이하일 때, 기본 공격 시 상대에게 1의 [연소]을 5초 동안 부여합니다. |
| Ignition / 이그니션 | 3 | 0 / 5 | [연소]를 보유한 적 대상에게 피해를 줄 때, 가하는 피해량이 [연소] * 3% 만큼 증가합니다. |
| FlameOfLife / 플레임 오브 라이프 | 4 | 4 / 6 | 적 유닛에게 [연소] 부여시 현재 잃은 생명력의 4%를 회복합니다. |
| WaveCicle / 웨이브 사이클 | 10 | 4 / 6 | 보호막 습득 시 모든 스킬의 재사용 대기 시간이 10% 감소합니다. |
| ShieldHandling / 실드 핸들링 | 10;20 | 0 / 5 | 가드율이 10% 증가합니다. 방패 장착 시 공격 속도가 20% 증가하며, 장착한 방패의 무게가 0이 됩니다. |
| ElasticGuard / 엘라스틱 가드 | 3;25 | 4 / 6 | 가드시 3초 동안 가드에 의해 감소된 피해량의 25% 만큼의 보호막을 얻습니다. |
| Withstand / 위드스탠드 | 25 | 4 / 6 | 생명력이 50% 이하일 때, 가드율이 25% 증가합니다. |
| InnerGrove / 이너 그루브 | 25 | 0 / 5 | 자신이 받는 모든 생명력 회복 효과가 25% 증가합니다. |
| LifeCycle / 라이프 사이클 | 2;1 | 4 / 6 | 전투 중 적 유닛 사망시 모든 아군 유닛에게 2초 동안 마법 공격력의 1%에 해당하는 [생명력 촉진]을 부여합니다. |
| HealingTouch / 힐링 터치 | 30 | 0 / 5 | 자연 지팡이를 장착하면 기본 공격이 아군의 생명력을 회복하는 힐링 터치로 변경됩니다. 회복량은 힐링 터치 공격의 정의를 따릅니다. |
| Initiative / 이니셔티브 | 15;35 | 0 / 5 | 전투 시작 시 15초 동안 공격 속도가 35% 증가합니다. |
| BattleCadence / 배틀케이던스 | 3;30 | 4 / 6 | 기본 공격 누적에 따라 물리 공격력의 30%에 해당하는 추가 일반 피해를 입힙니다. 정확한 누적 순서는 아래 경계 항목을 따릅니다. |

## 제외한 패시브 14개

| ID | 제외 근거 |
|---|---|
| WeaponDeflect | SkillTable 행만 있고 실행 클래스가 없다. Skill_Base.Create가 타입을 찾지 못하며 어떤 학습 트리에도 배치되어 있지 않다. |
| SwordmanTaunt | SkillTable 행만 있고 실행 클래스가 없다. Skill_Base.Create가 타입을 찾지 못하며 어떤 학습 트리에도 배치되어 있지 않다. |
| Maelstrom | 1타일 이내 유닛 수가 공격력 보정량을 결정한다. 카드 전투에 이 인접 관계가 없다. |
| BulkShot | 탄약 소모와 대상 인접 타일의 추가 타격을 결합한다. 탄약/인접 타일이 없다. |
| FireBattleMage | 화염 지팡이의 장착 조건을 우회한다. P2는 직업에 고정된 무기로 자기 트리만 배우므로 우회 권한이 아무 효과도 만들지 않는다. |
| ExtraBubble | 스킬 사용 시 버블링을 소환한다. 소환 시스템이 없다. |
| EbbAndFlow | 자신이 소환한 유닛/구조물의 사망을 관찰한다. 해당 대상이 없다. |
| DeepWater | 소환한 유닛/구조물의 영구 누적 수에 따른 보너스다. 소환 자체가 없다. 기본 공격 처치가 존재하는 SharpShooter와는 다르다. |
| Bastion | 인접한 적 수에 따라 가드율이 증가한다. 카드 간 타일 인접성이 없다. |
| GreenShade | 인접한 아군 식물 구조물 수로 피해를 줄인다. 식물 구조물/인접성이 없다. |
| LifeBurst | 기묘한 씨앗의 파괴와 인접 아군을 대상으로 한다. 씨앗 구조물이 없다. |
| NatureSpirit | 자연 지팡이 장착 조건을 우회한다. 고정 직업/자기 액티브 트리에서 의미가 없다. |
| RecklessAssault | 이동하지 않고 버틴 시간에 따른 장단점과 이동 시 초기화를 사용한다. 현재 모든 카드가 움직이지 않아 위치 선택의 대가가 사라진다. |
| BraceStance | 근접 기본 공격으로 쌓은 속도 버프가 이동 시 초기화된다. 현재 고정 카드 전투에는 이 핵심 초기화 조건이 없다. |

## 원본 설명과 실행이 다른 경계

- **SharpEdge**: Skill Values는 `[8,6]`이지만 AffecterTable의 PhysicalAttackPowerPerModifier 바인딩은 `{0}`이다. 실제 효과는 8초 동안 중첩당 8%, 최대 6중첩이다. P2 문구도 실제 실행값에 맞췄다. 원본 설명의 6%를 임의로 정답으로 삼지 않았다. 바인딩은 AffecterSheet.LinkValues → StringTools.ParseStats에서 실제로 해석된다.
- **Dizziness**: 설명에는 1초 기절이라고 적혔지만 실제 Values `[2,2]`를 읽어 2중첩을 소모하고 2초 기절을 부여한다.
- **BladeGrinding**: 실제 Passive는 다음 `IsBasicAttackSkill == false && SkillType == Active`에 적용되며 근접/물리 필터가 없다. DamageDealtBonus +50 및 HitChance +30이다. HasDuration=0이므로 코드에서 전달한 duration=1은 1초 만료가 아니다. 해당 다음 스킬 종료 시 제거한다.
- **BattleCadence**: 기본 피해 처리마다 누적한다. 원본은 `기존 stack>=2`이면 추가 물리30% 타격/스택 삭제 실행을 예약하고, 뒤이어 항상 +1을 예약한다. 따라서 초기 3번째 이후에는 다시 1부터 시작하는 원본 순서를 보존했다. 단순히 매 3회 모듈로 계산으로 바꾸지 않는다. 원본 값 `[3,30]`을 유지한다. 추가 피해는 `PassiveSkill_Base`가 발행하므로 `DamageExecutionData.GetDamageType/GetAttackType`이 None을 사용한다. 실제로 물리 공격력 계수 피해지만 확정 일반 피해로 명중/가드/치명타 난수를 소비하지 않으며 속성 저항도 없다. `DamageSimulator.CalculateResult`는 DamageType.None이면 즉시 Normal을 반환한다. 설명의 단순한 “물리 피해”와 다르다.
- **MockingGrin**: 문구는 자신이 도발한 대상이지만 실제 Passive는 공격자에게 Taunt가 있는지만 검사한다. Taunt의 시전자 ID를 추가 제한하지 않는다.
- **HealingTouch**: 패시브 Values의 30은 실제 회복 계산에 쓰이지 않는다. SKILL_HealingTouchAttack을 추가하는 실행이며 그 기본기의 계수 50%가 실제 회복량이다. 패시브 문구는 기본기 정의를 참조하도록 정리했다.
- **TwoHandGrip**: 현행 실제 조건은 한손 주무기 장착 + 보조장비 없음이다. 대검/창/활/세 지팡이에 적용하지 않는다.
- **BarrierResurge**: 설명은 시간 만료만 언급하지만 `PassiveSkill_Club.cs`는 음수 Barrier AffecterApplyData를 모두 관찰한다. 피격 소모도 `DamageSimulator.CalculateApplyDamageToUnitShield` → `DataApplier.CallOnAfterApply` → `TickEvent.OnAffecterApply`를 거친다. 따라서 실제 원본은 피격 소모와 시간 만료 모두 감소한 정수 스택의 20%를 회복한다. HP 차감 후 후속 회복을 처리하고, 사망한 인물은 회복하지 않는다.
- **WaveCicle**: 실제 보호막 증가가 있을 때 현재 남은 개별 스킬 CD를 10%씩 줄인다. 글로벌 CD는 대상이 아니다. Fortified가 보호막 증가량을 보정한 뒤 이 후속 사건을 한 번 처리한다.
- **SharpShooter**: 소유자가 기본 공격으로 직접 처치한 횟수만 누적하며 원거리 제한은 없다. 10/25/100/250/1000/2500에 도달할 때마다 치명타 확률 +7. 인물별 BasicAttackKills 원천을 저장해 기존 처치를 재추첨/재지급하지 않는다.
- **ExposedFlaw**: 시전자가 있는 피해에 대해 AlwaysHit와 AlwaysCritical을 켜고 1중첩을 소모한다. 물리/근접/액티브 전용이 아니다. 시전자가 없는 연소 틱에는 소비하지 않는다.

## 카드 전투로의 명시적 변환

- 기본 공격은 해당 무기의 원본 피해 계수와 전체 주기(StartDelay+EndDelay)를 사용한다. ATB에서 플레이어만 행동을 고르고 동료는 배운 액티브를 자동 사용한다. DS의 이동·회전·애니메이션 프레임/투사체 물체는 만들지 않는다.
- BattleStomp/GuardiansTaunt의 주변 적 집합은 전투 중 살아 있는 적 카드 전체다. Explosion의 원본 MultipleAny는 아군을 포함한 살아 있는 모든 카드 대상이다. 아군 피해를 몰래 삭제하지 않는다.
- 기본 무기의 정체성을 유지하기 위해 원본 기본기는 별도 데이터로 두되 대검의 양옆/창의 직선 관통/활의 근접 거리 감쇠는 좌표가 없는 P2에서 적용하지 않는다. 주요 대상의 원본 계수와 주기는 유지하며 UI 설명은 실제 단일 대상 효과를 표시하고 위치 효과의 차이는 이 문서에 남긴다. 이 예외를 제외한 위치 기반 학습 기술은 목록에서 제외했다.
- ShieldUp의 Slow는 원본 효과 이름과 지속시간을 유지한다. 현재 모든 전투 카드가 고정 위치이므로 이동 속도 변화는 화면/전투 결과에 영향을 주지 않는다. 방패 보너스의 가드율/가드 감소는 적용한다. 장비 무게 시스템이 없는 ShieldHandling의 방패 무게0 역시 현재 무게 비용을 만들지 않는다.
- 구조물·생산·건설·농사·은신·날씨·소환물·탄약·HP 부위는 이 범위에 새로 만들지 않는다. 원본 Item 74/Building 6 및 직업 트리에 속하지 않는 몬스터/아이템 부가/소환물/장비 전용 액티브는 학습 목록이 아니다.

## 시간·기력·상태 수치

- 실제 인물 원천은 `StatusValueTable`, 레거시 `GameSystem_Unit_Growth_FinalStats_DefaultValue_*`가 아니다. MaxEnergy 기본60 + 양수 WillPower×2, 나머지 다섯 계수0. 기본 주요 능력치10의 인물은 기력80이다. 최대기력% 보정이 있으면 그 합계에 곱한다.
- 실제 EnergyRegeneration은 -0.05/논리 갱신이다. 회복으로 뒤집지 않는다. LogicalRefreshInterval=60틱, 실시간60틱 기준 1초이며 기력 감소는 전투 시간이 흐를 때만 진행한다. P2의 플레이어 행동 선택 대기/모달/저장 실패 정지는 전투 시간을 멈춘다.
- 기력과 쿨다운은 `ExecutionConditions.CooldownCondition/EnergyCondition.OnSuccessCondition`처럼 스킬 효과 실행 전에 적용한다. 그러므로 최대 기력에서 BladeSpirit의 회복이 사라지지 않고, IronClad로 받은 보호막의 WaveCicle 후속 효과는 방금 시작한 IronClad 쿨다운도 줄일 수 있다.
- 글로벌 CD=5초. 일반 스킬 CD는 `base/(1+CooldownReduction×.01)`, IsDelayAdjustedByAttackSpeed면 양수 AttackSpeed로 나눈다. 기본기는 기력/SP를 소모하지 않는다.
- Burning: 논리1초마다 고정 마법 피해 `3×중첩`, 최대50, 보호막 무시. FireProof는 얻는 연소중첩 +100%, 연소피해 -40%. 저항/원본 마법 피해 규칙은 유지한다.
- Dizziness: 최대5, 2중첩 소모당 기절2초. HealthBoost: 논리1초마다 `10×중첩` 회복, 최대99. LifeCycle의 적용 중첩은 `Ceil(시전자 MagicAttackPower×1%)`, 2초다.
- 상태 중첩은 `AffecterSimulator.CalculateOriginalStack`의 `(int)((fixed+selfCoef+targetCoef)*(1+multiplier))`처럼 최종 배율 적용 후 0방향 절삭한다. 양수는 버림이다. 보호막도 Fortified의 추가20%를 적용한 뒤 정수화하고 최대999999로 제한한다. LifeCycle은 이 일반 처리 전에 자신의 명시적인 Ceil 계산을 수행한다. `MaxStack=-1`은 무한 누적이 아니라 최대1이다. 보호막 이하의 피해는 소수 부분까지 HP로 넘기지 않으며 정수 부분만 보호막에서 차감한다. 예를 들어 남은 보호막10에 피해0.5는 HP와 보호막 모두 감소시키지 않는다. 보호막보다 큰 피해는 보호막 전체를 없애고 남은 소수 피해를 HP에 적용한다.
- FatalVerdict/Ignition의 DataValue 배율은 DamageDealtBonus/속성 피해 보너스/DamageTakenBonus와 **한 가산항**으로 합친다. 최종식은 `기초 피해 × (1 + 값배율 + 가피증 + 속성가피증 + 받피증) × 결과배율 × 저항배율 × FinalDamageMultiplier`이다. 계수에 먼저 곱하면 다른 보너스와 불필요한 교차항이 생긴다.
- Fortification GuardChance+35, Disturbance HitChance-25, Stiffness DodgeChance-50, Vulnerable DamageTakenBonus+20. % 공격력/속도 보정은 영구+임시 보정값을 합산한 뒤 기초값에 한 번 곱한다.

## P2 실행 책임

- Sheet/Content는 파싱과 원본 수치·문구/참조 검증만 맡는다. 알려진 ID를 코드 whitelist로 제한하지 않는다.
- 학습/소지품/직업 조회와 저장은 기존 Store·MainFlow 조율 경로에 연결한다. UI는 읽기와 요청 발행만 한다.
- `CombatSkillPassives`는 영구 스탯, 임시 스탯, 스킬 시작/종료, 피해 전후, 처치/효과 사건을 계산한다. Counter/BattleCadence 추가 피해는 전투 엔진 콜백으로 실행해 처치·EXP·전리품·의뢰 중복 방지 경로를 공유한다.
- `CombatantSkills`는 에너지/쿨다운/효과/보호막을 소유한다. 저장되는 것은 습득 목록/SP/SharpShooter 실제 기본공격 처치 원천이다. 파생 전투 수치와 일시 버프는 저장하지 않는다.
