<?php
declare(strict_types=1);

namespace HelloWorld\Speakers;

use HelloWorld\Person;

final class KoSpeaker extends Person
{
    public function __construct(string $name)
    {
        parent::__construct($name, "ko");
    }

    public function speak(): string
    {
        return "안녕, 세상!";
    }
}
