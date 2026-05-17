package hellopekko.actors

import hellopekko.messages.SpeakerCommand
import org.apache.pekko.actor.typed.Behavior
import org.apache.pekko.actor.typed.javadsl.ActorContext
import org.apache.pekko.actor.typed.javadsl.Behaviors

class EnSpeakerBehavior private constructor(
    context: ActorContext<SpeakerCommand>,
    name: String
) : PersonBehavior(context, name, "en") {

    override fun greeting(): String = "Hello, World!"

    companion object {
        fun create(name: String): Behavior<SpeakerCommand> =
            Behaviors.setup { ctx -> EnSpeakerBehavior(ctx, name) }
    }
}
