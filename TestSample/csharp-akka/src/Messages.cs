namespace HelloAkka.Messages;

/// <summary>WorldActor가 모든 자식에게 인사를 시킬 때 전송.</summary>
public sealed record HelloAll;

/// <summary>WorldActor → SpeakerActor 개별 호출.</summary>
public sealed record SayHello;

/// <summary>SpeakerActor → WorldActor (또는 caller) 응답.</summary>
public sealed record HelloResponse(string Language, string Name, string Greeting);
