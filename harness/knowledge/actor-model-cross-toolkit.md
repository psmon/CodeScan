# Actor Model — Cross-Toolkit Knowledge

> 액터 모델은 **언어 스펙이 아니라 툴킷 스펙**으로 만들어지는 부모-자식 관계를 가진다.
> 정규식 분석으로는 이 hierarchy를 잡을 수 없다. 각 툴킷의 표현 차이를 정리하고
> CodeScan이 미래에 도입할 그래프 엣지/매처 전략을 명세한다.

## 핵심 개념 — 왜 어려운가

전통적 OOP는 상속(`class A : B`)이 **정적**으로 코드 표면에 나타나 정규식이 잡기 쉽다.
액터 모델은 부모-자식 관계가 **런타임 함수 호출** (`Context.ActorOf<T>()`,
`getContext().actorOf(Props.create(T.class))`, `context.spawn(T.create())`)로 만들어진다.

```
의도: "WorldActor는 EnSpeakerActor의 부모"
표면:
   C# Akka.NET: Context.ActorOf(Props.Create(() => new EnSpeakerActor(...)))
                Context.ActorOf(Props.Create(typeof(EnSpeakerActor), ...))
                Context.ActorOf(Props.Create<EnSpeakerActor>(...))
   Java Akka:   getContext().actorOf(Props.create(EnSpeakerActor.class), "name")
                getContext().actorOf(EnSpeakerActor.props(...), "name")
   Kotlin Pekko: context.spawn(EnSpeakerBehavior.create(...), "name")
```

→ 같은 의도가 **5+ 가지 표면**으로 표현된다. 정규식 1개로 잡을 수 없다.

## 3-툴킷 비교

| 항목 | Akka.NET 1.5 | Akka Classic 2.7 | Apache Pekko Typed 1.1+ |
|------|-------------|-----------------|------------------------|
| 언어 | C# (.NET) | Java/Scala | Java/Kotlin |
| 라이선스 | Apache 2.0 | BSL (2.7부터) | **Apache 2.0** (Akka의 OSS 포크) |
| 타입 모델 | untyped | untyped | **typed** (`AbstractBehavior<T>`) |
| 베이스 클래스 | `ReceiveActor`, `UntypedActor` | `AbstractActor` | `AbstractBehavior<T>` |
| 메시지 dispatch | `Receive<T>(handler)` | `receiveBuilder().match(Class, h)` | `newReceiveBuilder().onMessage(Class, h)` |
| Props 팩토리 | `Props.Create<T>()`, `Props.Create(() => new T())`, `Props.Create(typeof(T))` | `Props.create(T.class)`, `Props.create(T.class, args)` | (없음 — `Behavior<T>` 직접 사용) |
| 자식 생성 | `Context.ActorOf(Props, "name")` | `getContext().actorOf(Props, "name")` | `context.spawn(Behavior, "name")` |
| 응답 | `Sender.Tell(msg)` (암묵 sender) | `getSender().tell(msg, getSelf())` | `replyTo.tell(msg)` (메시지에 명시) |
| 자기 참조 | `Self` | `getSelf()` | `context.self` |

## TestSample fixture 매핑

| 디렉토리 | 툴킷 | 빌드 검증 |
|---------|------|----------|
| `TestSample/csharp-akka/` | Akka.NET 1.5.51 | Docker — ✅ |
| `TestSample/java-akka/` | Akka Classic 2.7.0 | Docker (Maven+JDK21) — ✅ |
| `TestSample/kotlin-pekko/` | Apache Pekko Typed 1.1.5 | Docker (Gradle+JDK21) — ✅ |

세 샘플 모두 **동일한 도메인**(World 부모 + En/Ko/Ja Speaker 자식)을 구현하여
**같은 의미 → 다른 표면** 비교의 결정적 기준점.

## 정규식 한계 매트릭스 (현재 상태)

| 의도 | C# Akka.NET 패턴 | Java Akka Classic | Kotlin Pekko Typed | regex 잡힘? |
|------|-----------------|-------------------|--------------------|---|
| 부모-자식 spawn | `Props.Create(() => new T())` | — | — | ⚠️ 우연 (`new` 덕분) |
| 부모-자식 spawn | `Props.Create(typeof(T))` | — | — | ❌ |
| 부모-자식 spawn | — | `Props.create(T.class)` | — | ❌ (`new` 없음) |
| 부모-자식 spawn | — | — | `context.spawn(T.create())` | ❌ (Kotlin은 `new` 키워드 자체 없음) |
| 액터 베이스 상속 | `: ReceiveActor` | `extends AbstractActor` | `: AbstractBehavior<T>` | ✅ |
| 메시지 dispatch (Receive 등록) | `Receive<T>(h)` | `.match(T.class, h)` | `.onMessage(T.class, h)` | ❌ 전부 |
| 메시지 전송 (Tell) | `actor.Tell(msg)` | `actor.tell(msg, self)` | `actor.tell(msg)` | ❌ (msg 생성은 `creates`로 잡힘) |

→ 4가지 부모-자식 패턴 중 **1개만 우연히** 잡힘. **부모-자식의 의미는 어느 경우에도 보존 안 됨.**

## 제안 신규 그래프 엣지

현재 `inherits_or_implements`/`creates`/`uses_type`/`imports` 만으로는 액터 hierarchy를 표현 불가.
의미 분석 단계에서 다음 엣지 추가 필요:

| 엣지 | 의미 | 검출 (의미 분석) |
|------|------|----------------|
| `spawns_child` | 부모 액터 → 자식 액터 타입 | `ActorOf`/`actorOf`/`spawn` 호출의 첫 인자 분석 (제네릭/`.class`/팩토리 함수 반환 타입) |
| `receives_message` | 액터가 처리 가능한 메시지 타입 | `Receive<T>(...)`/`.match(T.class, ...)`/`.onMessage(T.class, ...)` 인자 |
| `sends_message_to` | 특정 ActorRef에 특정 메시지 전송 | `actor.tell(msg)` — 데이터 흐름으로 actor의 정적 타입 추적 |
| `supervises_with` | 부모의 SupervisionStrategy 정책 | `supervisorStrategy()` 오버라이드 |
| `actor_named` | 액터 인스턴스의 logical path | `actorOf(props, "name")`의 `"name"` 인자 |

## 의미 분석 매처 사양 (도커 이미지별)

[[semantic-analyzer-docker]] 의 Phase 1-B를 3개 트랙으로 분리:

### `codescan/semantic-csharp` (Roslyn)

```csharp
// 다음 Symbol 매칭이 spawns_child 엣지 생성
ISymbol target;
if (invocation.Expression.ToString() is "Context.ActorOf" or "ActorRefFactory.actorOf")
{
    var firstArg = invocation.ArgumentList.Arguments[0].Expression;
    // 케이스 1: Props.Create<T>(...)  → semanticModel.GetSymbolInfo(genericArg)
    // 케이스 2: Props.Create(typeof(T), ...)  → TypeOfExpressionSyntax.Type
    // 케이스 3: Props.Create(() => new T(...))  → LambdaExpression → ObjectCreationExpression.Type
    // → target = 셋 중 하나의 ITypeSymbol
}
```

### `codescan/semantic-java` (JDT/Spoon)

```java
// MethodInvocation matcher
// target receiver: getContext()  OR  ActorRefFactory 구현체
// method name: "actorOf"
// first arg: MethodInvocation Props.create
//   - first arg of Props.create: TypeLiteral .class  → 표적 타입
//   - or static method call on actor class: T.props(...)  → 반환 타입
```

### `codescan/semantic-kotlin` (Kotlin Compiler / Pekko Typed)

```kotlin
// FQN "org.apache.pekko.actor.typed.javadsl.ActorContext.spawn" 호출
// first arg type: Behavior<T> — 제네릭 T가 자식이 처리할 메시지 타입
// 동시에 first arg가 BehaviorFactory()라면 그 클래스가 자식 액터 클래스
// → spawns_child + receives_message 동시 생성 (typed 의 큰 장점)
```

## 액터 패턴 검출 우선순위

1. **Pekko Typed (Kotlin)** — generic `AbstractBehavior<T>` 가 dispatch table을 직접 명시 → 매처 가장 단순
2. **Akka.NET (C#)** — Roslyn workspace 정보 풍부, dogfooding 가능 (CodeScan 자체가 .NET)
3. **Akka Classic (Java)** — JDT 통한 `.class` 리터럴 추적 필요. 매처 복잡도 중간

## 관련

- 코드: `TestSample/csharp-akka/`, `TestSample/java-akka/`, `TestSample/kotlin-pekko/`
- 평가 로그:
  - `harness/logs/test-sentinel/2026-05-18-00-53-akka-actor-hierarchy-fixture.md`
  - `harness/logs/test-sentinel/2026-05-18-01-04-actor-hierarchy-cross-toolkit.md`
- 외부 스킬: `D:\Code\Webnori\skill-actor-model`
- 후속 도커 플랜: [[semantic-analyzer-docker]]
- 언어 분석기 한계: [[language-analyzer-patterns]]
