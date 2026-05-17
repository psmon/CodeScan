# Semantic Analyzer Docker Plan

> 언어별 컴파일러/툴체인을 도커 컨테이너로 격리하여 의미 분석(semantic analysis) 결과를
> CodeScan 그래프에 머지하는 전략 — regex 전략의 한계를 극복.

## 배경

CodeScan은 현재 두 단계로 의존성 그래프를 만든다:

1. **regex 전략** (`RegexSourceDependencyStrategy`) — 항상 작동, 프로젝트 빌드 없이도 추출.
   장점: 빠름, AOT 안전, 외부 의존 없음. 한계: 컨텍스트 없는 토큰 매칭이라 false negative 존재.
2. **semantic-probe** (`SemanticProbeStrategy`) — 프로젝트 메타데이터(.sln, pom.xml, tsconfig 등)
   가 있는지만 감지하고 현재는 의도적으로 regex로 폴백.

**다음 단계는 semantic-probe 자리에 실제 컴파일러 백엔드를 끼우는 것.**

regex 전략으로 잡히지 않는 대표 케이스:
- Go: `pkg.NewType()` 같은 생성자 함수 호출은 `creates`로 인식 못 함 (`&Type{}` 직접 리터럴만 가능)
- TS: `interface` 구조적 매칭, generics 인스턴스화의 정확한 타입
- C#: `var x = new();` (target-typed new) 의 좌변 타입
- C++: typedef/using alias 전개, 매크로 확장 후의 실제 호출
- Rust: macro 안에서 일어나는 trait 구현 (`derive(...)`, `impl_trait!`)

## 도커 우선 전략의 이유

각 언어의 의미 분석은 본질적으로 **그 언어의 컴파일러를 돌려야** 한다. CodeScan 본체는 .NET 10 AOT
단일 바이너리로 배포되는데, 모든 언어 툴체인(JDK + Maven, Rust + cargo, Go SDK, clang+cmake, Node+tsc,
Roslyn workspaces 전체)을 끌어들이면 바이너리 크기/부팅 시간/플랫폼 의존이 폭발한다.

→ **언어별 컴파일러는 도커 이미지로 격리하고, CodeScan은 그 결과(JSON)만 머지한다.**

이는 부수 효과로 두 가지 이점도 준다:
1. 사용자 머신에 해당 언어 SDK가 없어도 됨 (도커만 있으면 됨)
2. 의미 분석 결과 캐싱 → 같은 프로젝트 재스캔 시 도커 재실행 없이 캐시 hit

## 아키텍처

```
사용자 코드 ─→ codescan scan
                   │
                   ├─→ DirectoryScanner / SourceAnalyzer (항상)
                   ├─→ RegexSourceDependencyStrategy (항상, 빠름)
                   │       └─ 결과 머지
                   └─→ SemanticProbeStrategy (옵션)
                           │
                           ├─ 1. 프로젝트 모델 검출 (.sln, pom.xml, tsconfig.json, go.mod, Cargo.toml, compile_commands.json)
                           ├─ 2. 캐시 키 계산 (source hash + tool version)
                           ├─ 3. ~/.codescan/semantic/<projectId>/<lang>.json 존재 검사
                           │       └─ hit → 머지하고 종료
                           ├─ 4. miss → docker run codescan/semantic-<lang>:latest
                           │       └─ /work 마운트, stdout 으로 JSON 출력
                           ├─ 5. JSON 검증 + 캐싱
                           └─ 6. regex 결과와 머지 (semantic 우선, regex가 보완)
```

## 도커 이미지 명세

각 이미지는 동일한 contract를 따른다:

```
INPUT:  /work (read-only mount of project root)
OUTPUT: stdout JSON (UTF-8, NDJSON 한 줄당 한 엣지 또는 노드)
EXIT:   0 = success, non-zero = failure (stderr 로 진단)
```

NDJSON 스키마:
```jsonl
{"kind":"node","type":"class","name":"EnSpeaker","file":"src/Speakers/EnSpeaker.cs","line":3}
{"kind":"edge","from":{"type":"class","name":"EnSpeaker"},"to":{"type":"class","name":"Person"},"rel":"inherits","detail":"semantic","line":3}
{"kind":"edge","from":{"type":"file","name":"Main.cs"},"to":{"type":"module","name":"HelloWorld.Speakers"},"rel":"imports","line":1}
```

이 NDJSON은 호스트 CodeScan이 그대로 `SourceDependency` 리스트로 매핑해 머지.

### 이미지 목록

| 언어 | 이미지 | 분석기 | 베이스 | 크기 추정 |
|------|--------|--------|-------|----------|
| C# | `codescan/semantic-csharp` | Roslyn (Microsoft.CodeAnalysis.CSharp.Workspaces) | mcr.microsoft.com/dotnet/sdk:9.0 | ~700MB |
| Java | `codescan/semantic-java` | Eclipse JDT 또는 Spoon (POM/Gradle 빌드 그래프 + AST) | eclipse-temurin:21-jdk + Maven | ~800MB |
| Kotlin | `codescan/semantic-kotlin` | kotlinc + Kotlin Analysis API | gradle:8.10-jdk21 | ~900MB |
| TS/JS | `codescan/semantic-typescript` | TypeScript Compiler API (ts.createProgram) | node:22-alpine + typescript | ~200MB |
| PHP | `codescan/semantic-php` | nikic/PHP-Parser + 자체 ResolveTraverser | php:8.3-cli-alpine + composer | ~150MB |
| Python | `codescan/semantic-python` | LibCST + jedi (cross-reference 해소) | python:3.12-alpine | ~250MB |
| Go | `codescan/semantic-go` | go/packages + go/types | golang:1.22-alpine | ~400MB |
| Rust | `codescan/semantic-rust` | rust-analyzer JSON-RPC (또는 cargo metadata + syn crate) | rust:1.79-alpine | ~1.2GB |
| C/C++ | `codescan/semantic-cpp` | Clang LibTooling (clang-query 또는 자체 plugin) | llvm/clang:19 + cmake | ~1.5GB |

도커 이미지는 별도 멀티스테이지로 빌드하여 분석기만 남기고, 빌드 도구(JDK 전체 등)는 런타임에서 제거.
사용자가 첫 분석 시 자동 pull, 또는 `codescan semantic install <lang>` 로 명시적 pull.

## 호스트 측 변경

`SemanticProbeStrategy.CanAnalyze`가 현재 항상 false를 반환 — 이 부분을 활성화:

```csharp
public bool CanAnalyze(FileEntry file, SourceDependencyContext context)
{
    var language = GetLanguage(file.Extension);
    if (language is null) return false;
    if (!context.SemanticCapabilities.TryGetValue(language, out var cap)) return false;
    if (!cap.ProjectModelFound) return false;
    if (!SemanticCache.IsConfiguredFor(language)) return false;  // 사용자 명시적 활성화 필요
    return true;
}
```

추가 컴포넌트:
- `Services/Semantic/SemanticCache.cs` — `~/.codescan/semantic/` 캐시 관리, 소스 해시 키
- `Services/Semantic/SemanticDockerRunner.cs` — `docker run --rm -v <root>:/work codescan/semantic-<lang>` 실행
- `Services/Semantic/SemanticResultMerger.cs` — NDJSON 파싱 후 `RegexSourceDependencyStrategy` 결과와 머지 (semantic을 신뢰원, regex가 fallback 보완)
- `Commands/SemanticCommand.cs` — `codescan semantic install/status/clear` 서브명령

신규 CLI:
```
codescan semantic status         # 어떤 언어의 의미 분석이 활성/캐시 상태인지
codescan semantic install csharp # 도커 이미지 pull
codescan semantic clear          # 캐시 비우기
codescan scan --semantic         # 일회성으로 semantic 활성화
```

## 빌드 - 사용자 환경 점검

각 도커 이미지는 다음을 자체 헬스체크에 포함:
```
codescan/semantic-<lang>:latest --self-check
```
→ 컴파일러 버전, 입력 contract 호환성, 출력 NDJSON 샘플을 출력. CI 통합용.

## TestSample 활용

TestSample의 10개 샘플 프로젝트가 이미 각 언어의 빌드 파일을 갖고 있어
**도커 이미지 회귀 테스트 케이스로 그대로 활용** 가능:

```
TestSample/
├── csharp/    .csproj         → codescan/semantic-csharp 검증
├── java/      pom.xml         → codescan/semantic-java
├── kotlin/    build.gradle.kts → codescan/semantic-kotlin
├── typescript/ tsconfig.json  → codescan/semantic-typescript
├── go/        go.mod          → codescan/semantic-go
├── rust/      Cargo.toml      → codescan/semantic-rust
└── cpp/       CMakeLists.txt  → codescan/semantic-cpp
```

각 이미지가 해당 TestSample 프로젝트에서 **regex 전략과 동일하거나 더 많은 엣지**를 추출하면 통과.
특히 다음 엣지가 추가로 잡혀야 함:
- Go: `world.Add(speakers.NewEnSpeaker("Alice"))` → `Main.go -[creates]-> EnSpeaker` (생성자 함수 추적)
- TS: generics 인스턴스화 정확한 타입
- Java: 어노테이션 기반 빈 자동 와이어링 (`@Inject` → 의존성 엣지)

## 우선순위 & 단계

1. **Phase 1 (선행)** — C# Roslyn 이미지. 자체 프로젝트(`CodeScan.csproj`)를 first target. dogfooding.
2. **Phase 2** — TS (compilation API), Go (`go/packages`), Python (jedi). 가장 단순한 컴파일러 API.
3. **Phase 3** — Rust (rust-analyzer), Java (JDT). 본격 의미 분석.
4. **Phase 4** — C++, Kotlin, PHP. 복잡도 가장 큼.

각 Phase 결과는 TestSample 기준의 매트릭스로 비교 (regex vs semantic).

## 위험 / 트레이드오프

- **첫 분석 지연** — 도커 이미지 pull (수백 MB) 필요. → 진행 표시줄 + 백그라운드 pull
- **사용자 환경 의존** — Docker Desktop 또는 Podman 필요. → CodeScan 코어는 regex만으로도 완전 동작.
- **캐시 무효화** — 소스 변경 시 도커 재실행. → 파일 단위 해시로 partial re-analyze
- **CI 환경** — GitHub Actions 등에서 도커-in-도커. → Job matrix로 언어별 분리 권장

## 평가 기준 (Phase별)

| 축 | 척도 | 합격선 |
|----|------|--------|
| 정확도 | TestSample 매트릭스에서 regex 대비 +X% 엣지 | regex와 동일 이상 |
| 속도 | 첫 분석 < 60초 (캐시 miss), 캐시 hit < 1초 | 필수 |
| 캐시 적중률 | 동일 소스 재스캔에서 100% | 필수 |
| Self-check 통과 | docker run --self-check | 필수 |

## 다음 액션

- [ ] `harness/agents/semantic-architect.md` 신규 에이전트 작성 (이 계획을 주관)
- [ ] Phase 1: Roslyn 이미지 PoC — `docker/semantic-csharp/` 디렉토리 + Dockerfile
- [ ] `codescan semantic` 서브명령 골조 (현재 미존재)
- [ ] `Services/Semantic/` 디렉토리 추가
- [ ] TestSample을 의미 분석 회귀 테스트의 fixture로 등록

## 관련

- 트리거: 언어별 의미 분석 도커 플랜
- 코드 참조: `Services/SourceGraphAnalyzer.cs` (`SemanticProbeStrategy`, `SemanticCapabilityDetector`)
- 이전 작업: [[2026-05-18-00-42-language-analyzer-refactor]] (regex 전략 확정)
- TestSample: `TestSample/<lang>/` (fixture)
