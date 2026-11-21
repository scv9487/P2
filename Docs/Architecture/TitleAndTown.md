# 타이틀·마을 구조

기획 기준은 [핵심 방향](../Planning/00_GameDirection.md)과 [타이틀·마을 기획](../Planning/02_TitleAndTown.md)이다.

## 실행과 수명

- 시작 씬: `Assets/0_Project/1_Title/0_Scenes/1_Title.unity`.
- 마을: `Assets/0_Project/2_Town/0_Scenes/2_Town.unity`. 타이틀과 마을 모두 직접 Play할 수 있다.
- 기본 Build Settings 순서는 타이틀, 마을, 던전, 기존 기반 테스트 씬이다. 추가된 던전 입구/선택 팝업/씬 연결은 [던전 구조](Dungeon.md)를 따른다.
- 각 씬의 MainFlow가 자신의 입력, EventQueue, Handler, 상태, Presenter와 View를 조립한다. 씬 전환은 `LoadSceneMode.Single`이며 이전 씬의 MainFlow와 UI를 해제한다.
- SceneLoader는 목적지 등록 여부를 검사하고 Unity 로드를 시작한다. 이전 씬에서 돌아올 코루틴의 완료를 새 씬 초기화 조건으로 삼지 않는다. 새 MainFlow.Awake가 자체 초기화를 수행한다.
- 전환 요청을 받은 MainFlow는 입력을 잠그고 대기 입력·요청을 비운다. 시작 실패는 잠금을 풀고 상태 문구로 안내한다. 새 씬은 새로운 입력과 화면 상태로 시작한다.

## 요청부터 화면까지

```text
TitleMenuView / TownView의 버튼
→ UIInputStream.AddInput(action, targetId, locationId)
→ InputStream (입력 시점의 문자열 값을 보관)
→ MainFlow.Update
   InputDispatcher / InputBinding → TownActionRequested
   EventQueue.Update → TownEventHandler.OnEvent
     타이틀: MainFlow의 씬 전환/종료 조율
     마을: 실행 시점의 입력 잠금/모달/장소 검사 → TownDataContainer
   TitleMenuView.Render 또는 TownViewPresenter.Refresh → TownView.Render
```

- 타이틀/마을 요청은 `TownActionRequested`, 기존 테스트 요청은 `UIActionRequested`/`SetLabelRequested`로 분리한다. Reflection 등록에서 한 요청 타입에 복수 처리기를 연결하지 않는다.
- Handler가 씬 전환 조율을 직접 호출하는 것은 같은 EventQueue 실행 단계 안의 기존 IZ 경로를 따른다. 동일 중앙 큐로 재등록하지 않는다.
- 장소와 인물의 정의는 TownSheet가 소유하며 변경 가능한 화면 상태는 TownDataContainer가 소유한다. 읽기 인터페이스는 ITownDataReader다.
- 장소 커맨드와 인물 요청에는 발생 당시 장소 ID를 넣는다. 이미 장소가 달라졌거나 세부 팝업이 열렸다면 거절한다. 이름이나 화면 Text를 데이터 식별자로 사용하지 않는다.
- 세부 화면은 커맨드 또는 인물 하나만 연다. Back은 세부 화면을 먼저 닫고 다음 입력에서 장소를 닫는다. 상태가 달라질 때 Revision을 증가시킨다.
- View의 동적 버튼은 시트의 장소/커맨드/방문 인물 목록으로 조립한다. 표시 조립과 입력 기록만 하며 거래·회복·의뢰 처리 결과를 확정하지 않는다.
- 시장과 주점의 거래는 별도 상점창을 연다. 구매·판매의 상태와 저장은 [상점 구조](Shop.md)를 따른다. 아래 마을 초기 구현의 검증 기록과 상점 추가 검증은 구분한다.
- 인물 카드는 현재 [공통 상호작용](CharactersAndParty.md)을 열어 대화·영입을 제공한다. 하단에는 활성 파티가 표시되고 대화·추방할 수 있다. 초기 네 인물의 단순 대화창 검증 기록은 현재 인물 6명·관계·파티 검증과 구분한다.
- 길드의 의뢰 수주·완료는 현재 [의뢰 게시판](Quests.md)을 열어 네 카드의 수락·목표·보고를 처리한다. 아래 초기 안내 팝업 검증과 의뢰 구현 이후의 검증은 구분한다.
- InputProfile은 FoundationTest/Title/Town으로 분리한다. 타이틀 Enter는 기본 진입이며 버튼이 선택되어 있으면 EventSystem Submit에 맡긴다. 마을 Esc는 Back이다.

## 시트와 이미지

시트는 `Assets/1_InternalTools/Util/DataSheetLoader/Table/`에 있다.

| 파일 | 역할 |
| --- | --- |
| TownLocationTable.csv | 장소 ID/이름/부제/설명, 좌측 커맨드 순서, 상단 방문 인물 순서, 배경 번호 |
| TownCommandTable.csv | 지원 커맨드 ID와 이름, 세부 팝업의 제목/본문 |
| TownCharacterTable.csv | 인물 ID/이름/역할, 초상화 번호, 첫 인사/대사 |
| TownTextTable.csv | 타이틀·마을·공통 UI 문자열 |

여러 참조 ID는 `;`로 구분한다. CSV는 따옴표로 감싼 쉼표/줄바꿈/겹따옴표를 지원한다. TownSheet에서 헤더, 중복 키, 지원 커맨드, 참조 존재, 필수 문구, 이미지 번호를 검사한다. 잘못된 시트는 초기화 오류로 표시한다. 변경 내용은 다음 씬 초기화 시 읽는다.

TownContent ScriptableObject는 네 CSV와 마을 배경·장소 배경·초상화 Sprite를 연결한다. `Load()`에서 실제 이미지 배열과 시트 인덱스를 함께 검사한다. 장소/인물 목록 순서와 그림의 Sprite 번호는 독립적으로 조정할 수 있다.

이미지는 `Assets/0_Project/2_Town/4_Art/`에 있다. 원본 그림과 생성 프롬프트, Sprite 분할 정보는 [그림 기록](../Art/TownAssets.md)에 있다. 문서 자체는 빌드에 포함하지 않는다.

## DS/IZ 근거와 축소

- DS `1_Title/1_Scripts/UI/TitleMenu/TitleMenuView.cs` 및 `999_Refactoring/.../InputModule/Command/InputEventFactory.cs`: 버튼이 UI 의도를 기록하고 입력 계층이 요청을 생성하는 책임을 유지했다. DS 캠페인 생성 화면과 게임별 상태는 가져오지 않았다.
- IZ `TitleMenuView.cs`, `MainFlow.BeginSceneTransition`, `SceneLoader.cs`: 입력 잠금, 보류 요청 정리, 씬 로드 사전 검사와 오류 경계를 참고했다. P2는 직접 Play 가능한 씬별 MainFlow를 선택하여 Entry/Loading/DontDestroyOnLoad가 필요 없다.
- P2 기존 EventQueue/DI/입력 스트림을 확장했다. 별도 이벤트 버스, 시뮬레이션, Observer, 경제/퀘스트 프레임워크는 추가하지 않았다.
- 기존 `2_Test` 씬과 Foundation 저장 형식은 유지한다. 마을의 장소 선택·열린 팝업은 저장하지 않는다.
- 이후 추가한 금화·소지품은 [모험 진행 저장](LootAndSettlement.md)을 사용한다. 마을 씬 진입 때 읽어 전경의 보유 내역에 표시하며, 장소 선택 상태와 분리한다.

## 검증

Unity 6000.0.58f2에서 2026-11-21에 확인했다.

- EditMode **48/48 통과**: 기존 기반 기능 37개와 마을 시트·요청 처리·화면 상태 11개. 잘못된 참조, 다른 장소에서 온 요청, 모달 뒤 입력, 뒤로 이동을 포함한다.
- PlayMode **7/7 통과**: 기존 기반 씬 4개와 실제 타이틀/마을 씬 3개. 타이틀 → 마을 → 타이틀 왕복, 중복 시작 입력, 네 장소의 커맨드/방문 인물 구성, 세부 팝업과 대화 닫기, 씬 교체 후 구독·상태 정리를 확인했다.
- 같은 PlayMode 테스트에서 현재 CSV 문구의 필요 높이와 실제 Text 영역을 비교했다. 타이틀·마을 전경·각 장소·커맨드 팝업·대화 화면의 글자 잘림 검사와 주요 제목 너비 검사가 통과했다.
- Windows x64 Development Player 빌드와 실행 검증이 통과했다. 사용자의 허용에 따라 Windows 잠금을 유지한 상태에서 임시 검증 코드를 포함한 별도 Player를 실행했다. 타이틀 왕복, 네 장소의 모든 커맨드, 네 인물의 대화, 닫기와 씬 교체 후 초기 상태를 확인했다.
- Player 검증은 화면 좌표에 대한 `EventSystem.RaycastAll`의 **최상단 hit만** 사용해 pointer down/up/click을 전달했다. 팝업 뒤의 나가기 버튼은 raycast로 선택되지 않고 상태도 변하지 않았으며, 팝업의 선택된 닫기 버튼은 EventSystem Submit으로 닫혔다. 검증용 코드에서만 각 입력 직전 MainFlow에 포커스를 부여했다. 이는 합성 Unity UI 입력 검증이며 물리 마우스·키보드 입력이나 `Input.GetKeyDown` 경로 검증은 아니다.
- Player가 실제 렌더링한 PNG **21장**을 직접 확인했다. 1280×800에서 타이틀·마을·네 장소·여섯 커맨드 팝업·네 인물 대화를 확인했다. 추가로 1920×1080의 마을/주점, 1024×768의 마을/길드 팝업/타이틀을 확인했다. 글자·얼굴 잘림, 이미지 사분면 섞임, 버튼 가림 문제는 발견되지 않았다.
- 일회성 씬 생성 도구와 Player 검증 코드·임시 빌드는 제거하고 생성된 씬/콘텐츠/이미지와 회귀 테스트를 유지한다.

로컬 결과: `Logs/Town-EditMode.xml`, `Logs/Town-PlayMode.xml`, `Logs/Town-Build.log`, `Logs/Town-PlayerQA-Build.log`, `Logs/TownPlayerQA-Player.log`, `Logs/TownPlayerQA/`의 PNG·실제 해상도 기록·성공 결과. 로그와 검증 캡처는 Git에 포함하지 않는다. 테스트는 실제 거래·휴식·의뢰 결과나 던전 진행을 검증하지 않는다. 해당 기능은 이번 단계의 구현 범위 밖이다.
