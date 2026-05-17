# csharp-akka — Akka.NET 액터 모델 샘플

`csharp/` 디렉토리의 동일한 도메인(World / 3개국어 Speaker)을 **Akka.NET 액터 모델**로 재구성한 샘플.
목적: **언어 스펙(상속)이 아닌 툴킷 스펙(액터 hierarchy)** 으로 만들어지는 의존성을
CodeScan이 얼마나 잡아내는지 측정하기 위한 fixture.

## 도메인

```
ActorSystem("HelloAkka")
└── /user/world  ─ WorldActor (부모)
    ├── /user/world/en  ─ EnSpeakerActor : PersonActor
    ├── /user/world/ko  ─ KoSpeakerActor : PersonActor
    └── /user/world/ja  ─ JaSpeakerActor : PersonActor
```

- **PersonActor** : `ReceiveActor` 추상 베이스, `SayHello` 메시지 처리
- **En/Ko/Ja SpeakerActor** : PersonActor 상속, `Greeting()` 오버라이드
- **WorldActor** : `PreStart()`에서 `Context.ActorOf(Props.Create(() => new XxxSpeakerActor(...)))`로 3개 자식 생성. `HelloAll` 메시지 수신 시 각 자식에게 `SayHello` 발송.

## 정규식 분석으로 잡혀야 하는 엣지

| 엣지 | 출처 | 정규식이 잡을까? |
|------|------|----------------|
| `inherits_or_implements` | `class EnSpeakerActor : PersonActor` 등 | ✅ |
| `inherits_or_implements` | `PersonActor : ReceiveActor` | ✅ |
| `imports` | `using Akka.Actor;`, `using HelloAkka.Messages;` | ✅ |

## 정규식이 **놓치는** 엣지 (액터 hierarchy 핵심)

| 엣지 (논리) | 출처 | 이유 |
|------|------|------|
| `creates_child_actor` (WorldActor → EnSpeakerActor) | `Context.ActorOf(Props.Create(() => new EnSpeakerActor(...)))` | `new EnSpeakerActor(...)` 부분은 잡히지만 **부모-자식 관계의 의미는 사라짐**. 그래프상 `WorldActor -[creates]-> EnSpeakerActor`로만 보임. |
| `actor_path` (`/user/world/en`) | `"en"` 인자 | 정규식은 액터 이름 인자 의미를 모름 |
| `sends_message_to` | `_en.Tell(new SayHello())` | `_en`은 `IActorRef` 필드. 타입은 같지만 어떤 실제 액터인지는 의미 분석 필요 |
| `supervises` | 부모 액터의 SupervisionStrategy | 별도 메서드 오버라이드 — 의미 분석 영역 |

## 빌드

```bash
cd TestSample/csharp-akka
dotnet build
dotnet run
```

도커:
```bash
docker build -t hello-akka .
docker run --rm hello-akka
```

기대 출력:
```
[en] Alice: Hello, World!
[ko] 진수: 안녕, 세상!
[ja] ハナコ: こんにちは、世界！
```
(순서는 비결정적 — 액터 메시지 전송이므로)
