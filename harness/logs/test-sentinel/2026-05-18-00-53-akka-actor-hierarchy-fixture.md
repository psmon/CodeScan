---
date: 2026-05-18T00:53+09:00
agent: test-sentinel
type: review
mode: log-eval
trigger: "TestSample/csharp 에 Akka.net 액터모델 동일 구성"
---

# Akka.NET 액터 모델 fixture — 툴킷 스펙(액터 hierarchy) 분석 한계 측정

## 실행 요약

`TestSample/csharp/`(POJO OOP)와 동일한 도메인을 **Akka.NET 1.5.x ReceiveActor**
패턴으로 재작성하여 `TestSample/csharp-akka/`로 만들었다.
목적은 단순 OOP가 아니라 **툴킷이 도입한 동적 부모-자식 관계(액터 hierarchy)** 를
CodeScan이 어디까지 추적할 수 있는지 측정하는 것이다.

지식 참조: `D:\Code\Webnori\skill-actor-model` (dotnet-akka-net SKILL.md).

## 도메인

```
ActorSystem("HelloAkka")
└── /user/world  ─ WorldActor (또는 GenericWorldActor)
    ├── /user/world/en  ─ EnSpeakerActor : PersonActor : ReceiveActor
    ├── /user/world/ko  ─ KoSpeakerActor : PersonActor : ReceiveActor
    └── /user/world/ja  ─ JaSpeakerActor : PersonActor : ReceiveActor
```

`WorldActor`와 `GenericWorldActor` 두 변형을 의도적으로 함께 작성:
- `WorldActor` — `Props.Create(() => new EnSpeakerActor("Alice"))` (람다 안 `new`)
- `GenericWorldActor` — `Props.Create(typeof(EnSpeakerActor), "Alice")` (`new` 없음, 타입 토큰만)

`dotnet run` 실제 실행으로 동작 확인됨:
```
[ko] 진수: 안녕, 세상!
[ja] ハナコ: こんにちは、世界！
[en] Alice: Hello, World!
```
(액터 메시지 순서는 비결정적)

## 결과 — 엣지 매트릭스 (Project #17)

### 언어 스펙 (regex가 잘 잡는 것)

| 엣지 | 정규식 결과 | 비고 |
|------|----------|------|
| `EnSpeakerActor -[inherits]-> PersonActor` | ✅ | C# `:` 상속 |
| `KoSpeakerActor -[inherits]-> PersonActor` | ✅ | |
| `JaSpeakerActor -[inherits]-> PersonActor` | ✅ | |
| `PersonActor -[inherits]-> ReceiveActor` | ✅ | **액터 패턴 시그널** — Akka 베이스 클래스 인식 |
| `WorldActor -[inherits]-> ReceiveActor` | ✅ | |
| `GenericWorldActor -[inherits]-> ReceiveActor` | ✅ | |
| `*.cs -[imports]-> Akka.Actor` | ✅ | 7개 파일 정확 |
| `*.cs -[imports]-> HelloAkka.Messages` | ✅ | |

### 툴킷 스펙 (회색 지대 — 우연/누락)

| 의도 | 코드 패턴 | regex 결과 | 의미 |
|------|----------|----------|------|
| WorldActor가 EnSpeaker의 **부모** | `Context.ActorOf(Props.Create(() => new EnSpeakerActor("Alice")), "en")` | ⚠️ `WorldActor -[creates]-> EnSpeakerActor` (**우연**히 잡힘) | 람다 내부 `new EnSpeakerActor`가 `NewObject` regex에 매치. 그래프상 일반 객체 생성과 구분 불가. **부모-자식 의미는 사라짐** |
| GenericWorldActor가 EnSpeaker의 **부모** | `Context.ActorOf(Props.Create(typeof(EnSpeakerActor), "Alice"), "en")` | ❌ **누락** | `new` 표현식 없음. 정규식이 토큰을 봐도 의미를 못 만듦 |
| WorldActor → 자식들 메시지 전송 | `_en.Tell(new SayHello())` | ⚠️ `WorldActor -[creates]-> SayHello` (잡히지만 누구→누구는 손실) | `_en` 변수의 동적 타깃 정보 없음 |
| WorldActor가 처리하는 메시지 | `Receive<HelloAll>(...)`, `Receive<HelloResponse>(...)` | ❌ 누락 | 액터 dispatch table은 의미 분석 필요 |
| 액터 패스 `/user/world/en` | `"en"` 인자 | ❌ 누락 | 문자열 리터럴의 의미 미파악 |
| supervision strategy | `SupervisorStrategy` 오버라이드 (이 샘플엔 없음) | — | 향후 케이스 |

### `WorldActor` vs `GenericWorldActor` 직접 비교

```
WorldActor       -[creates]-> EnSpeakerActor  ✅ (람다 안 new)
WorldActor       -[creates]-> KoSpeakerActor  ✅
WorldActor       -[creates]-> JaSpeakerActor  ✅
GenericWorldActor -[creates]-> SayHello       ⚠️ (Tell 안 new만 잡힘)
GenericWorldActor -[creates]-> ???            ❌ EnSpeaker/Ko/Ja 자식 액터 누락
```

→ **언어 표면(`new`)이 살아 있냐 없냐에 따라 같은 부모-자식 관계가 잡히기도 안 잡히기도 한다.**

## 신규 엣지 타입 제안 (액터 그래프 모델)

현재 그래프 엣지 7종 (`contains`, `defines`, `imports`, `inherits_or_implements`,
`creates`, `uses_type`, `authored`, `has_comment`, `documents`)으로는 액터 hierarchy를
1급 시민으로 표현할 수 없다. 다음 엣지 타입 추가 검토:

| 엣지 | 의미 | 검출 후보 (의미 분석 단계) |
|------|------|--------------------------|
| `spawns_child` | 부모 액터가 자식 액터를 만든다 | `Context.ActorOf(...)`, `Props.Create(...)`의 T |
| `receives_message` | 액터가 특정 메시지 타입을 처리한다 | `Receive<T>(...)`, `ReceiveAsync<T>(...)` 의 T |
| `sends_message_to` | 액터가 다른 액터에게 메시지 전송 | `actorRef.Tell(...)`, `actorRef.Ask(...)`, `Sender.Tell(...)` (데이터 흐름 분석 필요) |
| `supervises_with` | 부모 액터의 supervision 정책 | `SupervisorStrategy` 오버라이드 |
| `creates_child_at_path` | 액터 패스 식별 | `ActorOf(..., name)` 의 name 인자 |
| `routes_via` | 라우터 경유 | `Props.WithRouter(...)`, `FromConfig` |

도구 후보:
- **Roslyn** — C# AST에서 `MemberAccessExpressionSyntax.Name` 이 `"ActorOf"`/`"Tell"`이면 인자 정적 분석 가능. 제네릭 인자도 추출. Akka.NET 추적의 가장 정확한 백엔드.
- **Akka 컨피그 분석** — `application.conf` HOCON 파싱하여 라우터/dispatcher 메타데이터.
- **런타임 trace 머지** — Phobos / Akka.NET ClusterClient 텔레메트리를 그래프에 머지하는 옵션 (향후).

## 의미 분석 도커 플랜 영향

[[semantic-analyzer-docker]] 의 `codescan/semantic-csharp` (Roslyn) 이미지는
**다음 두 가지를 동시에 다뤄야 한다**:

1. **언어 스펙 정밀화** — Roslyn workspace로 `var x = new()`, `target-typed new`, alias 해소
2. **툴킷 스펙 인식** (Akka.NET, ASP.NET Core, MediatR 등 — extensible plugin)
   - 첫 PoC 후보: **Akka.NET 패턴 매처** (이 fixture로 검증)
   - `Symbol.ContainingType.ToDisplayString() == "Akka.Actor.IActorContext"` 같은 의미 기반 매칭

→ 도커 플랜의 Phase 1을 두 트랙으로 분리:
- **1-A**: 일반 C# Roslyn (target-typed new 등)
- **1-B**: **액터 fixture 전용** Akka 패턴 분석 — TestSample/csharp-akka가 회귀 테스트 fixture

## 평가 (test-sentinel 3축 + 액터 보강)

| 축 | 결과 |
|----|------|
| 빌드/실행 | ✅ `dotnet build` + `dotnet run` 통과, 3개 자식 액터 응답 확인 |
| 언어 커버리지 (기존) | ✅ inherits/imports 모두 정확 |
| **툴킷 엣지 커버리지** | ⚠️ **부분** — `new` 표현 의존 케이스만 우연히 잡힘. 정식 그래프 모델 미존재 |
| 의미 분석 fixture 가치 | ✅ **A** — Roslyn 이미지 회귀 측정의 결정적 fixture |

## 산출물

- `TestSample/csharp-akka/`
  - `HelloAkka.csproj` (Akka 1.5.51)
  - `src/Messages.cs` (HelloAll, SayHello, HelloResponse)
  - `src/Actors/PersonActor.cs` (추상 ReceiveActor)
  - `src/Actors/EnSpeakerActor.cs`, `KoSpeakerActor.cs`, `JaSpeakerActor.cs`
  - `src/Actors/WorldActor.cs` — `Props.Create(() => new XxxActor(...))` 패턴 (람다 안 new)
  - `src/Actors/GenericWorldActor.cs` — `Props.Create(typeof(XxxActor), ...)` 패턴 (new 없음)
  - `src/Program.cs` — `ActorSystem.Create`, `Tell(new HelloAll())`
  - `Dockerfile`, `.gitignore`, `README.md`

## 다음 단계 제안

1. **그래프 엣지 추가** (스키마 변경) — `spawns_child`, `receives_message`, `sends_message_to` 3종.
2. **Roslyn 도커 이미지 PoC** — 이 fixture를 입력으로:
   - `Context.ActorOf` 호출 → `spawns_child` 엣지
   - `Receive<T>(handler)` → `receives_message`
   - 람다 vs typeof 두 형태 모두 정확히 잡혀야 함
3. **TestSample/java/, TestSample/kotlin/ 에도 동일 fixture** — Akka Classic (Java) / Pekko Typed (Kotlin) 패턴은 호출 모양이 달라 별도 매처 필요. 비교 매트릭스 확장.
4. `harness/agents/actor-hierarchy-scout.md` 신규 에이전트 후보 — 툴킷별 액터 패턴 매처 메타.

## 관련

- 코드: `TestSample/csharp-akka/`
- 직전 작업: [[2026-05-18-00-42-language-analyzer-refactor]]
- 후속 도커 플랜: [[semantic-analyzer-docker]] (Phase 1-B 액터 트랙 추가 필요)
- 참고 스킬: `D:\Code\Webnori\skill-actor-model\plugins\skill-actor-model\skills\dotnet-akka-net\SKILL.md`
