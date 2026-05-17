package helloworld

class World {
    private val people: MutableList<Person> = mutableListOf()

    fun add(person: Person) {
        people.add(person)
    }

    fun helloAll(): List<String> = people.map { it.hello() }
}
