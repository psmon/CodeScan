using Akka.Actor;
using HelloAkka.Actors;
using HelloAkka.Messages;

using var system = ActorSystem.Create("HelloAkka");

var world = system.ActorOf(Props.Create(() => new WorldActor()), "world");
world.Tell(new HelloAll());

// 응답이 모두 출력될 시간 확보 후 종료.
await Task.Delay(TimeSpan.FromSeconds(1));
await system.Terminate();
