# Language Analyzer Patterns — Regex Catalog & Limits

> CodeScan의 정규식 전략(`Services/Analyzers/Languages/`)이 9개 언어 각각에 대해
> **무엇을 잡고 무엇을 놓치는지**의 단일 출처. 신규 회귀가 발견되면 이 문서를 업데이트한다.

## 분석기 ↔ 언어 매핑

| 분석기 | 확장자 | 메서드 추출 | 의존성 엣지 추출 |
|--------|--------|------------|---------------|
| `CSharpAnalyzer` | `.cs` | ✅ Allman/K&R 양쪽 | ✅ inherits/creates/uses_type/imports |
| `JavaAnalyzer` | `.java` | ✅ K&R | ✅ 동일 |
| `KotlinAnalyzer` | `.kt`, `.kts` | ✅ | ✅ 생성자 파라미터 인식, `Type(args)` creates |
| `JsTsAnalyzer` | `.js`, `.jsx`, `.ts`, `.tsx` | ✅ 4 패턴(class member/function/arrow/export) | ✅ super/return/throw 등 예약어 제외 |
| `PhpAnalyzer` | `.php` | ✅ | ✅ |
| `PythonAnalyzer` | `.py` | ✅ 들여쓰기 기반 | ✅ |
| `GoAnalyzer` | `.go` | ❌ (그래프 전용) | ✅ embedded struct → inherits |
| `RustAnalyzer` | `.rs` | ❌ (그래프 전용) | ✅ `impl Trait for Struct`, 그룹 import 분해 |
| `CppAnalyzer` | `.c`, `.cc`, `.cpp`, `.cxx`, `.h`, `.hpp`, `.hh`, `.hxx` | ❌ (그래프 전용) | ✅ `make_unique`/`make_shared`/`new` |

공통 헬퍼: `Services/Analyzers/AnalyzerHelpers.cs`
- `WalkBraceLanguage` — 클래스 brace 추적 (Allman/K&R 모두 지원, pending-class-on-brace 패턴)
- `NormalizeTypeName` — `<...>`, `(...)`, `?`, `[...]` 제거
- `IsLikelyType` — 대문자 시작 또는 `I` 접두 + 대문자 (인터페이스 추정)
- `SplitTypeList` — `,` 와 `:` 로 분리

## 알려진 한계 (검증된 회귀 fixture로 봉인됨)

### 회귀 ↔ Fixture 매핑

이 표의 각 항목은 `Tests/LanguageAnalyzerTests.cs` 또는 `TestSample/<lang>/` 에 의해
회귀로부터 보호된다. 신규 회귀 발견 시 fixture 추가 후 이 표 업데이트.

| 언어 | 회귀 | 해결 | Fixture |
|------|------|------|---------|
| C#/PHP | Allman `class X` 다음 줄 `{` → currentClass 즉시 pop | `WalkBraceLanguage`에 pending-class-on-brace | `LanguageAnalyzerTests.CSharp_AllmanStyle_BindsMethodsToClass` |
| JS/TS | `super(...)` 가 메서드로 잡힘 | `ReservedCall` 정규식 (super/return/throw/...) | `JsTs_SuperInsideConstructor_NotMethod` |
| Kotlin | 생성자 파라미터의 `String)` 가 base type으로 잡힘 | `(?:\s*\([^)]*\))?` 로 constructor params 선소비 | `Kotlin_PrimaryConstructor_DoesNotPolluteInheritance` |
| Kotlin | `new` 없는 `Type(args)` 호출이 `creates`로 안 잡힘 | `CreateCall` 정규식 + `fun`/`class` 라인 스킵 | `Kotlin_CreatesCall_DetectedWithoutNewKeyword` |
| Go | embedded struct(`type X struct { Base }`)가 inherits로 안 잡힘 | struct body 진입 시 `EmbeddedField` 매처 | `Go_EmbeddedStruct_BecomesInheritsEdge` |
| Go | `&Type{}` 컴포지트 리터럴 self-reference | currentType 같음 필터 | `Go_DoesNotEmitSelfReferenceOnCompositeLiteral` |
| Rust | `impl Trait for Struct` 미인식 | 별도 `ImplFor` regex | `Rust_ImplTraitForStruct_BecomesInheritsEdge` |
| Rust | `use crate::x::{A, B};` 끝 잘림 | 그룹 import 분해 | `Rust_GroupImport_ExpandsToOneEdgePerItem` |
| Rust | `Box`/`Vec`/`Self` 표준 wrapper 노이즈 | `StdLibWrappers` 필터 | (수동 매트릭스로 확인) |
| C++ | `std::make_unique<T>()` 미인식 | `CreateCall`에 세 alt 추가 | `Cpp_MakeUnique_CountsAsCreate` |

### 알려졌으나 미해결 (후속 작업 후보)

| 언어 | 한계 | 영향 | 해결 후보 |
|------|------|------|----------|
| Kotlin | `class X private constructor(...) : Y` 인식 실패 | inherits 누락 | regex에 modifier 시퀀스 흡수 (`(?:\s+\w+)*`) |
| Kotlin | `class X : Y(args)` 의 부모 생성자 호출이 `Y -[creates]-> X`로 오인 | 잘못된 creates 엣지 | 첫 `:` 이후 첫 `Type(args)`는 creates에서 제외 |
| 모든 언어 | 툴킷 패턴 (e.g. `ActorOf<T>`, `Props.create(T.class)`, `context.spawn(...)`) 미인식 | 부모-자식 액터 관계 손실 | 의미 분석 도커로 해결 ([[semantic-analyzer-docker]]) |

## 엣지 추출 동작 (언어별 차이)

### 공통 5종 엣지

| 엣지 | 의미 |
|------|------|
| `file -[contains]-> class` | 파일 안 클래스 |
| `class -[defines]-> method` | 메서드 정의 |
| `file -[imports]-> module` | `using`/`import`/`use`/`#include` |
| `class -[inherits_or_implements]-> type` | 상속/구현 |
| `class -[creates]-> type` | 생성자 호출 (`new` 또는 언어별 변형) |
| `class -[uses_type]-> type` | 타입 어노테이션, 필드, 매개변수 |

### 언어별 `creates` 패턴 변형

| 언어 | 인식 패턴 | 비고 |
|------|----------|------|
| C# | `new Type(...)` | 표준 |
| Java | `new Type(...)` | 표준 |
| Kotlin | `Type(args)` (no `new` keyword) | `class`/`fun` 선언 라인은 스킵 |
| JS/TS | `new Type(...)` | 표준 |
| PHP | `new Type(...)` | 표준 |
| Python | `Type(...)` 호출 형태 | 함수 호출과 구분 어려움 |
| Go | `new(Type)`, `make(Type)`, `&Type{}` | 컴포지트 리터럴 |
| Rust | `Type::new(...)`, `Type::default(...)`, `Type { ... }` | stdlib wrapper 필터 |
| C++ | `new Type(...)`, `make_unique<T>()`, `make_shared<T>()` | 세 alt regex |

## 신규 언어 분석기 추가 절차

1. `Services/Analyzers/Languages/<Lang>Analyzer.cs` 작성 (`ILanguageAnalyzer` 구현)
2. `LanguageAnalyzerRegistry._all` 배열에 추가
3. `TestSample/<lang>/` fixture 추가 (Hello World OOP — 다른 언어와 동일 도메인)
4. `Tests/LanguageAnalyzerTests.cs` 에 신규 언어 회귀 케이스 4종 이상 추가
5. 이 문서에 회귀 매트릭스 업데이트

## 측정된 정확도 (2026-05-18 기준)

10개 언어 fixture 대상:
- `inherits_or_implements`: **10/10** ✅
- `creates`: 9.5/10 (Go는 Go 컨벤션상 `&Type{}` 직접 생성 패턴이 없어 0 — false negative 아님)
- `imports`: **10/10** ✅
- 테스트: 108 통과 (23 신규 회귀 케이스 포함)

## 관련

- 코드: `Services/Analyzers/**`
- Fixture: `TestSample/<lang>/`
- 회귀 테스트: `Tests/LanguageAnalyzerTests.cs`
- 의미 분석 후속: [[semantic-analyzer-docker]]
- 액터 패턴 한계: [[actor-model-cross-toolkit]]
