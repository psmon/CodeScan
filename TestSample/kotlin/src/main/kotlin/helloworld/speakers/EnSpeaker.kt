package helloworld.speakers

import helloworld.Person

class EnSpeaker(name: String) : Person(name, "en") {
    override fun speak(): String = "Hello, World!"
}
