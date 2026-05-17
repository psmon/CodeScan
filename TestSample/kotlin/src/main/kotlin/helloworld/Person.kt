package helloworld

abstract class Person(val name: String, val language: String) {
    abstract fun speak(): String

    fun hello(): String = "[$language] $name: ${speak()}"
}
