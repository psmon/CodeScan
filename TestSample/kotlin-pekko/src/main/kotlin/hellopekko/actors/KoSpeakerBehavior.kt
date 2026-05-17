package hellopekko.actors

import hellopekko.messages.SpeakerCommand
import org.apache.pekko.actor.typed.Behavior
import org.apache.pekko.actor.typed.javadsl.ActorContext
import org.apache.pekko.actor.typed.javadsl.Behaviors

class KoSpeakerBehavior private constructor(
    context: ActorContext<SpeakerCommand>,
    name: String
) : PersonBehavior(context, name, "ko") {

    override fun greeting(): String = "안녕, 세상!"

    companion object {
        fun create(name: String): Behavior<SpeakerCommand> =
            Behaviors.setup { ctx -> KoSpeakerBehavior(ctx, name) }
    }
}
