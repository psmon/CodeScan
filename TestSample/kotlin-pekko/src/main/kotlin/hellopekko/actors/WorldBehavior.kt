package hellopekko.actors

import hellopekko.messages.HelloAll
import hellopekko.messages.HelloResponse
import hellopekko.messages.SayHello
import hellopekko.messages.SpeakerCommand
import hellopekko.messages.WorldCommand
import org.apache.pekko.actor.typed.ActorRef
import org.apache.pekko.actor.typed.Behavior
import org.apache.pekko.actor.typed.javadsl.AbstractBehavior
import org.apache.pekko.actor.typed.javadsl.ActorContext
import org.apache.pekko.actor.typed.javadsl.Behaviors
import org.apache.pekko.actor.typed.javadsl.Receive

/**
 * 부모 액터. setup 시 context.spawn으로 En/Ko/Ja 자식을 생성하고
 * HelloAll 메시지를 받으면 모든 자식에게 SayHello를 발송한다.
 */
class WorldBehavior private constructor(
    context: ActorContext<WorldCommand>
) : AbstractBehavior<WorldCommand>(context) {

    // typed: 각 ActorRef가 처리 가능한 메시지 타입을 컴파일러가 강제.
    private val en: ActorRef<SpeakerCommand> =
        context.spawn(EnSpeakerBehavior.create("Alice"), "en")
    private val ko: ActorRef<SpeakerCommand> =
        context.spawn(KoSpeakerBehavior.create("진수"), "ko")
    private val ja: ActorRef<SpeakerCommand> =
        context.spawn(JaSpeakerBehavior.create("ハナコ"), "ja")

    companion object {
        fun create(): Behavior<WorldCommand> =
            Behaviors.setup { ctx -> WorldBehavior(ctx) }
    }

    override fun createReceive(): Receive<WorldCommand> {
        return newReceiveBuilder()
            .onMessage(HelloAll::class.java) {
                en.tell(SayHello(context.self))
                ko.tell(SayHello(context.self))
                ja.tell(SayHello(context.self))
                this
            }
            .onMessage(HelloResponse::class.java) { msg ->
                println("[${msg.language}] ${msg.name}: ${msg.greeting}")
                this
            }
            .build()
    }
}
