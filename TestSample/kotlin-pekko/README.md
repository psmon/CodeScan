# kotlin-pekko — Apache Pekko Typed 액터 모델 샘플

`kotlin/` 디렉토리의 POJO OOP와 동일한 도메인을 **Apache Pekko Typed 1.1.5** 로 재구성.

## 도메인

```
ActorSystem<WorldCommand>("HelloPekko") = WorldBehavior
└── /user/en  ─ EnSpeakerBehavior : PersonBehavior : AbstractBehavior<SpeakerCommand>
└── /user/ko  ─ KoSpeakerBehavior : PersonBehavior : AbstractBehavior<SpeakerCommand>
└── /user/ja  ─ JaSpeakerBehavior : PersonBehavior : AbstractBehavior<SpeakerCommand>
```

> typed에서는 user guardian 자체가 `WorldBehavior`이고 자식 액터는 `/user/en` 처럼 매달림.

## 타입 안전성 핵심

- `AbstractBehavior<WorldCommand>` / `AbstractBehavior<SpeakerCommand>` — 각 액터의 메시지 타입 강제
- `sealed interface WorldCommand` / `sealed interface SpeakerCommand` — exhaustive 메시지 계층
- `SayHello(val replyTo: ActorRef<WorldCommand>)` — 응답 대상의 타입까지 명시
- 자식 생성: `context.spawn(EnSpeakerBehavior.create("Alice"), "en")` — Behavior 팩토리

## 빌드

```bash
gradle installDist
build/install/hellopekko/bin/hellopekko
```

도커:
```bash
docker build -t hello-pekko-kotlin .
docker run --rm hello-pekko-kotlin
```

## 정규식이 잡지 못하는 엣지

- `context.spawn(EnSpeakerBehavior.create("Alice"), "en")` — `EnSpeakerBehavior.create` 는 함수 호출. **부모-자식 의미는 손실**
- `Behaviors.setup { ctx -> EnSpeakerBehavior(ctx, name) }` — 람다 안 `new`가 없어서 (Kotlin은 `new` 키워드 자체가 없음) 더 잡기 어려움
- `.onMessage(SayHello::class.java, h)` — typed dispatch table
- `replyTo: ActorRef<WorldCommand>` — typed 응답 채널의 타깃 타입 정보는 의미 분석 필요
