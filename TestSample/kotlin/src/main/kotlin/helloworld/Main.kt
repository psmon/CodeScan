package helloworld

import helloworld.speakers.EnSpeaker
import helloworld.speakers.KoSpeaker
import helloworld.speakers.JaSpeaker

fun main() {
    val world = World()
    world.add(EnSpeaker("Alice"))
    world.add(KoSpeaker("진수"))
    world.add(JaSpeaker("ハナコ"))

    world.helloAll().forEach { println(it) }
}
