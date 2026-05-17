package hellopekko.actors

import hellopekko.messages.HelloResponse
import hellopekko.messages.SayHello
import hellopekko.messages.SpeakerCommand
import org.apache.pekko.actor.typed.Behavior
import org.apache.pekko.actor.typed.javadsl.AbstractBehavior
import org.apache.pekko.actor.typed.javadsl.ActorContext
import org.apache.pekko.actor.typed.javadsl.Receive

/**
 * Speaker 액터의 추상 베이스. greeting() 만 자식이 구현하면 SayHello 처리/응답은
 * 공통으로 처리된다. AbstractBehavior&lt;SpeakerCommand&gt; 라는 타입 파라미터가
 * 이 액터가 받을 수 있는 메시지를 컴파일 타임에 강제한다.
 */
abstract class PersonBehavior protected constructor(
    context: ActorContext<SpeakerCommand>,
    protected val name: String,
    protected val languageTag: String
) : AbstractBehavior<SpeakerCommand>(context) {

    protected abstract fun greeting(): String

    override fun createReceive(): Receive<SpeakerCommand> {
        return newReceiveBuilder()
            .onMessage(SayHello::class.java) { msg ->
                msg.replyTo.tell(HelloResponse(languageTag, name, greeting()))
                this
            }
            .build()
    }
}
