<?php
declare(strict_types=1);

namespace HelloWorld\Speakers;

use HelloWorld\Person;

final class JaSpeaker extends Person
{
    public function __construct(string $name)
    {
        parent::__construct($name, "ja");
    }

    public function speak(): string
    {
        return "こんにちは、世界！";
    }
}
