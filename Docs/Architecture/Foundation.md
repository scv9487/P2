# P2 기반 구조와 코드 읽기

이 문서는 초기 기반 테스트 씬의 구조와 당시 검증 기록이다. 타이틀·가챠샵 추가 이후의 게임 진입과 씬 구성은 [가챠 트레이너즈 구조](GachaTrainers.md)를 참고한다.

## 시작하기

Unity **6000.0.58f2**에서 프로젝트를 열고 `Assets/0_Project/2_Test/0_Scenes/2_Test.unity`를 열어 Play한다. 초기 기반 구축 당시 Build Settings에는 이 씬 하나를 등록했다. 이후 타이틀·가챠샵 씬을 추가하되 기반 회귀 테스트용으로 이 씬을 유지한다. 기존 템플릿 SampleScene은 실행 목록에서 제외한다.

숫자 증가/감소, 이름 입력 후 적용, 저장, 변경/초기화, 불러오기 순으로 확인한다. F1/F2/F5/F9/Escape 단축키와 모달 팝업도 사용할 수 있다. 자세한 규칙과 구현 기본값은 [기획서](../Planning/01_Foundation.md)에 있다.

## 실행 경로

```text
MainFlow.Awake
  CSV TextAsset → GameSystemTable → GameSystemSheet
  FoundationDataContainer 생성 → DI.PubSub
  InputStream / UIInputStream / InputFilter 생성
  FoundationEventHandler 등록 → EventQueue 요청 타입 연결
  InputDispatcher / FoundationViewPresenter 생성 → 버튼 Bind

버튼 callback → UIInputStream → InputStream
MainFlow.Update
  InputDispatcher: 키 수집 → InputBinding → EventQueue.AddEvent
  EventQueue.Update: FIFO → FoundationEventHandler.OnEvent
    InputFilter 최종 검사 → DataContainer 변경 또는 FoundationSave
  FoundationViewPresenter.Refresh: Reader 조회 → FoundationView.Render

MainFlow.OnDestroy
  버튼 listener / 보류 입력 / 보류 요청 / DI 등록 해제
```

1. `Assets/0_Project/999_Refactoring/1_Scripts/MainFlow/MainFlow.cs`: 씬 직렬화 연결, 생성·주입·실행 순서·수명을 읽는다.
2. 같은 폴더의 `Input/UIInputStream.cs`, `InputBinding.cs`, `InputDispatcher.cs`: 버튼과 키가 데이터 대신 요청을 만드는 경로다.
3. `Assets/0_Project/2_Test/1_Scripts/MainFlow/Event/`: 요청 DTO, 타입별 처리기 등록, 동기 큐, 요청 실행이다. 실행 도중 같은 큐 등록과 Update 재진입은 예외로 차단한다. 미등록 요청도 조용히 버리지 않는다.
4. `.../MainFlow/TestData/FoundationDataContainer.cs`: 숫자·이름·성공한 변경 횟수의 원본, 경계 검사, 초기화/복원이다. 상태 변경 실패는 숫자/이름/횟수를 일부만 변경하지 않는다.
5. `.../TestData/FoundationSaveData.cs` → `Assets/1_InternalTools/SW/SaveLoadHelper.cs`: 게임별 DTO 형식/범위 검사와 공통 파일 처리를 구분한다.
6. `Assets/0_Project/99_Shares/1_Scripts/View/FoundationViewPresenter.cs` → `UI/Foundation/FoundationView.cs`: Reader 조회, 표시, 편집 초안과 모달 입력 처리를 읽는다. 불러오기/초기화 및 이름 적용 성공 후 초안 갱신도 다음 View 단계에 반영한다.

## 이식 근거와 변경

DS 필수 계약은 `../DS/Docs/Architecture/{Architecture,LayerDetails,ObserverSystem}.md`에서 확인했다. IZ의 축소 구현과 `TechnicalStandards.md`, `FeatureDevelopmentGuide.md`를 대조했다.

| 기반 | DS 근거 | P2 적용 |
| --- | --- | --- |
| 실행 순서/수명 | `Assets/0_Project/999_Refactoring/1_Scripts/MainFlow/MainFlow.cs`의 InitCore/Update | IZ의 축소 조립을 참고한 단일 씬 MainFlow. 지속 씬 매니저가 필요 없어 DontDestroyOnLoad/SceneLoader 제외 |
| 입력 | 같은 폴더 `Input/InputDispatcher.cs`, `InputStream.cs`, `UIInputStream.cs`, `InputBinding.cs`, `InputFilter.cs` | 경로와 Refactor.Main.InputModule 유지. 지도/마을/전투/시간 입력 제거. IZ HashSet 대신 List로 입력 순서와 복수 입력 보존 |
| 요청 큐/처리기 | `Assets/0_Project/2_Campaign/1_Scripts/MainFlow/Event/EventQueue.cs`, `EventHandler.cs`, `EventHandlers/CampaignEventHandler.cs` | 타입별 Reflection 등록과 동기 FIFO. `2_Campaign`을 `2_Test`로 바꾸고 FoundationEventHandler 사용. 비동기/맵 라우팅 제외 |
| 상태/화면 | DS CampaignEventHandler의 ClanGoldRequested, CampaignInfoPresenter의 Reader 조회; IZ FoundationDataContainer/FoundationView/Presenter | 테스트 숫자/이름만 사용. View는 게임 데이터 쓰기 권한 없음 |
| DI | DS `999_Refactoring/1_Scripts/Util/DependencyInjection`; IZ DI/DIAttributes | IZ 구체 타입 인스턴스 필드 주입 코드 재사용. 시작 초기화/종료 해제, Domain Reload 비활성 재실행 대비 정적 초기화 |
| 시트 | DS/IZ `Assets/1_InternalTools/Util/DataSheetLoader/Table/GameSystemTable.cs`, `999_Refactoring/1_Scripts/SheetData/TableData/GameSystemSheet.cs` | IZ CSV 파서 재사용, P2에서 소비하는 세 가지 키만 유지. 씬 TextAsset 참조로 Player에 포함 |
| 파일 저장 | DS `Assets/1_InternalTools/SW/SaveLoadHelper.cs`의 임시 파일→검사→교체/복원; IZ 축소 SaveLoadHelper | JsonUtility 기반 공통 유틸 유지. 누락 필드 감지를 위해 sentinel DTO에 FromJsonOverwrite 사용. Format=P2.Foundation/Version=1 |
| 에셋 | IZ MaruBuri-Regular.ttf와 meta | 한글 uGUI용 폰트. 실제 Canvas/버튼/InputField/EventSystem을 씬에 직렬화 |

DS 전체 패키지, Addressables, Steam, 상용 게임 UI/프리팹/전투/캠페인·맵 데이터는 가져오지 않았다. P2의 2D URP·Input System 패키지와 `activeInputHandler=2 (Both)`를 유지해 IZ와 같은 legacy Input/uGUI 조합을 사용한다. 프로젝트 asmdef는 `P2.Foundation`, uGUI 참조는 `Unity.ugui`다.

## 저장과 실패

- `Application.persistentDataPath/P2Foundation/save.json` 단일 슬롯. P2 제품명은 유지한다. 실제 경로를 테스트 화면에 표시한다.
- `Format`, `Version`, `Counter`, `ProcessedRequestCount`, `Label`을 저장한다. 팝업, 안내 문구, 편집 초안은 저장하지 않는다.
- CSV의 현재 범위에 맞지 않는 저장은 거절한다. 숫자 필드 누락도 0으로 간주하지 않는다. 실패 결과는 Missing/Corrupt/UnsupportedVersion/IOError로 구분하고 현재 상태를 유지한다.
- 임시 파일을 UTF-8로 쓴 뒤 파싱 확인, File.Replace 또는 이동/복원 fallback을 사용한다. 모든 플랫폼의 강제 종료나 동시 저장을 보장하는 범용 트랜잭션은 아니다. 현재 저장은 Unity 메인 스레드에서 요청 순서대로 실행한다.

## 검증 기록

2026-11-21, Unity 6000.0.58f2에서 검증했다.

- **EditMode 37/37 통과:** 실제 요청 경로의 지연/FIFO/중복 입력, 저장→초기화→불러오기, 덮어쓰기, 필수 필드 누락/손상/버전/범위/I/O 실패 시 상태 유지, 숫자/문자 경계와 오버플로, 큐 재진입 금지, DI 교체/해제.
- **PlayMode 4/4 통과:** 실제 `2_Test` 씬의 직렬화 참조, 버튼 callback 이후 프레임 반영/표시, 문자 초안/적용/초기화, 모달 차단과 닫기 후 FIFO, 씬 재진입 시 상태/리스너 정리.
- **Windows x64 Mono Development Player 빌드/실행:** 실제 키보드 F1/F5/F9/Escape, 팝업 닫기 버튼, 한글 입력·적용, 팝업 중 및 문자 편집 중 단축키 차단, 저장/복원, 화면 표시를 확인했다. 처음 발견한 큰 숫자 잘림은 폰트 높이에 맞춰 씬의 Text 영역을 수정한 뒤 재확인했다. Player 로그에 런타임 오류가 없었다.
- **정리:** 일회성 씬 생성기/Editor 어셈블리/검증용 실행 파일을 제거했다. 실제 테스트 씬, 런타임 코드, 재사용할 회귀 테스트만 남긴다. 로그와 테스트 XML은 Git에서 제외되는 `Logs`에 있다.
- **미확인:** IL2CPP, Windows 이외 플랫폼, 강제 종료 중 디스크 장애. 이 범위까지 검증한 것으로 간주하지 않는다.

Unity Test Runner의 EditMode/PlayMode에서 `Assets/Tests`의 검증을 다시 실행할 수 있다. 저장 테스트는 GUID 임시 디렉터리를 사용하며 PlayMode 테스트는 실제 사용자 저장 파일을 쓰지 않는다.
