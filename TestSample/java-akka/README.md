# java-akka — Akka Classic (untyped) 액터 모델 샘플

`java/` 디렉토리의 POJO OOP와 동일한 도메인을 **Akka Classic 2.7.x (untyped)** 으로 재구성.

## 도메인

```
ActorSystem("HelloAkka")
└── /user/world  ─ WorldActor (부모, AbstractActor)
    ├── /user/world/en  ─ EnSpeakerActor : PersonActor : AbstractActor
    ├── /user/world/ko  ─ KoSpeakerActor : PersonActor : AbstractActor
    └── /user/world/ja  ─ JaSpeakerActor : PersonActor : AbstractActor
```

- 메시지 dispatch: `receiveBuilder().match(SayHello.class, handler).build()`
- 자식 생성: `getContext().actorOf(EnSpeakerActor.props("Alice"), "en")`
- props 팩토리: `Props.create(EnSpeakerActor.class, name)`
- 응답: `getSender().tell(new HelloResponse(...), getSelf())`

## 빌드

```bash
mvn -B package
java -jar target/helloakka-1.0.0.jar
```

도커:
```bash
docker build -t hello-akka-java .
docker run --rm hello-akka-java
```

## 정규식이 잡지 못하는 엣지

- `Props.create(EnSpeakerActor.class, ...)` — `EnSpeakerActor.class` 토큰이 부모-자식 시그널이지만 regex는 `class` 키워드와 구분 못함
- `.match(SayHello.class, h)` — 액터의 메시지 dispatch table
- `getSender().tell(...)` — 동적 발신자
- 액터 패스 `/user/world/en` — 문자열 `"en"`만 보면 의미 모름
