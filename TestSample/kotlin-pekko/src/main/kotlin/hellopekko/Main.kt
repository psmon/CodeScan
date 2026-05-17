package hellopekko

import hellopekko.actors.WorldBehavior
import hellopekko.messages.HelloAll
import org.apache.pekko.actor.typed.ActorSystem

fun main() {
    val system = ActorSystem.create(WorldBehavior.create(), "HelloPekko")
    try {
        system.tell(HelloAll)
        Thread.sleep(1000)
    } finally {
        system.terminate()
    }
}
