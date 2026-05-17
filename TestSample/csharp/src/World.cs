using System.Collections.Generic;

namespace HelloWorld;

public sealed class World
{
    private readonly List<Person> _people = new();

    public void Add(Person person) => _people.Add(person);

    public IEnumerable<string> HelloAll()
    {
        foreach (var person in _people)
        {
            yield return person.Hello();
        }
    }
}
