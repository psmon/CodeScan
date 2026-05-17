package helloworld.speakers

import helloworld.Person

class JaSpeaker(name: String) : Person(name, "ja") {
    override fun speak(): String = "こんにちは、世界！"
}
