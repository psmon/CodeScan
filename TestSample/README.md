# TestSample — CodeScan E2E Sample Projects

이 디렉토리는 CodeScan의 그래프 의존성 분석(`inherits_or_implements`, `creates`, `uses_type`, `imports`) 능력을 언어별로 검증하기 위한 **빌드 가능한 최소 샘플 프로젝트** 모음입니다.

## 공통 도메인

모든 언어가 동일한 OOP 시나리오를 구현합니다:

- **`Person`** — 추상 클래스/인터페이스. `Speak()`, `Hello()` 노출
- **`EnSpeaker`** / **`KoSpeaker`** / **`JaSpeaker`** — `Person` 상속/구현, 언어별 "Hello, World" 응답
- **`World`** — `Person` 컬렉션 보유. `HelloAll()` 호출 시 모든 Person이 자기 언어로 응답
- **진입점** — `World`를 만들고 3개 Speaker를 등록한 뒤 `HelloAll()` 실행

이 구조는 다음 엣지를 모두 만들어 냅니다:

| 엣지 | 발견 위치 |
|------|----------|
| `file -[contains]-> class` | 모든 클래스 파일 |
| `class -[defines]-> method` | `Speak`, `Hello`, `Add`, `HelloAll` 등 |
| `class -[inherits_or_implements]-> Person` | 3개 Speaker 모두 |
| `class -[creates]-> EnSpeaker` 등 | `World` 또는 `Main`에서 `new` 호출 |
| `class -[uses_type]-> Person` | `World`의 컬렉션 필드 / 매개변수 |
| `file -[imports]-> module` | 각 파일의 `using` / `import` / `use` / `#include` |

## 지원 언어

| 언어 | 디렉토리 | 빌드 |
|------|---------|------|
| C# | `csharp/` | `dotnet build` |
| Java | `java/` | `mvn package` |
| Kotlin | `kotlin/` | `gradle build` |
| JavaScript | `javascript/` | `node src/main.js` |
| TypeScript | `typescript/` | `tsc && node dist/main.js` |
| PHP | `php/` | `php src/main.php` |
| Python | `python/` | `python src/main.py` |
| Go | `go/` | `go build ./...` |
| Rust | `rust/` | `cargo build` |
| C/C++ | `cpp/` | `cmake --build` |

## Docker 빌드

각 샘플은 `Dockerfile`을 포함합니다. 도커가 있는 환경에서:

```bash
cd TestSample/<lang>
docker build -t hello-<lang> .
```

빌드 산출물이 codescan 스캔에 필요한 경우 빌드 중 `/out`을 호스트로 복사하도록 멀티스테이지를 구성했습니다.

## E2E 검증 방법

```bash
codescan scan TestSample/csharp
codescan query "MATCH (c:class)-[r:inherits_or_implements]->(t:type) RETURN c, t"
codescan query "MATCH (c:class)-[r:creates]->(t:type) RETURN c, t"
codescan query "MATCH (c:class)-[r:uses_type]->(t:type) RETURN c, t"
```

각 언어별 검증 결과는 `harness/logs/test-sentinel/` 아래에 기록됩니다.
