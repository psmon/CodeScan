<?php
declare(strict_types=1);

namespace HelloWorld;

final class World
{
    /** @var Person[] */
    private array $people = [];

    public function add(Person $person): void
    {
        $this->people[] = $person;
    }

    /** @return string[] */
    public function helloAll(): array
    {
        $lines = [];
        foreach ($this->people as $p) {
            $lines[] = $p->hello();
        }
        return $lines;
    }
}
