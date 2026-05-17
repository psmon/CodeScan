using HelloWorld;
using HelloWorld.Speakers;

var world = new World();
world.Add(new EnSpeaker("Alice"));
world.Add(new KoSpeaker("진수"));
world.Add(new JaSpeaker("ハナコ"));

foreach (var line in world.HelloAll())
{
    Console.WriteLine(line);
}
