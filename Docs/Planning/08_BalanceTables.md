# 게임 밸런스 CSV

2026-11-25 후속 요청. 초기 금화·매 전투 보상처럼 조정하는 수치를 코드 상수에서 편집 가능한 시트로 옮긴다. 경제부터 가챠 확률, 기본 크리쳐와 스킬, 각인 수정, 진화 성장, 공통 전투 수치까지 포함했다. 기존 기본값은 유지했다.

## 편집 파일

모든 CSV는 `Assets/1_InternalTools/Util/DataSheetLoader/Table/`에 있다. Excel 또는 텍스트 편집기로 수정하고 **UTF-8 CSV(쉼표로 분리)**로 저장한다. 새 시트의 헤더는 `Key,Values,Description`이다. **Values 열을 수정**하고 Description에서 단위와 배열 순서를 확인한다. 배열은 `|`로 구분한다.

| 파일 | 조정 대상 |
| --- | --- |
| [GachaEconomyTable.csv](../../Assets/1_InternalTools/Util/DataSheetLoader/Table/GachaEconomyTable.csv) | 초기 금화, 두 가챠 가격, 1~10단계 승리 보상 배열, 양쪽 가챠 등급별 가중치 |
| [GachaCreatureTable.csv](../../Assets/1_InternalTools/Util/DataSheetLoader/Table/GachaCreatureTable.csv) | 6종의 생명력·물공·마공·공속·이속·사거리·투사체·스킬 배율/쿨다운, 거북 보호·수달 회복·멧돼지 밀기 |
| [GachaCrystalTable.csv](../../Assets/1_InternalTools/Util/DataSheetLoader/Table/GachaCrystalTable.csv) | 12종 각인의 발동 횟수, 보호막·반사·피해 증가·처형·회복·추가 확률·지속/재발동 |
| [GachaCombatTable.csv](../../Assets/1_InternalTools/Util/DataSheetLoader/Table/GachaCombatTable.csv) | 공통 능력치, 기력/소모량, 소환·회피, 감속/연소, 저항 공식 기준·가드 상한, AI 거리/이동/치유 기준, 상점 이동/상호작용 거리 |
| [GachaEvolutionTable.csv](../../Assets/1_InternalTools/Util/DataSheetLoader/Table/GachaEvolutionTable.csv) | 랭크 1~3 성장 배열, A/B 및 AA/AB/BA/BB 능력치 배율·보너스 |
| [GachaStageTable.csv](../../Assets/1_InternalTools/Util/DataSheetLoader/Table/GachaStageTable.csv) | 기존 단계별 적 물공·마공·생명력 배율과 소환 편성 배열. 기존 5열 형식 유지 |

새 5개 시트에는 총 177개 키가 있다. 단계 수 10, 출전 최대 3, 3합성·랭크 1~3, 콘텐츠 종류/계통과 스킬의 행동 형태는 기존 게임 규칙이다. 화면 위치·색상·프레임 수·순수 연출 시간·시뮬레이션 시간 간격·오차 허용값은 밸런스 시트 대상에 포함하지 않는다.

## 금화와 보상

`GachaEconomyTable.csv`의 기본 값:

```csv
Key,Values,Description
Economy.StartingGold,600,새 모험 시작 금화
Economy.CreaturePrice,100,크리쳐 가챠 1회 가격
Economy.CrystalPrice,60,수정 가챠 1회 가격
Economy.VictoryGold,200|200|200|200|200|200|200|200|200|200,1~10스테이지 승리 보상
```

예를 들어 시작 금화를 1000으로, 단계별 보상을 100/120/…/280으로 바꾸려면:

```csv
Economy.StartingGold,1000,새 모험 시작 금화
Economy.VictoryGold,100|120|140|160|180|200|220|240|260|280,1~10스테이지 승리 보상
```

- 보상은 **방금 승리한 스테이지**의 배열 값이다. 지급 후 다음 단계로 증가한다. 결과 화면도 같은 값을 표시한다.
- 가격·보상·초기 금화는 0 이상의 정수다. 가격 0은 무료 뽑기, 보상 0은 금화 없는 승리로 동작한다. 각 설정값 상한은 10,000,000 G다.
- 중복 결과 확인은 중복 지급하지 않는다. 패배·무승부·포기는 승리 보상이 없다.
- 10단계 보상도 정산한 뒤 모험을 끝낸다. 새 모험은 현재 시트의 초기 금화를 사용하고 이전 보유·진행을 초기화한다.
- 타이틀의 시작 금화, 머신 앞 가격표, 구매 버튼, 돈 부족 판정·실제 차감, 전투 결과와 보상 메시지가 모두 같은 값을 읽는다.
- 기존처럼 방출은 환급이 없고 장착/해제/진화는 무료다. 별도 매각이나 유료 장착 같은 새 경제 행동은 추가하지 않았다.

## 확률 배열

`Gacha.CreatureRarityWeights`와 `Gacha.CrystalRarityWeights`의 배열 순서는 **일반|고급|희귀|전설**이다. 기본값은 각각 `300|180|90|30`, `600|360|180|60`이며 둘 다 **50/30/15/5%**다.

합계 100일 필요는 없다. `50|30|15|5`도 같은 확률이다. `70|20|9|1`은 70/20/9/1%다. 0은 해당 등급을 제외하고, 네 값 모두 0은 오류다. 같은 등급 안에서 균등하게 배분되도록 정수 추첨 구간을 확장하므로 적은 가중치가 반올림으로 사라지지 않는다. 구매창의 등급·개별 확률도 실제 가중치에서 계산한다. 전설 크리쳐 0종의 구간은 기존대로 선택권으로 보관한다.

## 전투 수치와 단위

- `Creature.<영문종ID>.PhysicalAttack`과 `MagicAttack`은 독립적이다. 기본 공격은 물리, 스킬은 마법 공격력을 사용한다.
- `...Multiplier`, `...Ratio`, `SkillDamage`는 **1=100%**, `0.25=25%`다. `...Chance`, `...Bonus`, `GuardDamageReduction` 등 능력치 퍼센트는 기존 DS 단위 **25=25%**다. 행 설명을 확인한다.
- `...Seconds`, `...Cooldown`, `BurnInterval`은 초다. `AttackSpeed`는 초당 기본 공격 횟수, 거리/속도는 월드 단위다.
- `Battle.BurnSeconds/Damage/Interval`은 불씨·부싯돌의 공유 연소, `Battle.SlowSeconds/SlowMoveRatio`는 빙결창·광대의 공유 감속이다.
- 수정 효과 설명, 발동 횟수 표시, 스킬 설명·기력 비용, 보관함의 최종 능력치·진화 미리보기도 실제 설정으로 구성한다.
- `Evolution.RankGrowth`는 랭크 1/2/3의 기본 체력·물공·마공 배율이다. A/B의 배율·추가 보너스에 이어 랭크 3 역할을 적용한다. 확률/기력 등 직접 대입하는 역할 능력치는 해당 분기의 최종 값이다.
- 생명력은 기존처럼 반올림하고 스테이지 배율 적용 시 최소 1이다. 범위를 벗어나는 최종 성장 조합은 로드 시 오류로 처리한다.

## 반영 시점과 오류

**Play 종료 → CSV 저장 → Unity 임포트 완료 → 다시 Play**하면 반영된다. 새 타이틀/런에서 숫자 시트를 읽고 불변 설정을 만든다. 런의 구매·장착·진화와 전투 양쪽 크리쳐는 같은 설정을 유지한다. 진행 중인 모험의 상점 재진입만으로 가격이나 스킬 수치가 바뀌지 않는다. 타이틀로 돌아와 새 모험을 시작하면 변경된 시트를 읽는다. 배포 Player는 빌드에 포함된 시트를 사용하므로 변경 후 다시 빌드한다.

`Resources/GachaBalance.asset`은 5개 CSV 참조만 가진 연결 에셋이며 숫자의 복제본은 저장하지 않는다. 세 씬의 MainFlow와 단위 테스트 기본 설정이 같은 CSV를 읽는다. 누락·중복/알 수 없는 키·잘못된 배열 길이·소수 금화·음수 비용·NaN/무한대·0 주기 연소는 명확한 Console 오류와 함께 초기화를 중단한다. 코드의 숨겨진 기본값으로 대체하지 않는다.

## 검증 기록

2026-11-25, Unity **6000.0.58f2**에서 확인했다. 사용자 에디터는 유지하고 별도 프로젝트 복사본에서 실행했다.

- **EditMode 201/201 통과:** 기본값 유지, 시작 금화·가격·10단계 보상, 중복 지급 방지, 무료 구매·0 보상, 등급 가중치, 크리쳐·스킬·각인·진화 수치의 실제 소비, 잘못된 시트 거부를 검사했다.
- **실제 씬 PlayMode 29/29 통과:** 편집한 설정으로 타이틀·머신 가격·구매·확률 안내·단계별 결과와 정산·진화 미리보기·스킬 기력 표시를 확인했다.
- **Windows x64 Development Player 빌드·실행 통과:** 검증 복사본의 CSV 자체를 시작 금화 1234, 가격 137/29, 1~10단계 보상 17/34/…/170, 크리쳐 일반 100%·수정 전설 100%, 잿불여우 생명력 120·물공 24·마공 32·스킬 배율 1.5, 스킬 기력 비용 40으로 수정했다. 실제 구매 버튼·스킬 피해 48/기력 40 소모·10단계 정산·완료 후 새 런 1234 G 초기화와 화면 렌더를 확인했다. 단계 전환을 위한 기절은 fixture이며 OS 키보드·마우스 주입 검증은 아니다.
- **최종 소스 재검증:** Player 전용 임시 부팅 코드를 제거하고 기본 CSV로 복원한 복사본에서도 EditMode 201개와 PlayMode 29개가 모두 통과했다. 원본 시트의 기본값은 변경하지 않았다. 로그·XML·편집값 기록·화면 캡처·소스 SHA-256 목록은 Git에서 제외되는 `Logs/BalanceQA/`에 보관한다. 기존 Player 종료 시 ComputeBuffer 정리 경고 외에 실행 중 오류나 검증 실패는 없었다.
