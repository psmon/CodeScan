<?php
declare(strict_types=1);

namespace HelloWorld;

abstract class Person
{
    public function __construct(
        public readonly string $name,
        public readonly string $language
    ) {}

    abstract public function speak(): string;

    public function hello(): string
    {
        return sprintf("[%s] %s: %s", $this->language, $this->name, $this->speak());
    }
}
