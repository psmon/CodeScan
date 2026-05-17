<?php
declare(strict_types=1);

require_once __DIR__ . '/Person.php';
require_once __DIR__ . '/World.php';
require_once __DIR__ . '/Speakers/EnSpeaker.php';
require_once __DIR__ . '/Speakers/KoSpeaker.php';
require_once __DIR__ . '/Speakers/JaSpeaker.php';

use HelloWorld\World;
use HelloWorld\Speakers\EnSpeaker;
use HelloWorld\Speakers\KoSpeaker;
use HelloWorld\Speakers\JaSpeaker;

$world = new World();
$world->add(new EnSpeaker("Alice"));
$world->add(new KoSpeaker("진수"));
$world->add(new JaSpeaker("ハナコ"));

foreach ($world->helloAll() as $line) {
    echo $line . PHP_EOL;
}
