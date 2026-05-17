cmake_minimum_required(VERSION 3.20)
project(helloworld CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_executable(helloworld
    src/Person.cpp
    src/World.cpp
    src/speakers/EnSpeaker.cpp
    src/speakers/KoSpeaker.cpp
    src/speakers/JaSpeaker.cpp
    src/main.cpp
)
target_include_directories(helloworld PRIVATE src)
