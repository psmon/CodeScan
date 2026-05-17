package hellopekko.messages

import org.apache.pekko.actor.typed.ActorRef

/**
 * WorldBehavior가 처리하는 명령 계층. sealed interface 로 exhaustive 보장.
 */
sealed interface WorldCommand

data object HelloAll : WorldCommand
data class HelloResponse(
    val language: String,
    val name: String,
    val greeting: String
) : WorldCommand

/**
 * SpeakerBehavior 계열이 처리하는 명령. replyTo로 응답 ActorRef 명시 — typed 패턴 핵심.
 */
sealed interface SpeakerCommand

data class SayHello(val replyTo: ActorRef<WorldCommand>) : SpeakerCommand
