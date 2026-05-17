---
date: 2026-05-18T01:04+09:00
agent: test-sentinel
type: review
mode: log-eval
trigger: "java(untype actor) + 코틀린(pekko typed actor) 유사 적용"
---

# 액터 hierarchy fixture 확장 — Java Akka Classic + Kotlin Pekko Typed

## 실행 요약

[[2026-05-18-00-53-akka-actor-hierarchy-fixture]] 의 C# Akka.NET fixture에 이어,
같은 도메인(World 부모 + En/Ko/Ja Speaker 자식)을 **두 개의 다른 액터 툴킷**으로 재구성:

| 디렉토리 | 툴킷 | 타입 모델 | 빌드 |
|---------|------|----------|------|
| `TestSample/csharp-akka/` | Akka.NET 1.5.51 | untyped (ReceiveActor) | dotnet 9 |
| `TestSample/java-akka/` | Akka Classic 2.7.0 | untyped (AbstractActor) | Maven 3.9 + JDK 21 |
| `TestSample/kotlin-pekko/` | Pekko Typed 1.1.5 | **typed** (AbstractBehavior&lt;T&gt;) | Gradle 8.10 + JDK 21 |

세 샘플 모두 도커 빌드 + 실제 실행 검증 완료:

```
$ docker run --rm hello-akka-java          # 정상 응답 3개국어
$ docker run --rm hello-pekko-kotlin       # 정상 응답 3개국어
$ docker run --rm hello-akka               # (앞서 검증, csharp-akka)
```

## 결과 매트릭스 — 같은 의미, 다른 표면

### `inherits_or_implements` (액터 베이스 클래스 추적)

| 툴킷 | 결과 | 비고 |
|------|------|------|
| C# Akka.NET | ✅ 5/5 | SpeakerActor→PersonActor, PersonActor/WorldActor→ReceiveActor |
| **Java Akka Classic** | ✅ **5/5** | 동일 패턴 — AbstractActor 베이스 |
| **Kotlin Pekko Typed** | ⚠️ **2/2 (잘못)** | `data object HelloAll : WorldCommand`, `data class SayHello(...) : SpeakerCommand` 만 잡힘. **`class XxxBehavior private constructor(...)` 의 부모 클래스 미인식** — 새 회귀 발견 |

### `creates` (액터 hierarchy 부모-자식 — 핵심)

| 툴킷 | 코드 패턴 | regex 결과 | 정확도 |
|------|----------|----------|--------|
| **C# Akka.NET (WorldActor)** | `Context.ActorOf(Props.Create(() => new EnSpeakerActor(...)))` | ✅ 우연히 잡힘 (람다 안 `new`) | 표면 운빨 |
| **C# Akka.NET (GenericWorldActor)** | `Context.ActorOf(Props.Create(typeof(EnSpeakerActor), ...))` | ❌ 누락 | `new` 없음 |
| **Java Akka Classic** | `getContext().actorOf(EnSpeakerActor.props("Alice"), "en")` + `Props.create(EnSpeakerActor.class, name)` | ❌ **자식 액터 0/3 누락** | `new` 없음, `.class` 토큰 의미 미파악 |
| **Kotlin Pekko Typed** | `context.spawn(EnSpeakerBehavior.create("Alice"), "en")` | ❌ **자식 액터 0/3 누락** | 람다 안 `new` 없음 (Kotlin은 `new` 키워드 자체 없음) |
| **Kotlin Pekko Typed (false positive)** | `class XxxBehavior : PersonBehavior(context, name, "en")` | ⚠️ `XxxBehavior -[creates]-> PersonBehavior` 3건 | **잘못된 엣지** — 슈퍼클래스 생성자 호출을 새 객체 생성으로 오인 |

### `imports` (Akka/Pekko 패키지 시그널)

| 툴킷 | 결과 |
|------|------|
| C# Akka.NET | ✅ `using Akka.Actor;` 정확 |
| Java Akka Classic | ✅ `akka.actor.AbstractActor`, `akka.actor.Props` 등 정확 |
| Kotlin Pekko Typed | ✅ `org.apache.pekko.actor.typed.*` 정확 — 패키지로 typed/untyped 식별 가능 |

## 결정적 발견 — 툴킷의 표면 다양성

같은 **부모-자식 액터 관계**가 세 툴킷에서 표현되는 방식:

```csharp
// C# Akka.NET — 람다 + new
Context.ActorOf(Props.Create(() => new EnSpeakerActor("Alice")), "en")

// C# Akka.NET — typeof (new 없음)
Context.ActorOf(Props.Create(typeof(EnSpeakerActor), "Alice"), "en")
```

```java
// Java Akka Classic — Props.create + .class (new 없음)
getContext().actorOf(Props.create(EnSpeakerActor.class, "Alice"), "en")
```

```kotlin
// Kotlin Pekko Typed — Behavior 팩토리 (new 없음, Kotlin은 new 키워드 없음)
context.spawn(EnSpeakerBehavior.create("Alice"), "en")
```

→ **`new` 표현식이 보이는 경우는 단 하나** (`Props.Create(() => new X(...))`).
나머지 3가지 패턴은 정규식이 절대 잡지 못한다.
→ regex 전략으로는 부모-자식 관계의 ~25%만 우연히 잡히고, 의미는 전혀 보존되지 않는다.

## Kotlin Analyzer 신규 회귀 2건 발견

이번 작업으로 KotlinAnalyzer regex의 두 가지 추가 회귀가 노출됨:

### R1) `class X private constructor(...) : Base(...)` 인식 실패

```kotlin
class EnSpeakerBehavior private constructor(
    context: ActorContext<SpeakerCommand>,
    name: String
) : PersonBehavior(context, name, "en")
```

현재 KotlinAnalyzer의 `ClassWithBase` regex:
```
(?:class|...)\s+(\w+)(?:\s*\([^)]*\))?(?:\s*:\s*([^{]+))?
```

`private constructor` 키워드 시퀀스가 `\w+` 다음의 `(...)` 또는 `:` 어느 쪽에도 매치 안됨. 결과: inherits 엣지 누락.

수정 방향: `(\w+)` 뒤에 optional하게 `\s+private\s+constructor` 같은 키워드 시퀀스를 흡수하거나, 더 일반화해서 `(?:\s+\w+)*` 로 modifier들을 소비.

### R2) `class X : Y(args)` 부모 생성자 호출이 creates로 잘못 인식

```kotlin
class EnSpeakerBehavior(...) : PersonBehavior(context, name, "en")
```

여기서 `PersonBehavior(context, ...)`는 슈퍼클래스 생성자 호출(Kotlin syntax)인데,
KotlinAnalyzer의 `CreateCall` regex (`\b([A-Z]\w*)\s*\(`)가 매치하여 `XxxBehavior -[creates]-> PersonBehavior` 엣지를 만들어 냄.

수정 방향: class 선언 라인에서 처음 `:` 이후 첫 `Type(args)` 는 슈퍼클래스 생성자 호출이므로 creates에서 제외. 또는 base type을 별도 추출하고 다른 케이스와 구분.

> 이 두 회귀는 후속 작업 후보. fixture는 그대로 두어 회귀가 다시 발생하지 않도록 한다.

## 의미 분석 도커 플랜 영향 — 확장된 매처 요구

[[semantic-analyzer-docker]] Phase 1-B 액터 트랙은 이제 **3가지 툴킷** 매처가 필요:

| 매처 | 입력 패턴 | 출력 엣지 |
|------|----------|----------|
| **Akka.NET 매처 (Roslyn)** | `Context.ActorOf(Props.Create<T>(...))`, `Props.Create(typeof(T), ...)` | `parent -[spawns_child]-> T` |
| **Akka Classic 매처 (JDT/Spoon)** | `getContext().actorOf(T.props(...), "name")`, `Props.create(T.class, ...)` | `parent -[spawns_child]-> T` |
| **Pekko Typed 매처 (JDT/Spoon + Kotlin Compiler)** | `context.spawn(T.create(...), "name")`, `Behaviors.setup { ... }` | `parent -[spawns_child]-> T` + `T -[receives]-> M` (T의 generic 타입 파라미터에서) |

→ **Pekko Typed가 가장 의미 분석 친화적**. `AbstractBehavior<T>` 의 `<T>` 가 처리 메시지 타입을 명시하고, `ActorRef<T>` 의 `<T>` 가 응답 채널 타입을 강제. 정적 분석기는 generic 인자만 봐도 `receives_message` 엣지를 정확하게 만들 수 있음.

## 평가 (test-sentinel + actor-hierarchy 보강)

| 축 | 결과 |
|----|------|
| 빌드/실행 | ✅ 도커 빌드 + 실행, 3개 자식 액터 응답 확인 (Java, Kotlin 양쪽) |
| 액터 베이스 클래스 인식 | C#/Java ✅, Kotlin ⚠️ (private constructor 회귀) |
| **부모-자식 spawn 엣지** | C# 부분/✅, **Java 0/3**, **Kotlin 0/3** |
| 새 회귀 발견 | **+2건** (Kotlin private constructor, Kotlin super-call as creates) |
| 의미 분석 fixture 가치 | ✅ **A** — 3-언어 cross-toolkit 회귀 입력 |

## 산출물

```
TestSample/
├── csharp-akka/        Akka.NET 1.5 — 기존 (#17)
├── java-akka/          Akka Classic 2.7 — 신규 (#18)
│   ├── pom.xml
│   ├── src/main/java/helloakka/
│   │   ├── Main.java
│   │   ├── actors/{Person, En/Ko/Ja Speaker, World}Actor.java
│   │   └── messages/{HelloAll, SayHello, HelloResponse}.java
│   ├── Dockerfile  (Maven 3.9 + JDK 21)
│   └── .gitignore
└── kotlin-pekko/       Pekko Typed 1.1.5 — 신규 (#19)
    ├── build.gradle.kts (Pekko Typed 1.1.5, Kotlin 2.0, JDK 21)
    ├── settings.gradle.kts
    ├── src/main/kotlin/hellopekko/
    │   ├── Main.kt
    │   ├── actors/{Person, En/Ko/Ja Speaker, World}Behavior.kt
    │   └── messages/Commands.kt (sealed interface)
    ├── Dockerfile  (Gradle 8.10 + JDK 21)
    └── .gitignore
```

도커 이미지:
- `hello-akka-java` (Maven + Akka 2.7.0)
- `hello-pekko-kotlin` (Gradle + Pekko 1.1.5)

## 다음 단계 제안 (우선순위 순)

1. **그래프 엣지 추가** — `spawns_child`, `receives_message`, `sends_message_to` 3종을 GraphModels에 추가
2. **3-툴킷 매처 PoC** (의미 분석 도커):
   - 가장 정보량 많은 **Pekko Typed**(JDT 기반)부터 — `AbstractBehavior<T>` generic으로 receives 엣지 자동 도출
   - **Akka Classic** (Java) — `Props.create(T.class)` 의 첫 인자 추적
   - **Akka.NET** (Roslyn) — `Props.Create<T>` / `Props.Create(typeof(T))` / `Props.Create(() => new T())` 세 형태 통합
3. **Kotlin Analyzer 회귀 수정** (R1, R2):
   - `class X private constructor(...) : Y` 인식
   - `class X : Y(args)` 의 super-call을 creates에서 제외
4. `harness/agents/actor-hierarchy-scout.md` 에이전트 신설 — 툴킷별 매처 메타 관리

## 관련

- 코드: `TestSample/java-akka/`, `TestSample/kotlin-pekko/`
- 이전 작업: [[2026-05-18-00-53-akka-actor-hierarchy-fixture]] (C#)
- 도커 플랜: [[semantic-analyzer-docker]] (Phase 1-B 트랙 3개로 확장)
- 액터 지식: `D:\Code\Webnori\skill-actor-model` (java-akka-classic, kotlin-pekko-typed)
- 후속 회귀: KotlinAnalyzer R1 (private constructor) / R2 (super-call as creates)
