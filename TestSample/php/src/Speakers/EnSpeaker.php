<?php
declare(strict_types=1);

namespace HelloWorld\Speakers;

use HelloWorld\Person;

final class EnSpeaker extends Person
{
    public function __construct(string $name)
    {
        parent::__construct($name, "en");
    }

    public function speak(): string
    {
        return "Hello, World!";
    }
}
