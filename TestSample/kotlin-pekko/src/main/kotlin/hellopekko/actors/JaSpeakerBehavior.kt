package hellopekko.actors

import hellopekko.messages.SpeakerCommand
import org.apache.pekko.actor.typed.Behavior
import org.apache.pekko.actor.typed.javadsl.ActorContext
import org.apache.pekko.actor.typed.javadsl.Behaviors

class JaSpeakerBehavior private constructor(
    context: ActorContext<SpeakerCommand>,
    name: String
) : PersonBehavior(context, name, "ja") {

    override fun greeting(): String = "こんにちは、世界！"

    companion object {
        fun create(name: String): Behavior<SpeakerCommand> =
            Behaviors.setup { ctx -> JaSpeakerBehavior(ctx, name) }
    }
}
