---
name: actor-model-cross-toolkit
type: knowledge
domain: actor
description: 액터 모델의 툴킷별 부모-자식 표현 차이와, CodeScan이 미래에 도입할 그래프 엣지/매처 전략.
governs: []
anchor: none  # 미래 전략 명세(설계-고아, Type C) — 현재 코드에 앵커 없음. 도입 시 governs 채움.
---

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

## 4-툴킷 비교

| 항목 | Akka.NET 1.5 | Akka Classic 2.7 | Apache Pekko Typed 1.1+ | **Microsoft Orleans 9** |
|------|-------------|-----------------|------------------------|------------------------|
| 언어 | C# (.NET) | Java/Scala | Java/Kotlin | C# (.NET) |
| 라이선스 | Apache 2.0 | BSL (2.7부터) | **Apache 2.0** (Akka의 OSS 포크) | MIT |
| 타입 모델 | untyped | untyped | **typed** (`AbstractBehavior<T>`) | **virtual actor** (`Grain` + interface) |
| 베이스 클래스 | `ReceiveActor`, `UntypedActor` | `AbstractActor` | `AbstractBehavior<T>` | `Grain` (구현) + `IGrainWith*Key` (인터페이스) |
| 메시지 dispatch | `Receive<T>(handler)` | `receiveBuilder().match(Class, h)` | `newReceiveBuilder().onMessage(Class, h)` | grain 인터페이스의 **method 호출 (RPC)** |
| Props 팩토리 | `Props.Create<T>()`, `Props.Create(() => new T())`, `Props.Create(typeof(T))` | `Props.create(T.class)`, `Props.create(T.class, args)` | (없음 — `Behavior<T>` 직접 사용) | (없음 — 인터페이스 마커 + 런타임 활성화) |
| 자식 생성 / 참조 | `Context.ActorOf(Props, "name")` | `getContext().actorOf(Props, "name")` | `context.spawn(Behavior, "name")` | **`GrainFactory.GetGrain<T>(key)`** (lookup; 첫 호출 시 자동 활성화) |
| 응답 | `Sender.Tell(msg)` (암묵 sender) | `getSender().tell(msg, getSelf())` | `replyTo.tell(msg)` (메시지에 명시) | `return Task<T>` (RPC 반환값) |
| 자기 참조 | `Self` | `getSelf()` | `context.self` | `this.GetPrimaryKey*()` |
| 패러다임 | 계층적 hierarchy | 계층적 hierarchy | 계층적 hierarchy (typed) | **virtual actor** (lifecycle: 런타임 제어) |

## TestSample fixture 매핑

| 디렉토리 | 툴킷 | 빌드 검증 |
|---------|------|----------|
| `TestSample/csharp-akka/` | Akka.NET 1.5.51 | Docker — ✅ |
| `TestSample/java-akka/` | Akka Classic 2.7.0 | Docker (Maven+JDK21) — ✅ |
| `TestSample/kotlin-pekko/` | Apache Pekko Typed 1.1.5 | Docker (Gradle+JDK21) — ✅ |
| **`TestSample/csharp-orleans/`** | **Microsoft Orleans 9.0.1** | Docker (.NET SDK 9.0) — ✅ (P2 합류) |

네 샘플 모두 **동일한 도메인**(World 부모 + En/Ko/Ja Speaker 자식)을 구현하여
**같은 의미 → 다른 표면** 비교의 결정적 기준점.

> Orleans는 hierarchy 패러다임 자체가 다름 (virtual actor) — World가 children을
> spawn하지 않고 `GrainFactory.GetGrain<T>(id)` 로 lookup한다. 같은 도메인을 다른
> 패러다임으로 표현하여 의미 분석 매처의 **virtual-actor vs hierarchical-actor 구분**
> 능력을 시험한다.

## 정규식 한계 매트릭스 (4-툴킷, P2 후)

| 의도 | C# Akka.NET | Java Akka Classic | Kotlin Pekko Typed | C# Orleans | regex 잡힘? |
|------|-------------|-------------------|--------------------|------------|---|
| 부모-자식 spawn | `Props.Create(() => new T())` | — | — | — | ⚠️ 우연 (`new` 덕분) |
| 부모-자식 spawn | `Props.Create(typeof(T))` | — | — | — | ❌ |
| 부모-자식 spawn | — | `Props.create(T.class)` | — | — | ❌ (`new` 없음) |
| 부모-자식 spawn | — | — | `context.spawn(T.create())` | — | ❌ (Kotlin은 `new` 키워드 자체 없음) |
| **virtual-actor activates** | — | — | — | `GrainFactory.GetGrain<T>(id)` | ❌ (generic argument extraction 필요) |
| 액터 베이스 상속 | `: ReceiveActor` | `extends AbstractActor` | `: AbstractBehavior<T>` | `: Grain, IFooGrain` | ✅ (Kotlin R1/R2 봉합 후 모두 5/5) |
| 메시지 dispatch (Receive 등록) | `Receive<T>(h)` | `.match(T.class, h)` | `.onMessage(T.class, h)` | (메서드 정의 — `defines`로 잡힘) | ❌ Akka 계열 / Orleans는 `defines`로 가시 |
| 메시지 전송 | `actor.Tell(msg)` | `actor.tell(msg, self)` | `actor.tell(msg)` | `await grain.Method(args)` (RPC) | ❌ Akka 계열 / Orleans는 method invocation으로 가시 |

→ Akka 계열 4가지 부모-자식 패턴 + Orleans virtual-actor 패턴 = **5가지 spawn-equivalent 표면** 중
**regex로 잡히는 의미 0건** (1개 우연 매치 외). 모두 의미 분석 매처 영역.

→ Orleans는 dispatch/전송 의미가 method-level이라 **`defines` + `creates`로 부분 가시**.
순수 actor 패러다임(Akka)보다 정규식 친화적인 면이 있다.

## 제안 신규 그래프 엣지

현재 `inherits_or_implements`/`creates`/`uses_type`/`imports` 만으로는 액터 hierarchy를 표현 불가.
의미 분석 단계에서 다음 엣지 추가 필요 (`Models/EdgeKinds.cs`에 const 등록 완료, v1.3.0+):

| 엣지 | 의미 | 검출 (의미 분석) |
|------|------|----------------|
| `spawns_child` | 부모 액터 → 자식 액터 타입 (Akka 계열) | `ActorOf`/`actorOf`/`spawn` 호출의 첫 인자 분석 (제네릭/`.class`/팩토리 함수 반환 타입) |
| `receives_message` | 액터가 처리 가능한 메시지 타입 | `Receive<T>(...)`/`.match(T.class, ...)`/`.onMessage(T.class, ...)` 인자 |
| `sends_message_to` | 특정 ActorRef에 특정 메시지 전송 | `actor.tell(msg)` — 데이터 흐름으로 actor의 정적 타입 추적 |
| `supervises_with` | 부모의 SupervisionStrategy 정책 | `supervisorStrategy()` 오버라이드 |
| `actor_named` | 액터 인스턴스의 logical path | `actorOf(props, "name")`의 `"name"` 인자 |
| **`activates`** | virtual-actor lookup → 자식 grain 타입 (Orleans) | `GrainFactory.GetGrain<T>(id)` 의 generic 인자 추적 + `IGrainFactory` 변수의 `.GetGrain<T>` |

→ Orleans 패러다임에서는 spawn이 아니라 **lookup-then-activate**라 의미가 다름.
같은 `spawns_child`로 묶으면 hierarchy 의도가 흐려지므로 별도 `activates` 엣지로 분리.

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
2. **Akka.NET (C#)** — Roslyn workspace 정보 풍부, dogfooding 가능 (CodeScan 자체가 .NET). v1.3.0에서 Phase 1 foundation 합류 완료
3. **Orleans (C#)** — `GrainFactory.GetGrain<T>(id)` 매처 Roslyn에서 단순 (`InvocationExpressionSyntax` + `GenericNameSyntax`). Akka.NET 매처와 같은 도커 이미지(`codescan/semantic-csharp`)에서 두 패턴 모두 처리 가능
4. **Akka Classic (Java)** — JDT 통한 `.class` 리터럴 추적 필요. 매처 복잡도 중간

### Orleans 매처 사양 (Roslyn 추가)

```csharp
// 다음 Symbol 매칭이 activates 엣지 생성 (codescan/semantic-csharp Phase 1-B)
if (invocation.Expression is MemberAccessExpressionSyntax
    {
        Name: GenericNameSyntax { Identifier.ValueText: "GetGrain", TypeArgumentList.Arguments: { Count: > 0 } typeArgs }
    }
    && model.GetSymbolInfo(typeArgs[0]).Symbol is INamedTypeSymbol target)
{
    var owner = invocation.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault();
    if (owner is not null)
        EmitEdge("class", owner.Identifier.Text, "type", target.Name, "activates", line);
}
```

→ `WorldGrain -[activates]-> IEnSpeakerGrain` 같은 엣지가 emit됨. 동일 fixture에서 Akka.NET 매처는 `spawns_child` 를 emit하므로 두 패러다임이 **서로 다른 엣지 종류로 그래프에 구별**됨.

## 관련

- 코드: `TestSample/csharp-akka/`, `TestSample/java-akka/`, `TestSample/kotlin-pekko/`
- 평가 로그:
  - `harness/logs/test-sentinel/2026-05-18-00-53-akka-actor-hierarchy-fixture.md`
  - `harness/logs/test-sentinel/2026-05-18-01-04-actor-hierarchy-cross-toolkit.md`
- 외부 스킬: `D:\Code\Webnori\skill-actor-model`
- 후속 도커 플랜: [[semantic-analyzer-docker]]
- 언어 분석기 한계: [[language-analyzer-patterns]]
