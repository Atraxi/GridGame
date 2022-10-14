# GridGame
A web server game. A small experiment written over xmas, might eventually be expanded to support plugin style games.

Depends on https://sparkjava.com version: `spark-core-2.3.jar` downloaded to the project root

This is very barebones and has been built & run entirely on command line, e.g.

`javac src/Game.java src/Server.java --class-path spark-core-2.3.jar -d out`

And run:

`java -classpath "spark-core-2.3.jar;out" Server`
