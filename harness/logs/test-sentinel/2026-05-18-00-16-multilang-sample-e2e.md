---
date: 2026-05-18T00:16+09:00
agent: test-sentinel
type: review
mode: log-eval
trigger: "Prompt/05-TestForSampleProject.md 수행"
---

# 10개 언어 샘플 프로젝트 + CodeScan 그래프 의존성 E2E 검증

## 실행 요약

CodeScan의 그래프 의존성 분석(특히 `inherits_or_implements`, `creates`, `imports`)이
정규식 기반 전략으로 얼마나 정확하게 동작하는지 검증하기 위해, 10개 언어 각각에
**동일한 OOP 시나리오**(Person 추상 → En/Ko/Ja Speaker 상속 + World 컬렉션)를
빌드 가능한 형태로 작성하고 codescan으로 스캔하여 엣지를 매트릭스로 정리했다.

## 산출물

```
TestSample/
├── README.md                ← 공통 설계 + 검증 방법
├── Script/docker-build-all.ps1  ← 10개 언어 Docker 빌드 검증 스크립트
├── csharp/      (.NET 9.0 + Dockerfile + .gitignore)
├── java/        (Maven, Java 21)
├── kotlin/      (Gradle KTS, Kotlin 2.0, JDK 21)
├── javascript/  (Node 22, ESM)
├── typescript/  (TS 5.5, strict)
├── php/         (PHP 8.3, namespaces)
├── python/      (Python 3.12, ABC)
├── go/          (Go 1.22, modules)
├── rust/        (Rust 2021)
└── cpp/         (C++20, CMake)
```

모든 샘플은 다음 4개의 공통 클래스를 가진다:
- `Person` — 추상/인터페이스 (Speak/Hello)
- `EnSpeaker` / `KoSpeaker` / `JaSpeaker` — Person 상속/구현
- `World` — Person 컬렉션 보유

CodeScan 등록 결과: Project ID **#7 ~ #16** (`codescan projects`).

## 결과 — 언어 × 엣지 매트릭스

### 기본 인덱싱 (scan)

| 언어 | Files | Methods | Class 바인딩 |
|------|-------|---------|-------------|
| C#         | 9  | 7  | ⚠️ `[Global]` (클래스명 미바인딩) |
| Java       | 9  | 8  | ✅ 정확 |
| Kotlin     | 10 | 8  | ✅ 정확 |
| JavaScript | 9  | 14 | ⚠️ `super()`/`constructor` 가 메서드로 잡힘 |
| TypeScript | 10 | 13 | ⚠️ 동일 |
| PHP        | 9  | 11 | ⚠️ `[Global]` (네임스페이스 사용 시) |
| Python     | 11 | 13 | ✅ 정확 |
| Go         | 9  | 0  | 사양상 그래프 전용 |
| Rust       | 10 | 0  | 사양상 그래프 전용 |
| C/C++      | 14 | 0  | 사양상 그래프 전용 |

### `inherits_or_implements` (3 Speaker → Person)

| 언어 | 검출 | 비고 |
|------|------|------|
| C#         | ✅ 3/3 | + `World -[uses_type]-> Person` |
| Java       | ✅ 3/3 | + 정확 |
| Kotlin     | ⚠️ 3/3 | **노이즈: `String)`, `String`이 base type으로 잘못 인식** (생성자 매개변수 파싱 버그) |
| JavaScript | ✅ 3/3 | 정확 |
| TypeScript | ✅ 3/3 | 정확 |
| PHP        | ✅ 3/3 | 정확 |
| Python     | ✅ 3/3 | + `Person -[inherits_or_implements]-> ABC` (정확) |
| Go         | ❌ 0/3 | **embedded struct(`person.Base`)를 상속 관계로 인식 못함** |
| Rust       | ❌ 0/3 | **`impl Trait for Struct` 패턴 미인식** |
| C/C++      | ✅ 3/3 | 헤더에서 정확하게 추출 |

### `creates` (Main/Program → 3 Speaker + World)

| 언어 | 검출 | 비고 |
|------|------|------|
| C#         | ✅ 4/4 | Program → World/En/Ko/Ja |
| Java       | ✅ 4/4 | + `World -[creates]-> ArrayList` (정확) |
| Kotlin     | ❌ 0/4 | **`EnSpeaker("Alice")` 같은 호출 미인식** (`new` 없는 Kotlin 패턴) |
| JavaScript | ✅ 4/4 | + `Person -[creates]-> Error` (노이즈, throw new Error) |
| TypeScript | ✅ 4/4 | 정확 |
| PHP        | ✅ 4/4 | 정확 |
| Python     | ✅ 4/4 | 정확 |
| Go         | ⚠️ 5+ | **잘못된 자기 참조: `EnSpeaker -[creates]-> EnSpeaker`** (생성자 패턴 오인식) |
| Rust       | ⚠️ 4/4 + 노이즈 | `Vec`, `Box` 등 표준 라이브러리도 함께 검출 |
| C/C++      | ❌ 0/4 | **`std::make_unique<Type>(...)` 패턴 미인식** |

### `imports` (각 파일의 의존 모듈)

| 언어 | 검출 | 비고 |
|------|------|------|
| C#         | ✅ | `using HelloWorld;` 등 정확 |
| Java       | ✅ | 정확 |
| Kotlin     | ✅ | 정확 |
| JavaScript | ✅ | 상대 경로(`../Person.js`) 그대로 |
| TypeScript | ✅ | 정확 |
| PHP        | ✅ | namespace path 정확 |
| Python     | ✅ | + `from abc` 도 정확 |
| Go         | ✅ | 정확 |
| Rust       | ⚠️ | **`use crate::person::{Person, PersonBase};` 의 마지막 토큰을 `crate::person::`(끝 잘림)으로 잡음** |
| C/C++      | ✅ | `#include` 정확 |

## 평가 (test-sentinel 3축 + e2e 보강)

| 축 | 결과 | 평가 |
|----|------|------|
| 빌드/테스트 통과 | 코드스캔 자체는 정상 동작 (10개 프로젝트 인덱싱 완료) | ✅ |
| 언어 커버리지 | 10/10 언어에서 graph 결과 산출 | ✅ |
| 엣지 정확도 | inherits 8/10, creates 7/10, imports 9/10 (정확/노이즈/누락 기준) | ⚠️ B |
| 신규 regex 대응 | — | — |

### 핵심 발견 (코드스캔 개선 후보)

1. **Class 바인딩 회귀 (C#, PHP)** — `[Theory]/[Fact]` 클래스 이름이 `[Global]`로 표시. 클래스→메서드 그래프 엣지 형성은 정상이지만 트리 출력에 클래스명이 누락됨.

2. **JS/TS — `super()`/`constructor`을 메서드로 카운팅** — 생성자 안의 `super()` 호출 한 줄이 별도 메서드로 인덱싱되어 14/13 메서드처럼 부풀려짐.

3. **Kotlin — 생성자 시그니처 파싱 노이즈** — `class EnSpeaker(name: String) : Person(name, "en")` 에서 `String)` 토큰이 base type으로 잘못 잡힘. 이는 `inherits_or_implements` 검색의 정밀도를 떨어뜨림.

4. **Kotlin — `creates` 엣지 미검출** — Kotlin에는 `new` 키워드가 없어서 `EnSpeaker("Alice")` 같은 직접 호출을 생성자 호출로 인식하지 못함.

5. **Go — `inherits_or_implements` 미검출** — embedded struct(`person.Base`)를 상속 관계로 보지 못함. CodeScan 사양에는 Go가 "그래프 의존성 스캔만"이라고 명시되어 있으므로 정책 결정 필요.

6. **Go — `creates` 오인식** — `&EnSpeaker{...}` 컴포지트 리터럴을 자기 생성으로 잡아 `EnSpeaker -[creates]-> EnSpeaker` 같은 자기 참조 엣지를 만들어 냄.

7. **Rust — `impl Trait for Struct` 패턴 미인식** — `inherits_or_implements` 0건. 가장 중요한 dependency 단서임에도 누락.

8. **Rust — `use` 구문의 group import 파싱 버그** — `use crate::person::{Person, PersonBase};` 가 `crate::person::` 토큰(끝의 `{}` 직전에서 잘림)으로 잡힘.

9. **C++ — `std::make_unique<T>` 미인식** — `creates` 엣지 0건. 현대 C++에서 가장 흔한 생성 패턴이라 보강 가치 큼.

## 다음 단계 제안

- 우선순위 1 (그래프 정확도 회복):
  - **Rust** `impl Trait for Struct` 패턴 추가 (regex-safety-guard 협업)
  - **Go** embedded struct → inherits_or_implements 엣지 추가
  - **Kotlin** 생성자 시그니처 → base type 추출 정밀화 (`String)` 노이즈 제거)
- 우선순위 2 (생성 패턴 보강):
  - **C++** `std::make_unique`, `std::make_shared`, placement-new 패턴
  - **Kotlin** `Type(args)` 직접 호출 패턴
- 우선순위 3 (메서드 카운트 정확화):
  - **JS/TS** `super(...)` 호출과 `constructor` 본문을 별도 메서드로 인식하지 않도록 보정
  - **C#/PHP** 트리 출력의 `[Global]` 회귀 원인 식별 (그래프 엣지는 정상이므로 표시 로직 문제)

## 참고 — 검증 명령

```pwsh
codescan scan TestSample/<lang>
codescan query "MATCH (c:class)-[r:inherits_or_implements]->(t:type) RETURN c,t" -p <id>
codescan query "MATCH (a)-[r:creates]->(t:type) RETURN a,t"                       -p <id>
codescan query "MATCH (f:file)-[r:imports]->(m:module) RETURN f,m"               -p <id>
```

> **주의**: `--project-id` 플래그는 동작하지 않으며 `--project` (또는 `-p`)를 사용해야 한다.
> 첫 시도에서 모든 프로젝트가 동일 결과를 반환한 원인이었다.

## 관련

- 트리거: `Prompt/05-TestForSampleProject.md`
- 디렉토리: `TestSample/`
- 도구: `codescan v0.4.2` (`~/.codescan/bin/codescan.exe`)
- 협업 후보: [[regex-safety-guard]] (각 언어 regex 보강), [[aot-compatibility-scout]] (보강된 regex의 AOT 호환 검증)
