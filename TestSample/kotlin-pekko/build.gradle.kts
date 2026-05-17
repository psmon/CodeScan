plugins {
    kotlin("jvm") version "2.0.0"
    application
}

repositories {
    mavenCentral()
}

val pekkoVersion = "1.1.5"

dependencies {
    implementation("org.apache.pekko:pekko-actor-typed_2.13:$pekkoVersion")
    implementation("ch.qos.logback:logback-classic:1.5.18")
}

application {
    mainClass.set("hellopekko.MainKt")
}

kotlin {
    jvmToolchain(21)
}
