---
date: 2026-05-18T00:42+09:00
agent: test-sentinel
type: improvement
mode: log-eval
trigger: "regex 부터 강화 + 구조개선 (언어별 분기)"
---

# 언어별 분석기 분리 + 회귀 8건 동시 해결 (구조 개선)

## 실행 요약

[[2026-05-18-00-16-multilang-sample-e2e]]에서 식별한 9건의 회귀를
**언어별 패턴 매칭으로 디스패치하는 구조 개선**과 함께 일괄 해결.

기존 구조:
- `SourceAnalyzer.ExtractWithBraces()` — C#/Java/Kotlin/PHP 4언어가 공용
- `SourceGraphAnalyzer.AnalyzeBraceLanguage()` — 9언어가 공용
- 한 언어 패턴을 고치면 다른 언어가 흔들림 (회귀 양산 구조)

신규 구조:
```
Services/Analyzers/
├── ILanguageAnalyzer.cs          ← Methods + Dependencies 통합 인터페이스
├── LanguageAnalyzerRegistry.cs   ← 확장자→analyzer 매핑 (switch 패턴)
├── AnalyzerHelpers.cs            ← brace-walk, normalize, IsLikelyType (공통)
└── Languages/                     ← 언어별 9개 클래스, 각자 자기 regex 보유
    ├── CSharpAnalyzer.cs
    ├── JavaAnalyzer.cs
    ├── KotlinAnalyzer.cs
    ├── JsTsAnalyzer.cs
    ├── PhpAnalyzer.cs
    ├── PythonAnalyzer.cs
    ├── GoAnalyzer.cs
    ├── RustAnalyzer.cs
    └── CppAnalyzer.cs
```

기존 `SourceAnalyzer` / `HybridSourceGraphAnalyzer`는 facade로 축소 —
외부 API 보존, 내부적으로 `LanguageAnalyzerRegistry`로 위임만 함.
C# 모던 패턴 활용: file-scoped namespace, pattern matching (`is {Success: true}`),
expression-bodied methods, collection expressions `[]`, switch expression.

## 결과 매트릭스 — Before vs After

### `inherits_or_implements` (3 Speaker → Person)

| 언어 | Before | After | 변화 |
|------|--------|-------|------|
| C# | ✅ 3/3 | ✅ 3/3 | — |
| Java | ✅ 3/3 | ✅ 3/3 | — |
| Kotlin | ⚠️ 3/3 + `String)`, `String` 노이즈 | ✅ 3/3 무노이즈 | **개선** |
| JS | ✅ 3/3 | ✅ 3/3 | — |
| TS | ✅ 3/3 | ✅ 3/3 | — |
| PHP | ✅ 3/3 | ✅ 3/3 | — |
| Python | ✅ 3/3 + Person→ABC | 동일 | — |
| **Go** | ❌ 0/3 | ✅ 3/3 (Speaker→Base) | **해결** |
| **Rust** | ❌ 0/3 | ✅ 3/3 (Speaker→Person) | **해결** |
| C++ | ✅ 3/3 | ✅ 3/3 | — |

### `creates` (Main → 3 Speaker + World)

| 언어 | Before | After | 변화 |
|------|--------|-------|------|
| C# | ✅ 4/4 | ✅ 4/4 | — |
| Java | ✅ 4/4 + ArrayList | ✅ 4/4 + ArrayList | — |
| **Kotlin** | ❌ 0/4 | ✅ 4/4 | **해결** |
| JS | ✅ 4/4 + Error 노이즈 | 동일 | — |
| TS | ✅ 4/4 | ✅ 4/4 | — |
| PHP | ✅ 4/4 | ✅ 4/4 | — |
| Python | ✅ 4/4 | ✅ 4/4 | — |
| **Go** | ⚠️ 5+ self-ref 노이즈 | ✅ 노이즈 0 (Go 컨벤션상 `&Type{}` 직접 생성 없음) | **해결** |
| **Rust** | ⚠️ + Vec/Box 노이즈 | ✅ 4/4 (main.rs) + 3 PersonBase, 표준 wrapper 필터 | **해결** |
| **C++** | ❌ 0/4 | ✅ 3/4 (`make_unique` 인식, World는 stack alloc) | **해결** |

### `imports`

| 언어 | Before | After |
|------|--------|-------|
| Rust | ⚠️ `crate::person::` 끝 잘림 | ✅ `crate::person::Person`, `crate::person::PersonBase` 둘 다 |
| 기타 9개 | ✅ | 동일 |

### Method 추출 (SourceAnalyzer)

| 언어 | Before | After |
|------|--------|-------|
| JS | 14 methods (super, constructor 중복) | 11 methods (super 노이즈 제거) |
| TS | 13 methods | 10 methods (super 노이즈 제거) |
| C#, PHP | `[Global]` 회귀 — 이미 K&R 케이스는 동작했음 | Allman 케이스도 동작 (테스트로 회귀 방지) |

## 적용된 수정 (언어별)

| Analyzer | 수정 |
|----------|------|
| `CSharpAnalyzer` | `WalkBraceLanguage` 헬퍼로 Allman/K&R 동시 지원 (pending-class-on-brace) |
| `JavaAnalyzer` | 분리만 (기존 동작 유지) |
| `KotlinAnalyzer` | (1) 생성자 파라미터 `(...)`를 inheritance `:`보다 먼저 소비 (2) `Type(args)` creates 패턴 (Kotlin에 `new` 없음) (3) `fun`/`class` 선언 라인에서 creates/uses_type 스킵 |
| `JsTsAnalyzer` | `ReservedCall` 패턴으로 `super`, `return`, `throw`, `yield`, `new`, `delete`, `typeof`, `void`, `await`, `in`, `of` 제외 |
| `PhpAnalyzer` | Allman 동일 (공유 헬퍼 사용) |
| `PythonAnalyzer` | 분리만 (기존 동작 유지) |
| `GoAnalyzer` | (1) 별도 워크플로우 (brace 추적) (2) struct body 진입 시 embedded field → inherits_or_implements (3) self-ref 필터 |
| `RustAnalyzer` | (1) `impl Trait for Struct` 패턴 추가 (2) `use prefix::{A, B};` group import 분해 (3) `Box`/`Vec`/`Self`/기타 stdlib wrapper 필터 |
| `CppAnalyzer` | `make_unique<T>` / `make_shared<T>` 캡처 (qualified/unqualified 모두) |

## 테스트

- 기존 85 테스트 전부 통과 (회귀 0)
- 신규 23개 회귀 테스트 추가 (`Tests/LanguageAnalyzerTests.cs`) — 모두 통과
- 합계 **108/108 통과**

신규 테스트 케이스:
- C# Allman/K&R 두 스타일 모두 메서드를 클래스에 바인딩
- JS/TS `super()`, `return`, `throw`가 메서드로 안 잡힘
- Kotlin 생성자 파라미터가 base type으로 안 새 나감
- Kotlin `new` 없는 `Type(args)` 호출이 creates로 잡힘
- Go embedded struct → inherits_or_implements
- Go composite literal의 self-reference 제거
- Rust `impl Trait for Struct` → inherits_or_implements
- Rust group import가 각 항목으로 분해
- C++ `make_unique` / `make_shared` / `new` 세 패턴 모두
- Registry: 12개 확장자가 정확한 언어로 해소

## 평가 (test-sentinel + regex-safety-guard 3축)

| 축 | Before | After |
|----|--------|-------|
| 빌드/테스트 통과 | 85 pass | 108 pass (23 신규) |
| 언어 커버리지 | 10/10 | 10/10 |
| 엣지 정확도 | inherits 8/10, creates 7/10, imports 9/10 | **inherits 10/10, creates 9.5/10, imports 10/10** |
| 구조 응집도 | 9언어 공용 함수 — 회귀 양산 | **언어별 1:1 클래스 — 격리됨** |
| AOT 안전성 | 위반 0 | 위반 0 (모든 regex `[GeneratedRegex]`) |

## 다음 단계 — 의미 분석 도커 플랜 (예고)

regex 한계 (예: Go에서 `&Type{}` 직접 생성만 잡히고 `pkg.NewType()` 생성자 함수 호출은 못 잡음)를 극복하기 위한 컴파일러 백엔드 분석.

설계 예정:
- `SemanticProbeStrategy`가 이미 자리는 잡혀 있음 — `harness/knowledge/semantic-analyzer-docker.md` 작성 예정
- 언어별 분석 도커 이미지: Roslyn (C#), JDT/Spoon (Java), tsc (TS), go/packages (Go), rust-analyzer (Rust), Clang LibTooling (C++)
- 호스트는 도커 빌드 산출물(JSON/그래프)을 `~/.codescan/semantic/` 에 캐싱하고 CodeScan이 이를 머지

## 관련

- 코드: `Services/Analyzers/**`, `Tests/LanguageAnalyzerTests.cs`
- 이전 로그: [[2026-05-18-00-16-multilang-sample-e2e]]
- 후속: [[semantic-analyzer-docker]] (다음 단계)
