---
name: testsample-build
description: >-
  Per-language local build knowledge for pre-building the TestSample/<lang>
  sample projects so CodeScan can harvest static-analysis metadata (dependency
  closures, resolved inheritance, method signatures) from the build artifacts —
  WITHOUT running the semantic-docker images. Use when the user asks to
  "pre-build the samples", "build TestSample for harvesting", verify what
  static-analysis metadata a build produces, or extend/validate the build-
  artifact harvest strategy. Do NOT auto-trigger; invoke on explicit request.
---

# TestSample Prebuild & Harvest

> 목적: 각 언어 컴파일러/툴체인으로 `TestSample/<lang>` 을 **선행 빌드**하여,
> 빌드가 남기는 정적분석 산출물(의존성 폐포·해소된 상속·시그니처)을 CodeScan 이
> **빌드 없이 수확(harvest)** 할 수 있는지 검증하고 회귀 fixture 로 쓴다.
> Docker 이미지(수백 MB pull + 컴파일)를 매 스캔에 돌리는 것은 오버엔지니어링이므로,
> "이미 산출된 결과물을 상대경로에서 읽는" 경량 전략을 우선한다.
> 배경 기술검토: `harness/knowledge/graph-reconciliation.md` 의 후속, 그리고
> `harness/knowledge/semantic-analyzer-docker.md`(무거운 대안).

## 핵심 원칙 — 3티어 + 신선도 가드

1. **T1 매니페스트** (항상 존재, 무빌드): package.json / *.csproj / project.assets.json /
   pom.xml / go.mod / Cargo.toml / composer.json → 의존성·모듈 엣지. **CodeScan 자체 파싱**.
2. **T2 타입선언 산출물** (빌드/배포됐을 때만): `.d.ts` / `.pyi` / `.class` / `.dll` / `.rmeta`.
   해소된 상속·시그니처. 일부는 CodeScan 자체 파싱, 일부는 리더 필요.
3. **T3 분석기 캐시** (툴 실행됐을 때만): `.mypy_cache` / `compile_commands.json` / SARIF.
   **버전 취약** — 포맷이 바뀔 수 있음(아래 Python 참고).

> **신선도 가드 필수**: T2/T3 산출물은 옛 소스로 만들어졌을 수 있다. 반드시
> `artifact_mtime > source_mtime` 일 때만 신뢰하고, 오래됐으면 regex 로 폴백한다.
> (역으로 이 판정을 노출하면 "빌드 산출물이 소스보다 오래됨" 경고가 공짜로 나온다.)

## 로컬 툴체인 (이 머신 기준, 확인된 값)

| 툴 | 상태 | 비고 |
|----|------|------|
| dotnet 10.0.301 | ✅ | C# 빌드 |
| node 24 / npm 11 | ✅ | TS/JS 빌드 (`npx tsc`) |
| JBR (Rider 번들) javac 25 | ✅ | `"/c/Program Files/JetBrains/JetBrains Rider */jbr/bin/javac.exe"` — Maven 없이 Java 직접 컴파일 |
| py (pythoncore 3.14) | ✅ | pip 있음. `python`(hermes venv)는 pip 없음 → **`py` 사용** |
| go / cargo / rustc / kotlinc / mvn / gradle / cmake / clang / php | ❌ | 미설치 — 빌드 불가, 지식으로만 기록 |

## 언어별 빌드 → 수확 (검증 상태)

### C# / .NET — ✅ 검증 (가장 강력)
```bash
dotnet build TestSample/csharp/HelloWorld.csproj        # 또는 csharp-akka / csharp-orleans
```
산출물 & 수확:
- `obj/project.assets.json` — **전이 의존성 폐포**(akka 샘플: Akka 1.5.51 + 전이 11개). JSON → CodeScan 자체 파싱. (실은 `dotnet restore` 만으로도 생성 = 무컴파일)
- `bin/**/{name}.deps.json` — 런타임 의존 그래프(JSON).
- `bin/**/{name}.dll` — **어셈블리 메타데이터**. CodeScan 이 .NET 이므로 `System.Reflection.Metadata`(빌트인·AOT 안전)로 타입/베이스/인터페이스/시그니처를 **외부툴 0** 으로 읽음. 포맷=ECMA-335(안정).

### Java / Kotlin (JVM) — ✅ 검증 (중요 언어)
```bash
JBR="/c/Program Files/JetBrains/JetBrains Rider 2026.1.2/jbr/bin"
"$JBR/javac.exe" -d TestSample/java/target/classes $(find TestSample/java/src -name '*.java')
# Kotlin: kotlinc 필요(미설치). 산출물 .class 포맷은 Java 와 동일 → 같은 하베스터 적용.
```
산출물 & 수확:
- `target/classes/**/*.class` (Maven 은 `target/classes`, Gradle 은 `build/classes`) — **JVM 바이트코드**.
- 클래스파일 포맷(magic `0xCAFEBABE` → constant pool → this/super/interfaces)을 파싱하면
  **해소된 상속을 정확히** 추출. 실증: `EnSpeaker extends helloworld/Person`, Ja/Ko 동일.
- **JVM 툴 불필요** — 순수 바이너리 파싱(→ `Services/Harvest/JvmClassHarvester`, 영입됨).
- Kotlin: 동일 `.class` + `@kotlin.Metadata` 어노테이션에 Kotlin 고유 정보(추가 수확 여지).
- 의존성: pom.xml / build.gradle(.kts) = T1 무빌드.

### TypeScript / JS — ✅ 검증
```bash
cd TestSample/typescript && npm install
npx tsc --declaration --emitDeclarationOnly       # dist/**/*.d.ts 생성
```
산출물 & 수확:
- `dist/**/*.d.ts` — **해소된 상속·시그니처**. 실증: `EnSpeaker extends Person`,
  `add(person: Person): void`, import 경로 해소. `.d.ts` 는 TS 문법 텍스트 →
  **기존 `JsTsAnalyzer` 를 `.d.ts` 에 겨누면 재사용** 가능(regex 가 원본 .ts 보다 정확히 잡음).
- T1: package.json / tsconfig.json.

### Python — ⚠️ 부분 (mypy 캐시 버전 취약)
```bash
py -m pip install mypy
cd TestSample/python && py -m mypy src --cache-dir .mypy_cache
```
- 구버전 mypy: `.mypy_cache/**/*.data.json`(JSON, 수확 쉬움).
- **mypy 3.14: SQLite `cache.N.db` 의 `files2(path,mtime,data)`, data 는 바이너리 `.data.ff`**
  (JSON 아님). 심볼(`src.world`,`Person`)은 들어있으나 mypy 전용 포맷 → 수확 비용 큼/취약.
- **더 안정적 경로**: `.pyi` 스텁(있으면, Python 문법 → 기존 분석기), pyproject/requirements(T1).
- `files2.mtime` = 신선도 가드 재료.

### 미설치 언어 (지식만)
- **Go**: `go build`/`go list -json`. go.mod/sum = T1 무빌드. 깊은 건 go/packages(툴체인).
- **Rust**: `cargo build`. Cargo.toml/lock = T1. `target/**/*.rmeta` = 크레이트 메타(빌드시).
- **C/C++**: `cmake -DCMAKE_EXPORT_COMPILE_COMMANDS=ON` → **`compile_commands.json`**(clangd 의 열쇠, 소비엔 clang 필요).
- **PHP**: composer.json = T1. PHPStan/Psalm 캐시(툴 실행시). 컴파일 산출물 없음.

## 영입(adopt) 현황

- ✅ **`Services/Harvest/JvmClassHarvester`** — `.class` 바이너리에서 `type -[inherits_or_implements]-> super/interface` 추출. 외부툴 0, AOT 안전. Java+Kotlin 공용. 이식성 유닛테스트(임베드 `.class`).
- ⏳ 다음 후보: C# `System.Reflection.Metadata` 하베스터(.dll), `.d.ts` 하베스트(기존 JsTsAnalyzer 재사용), T1 매니페스트 하베스트(전 언어 의존성 엣지).
- 파이프라인 편입: `ISourceDependencyStrategy` 에 `ArtifactHarvestStrategy` 를
  `[docker(opt-in) → artifact-harvest(auto,경량) → regex(always)]` 순서로 배치(신선도 가드 포함).

## 회귀 fixture
`TestSample/<lang>` 은 각 언어 빌드파일을 갖춘 fixture. 하베스터가 regex 대비
**동일 이상 엣지**(특히 해소된 상속)를 뽑으면 통과.
