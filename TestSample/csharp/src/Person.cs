namespace HelloWorld;

public abstract class Person
{
    public string Name { get; }
    public string Language { get; }

    protected Person(string name, string language)
    {
        Name = name;
        Language = language;
    }

    public abstract string Speak();

    public string Hello() => $"[{Language}] {Name}: {Speak()}";
}
