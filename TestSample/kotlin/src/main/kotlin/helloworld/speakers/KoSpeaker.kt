package helloworld.speakers

import helloworld.Person

class KoSpeaker(name: String) : Person(name, "ko") {
    override fun speak(): String = "안녕, 세상!"
}
