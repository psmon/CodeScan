package helloakka.messages;

/** SpeakerActor → WorldActor 응답. */
public record HelloResponse(String language, String name, String greeting) {}
